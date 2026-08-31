#!/bin/zsh
set -eu
setopt pipefail

readonly MIB=$((1024 * 1024))
readonly NORMAL_TARGET_MIB=512
readonly NORMAL_WARNING_MIB=1536
readonly NORMAL_SOFT_MIB=2304
readonly NORMAL_HARD_MIB=3072
readonly ABSOLUTE_MIB=8192
readonly NORMAL_INTERVAL=10
readonly NORMAL_SOFT_SAMPLES=3
readonly NORMAL_TERM_GRACE=5
readonly MAX_OWNED_PROCESSES=16
readonly TOOL_TIMEOUT_SECONDS=2

duration=900
expected_sha=""
test_profile=0
allow_clean_exit=0

usage() {
  print -u2 "usage: $0 [--duration seconds] [--expected-sha sha256] [--allow-clean-exit] [--test-profile] -- command [args...]"
  exit 64
}

while (( $# > 0 )); do
  case "$1" in
    --duration)
      (( $# >= 2 )) || usage
      duration="$2"
      shift 2
      ;;
    --expected-sha)
      (( $# >= 2 )) || usage
      expected_sha="$2"
      shift 2
      ;;
    --test-profile)
      test_profile=1
      shift
      ;;
    --allow-clean-exit)
      allow_clean_exit=1
      shift
      ;;
    --)
      shift
      break
      ;;
    *) usage ;;
  esac
done

