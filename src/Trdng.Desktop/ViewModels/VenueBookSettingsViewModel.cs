using CommunityToolkit.Mvvm.ComponentModel;
using Trdng.Core.Instruments;
using Trdng.Core.MarketData;

namespace Trdng.Desktop.ViewModels;

public partial class VenueBookSettingsViewModel : ObservableObject
{
    private double _viewportWidth;
    private double _halfViewportHeight;
    private bool _normalizing;

    public VenueBookSettingsViewModel(TradingVenue venue, int maximumDepth)
    {
        if (maximumDepth is < BookDisplayPolicy.MinimumDepth or >
            BookDisplayPolicy.MaximumDepthPerSide)
            throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        Venue = venue;
        MaximumDepth = maximumDepth;
        var defaults = BookBarPalette.OwnerDefault;
        AskColor = defaults.Ask;
        LargestAskColor = defaults.LargestAsk;
        BidColor = defaults.Bid;
        LargestBidColor = defaults.LargestBid;
    }

    public TradingVenue Venue { get; }
    public int MaximumDepth { get; }
    public string SettingsTitle => $"{Venue.ToString().ToUpperInvariant()} · НАСТРОЙКИ СТАКАНА";
    public event Action? Changed;

    [ObservableProperty]
    public partial bool AutomaticDepth { get; set; } = true;

    [ObservableProperty]
    public partial decimal ManualDepth { get; set; } = 24;

    [ObservableProperty]
    public partial decimal GestureStep { get; set; } = 1;

    [ObservableProperty]
    public partial bool AutomaticVolumeScale { get; set; } = true;

    [ObservableProperty]
    public partial decimal ManualVolumeReference { get; set; } = 10_000;

    [ObservableProperty]
    public partial string AskColor { get; set; }

    [ObservableProperty]
    public partial string LargestAskColor { get; set; }

    [ObservableProperty]
    public partial string BidColor { get; set; }

    [ObservableProperty]
    public partial string LargestBidColor { get; set; }

    [ObservableProperty]
    public partial string PersistenceState { get; set; } = "НАСТРОЙКИ · ПО УМОЛЧАНИЮ";

    public BookDisplayLayout Layout => BookDisplayPolicy.Resolve(
        AutomaticDepth,
        (int)ManualDepth,
        MaximumDepth,
        _viewportWidth,
        _halfViewportHeight);

    public string DepthLabel => AutomaticDepth
        ? $"АВТО · {Layout.Depth} УРОВНЕЙ"
        : $"РУЧНАЯ · {Layout.Depth} УРОВНЕЙ";

    public string PaletteState => BookBarPalette.TryCreate(
        AskColor, LargestAskColor, BidColor, LargestBidColor, out _)
        ? "ЦВЕТА · ГОТОВО"
        : "ЦВЕТА · ОШИБКА HEX · ИСПОЛЬЗУЮТСЯ БАЗОВЫЕ";

    public BookBarPalette Palette => BookBarPalette.TryCreate(
        AskColor, LargestAskColor, BidColor, LargestBidColor, out var palette)
        ? palette
        : BookBarPalette.OwnerDefault;

    public void SetViewport(double width, double halfHeight)
    {
        if (!double.IsFinite(width) || width < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(halfHeight) || halfHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(halfHeight));
        var before = Layout;
        _viewportWidth = width;
        _halfViewportHeight = halfHeight;
        var after = Layout;
        if (before != after) NotifyChanged();
    }

    public void AdjustDepth(int direction)
    {
        var depth = BookDisplayPolicy.AdjustDepth(
            Layout.Depth,
            direction,
            (int)GestureStep,
            MaximumDepth);
        AutomaticDepth = false;
        ManualDepth = depth;
    }

    public void ResetPalette()
    {
        var defaults = BookBarPalette.OwnerDefault;
        _normalizing = true;
        try
        {
            AskColor = defaults.Ask;
            LargestAskColor = defaults.LargestAsk;
            BidColor = defaults.Bid;
            LargestBidColor = defaults.LargestBid;
        }
        finally { _normalizing = false; }
        NotifyChanged();
    }

    public BookDisplaySettingsSnapshot Snapshot() => new(
        Venue,
        AutomaticDepth,
        (int)ManualDepth,
        (int)GestureStep,
        AutomaticVolumeScale,
        ManualVolumeReference,
        AskColor,
        LargestAskColor,
        BidColor,
        LargestBidColor);

    public void Apply(BookDisplaySettingsSnapshot settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Venue != Venue ||
            !BookDisplaySettingsValidation.IsValid(settings))
            throw new InvalidDataException("Book display settings are invalid.");
        _normalizing = true;
        try
        {
            AutomaticDepth = settings.AutomaticDepth;
            ManualDepth = settings.ManualDepth;
            GestureStep = settings.GestureStep;
            AutomaticVolumeScale = settings.AutomaticVolumeScale;
            ManualVolumeReference = settings.ManualVolumeReference;
            AskColor = settings.AskColor;
            LargestAskColor = settings.LargestAskColor;
            BidColor = settings.BidColor;
            LargestBidColor = settings.LargestBidColor;
        }
        finally { _normalizing = false; }
        NotifyChanged();
    }

    partial void OnAutomaticDepthChanged(bool value) => NotifyChanged();
    partial void OnAutomaticVolumeScaleChanged(bool value) => NotifyChanged();

    partial void OnManualDepthChanged(decimal value)
    {
        var normalized = Math.Clamp(
            decimal.Truncate(value), BookDisplayPolicy.MinimumDepth, MaximumDepth);
        if (value != normalized)
        {
            ManualDepth = normalized;
            return;
        }
        NotifyChanged();
    }

    partial void OnGestureStepChanged(decimal value)
    {
        var normalized = Math.Clamp(decimal.Truncate(value), 1, 20);
        if (value != normalized)
        {
            GestureStep = normalized;
            return;
        }
        NotifyChanged();
    }

    partial void OnManualVolumeReferenceChanged(decimal value)
    {
        var normalized = Math.Clamp(value, 0.00000001m,
            BookDisplayPolicy.MaximumManualVolumeReference);
        if (value != normalized)
        {
            ManualVolumeReference = normalized;
            return;
        }
        NotifyChanged();
    }

    partial void OnAskColorChanged(string value) => NotifyChanged();
    partial void OnLargestAskColorChanged(string value) => NotifyChanged();
    partial void OnBidColorChanged(string value) => NotifyChanged();
    partial void OnLargestBidColorChanged(string value) => NotifyChanged();

    private void NotifyChanged()
    {
        if (_normalizing) return;
        OnPropertyChanged(nameof(Layout));
        OnPropertyChanged(nameof(DepthLabel));
        OnPropertyChanged(nameof(PaletteState));
        OnPropertyChanged(nameof(Palette));
        Changed?.Invoke();
    }
}
