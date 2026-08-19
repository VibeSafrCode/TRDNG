namespace Trdng.Core.MarketData;

public static class InstrumentTickSize
{
    public static decimal? Resolve(decimal? first, decimal? second)
    {
        var valid = new[] { first, second }
            .Where(static tick => tick is > 0)
            .Select(static tick => tick!.Value)
            .Distinct()
            .Order()
            .ToArray();
        if (valid.Length == 0)
        {
            return null;
        }

        var smallest = valid[0];
        return valid.All(tick => tick % smallest == 0)
            ? smallest
            : null;
    }
}
