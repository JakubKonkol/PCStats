using System.Threading;
using System.Windows;
using System.Windows.Threading;
using PCStats.Services;
using PCStats.ViewModels;
using PCStats.Views;

namespace PCStats;

public partial class App : Application, IShell
{
    private Mutex? _singleInstance;
    private SettingsStore? _store;
    private HardwareMonitorService? _monitor;
    private MainViewModel? _mainViewModel;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private TrayIconService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --multi is a development escape hatch for running a test build next to the installed one.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\PCStats.SingleInstance", out var createdNew);
        if (!createdNew && !e.Args.Contains("--multi"))
        {
            // Another copy is already running; just bring it up via the tray instead of a second widget.
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        Logger.Info($"PCStats {typeof(App).Assembly.GetName().Version} starting");

        _store = SettingsStore.Load();
        _monitor = new HardwareMonitorService();
        _mainViewModel = new MainViewModel(_monitor, _store, this, Dispatcher);
        _mainWindow = new MainWindow(_mainViewModel, _store);
        _tray = new TrayIconService(this, _store);

        _mainWindow.Show();
        _ = InitializeViewModelAsync(_mainViewModel, openSettings: e.Args.Contains("--settings"));
    }

    private async Task InitializeViewModelAsync(MainViewModel viewModel, bool openSettings)
    {
        try
        {
            await viewModel.InitializeAsync();
            if (openSettings)
                ShowSettings();
        }
        catch (Exception ex)
        {
            Logger.Error("View model initialisation failed", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settingsWindow?.Close();
        _tray?.Dispose();
        _store?.SaveNow();
        _monitor?.Dispose();
        _singleInstance?.Dispose();
        Logger.Info("PCStats stopped");
        base.OnExit(e);
    }

    // ---------------------------------------------------------------- IShell

    public void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _mainViewModel!.SetSettingsOpen(true);
        var vm = new SettingsViewModel(_store!, _monitor!, Dispatcher);
        _settingsWindow = new SettingsWindow(vm);
        _settingsWindow.Closed += (_, _) =>
        {
            vm.Dispose();
            _settingsWindow = null;
            _mainViewModel.SetSettingsOpen(false);
        };
        _settingsWindow.Show();
    }

    public void HideToTray() => _mainWindow?.Hide();

    public void ShowWidget()
    {
        if (_mainWindow is null)
            return;
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    public bool IsWidgetVisible => _mainWindow?.IsVisible == true;

    public void Quit() => Shutdown();

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(
            $"Wystąpił nieoczekiwany błąd:\n\n{e.Exception.Message}\n\nSzczegóły zapisano w {Logger.Directory}\\log.txt",
            "PC Stats", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
