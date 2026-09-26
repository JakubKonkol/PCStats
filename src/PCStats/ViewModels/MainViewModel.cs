using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCStats.Models;
using PCStats.Services;

namespace PCStats.ViewModels;

/// <summary>Abstraction over windows the main view model needs to open, keeps the VM free of view types.</summary>
public interface IShell
{
    void ShowSettings();
    void HideToTray();
    void Quit();

    /// <summary>Starts a fresh copy of the app and exits this one, e.g. after installing the sensor driver.</summary>
    void Restart();
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IHardwareMonitor _monitor;
    private readonly SettingsStore _store;
    private readonly IShell _shell;
    private readonly Dispatcher _dispatcher;
    private HardwareSnapshot _latest = HardwareSnapshot.Empty;

    public MainViewModel(IHardwareMonitor monitor, SettingsStore store, IShell shell, Dispatcher dispatcher)
    {
        _monitor = monitor;
        _store = store;
        _shell = shell;
        _dispatcher = dispatcher;

        _store.Changed += OnSettingsChanged;
        ApplyAppearance(_store.Current);
    }

    public ObservableCollection<MetricTileViewModel> Tiles { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; private set; } = true;

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "Detecting hardware…";

    [ObservableProperty]
    public partial string? HintText { get; private set; }

    [ObservableProperty]
    public partial int Columns { get; private set; } = 2;

    [ObservableProperty]
    public partial bool IsCompact { get; private set; }

    [ObservableProperty]
    public partial double Scale { get; private set; } = 1.0;

    [ObservableProperty]
    public partial Brush WindowBackground { get; private set; } = Brushes.Black;

    [ObservableProperty]
    public partial bool AlwaysOnTop { get; private set; } = true;

    [ObservableProperty]
    public partial bool ClickThrough { get; private set; }

    [ObservableProperty]
    public partial bool ShowHeader { get; private set; } = true;

    [ObservableProperty]
    public partial bool ShowSparklines { get; private set; } = true;

    /// <summary>Replaces the loading text, for work the shell does before hardware detection starts.</summary>
    public void SetStatus(string text) => StatusText = text;

    public async Task InitializeAsync()
    {
        try
        {
            _monitor.SetUpdateInterval(TimeSpan.FromMilliseconds(_store.Current.UpdateIntervalMs));
            await _monitor.InitializeAsync();
        }
        catch (Exception ex)
        {
            StatusText = "Could not start the hardware monitor.";
            HintText = ex.Message;
            return;
        }

        if (_store.Current.Metrics.Count == 0)
            SeedDefaultSelection();

        RebuildTiles();
        _monitor.SnapshotUpdated += OnSnapshot;
        _latest = _monitor.Latest;
        ApplySnapshot(_latest);

        IsLoading = false;
        HintText = _monitor.IsCpuDriverAvailable
            ? null
            : "PawnIO driver missing – CPU temperature and clocks are unavailable. Install it under Settings → Behaviour.";
    }

    [RelayCommand]
    private void OpenSettings() => _shell.ShowSettings();

    [RelayCommand]
    private void HideToTray() => _shell.HideToTray();

    [RelayCommand]
    private void Exit() => _shell.Quit();

    [RelayCommand]
    private void ToggleAlwaysOnTop() => _store.Update(s => s.AlwaysOnTop = !s.AlwaysOnTop);

    [RelayCommand]
    private void ToggleClickThrough() => _store.Update(s => s.ClickThrough = !s.ClickThrough);

    [RelayCommand]
    private void ToggleLayout() =>
        _store.Update(s => s.Layout = s.Layout == LayoutMode.Tiles ? LayoutMode.Compact : LayoutMode.Tiles);

    // ------------------------------------------------------------------ data flow

    private void OnSnapshot(object? sender, HardwareSnapshot snapshot)
    {
        _latest = snapshot;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(HardwareSnapshot snapshot)
    {
        if (!ReferenceEquals(snapshot, _latest))
            return; // a newer snapshot is already queued; skip stale work

        var settings = _store.Current;
        foreach (var tile in Tiles)
            tile.Apply(snapshot, settings);
    }

    private void OnSettingsChanged(object? sender, SettingsChange change)
    {
        var settings = _store.Current;
        ApplyAppearance(settings);

        switch (change)
        {
            case SettingsChange.Metrics:
                RebuildTiles();
                ApplySnapshot(_latest);
                break;
            case SettingsChange.Behaviour:
                _monitor.SetUpdateInterval(TimeSpan.FromMilliseconds(settings.UpdateIntervalMs));
                ApplySnapshot(_latest);
                break;
            case SettingsChange.Appearance:
                ApplySnapshot(_latest); // bar visibility depends on ShowBars
                break;
        }
    }

    private void ApplyAppearance(AppSettings s)
    {
        Columns = s.Columns;
        IsCompact = s.Layout == LayoutMode.Compact;
        Scale = s.Scale;
        AlwaysOnTop = s.AlwaysOnTop;
        ClickThrough = s.ClickThrough;
        ShowHeader = s.ShowHeader;
        ShowSparklines = s.ShowSparklines && !IsCompact;

        var alpha = (byte)Math.Round(Math.Clamp(s.BackgroundOpacity, 0, 1) * 255);
        var background = new SolidColorBrush(Color.FromArgb(alpha, 0x0F, 0x11, 0x15));
        background.Freeze();
        WindowBackground = background;

        ApplyAccent(s.AccentColor);
    }

    private static void ApplyAccent(string hex)
    {
        Color color;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            color = Color.FromRgb(0x3B, 0x82, 0xF6);
        }

        var resources = Application.Current.Resources;
        resources["AccentColor"] = color;
        resources["AccentBrush"] = Freeze(new SolidColorBrush(color));
        resources["AccentSoftBrush"] = Freeze(new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B)));
        resources["AccentFaintBrush"] = Freeze(new SolidColorBrush(Color.FromArgb(0x14, color.R, color.G, color.B)));
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private void SeedDefaultSelection()
    {
        var defaults = _monitor.Metrics
            .Where(m => m.RecommendedOrder >= 0)
            .OrderBy(m => m.RecommendedOrder)
            .Select(m => new MetricSelection { Id = m.Id })
            .ToList();
        _store.UpdateSilently(s => s.Metrics = defaults);
    }

    private void RebuildTiles()
    {
        var byId = _monitor.Metrics.ToDictionary(m => m.Id);
        Tiles.Clear();
        foreach (var selection in _store.Current.Metrics)
        {
            if (byId.TryGetValue(selection.Id, out var descriptor))
                Tiles.Add(new MetricTileViewModel(descriptor, selection.CustomLabel));
        }

        StatusText = Tiles.Count == 0 ? "No metrics selected – click ⚙ to pick some." : string.Empty;
        if (!_settingsOpen)
            _monitor.SetActiveMetrics(Tiles.Select(t => t.Id).ToList());
    }

    private bool _settingsOpen;

    /// <summary>While the settings window previews every sensor, polling must cover all hardware.</summary>
    public void SetSettingsOpen(bool open)
    {
        _settingsOpen = open;
        _monitor.SetActiveMetrics(open ? null : Tiles.Select(t => t.Id).ToList());
    }
}
