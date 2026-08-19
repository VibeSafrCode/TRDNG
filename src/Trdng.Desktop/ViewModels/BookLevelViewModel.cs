namespace Trdng.Desktop.ViewModels;

public sealed record BookLevelViewModel(
    string Price,
    string Quantity,
    double VisualWidth,
    string QuantityColor,
    string SignificanceMarker,
    string Behavior,
    string BehaviorColor,
    string BehaviorBackground,
    double RowHeight,
    double TextSize,
    double BehaviorTextSize,
    double RowOpacity);
