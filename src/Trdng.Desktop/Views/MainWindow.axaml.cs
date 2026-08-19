using Avalonia.Controls;
using Avalonia.Input;
using Trdng.Desktop.ViewModels;
using Trdng.Core.Instruments;
using Trdng.Core.Orders;

namespace Trdng.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _pinchAdjusted;

    public MainWindow()
    {
        InitializeComponent();
        OrderBookSurface.AddHandler(InputElement.PinchEvent, OnPinch);
        OrderBookSurface.AddHandler(InputElement.PinchEndedEvent, OnPinchEnded);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void MoreDepth_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel?.ShowMoreDepth();

    private void LessDepth_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel?.ShowLessDepth();

    private async void SelectApt_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel) await viewModel.SelectAssetAsync("APT");
    }

    private async void SelectBtc_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel) await viewModel.SelectAssetAsync("BTC");
    }

    private async void SelectSpot_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel) await viewModel.SelectProductAsync(MarketProduct.Spot);
    }

    private async void SelectFutures_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel) await viewModel.SelectProductAsync(MarketProduct.Perpetual);
    }

    private void SelectDryRunMexc_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel?.SelectDryRunVenue(TradingVenue.Mexc);

    private void SelectDryRunGate_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel?.SelectDryRunVenue(TradingVenue.Gate);

    private void SelectDryRunBybit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel?.SelectDryRunVenue(TradingVenue.Bybit);

    private void SelectDryRunBuy_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel?.SelectDryRunSide(OrderSide.Buy);

    private void SelectDryRunSell_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel?.SelectDryRunSide(OrderSide.Sell);

    private void EngageDryRunStop_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel?.EngageDryRunStop();

    private void DisengageDryRunStop_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel?.DisengageDryRunStop();

    private void PrepareDryRunSimulation_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel?.PrepareDryRunSimulation();

    private void ConfirmDryRunSimulation_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel?.ConfirmDryRunSimulation();

    private void PlayDryRunSimulation_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel?.PlayDryRunSimulation();

    private void ArmMexcCredentialRevoke_Click(object? sender,
        Avalonia.Interactivity.RoutedEventArgs e) => ViewModel?.ArmMexcCredentialRevoke();

    private async void ConfirmMexcCredentialRevoke_Click(object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            await viewModel.ConfirmMexcCredentialRevokeAsync();
    }

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        if (_pinchAdjusted)
        {
            return;
        }

        if (e.Scale >= 1.08)
        {
            ViewModel?.ShowLessDepth();
            _pinchAdjusted = true;
            e.Handled = true;
        }
        else if (e.Scale <= 0.92)
        {
            ViewModel?.ShowMoreDepth();
            _pinchAdjusted = true;
            e.Handled = true;
        }
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e) =>
        _pinchAdjusted = false;
}
