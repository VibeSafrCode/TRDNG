using Avalonia.Controls;
using Avalonia.Input;
using Trdng.Desktop.ViewModels;
using Trdng.Core.Instruments;
using Trdng.Core.Orders;

namespace Trdng.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void SelectApt_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel) await viewModel.SelectAssetAsync("APT");
    }

    private async void SelectBtc_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel) await viewModel.SelectAssetAsync("BTC");
    }

    private async void SelectCatalogInstrument_Click(object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string pairId } && ViewModel is { } viewModel)
            await viewModel.SelectInstrumentAsync(pairId);
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

    private async void SaveReadOnlyCredentials_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    { if (ViewModel is { } vm) await vm.SaveReadOnlyCredentialsAsync(vm.ReadOnlyReplaceConfirmed); }
    private async void SaveOrderTestCredentials_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    { if (ViewModel is { } vm) await vm.SaveOrderTestCredentialsAsync(vm.OrderTestReplaceConfirmed); }
    private async void ArmReadOnlyCredentialRevoke_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    { if (ViewModel is { } vm) await vm.ArmCredentialRevokeAsync(true); }
    private async void ConfirmReadOnlyCredentialRevoke_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    { if (ViewModel is { } vm) await vm.ConfirmCredentialRevokeAsync(true); }
    private async void ArmOrderTestCredentialRevoke_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    { if (ViewModel is { } vm) await vm.ArmCredentialRevokeAsync(false); }
    private async void ConfirmOrderTestCredentialRevoke_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    { if (ViewModel is { } vm) await vm.ConfirmCredentialRevokeAsync(false); }

    private void BookViewport_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is Control { Tag: string venueName } &&
            Enum.TryParse<TradingVenue>(venueName, true, out var venue))
            ViewModel?.UpdateBookViewport(venue, e.NewSize.Width, e.NewSize.Height);
    }

    private void BookViewport_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0 || sender is not Control { Tag: string venueName } ||
            !Enum.TryParse<TradingVenue>(venueName, true, out var venue)) return;
        ViewModel?.AdjustBookDepth(venue, e.Delta.Y > 0 ? -1 : 1);
        e.Handled = true;
    }

    private void ResetBookPalette_Click(object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { Tag: TradingVenue venue })
            ViewModel?.ResetBookPalette(venue);
        else if (sender is Control { Tag: string venueName } &&
                 Enum.TryParse<TradingVenue>(venueName, true, out venue))
            ViewModel?.ResetBookPalette(venue);
    }

    private void OpenBookSettings_Click(object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string venueName } ||
            !Enum.TryParse<TradingVenue>(venueName, true, out var venue) ||
            ViewModel is not { } viewModel) return;

        BookSettingsContent.Content = venue switch
        {
            TradingVenue.Mexc => viewModel.MexcBookSettings,
            TradingVenue.Gate => viewModel.GateBookSettings,
            TradingVenue.Bybit => viewModel.BybitBookSettings,
            _ => null
        };
        if (BookSettingsContent.Content is null) return;
        BookSettingsBackdrop.IsVisible = true;
        BookSettingsOverlay.IsVisible = true;
    }

    private void CloseBookSettings_Click(object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        BookSettingsOverlay.IsVisible = false;
        BookSettingsBackdrop.IsVisible = false;
        BookSettingsContent.Content = null;
    }

}
