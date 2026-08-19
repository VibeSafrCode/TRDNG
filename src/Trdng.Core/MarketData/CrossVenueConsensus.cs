namespace Trdng.Core.MarketData;

public enum MarketConsensus
{
    NoConsensus,
    Mixed,
    Bullish,
    Bearish
}

public readonly record struct VenueLiquiditySignal(
    string Venue,
    LiquiditySide Side,
    LiquidityBehavior Behavior,
    decimal Quantity);

public readonly record struct CrossVenueVerdict(
    MarketConsensus Consensus,
    decimal NetScore,
    decimal GrossWeight,
    int VenueCount);

public static class CrossVenueConsensus
{
    public static CrossVenueVerdict Evaluate(
        IEnumerable<VenueLiquiditySignal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        decimal net = 0;
        decimal gross = 0;
        var venues = new HashSet<string>(StringComparer.Ordinal);

        foreach (var signal in signals)
        {
            if (signal.Quantity <= 0 ||
                signal.Behavior == LiquidityBehavior.Normal)
            {
                continue;
            }

            var behaviorWeight = signal.Behavior switch
            {
                LiquidityBehavior.Building => 1m,
                LiquidityBehavior.Holding => 0.45m,
                LiquidityBehavior.Pulling => -1m,
                LiquidityBehavior.Absorbing => 1.2m,
                _ => 0m
            };
            var sideDirection = signal.Side == LiquiditySide.Bid ? 1m : -1m;
            var weight = signal.Quantity * decimal.Abs(behaviorWeight);

            net += signal.Quantity * behaviorWeight * sideDirection;
            gross += weight;
            venues.Add(signal.Venue);
        }

        if (gross == 0 || venues.Count == 0)
        {
            return new CrossVenueVerdict(
                MarketConsensus.NoConsensus,
                0,
                0,
                venues.Count);
        }

        var normalized = net / gross;
        var consensus = decimal.Abs(normalized) < 0.25m
            ? MarketConsensus.Mixed
            : normalized > 0
                ? MarketConsensus.Bullish
                : MarketConsensus.Bearish;

        return new CrossVenueVerdict(consensus, net, gross, venues.Count);
    }
}
