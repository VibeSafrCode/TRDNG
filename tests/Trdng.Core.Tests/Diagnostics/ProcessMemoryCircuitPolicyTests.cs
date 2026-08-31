using Trdng.Core.Diagnostics;

namespace Trdng.Core.Tests.Diagnostics;

public sealed class ProcessMemoryCircuitPolicyTests
{
    [Fact]
    public void SoftLimitRequiresThreeConsecutiveSamples()
    {
        var policy = new ProcessMemoryCircuitPolicy();
        var sample = new ProcessMemorySample(
            ProcessMemoryCircuitPolicy.SoftLimitBytes,
            1);

        Assert.Equal(ProcessMemoryCircuitAction.Warning, policy.Observe(sample));
        Assert.Equal(ProcessMemoryCircuitAction.Continue, policy.Observe(sample));
        Assert.Equal(ProcessMemoryCircuitAction.SoftStop, policy.Observe(sample));
    }

    [Fact]
    public void HealthySampleResetsSoftSequence()
    {
        var policy = new ProcessMemoryCircuitPolicy();
        var soft = new ProcessMemorySample(ProcessMemoryCircuitPolicy.SoftLimitBytes, 1);
        var healthy = new ProcessMemorySample(ProcessMemoryCircuitPolicy.WorkingTargetBytes, 1);

        Assert.Equal(ProcessMemoryCircuitAction.Warning, policy.Observe(soft));
        Assert.Equal(ProcessMemoryCircuitAction.Continue, policy.Observe(soft));
        Assert.Equal(ProcessMemoryCircuitAction.Continue, policy.Observe(healthy));
        Assert.Equal(ProcessMemoryCircuitAction.Continue, policy.Observe(soft));
        Assert.Equal(ProcessMemoryCircuitAction.Continue, policy.Observe(soft));
        Assert.Equal(ProcessMemoryCircuitAction.SoftStop, policy.Observe(soft));
    }

    [Fact]
    public void HardLimitTripsImmediatelyAndCannotBeReset()
    {
        var policy = new ProcessMemoryCircuitPolicy();

        Assert.Equal(ProcessMemoryCircuitAction.HardStop, policy.Observe(new(
            ProcessMemoryCircuitPolicy.HardLimitBytes, 1)));
        Assert.Equal(ProcessMemoryCircuitAction.HardStop, policy.Observe(new(1, 1)));
    }

    [Fact]
    public void ImmutableLimitsStayBelowAbsoluteCeiling()
    {
        Assert.True(ProcessMemoryCircuitPolicy.WorkingTargetBytes <
                    ProcessMemoryCircuitPolicy.WarningLimitBytes);
        Assert.True(ProcessMemoryCircuitPolicy.WarningLimitBytes <
                    ProcessMemoryCircuitPolicy.SoftLimitBytes);
        Assert.True(ProcessMemoryCircuitPolicy.SoftLimitBytes <
                    ProcessMemoryCircuitPolicy.HardLimitBytes);
        Assert.True(ProcessMemoryCircuitPolicy.HardLimitBytes <
                    ProcessMemoryCircuitPolicy.AbsoluteTreeLimitBytes);
    }
}
