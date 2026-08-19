using Trdng.Core.Instruments;

namespace Trdng.Core.Orders;

public enum DryRunCapabilityMode { Denied, Simulation, Available }

public static class DryRunCapabilityPolicy
{
    public static DryRunCapabilityMode Evaluate(VenueInstrumentCapability? capability) =>
        capability?.Trading switch
        {
            CapabilityAvailability.Available => DryRunCapabilityMode.Available,
            CapabilityAvailability.NotImplemented => DryRunCapabilityMode.Simulation,
            _ => DryRunCapabilityMode.Denied
        };
}
