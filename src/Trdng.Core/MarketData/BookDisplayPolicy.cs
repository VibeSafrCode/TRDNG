namespace Trdng.Core.MarketData;

public sealed record BookBarPalette(
    string Ask,
    string LargestAsk,
    string Bid,
    string LargestBid)
{
    public static BookBarPalette OwnerDefault { get; } = new(
        "#B3FFD60A",
        "#D9FF453A",
        "#B30A84FF",
        "#D930D158");

    public static bool TryCreate(
        string ask,
        string largestAsk,
        string bid,
        string largestBid,
        out BookBarPalette palette)
    {
        palette = OwnerDefault;
        if (!IsColor(ask) || !IsColor(largestAsk) ||
            !IsColor(bid) || !IsColor(largestBid))
            return false;
        palette = new(ask.ToUpperInvariant(), largestAsk.ToUpperInvariant(),
            bid.ToUpperInvariant(), largestBid.ToUpperInvariant());
        return true;
    }

    private static bool IsColor(string value)
    {
        if (value is null || value.Length is not (7 or 9) || value[0] != '#')
            return false;
        return value.AsSpan(1).ToString().All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
    }
}

public readonly record struct BookDisplayLayout(
    int Depth,
    double RowHeight,
    double BarWidth);

public static class BookDisplayPolicy
{
    public const int MinimumDepth = 8;
    public const int MaximumDepthPerSide = 200;
    public const decimal MaximumManualVolumeReference = 1_000_000_000_000m;
    public const double TargetRowHeight = 18;
    public const double HorizontalContentMargin = 20;
    public const double SpreadSafetyMarginPerSide = 12;

    public static BookDisplayLayout Resolve(
        bool automaticDepth,
        int manualDepth,
        int maximumDepth,
        double viewportWidth,
        double halfViewportHeight)
    {
        if (maximumDepth is < MinimumDepth or > MaximumDepthPerSide)
            throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        if (manualDepth < MinimumDepth || manualDepth > maximumDepth)
            throw new ArgumentOutOfRangeException(nameof(manualDepth));
        if (!double.IsFinite(viewportWidth) || viewportWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (!double.IsFinite(halfViewportHeight) || halfViewportHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(halfViewportHeight));

        var automatic = (int)Math.Floor(halfViewportHeight / TargetRowHeight);
        var depth = automaticDepth
            ? Math.Clamp(automatic, MinimumDepth, maximumDepth)
            : manualDepth;
        var rowHeight = halfViewportHeight <= 0
            ? TargetRowHeight
            : halfViewportHeight / depth;
        return new(
            depth,
            rowHeight,
            Math.Max(0, viewportWidth - HorizontalContentMargin));
    }

    public static int AdjustDepth(int currentDepth, int direction, int step,
        int maximumDepth)
    {
        if (currentDepth < MinimumDepth || currentDepth > maximumDepth)
            throw new ArgumentOutOfRangeException(nameof(currentDepth));
        if (direction is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (step is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(step));
        return Math.Clamp(currentDepth + direction * step, MinimumDepth, maximumDepth);
    }
}

public static class VisibleBookVolumeScale
{
    public readonly record struct SideReferences(
        decimal AskLargest,
        decimal BidLargest,
        decimal AskReference,
        decimal BidReference);

    public static SideReferences ResolveSides(
        IEnumerable<decimal> askQuantities,
        IEnumerable<decimal> bidQuantities,
        bool automatic,
        decimal manual)
    {
        var askLargest = Largest(askQuantities);
        var bidLargest = Largest(bidQuantities);
        return new(
            askLargest,
            bidLargest,
            Reference(askLargest, automatic, manual),
            Reference(bidLargest, automatic, manual));
    }

    public static decimal Largest(IEnumerable<decimal> quantities)
    {
        ArgumentNullException.ThrowIfNull(quantities);
        var maximum = 0m;
        foreach (var quantity in quantities)
        {
            if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantities));
            maximum = decimal.Max(maximum, quantity);
        }
        return maximum;
    }

    public static decimal Reference(decimal largestVisible, bool automatic, decimal manual)
    {
        if (largestVisible < 0) throw new ArgumentOutOfRangeException(nameof(largestVisible));
        if (!automatic && manual <= 0)
            throw new ArgumentOutOfRangeException(nameof(manual));
        return automatic ? largestVisible : manual;
    }

    public static double Ratio(decimal quantity, decimal reference)
    {
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (reference <= 0) return 0;
        return Math.Clamp((double)(quantity / reference), 0, 1);
    }
}
