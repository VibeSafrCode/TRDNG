using CommunityToolkit.Mvvm.ComponentModel;

namespace Trdng.Desktop.ViewModels;

internal readonly record struct BookLevelPresentation(
    string Price,
    string Quantity,
    double VisualWidth,
    string BarColor,
    string QuantityColor,
    string SignificanceMarker,
    string Behavior,
    string BehaviorColor,
    string BehaviorBackground,
    double RowHeight,
    double TextSize,
    double BehaviorTextSize,
    double RowOpacity);

public sealed partial class BookLevelViewModel : ObservableObject
{
    internal BookLevelViewModel(BookLevelPresentation presentation) =>
        Apply(presentation);

    [ObservableProperty] public partial string Price { get; set; } = string.Empty;
    [ObservableProperty] public partial string Quantity { get; set; } = string.Empty;
    [ObservableProperty] public partial double VisualWidth { get; set; }
    [ObservableProperty] public partial string BarColor { get; set; } = "#00000000";
    [ObservableProperty] public partial string QuantityColor { get; set; } = "#F7FAFF";
    [ObservableProperty] public partial string SignificanceMarker { get; set; } = string.Empty;
    [ObservableProperty] public partial string Behavior { get; set; } = string.Empty;
    [ObservableProperty] public partial string BehaviorColor { get; set; } = "#8B93A1";
    [ObservableProperty] public partial string BehaviorBackground { get; set; } = "#00000000";
    [ObservableProperty] public partial double RowHeight { get; set; }
    [ObservableProperty] public partial double TextSize { get; set; }
    [ObservableProperty] public partial double BehaviorTextSize { get; set; }
    [ObservableProperty] public partial double RowOpacity { get; set; }

    internal void Apply(BookLevelPresentation value)
    {
        Price = value.Price;
        Quantity = value.Quantity;
        VisualWidth = value.VisualWidth;
        BarColor = value.BarColor;
        QuantityColor = value.QuantityColor;
        SignificanceMarker = value.SignificanceMarker;
        Behavior = value.Behavior;
        BehaviorColor = value.BehaviorColor;
        BehaviorBackground = value.BehaviorBackground;
        RowHeight = value.RowHeight;
        TextSize = value.TextSize;
        BehaviorTextSize = value.BehaviorTextSize;
        RowOpacity = value.RowOpacity;
    }
}
