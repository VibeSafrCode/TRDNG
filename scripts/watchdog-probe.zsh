#!/bin/zsh
set -eu

mode="${1:-term}"
mib="${2:-80}"
[[ "$mode" == "term" || "$mode" == "ignore-term" || "$mode" == "orphan" ]] || exit 64
[[ "$mib" == <-> && "$mib" -ge 1 && "$mib" -le 192 ]] || exit 64

if [[ "$mode" == "orphan" ]]; then
  /usr/bin/perl -e '
    my ($mib) = @ARGV;
    $SIG{TERM} = "IGNORE";
    my $blob = "x" x ($mib * 1024 * 1024);
    sleep 300;
  ' "$mib" &!
  sleep 1
  exit 0
fi

if [[ "$mode" == "ignore-term" ]]; then
  trap '' TERM
fi

/usr/bin/perl -e '
  my ($mode, $mib) = @ARGV;
  $SIG{TERM} = "IGNORE" if $mode eq "ignore-term";
  my $bytes = $mib * 1024 * 1024;
  my $blob = "x" x $bytes;
  print length($blob), "\n";
  sleep 300;
' "$mode" "$mib" &
child_pid=$!
wait "$child_pid"
