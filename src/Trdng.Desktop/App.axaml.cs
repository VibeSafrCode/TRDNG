using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Trdng.Core.Diagnostics;
using Trdng.Desktop.Diagnostics;
using Trdng.Desktop.ViewModels;
using Trdng.Desktop.Views;

namespace Trdng.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            var memoryGuard = new RuntimeProcessMemoryGuard();
            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            desktop.MainWindow = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            memoryGuard.Tripped += action =>
            {
                if (action == ProcessMemoryCircuitAction.Warning)
                {
                    Console.Error.WriteLine("MEMORY_GUARD_WARNING");
                    viewModel.ApplyMemoryWarning();
                    return;
                }
                Console.Error.WriteLine(action == ProcessMemoryCircuitAction.HardStop
                    ? "MEMORY_GUARD_HARD_STOP" : "MEMORY_GUARD_SOFT_STOP");
                if (action == ProcessMemoryCircuitAction.HardStop)
                {
                    Environment.Exit(79);
                }

                _ = Task.Run(async () =>
                {
                    var stop = viewModel.EmergencyStopPublicDataAsync();
                    await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(2)))
                        .ConfigureAwait(false);
                    Environment.Exit(78);
                });
            };
            memoryGuard.Start();
            var shutdownStarted = 0;
            var shutdownComplete = 0;
            async Task CompleteShutdownAsync()
            {
                if (Interlocked.Exchange(ref shutdownStarted, 1) != 0) return;
                memoryGuard.Dispose();
                try { await viewModel.DisposeAsync(); }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"SHUTDOWN_CLEANUP_FAILED:{exception.GetType().Name}");
                }
                finally
                {
                    Volatile.Write(ref shutdownComplete, 1);
                    await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown());
                }
            }
            mainWindow.Closing += (_, eventArgs) =>
            {
                if (Volatile.Read(ref shutdownComplete) != 0) return;
                eventArgs.Cancel = true;
                _ = CompleteShutdownAsync();
            };
            desktop.ShutdownRequested += (_, eventArgs) =>
            {
                if (Volatile.Read(ref shutdownComplete) != 0) return;
                eventArgs.Cancel = true;
                _ = CompleteShutdownAsync();
            };
            desktop.Exit += (_, _) => memoryGuard.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
