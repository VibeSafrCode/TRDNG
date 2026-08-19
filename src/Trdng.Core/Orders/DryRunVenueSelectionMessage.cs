using Trdng.Core.Instruments;

namespace Trdng.Core.Orders;

public static class DryRunVenueSelectionMessage
{
    public static string Rejected(TradingVenue requested, TradingVenue active) =>
        $"{requested.ToString().ToUpperInvariant()} ОТКЛОНЕНА · АКТИВНА " +
        active.ToString().ToUpperInvariant();
}