(( $# > 0 )) || usage
[[ "$duration" == <-> ]] || usage
(( duration >= 5 && duration <= 14400 )) || usage

target="$1"
[[ -f "$target" && -x "$target" ]] || {
  print -u2 "GUARD_REFUSED: target is not an executable file"
  exit 65
}

if (( test_profile )); then
  [[ "${target:t}" == "watchdog-probe.zsh" ]] || {
    print -u2 "GUARD_REFUSED: test profile is restricted to watchdog-probe.zsh"
    exit 65
  }
  soft_mib=64
  hard_mib=128
  warning_mib=48
  target_mib=32
  interval=1
  soft_samples=3
  term_grace=2
  guard_system=0
else
  target_mib=$NORMAL_TARGET_MIB
  warning_mib=$NORMAL_WARNING_MIB
  soft_mib=$NORMAL_SOFT_MIB
  hard_mib=$NORMAL_HARD_MIB
  interval=$NORMAL_INTERVAL
  soft_samples=$NORMAL_SOFT_SAMPLES
  term_grace=$NORMAL_TERM_GRACE
  guard_system=1
fi

(( target_mib < warning_mib && warning_mib < soft_mib &&
    soft_mib < hard_mib && hard_mib < ABSOLUTE_MIB )) || {
  print -u2 "GUARD_REFUSED: invalid immutable memory budget"
  exit 65
}

actual_sha="$(shasum -a 256 "$target" | awk '{print $1}')"
if [[ -z "$expected_sha" ]] ||
    (( ${#expected_sha} != 64 )) ||
    ! print -r -- "$expected_sha" | grep -Eq '^[0-9a-f]{64}$'; then
  print -u2 "GUARD_REFUSED: --expected-sha with 64 lowercase hex characters is required"
  exit 66
fi
if [[ "$actual_sha" != "$expected_sha" ]]; then
  print -u2 "GUARD_REFUSED: target SHA-256 mismatch"
  exit 66
fi

run_id="$(date -u +%Y%m%dT%H%M%SZ)-$$"
diagnostics_root="artifacts/qa-diagnostics"
run_dir="$diagnostics_root/$run_id"
pid_file="$diagnostics_root/trdng-guard.pid"
lock_dir="$diagnostics_root/trdng-guard.lock"
mkdir -p "$diagnostics_root"

process_start() {
  ps -o lstart= -p "$1" 2>/dev/null | sed 's/^[[:space:]]*//;s/[[:space:]]*$//'
}

if ! mkdir "$lock_dir" 2>/dev/null; then
  previous_guard_pid="$(sed -n '1p' "$lock_dir/owner.pid" 2>/dev/null || true)"
  previous_guard_start="$(sed -n '1p' "$lock_dir/owner.start" 2>/dev/null || true)"
  current_guard_start=""
  if [[ "$previous_guard_pid" == <-> ]]; then
    current_guard_start="$(process_start "$previous_guard_pid" || true)"
  fi
  if [[ -n "$current_guard_start" && "$current_guard_start" == "$previous_guard_start" ]]; then
    print -u2 "GUARD_REFUSED: another guarded run is active"
    exit 67
  fi
  print -u2 "GUARD_REFUSED: stale guard lock requires manual inspection and removal"
  exit 67
fi
print "$$" > "$lock_dir/owner.pid"
process_start "$$" > "$lock_dir/owner.start"
mkdir -p "$run_dir"

root_pid=""
cleanup_done=0
result="RUNNING"
kill_escalated=0
cleanup_failed=0
typeset -A owned_starts

identity_alive() {
  local pid="$1"
  local expected="${owned_starts[$pid]-}"
  [[ -n "$expected" ]] || return 1
  local current state
  current="$(process_start "$pid" || true)"
  [[ -n "$current" && "$current" == "$expected" ]] || return 1
  state="$(ps -o stat= -p "$pid" 2>/dev/null | awk '{print $1}')"
  [[ -n "$state" && "$state" != Z* ]]
}

remember_pid() {
  local pid="$1"
  [[ "$pid" == <-> ]] || return 0
  local start
  start="$(process_start "$pid" || true)"
  [[ -n "$start" ]] || return 0
  if [[ -n "${owned_starts[$pid]-}" && "${owned_starts[$pid]}" != "$start" ]]; then
    return 0
  fi
  owned_starts[$pid]="$start"
}

refresh_owned() {
  local pid child changed
  local -a group_pids children
  group_pids=("${(@f)$(ps -axo pid=,pgid= 2>/dev/null |
    awk -v group="$root_pid" '$2 == group {print $1}' || true)}" )
  for pid in "${group_pids[@]}"; do
    [[ -n "$pid" ]] && remember_pid "$pid"
  done

  changed=1
  while (( changed )); do
    changed=0
    for pid in "${(@k)owned_starts}"; do
      identity_alive "$pid" || continue
      children=("${(@f)$(pgrep -P "$pid" 2>/dev/null || true)}")
      for child in "${children[@]}"; do
        [[ -n "$child" ]] || continue
        if [[ -z "${owned_starts[$child]-}" ]]; then
          remember_pid "$child"
          [[ -n "${owned_starts[$child]-}" ]] && changed=1
        fi
      done
    done
  done
  return 0
}

tree_pids() {
  local pid
  for pid in "${(@k)owned_starts}"; do
    identity_alive "$pid" && print -- "$pid"
  done
  return 0
}

tree_alive() {
  refresh_owned
  local pid
  for pid in "${(@f)$(tree_pids)}"; do
    [[ -n "$pid" ]] && return 0
  done
  return 1
}

measure_tree_footprint() {
  local -a owner_pids tool_pids output_files
  local pid tool_pid output_file index bytes
  local failed=0
  local total=0
  owner_pids=("$@")
  (( ${#owner_pids[@]} > 0 && ${#owner_pids[@]} <= MAX_OWNED_PROCESSES )) || return 1

  for pid in "${owner_pids[@]}"; do
    output_file="$run_dir/.footprint-sample-$pid"
    footprint -f bytes --noCategories -p "$pid" > "$output_file" 2>/dev/null &
    tool_pid=$!
    tool_pids+=("$tool_pid")
    output_files+=("$output_file")
  done

  local deadline=$(( SECONDS + TOOL_TIMEOUT_SECONDS ))
  local any_alive=1
  while (( any_alive && SECONDS < deadline )); do
    any_alive=0
    for tool_pid in "${tool_pids[@]}"; do
      kill -0 "$tool_pid" 2>/dev/null && any_alive=1
    done
    if (( any_alive )); then
      sleep 0.1
    fi
  done

  for (( index=1; index<=${#tool_pids[@]}; index++ )); do
    tool_pid="${tool_pids[$index]}"
    output_file="${output_files[$index]}"
    if kill -0 "$tool_pid" 2>/dev/null; then
      kill -TERM "$tool_pid" 2>/dev/null || true
      sleep 0.1
      kill -KILL "$tool_pid" 2>/dev/null || true
      failed=1
    fi
    wait "$tool_pid" 2>/dev/null || failed=1
    bytes="$(awk '/phys_footprint:/{print $2; exit}' "$output_file" 2>/dev/null || true)"
    if [[ "$bytes" == <-> ]]; then
      (( total += bytes ))
    else
      failed=1
    fi
    rm -f "$output_file"
  done
  (( failed == 0 )) || return 1
  REPLY="$total"
}

signal_tree() {
  local signal="$1"
  local -a pids
  local index pid
  refresh_owned
  pids=("${(@f)$(tree_pids)}")
  for (( index=${#pids[@]}; index>=1; index-- )); do
    pid="${pids[$index]}"
    kill -"$signal" "$pid" 2>/dev/null || true
  done
}

capture_diagnostics() {
  local reason="$1"
  local -a pids owned_pids
  local pid
  refresh_owned
  pids=("${(@f)$(tree_pids)}")
  owned_pids=("${(@k)owned_starts}")
  {
    print "reason=$reason"
    print "utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    print "target_sha256=$actual_sha"
    print "root_pid=$root_pid"
    print "owned_pids=${(j:,:)owned_pids}"
    print "live_pids=${(j:,:)pids}"
    print "target_mib=$target_mib warning_mib=$warning_mib soft_mib=$soft_mib hard_mib=$hard_mib absolute_mib=$ABSOLUTE_MIB"
  } > "$run_dir/incident.txt"
  local diagnostic_count=0
  for pid in "${pids[@]}"; do
    (( diagnostic_count += 1 ))
    if (( diagnostic_count > MAX_OWNED_PROCESSES )); then
      print "diagnostics_truncated_at=$MAX_OWNED_PROCESSES" >> "$run_dir/incident.txt"
      break
    fi
    ps -o pid=,ppid=,lstart=,rss=,vsz=,command= -p "$pid" >> "$run_dir/processes.txt" 2>/dev/null || true
  done
  return 0
}

stop_tree() {
  local reason="$1"
  local term_started="$(date +%s)"
  signal_tree TERM
  while tree_alive && (( $(date +%s) - term_started < term_grace )); do
    sleep 0.1
  done
  if tree_alive; then
    kill_escalated=1
    print "$(date -u +%Y-%m-%dT%H:%M:%SZ),KILL_ESCALATION" >> "$run_dir/events.csv"
    signal_tree KILL
  fi
  local verification_deadline=$(( SECONDS + 2 ))
  while tree_alive && (( SECONDS < verification_deadline )); do
    sleep 0.1
  done
  if tree_alive; then
    cleanup_failed=1
    print "$(date -u +%Y-%m-%dT%H:%M:%SZ),OWNED_TREE_SURVIVED" >> "$run_dir/events.csv"
  fi
  capture_diagnostics "$reason"
}

cleanup() {
  (( cleanup_done == 0 )) || return 0
  cleanup_done=1
  if [[ -n "$root_pid" ]] && tree_alive; then
    stop_tree "TRAP_CLEANUP"
  fi
  rm -f "$pid_file"
  rm -f "$lock_dir/owner.pid" "$lock_dir/owner.start"
  rmdir "$lock_dir" 2>/dev/null || true
}
trap cleanup EXIT INT TERM HUP

print "utc,event,tree_rss_bytes,tree_physical_footprint_bytes,system_free_percent,swapouts" > "$run_dir/samples.csv"
print "utc,event" > "$run_dir/events.csv"
{
  print "target=$target"
  print "target_sha256=$actual_sha"
  print "duration_seconds=$duration"
  print "target_mib=$target_mib"
  print "warning_mib=$warning_mib"
  print "soft_mib=$soft_mib"
  print "hard_mib=$hard_mib"
  print "absolute_mib=$ABSOLUTE_MIB"
  print "interval_seconds=$interval"
  print "soft_samples=$soft_samples"
  print "term_grace_seconds=$term_grace"
  print "allow_clean_exit=$allow_clean_exit"
} > "$run_dir/manifest.txt"

/usr/bin/perl -MPOSIX -e '
  POSIX::setsid() >= 0 or die "setsid failed\n";
  exec @ARGV or die "exec failed\n";
' -- "$@" > "$run_dir/target.stdout.log" 2> "$run_dir/target.stderr.log" &
root_pid=$!
for _ in {1..20}; do
  root_pgid="$(ps -o pgid= -p "$root_pid" 2>/dev/null | awk '{print $1}' || true)"
  [[ "$root_pgid" == "$root_pid" ]] && break
  sleep 0.1
done
if [[ "${root_pgid:-}" != "$root_pid" ]]; then
  for escaped_pid in "${(@f)$(ps -axo pid=,pgid= 2>/dev/null |
    awk -v group="$root_pid" '$2 == group {print $1}' || true)}"; do
    [[ "$escaped_pid" == <-> ]] && kill -TERM "$escaped_pid" 2>/dev/null || true
  done
  print -u2 "GUARD_REFUSED: target did not enter an isolated process session"
  exit 68
fi
remember_pid "$root_pid"
print "$root_pid" > "$pid_file"
print "root_pid=$root_pid" >> "$run_dir/manifest.txt"
print "$(date -u +%Y-%m-%dT%H:%M:%SZ),START" >> "$run_dir/events.csv"

started_at="$(date +%s)"
next_sample_at="$started_at"
soft_count=0
warning_captured=0
swap_growth_samples=0
vm_stat_initial="$(vm_stat 2>/dev/null || true)"
page_size="$(print -r -- "$vm_stat_initial" | awk 'NR==1 {gsub(/[^0-9]/, "", $0); print $0+0}')"
baseline_swapouts="$(print -r -- "$vm_stat_initial" | awk -F: '/Swapouts:/{gsub(/[^0-9]/, "", $2); print $2+0}')"
previous_swapouts="$baseline_swapouts"
target_status=""
while identity_alive "$root_pid"; do
  refresh_owned
  now="$(date +%s)"
  if (( now - started_at >= duration )); then
    result="PASS_DURATION"
    stop_tree "$result"
    if (( kill_escalated )); then
      result="DURATION_SHUTDOWN_KILL_ESCALATED"
    fi
    break
  fi

  if (( now < next_sample_at )); then
    sleep 1
    continue
  fi
  next_sample_at=$(( now + interval ))

  pids=("${(@f)$(tree_pids)}")
  if (( ${#pids[@]} == 0 || ${#pids[@]} > MAX_OWNED_PROCESSES )); then
    result="PROCESS_TREE_LIMIT_OR_EMPTY"
    stop_tree "$result"
    break
  fi
  tree_rss=0
  for pid in "${pids[@]}"; do
    rss_kib="$(ps -o rss= -p "$pid" 2>/dev/null | awk 'NF {print $1; exit}' || true)"
    if [[ "$rss_kib" != <-> ]]; then
      result="MEASUREMENT_FAILED"
      stop_tree "$result"
      break 2
    fi
    (( tree_rss += rss_kib * 1024 ))
  done
  if ! measure_tree_footprint "${pids[@]}"; then
    result="MEASUREMENT_FAILED"
    stop_tree "$result"
    break
  fi
  tree_footprint="$REPLY"
  observed=$(( tree_rss > tree_footprint ? tree_rss : tree_footprint ))
  system_free_percent=100
  current_swapouts="$previous_swapouts"
  if (( guard_system )); then
    system_free_percent="$(memory_pressure -Q 2>/dev/null | awk -F: '/free percentage/{gsub(/[^0-9]/, "", $2); print $2+0}' || true)"
    current_swapouts="$(vm_stat 2>/dev/null | awk -F: '/Swapouts:/{gsub(/[^0-9]/, "", $2); print $2+0}' || true)"
    if [[ "$system_free_percent" != <-> || "$current_swapouts" != <-> ||
          "$page_size" != <-> || "$baseline_swapouts" != <-> ]]; then
      result="MEASUREMENT_FAILED"
      stop_tree "$result"
      break
    fi
  fi
  print "$(date -u +%Y-%m-%dT%H:%M:%SZ),SAMPLE,$tree_rss,$tree_footprint,$system_free_percent,$current_swapouts" >> "$run_dir/samples.csv"

  if (( guard_system )); then
    swap_interval_bytes=$(( (current_swapouts - previous_swapouts) * page_size ))
    swap_total_bytes=$(( (current_swapouts - baseline_swapouts) * page_size ))
    if (( swap_interval_bytes >= 4 * MIB )); then
      (( swap_growth_samples += 1 ))
    else
      swap_growth_samples=0
    fi
    if (( system_free_percent <= 5 )); then
      result="SYSTEM_PRESSURE_CRITICAL"
      stop_tree "$result"
      break
    fi
    if (( swap_total_bytes >= 64 * MIB || swap_growth_samples >= 2 )); then
      result="SYSTEM_SWAP_GROWTH"
      stop_tree "$result"
      break
    fi
    previous_swapouts="$current_swapouts"
  fi

  if (( observed >= ABSOLUTE_MIB * MIB )); then
    result="ABSOLUTE_LIMIT"
    stop_tree "$result"
    break
  fi
  if (( observed >= hard_mib * MIB )); then
    result="HARD_LIMIT"
    stop_tree "$result"
    break
  fi
  if (( observed >= warning_mib * MIB && warning_captured == 0 )); then
    warning_captured=1
    capture_diagnostics "WARNING_LIMIT"
    print "$(date -u +%Y-%m-%dT%H:%M:%SZ),WARNING_LIMIT" >> "$run_dir/events.csv"
  fi
  if (( observed >= soft_mib * MIB )); then
    (( soft_count += 1 ))
    if (( soft_count >= soft_samples )); then
      result="SOFT_LIMIT_SUSTAINED"
      stop_tree "$result"
      break
    fi
  else
    soft_count=0
  fi
  sleep 1
done

if [[ "$result" == "RUNNING" ]]; then
  wait "$root_pid" || target_status=$?
  target_status="${target_status:-0}"
  refresh_owned
  if tree_alive; then
    result="ORPHAN_AFTER_ROOT_EXIT"
    stop_tree "$result"
  elif (( allow_clean_exit && target_status == 0 )); then
    result="PASS_TARGET_EXIT"
  else
    result="TARGET_EXIT_$target_status"
  fi
fi

if (( cleanup_failed )); then
  result="${result}_OWNED_TREE_SURVIVED"
fi

print "result=$result" >> "$run_dir/manifest.txt"
print "$(date -u +%Y-%m-%dT%H:%M:%SZ),$result" >> "$run_dir/events.csv"
cleanup_done=1
rm -f "$pid_file"
rm -f "$lock_dir/owner.pid" "$lock_dir/owner.start"
rmdir "$lock_dir" 2>/dev/null || true
print "GUARD_RESULT=$result RUN_DIR=$run_dir"
[[ "$result" == "PASS_DURATION" || "$result" == "PASS_TARGET_EXIT" ]]
