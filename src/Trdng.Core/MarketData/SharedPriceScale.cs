namespace Trdng.Core.MarketData;

public sealed record SharedPriceScale(
    decimal TickSize,
    decimal ReferencePrice,
    IReadOnlyList<decimal> Asks,
    IReadOnlyList<decimal> Bids)
{
    public static SharedPriceScale? Build(
        OrderBookSnapshot? first,
        OrderBookSnapshot? second,
        int depth,
        decimal? preferredTickSize = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);

        var books = new[] { first, second }
            .Where(static book =>
                book?.BestBid is not null && book.BestAsk is not null)
            .Cast<OrderBookSnapshot>()
            .ToArray();
        if (books.Length == 0)
        {
            return null;
        }

        var reference = books
            .Select(static book =>
                (book.BestBid!.Value + book.BestAsk!.Value) / 2)
            .Average();
        var detectedTick = books
            .Select(DetectTick)
            .Where(static value => value > 0)
            .DefaultIfEmpty(DefaultTick(reference))
            .Min();
        var tick = preferredTickSize is > 0 &&
                   PricesAlignToTick(books, preferredTickSize.Value)
            ? preferredTickSize.Value
            : detectedTick;

        var bidAnchor = decimal.Floor(reference / tick) * tick;
        if (bidAnchor == reference)
        {
            bidAnchor -= tick;
        }

        var asks = Enumerable.Range(1, depth)
            .Select(index => bidAnchor + (tick * index))
            .ToArray();
        var bids = Enumerable.Range(0, depth)
            .Select(index => bidAnchor - (tick * index))
            .ToArray();

        return new SharedPriceScale(tick, reference, asks, bids);
    }

    private static bool PricesAlignToTick(
        IEnumerable<OrderBookSnapshot> books,
        decimal tick) =>
        books
            .SelectMany(static book => book.Bids.Concat(book.Asks))
            .All(level => level.Price % tick == 0);

    private static decimal DetectTick(OrderBookSnapshot book)
    {
        var prices = book.Bids
            .Concat(book.Asks)
            .Select(static level => level.Price)
            .Distinct()
            .Order()
            .ToArray();
        decimal? minimum = null;
        for (var index = 1; index < prices.Length; index++)
        {
            var difference = prices[index] - prices[index - 1];
            if (difference > 0 && (minimum is null || difference < minimum))
            {
                minimum = difference;
            }
        }

        return minimum ?? 0;
    }

    private static decimal DefaultTick(decimal reference) =>
        reference < 1 ? 0.0001m :
        reference < 10 ? 0.001m :
        reference < 100 ? 0.01m :
        0.1m;
}
