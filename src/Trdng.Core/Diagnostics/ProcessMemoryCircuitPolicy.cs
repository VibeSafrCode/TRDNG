namespace Trdng.Core.Diagnostics;

public enum ProcessMemoryCircuitAction
{
    Continue,
    Warning,
    SoftStop,
    HardStop
}

public readonly record struct ProcessMemorySample(
    long WorkingSetBytes,
    long ManagedHeapBytes);

public sealed class ProcessMemoryCircuitPolicy
{
    public const long Mebibyte = 1024L * 1024L;
    public const long WorkingTargetBytes = 512L * Mebibyte;
    public const long WarningLimitBytes = 1_536L * Mebibyte;
    public const long SoftLimitBytes = 2_304L * Mebibyte;
    public const long HardLimitBytes = 3_072L * Mebibyte;
    public const long AbsoluteTreeLimitBytes = 8_192L * Mebibyte;
    public const int RequiredSoftSamples = 3;

    private int _consecutiveSoftSamples;
    private bool _warningIssued;
    private bool _tripped;

    public ProcessMemoryCircuitAction Observe(ProcessMemorySample sample)
    {
        if (sample.WorkingSetBytes < 0 || sample.ManagedHeapBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(sample));
        if (_tripped) return ProcessMemoryCircuitAction.HardStop;

        var observed = Math.Max(sample.WorkingSetBytes, sample.ManagedHeapBytes);
        if (observed >= HardLimitBytes || observed >= AbsoluteTreeLimitBytes)
        {
            _tripped = true;
            return ProcessMemoryCircuitAction.HardStop;
        }

        if (observed >= SoftLimitBytes)
        {
            _consecutiveSoftSamples++;
            if (_consecutiveSoftSamples >= RequiredSoftSamples)
            {
                _tripped = true;
                return ProcessMemoryCircuitAction.SoftStop;
            }
        }
        else
        {
            _consecutiveSoftSamples = 0;
        }

        if (observed >= WarningLimitBytes && !_warningIssued)
        {
            _warningIssued = true;
            return ProcessMemoryCircuitAction.Warning;
        }

        return ProcessMemoryCircuitAction.Continue;
    }
}
