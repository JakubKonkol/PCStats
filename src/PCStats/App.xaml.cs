using System.Diagnostics;
using System.Globalization;
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

        // The UI is English, so format numbers the English way (4.65 GHz) whatever the Windows region is.
        var culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

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
            await OfferPawnIoInstallAsync(viewModel);
            await viewModel.InitializeAsync();
            if (openSettings)
                ShowSettings();
        }
        catch (Exception ex)
        {
            Logger.Error("View model initialisation failed", ex);
        }
    }

    /// <summary>
    /// First-run offer to install the bundled sensor driver. Runs before the hardware monitor opens,
    /// so a fresh install is picked up without a restart.
    /// </summary>
    private async Task OfferPawnIoInstallAsync(MainViewModel viewModel)
    {
        if (PawnIoInstaller.IsInstalled || _store!.Current.PawnIoOfferDeclined)
            return;

        // Owned by the topmost widget so the question cannot end up hidden behind other windows.
        var answer = MessageBox.Show(_mainWindow!,
            "CPU temperature, clock and voltage readings need the PawnIO driver (signed, open source – pawnio.eu).\n\n" +
            "Its installer is built into PC Stats. Install it now?\n\n" +
            "Everything else works without it, and you can install it later under Settings → Behaviour.",
            "PC Stats", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            _store.UpdateSilently(s => s.PawnIoOfferDeclined = true);
            return;
        }

        viewModel.SetStatus("Installing the PawnIO driver…");
        if (!await PawnIoInstaller.InstallAsync())
        {
            MessageBox.Show(_mainWindow!,
                $"PawnIO could not be installed. Details were written to {Logger.Directory}\\log.txt",
                "PC Stats", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        viewModel.SetStatus("Detecting hardware…");
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
        var vm = new SettingsViewModel(_store!, _monitor!, Dispatcher, this);
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

    public void Restart()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            Logger.Warn("Cannot restart: process path unknown");
            return;
        }

        _store?.SaveNow();

        // Free the single-instance lock first, otherwise the new copy finds this one and exits.
        try
        {
            _singleInstance?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned: this copy was started with --multi next to another one.
        }
        _singleInstance?.Dispose();
        _singleInstance = null;

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Logger.Error("Restart failed", ex);
            return;
        }

        Shutdown();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nDetails were written to {Logger.Directory}\\log.txt",
            "PC Stats", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
