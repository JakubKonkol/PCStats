using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCStats.Models;
using PCStats.Services;

namespace PCStats.ViewModels;

/// <summary>A metric available for selection, with a live preview so the user can tell what it measures.</summary>
public sealed partial class MetricOptionViewModel : ObservableObject
{
    private readonly Action<MetricOptionViewModel, bool> _onToggle;

    public MetricOptionViewModel(MetricDescriptor descriptor, bool isSelected, Action<MetricOptionViewModel, bool> onToggle)
    {
        Descriptor = descriptor;
        _onToggle = onToggle;
        Suppress = true;
        IsSelected = isSelected;
        Suppress = false;
    }

    public MetricDescriptor Descriptor { get; }

    public string Id => Descriptor.Id;

    public string Label => Descriptor.DefaultLabel;

    public string Detail => Descriptor.IsSynthetic ? Descriptor.SensorName : $"{Descriptor.SensorName} · {Descriptor.Kind}";

    public string Group => Descriptor.HardwareName;

    public HardwareCategory Category => Descriptor.Category;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial string LiveValue { get; set; } = "—";

    /// <summary>True while the selection list is being synchronised, suppresses re-entrant updates.</summary>
    public bool Suppress { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!Suppress)
            _onToggle(this, value);
    }
}

public sealed partial class SelectedMetricViewModel : ObservableObject
{
    private readonly Action _onLabelChanged;

    public SelectedMetricViewModel(MetricDescriptor descriptor, string? customLabel, Action onLabelChanged)
    {
        Descriptor = descriptor;
        _onLabelChanged = onLabelChanged;
        CustomLabel = customLabel ?? string.Empty;
    }

    public MetricDescriptor Descriptor { get; }

    public string Id => Descriptor.Id;

    public string DefaultLabel => Descriptor.DefaultLabel;

    [ObservableProperty]
    public partial string CustomLabel { get; set; }

    partial void OnCustomLabelChanged(string value) => _onLabelChanged();
}

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    public static IReadOnlyList<string> AccentPresets { get; } =
    [
        "#3B82F6", "#22D3EE", "#10B981", "#A3E635", "#F59E0B", "#F97316", "#EF4444", "#EC4899", "#A855F7", "#E5E7EB",
    ];

    private readonly SettingsStore _store;
    private readonly IHardwareMonitor _monitor;
    private readonly Dispatcher _dispatcher;
    private readonly List<MetricOptionViewModel> _options;
    private bool _syncing;

    public SettingsViewModel(SettingsStore store, IHardwareMonitor monitor, Dispatcher dispatcher)
    {
        _store = store;
        _monitor = monitor;
        _dispatcher = dispatcher;
        _syncing = true; // nothing constructed below may push back into the store

        var selectedIds = store.Current.Metrics.Select(m => m.Id).ToHashSet();
        _options = monitor.Metrics
            .OrderBy(m => CategoryRank(m.Category))
            .ThenBy(m => m.HardwareName)
            .ThenBy(m => m.Kind)
            .ThenBy(m => m.SensorName)
            .Select(m => new MetricOptionViewModel(m, selectedIds.Contains(m.Id), OnOptionToggled))
            .ToList();

        OptionsView = CollectionViewSource.GetDefaultView(_options);
        OptionsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MetricOptionViewModel.Group)));
        OptionsView.Filter = FilterOption;

        var byId = monitor.Metrics.ToDictionary(m => m.Id);
        foreach (var selection in store.Current.Metrics)
        {
            if (byId.TryGetValue(selection.Id, out var d))
                Selected.Add(CreateSelected(d, selection.CustomLabel));
        }

        LoadFromSettings(store.Current);
        _syncing = false;

        _monitor.SnapshotUpdated += OnSnapshot;
        UpdateLiveValues(_monitor.Latest);
    }

    // ------------------------------------------------------------------ metrics

    public ICollectionView OptionsView { get; }

    public ObservableCollection<SelectedMetricViewModel> Selected { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SelectedMetricViewModel? CurrentSelected { get; set; }

    partial void OnSearchTextChanged(string value) => OptionsView.Refresh();

    private bool FilterOption(object item)
    {
        if (item is not MetricOptionViewModel option || string.IsNullOrWhiteSpace(SearchText))
            return true;

        var q = SearchText.Trim();
        return option.Label.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || option.Descriptor.SensorName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || option.Group.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private void OnOptionToggled(MetricOptionViewModel option, bool selected)
    {
        if (_syncing)
            return;

        if (selected && Selected.All(s => s.Id != option.Id))
            Selected.Add(CreateSelected(option.Descriptor, null));
        else if (!selected)
        {
            var existing = Selected.FirstOrDefault(s => s.Id == option.Id);
            if (existing is not null)
                Selected.Remove(existing);
        }

        PushSelection();
    }

    [RelayCommand]
    private void MoveUp(SelectedMetricViewModel? item) => Move(item, -1);

    [RelayCommand]
    private void MoveDown(SelectedMetricViewModel? item) => Move(item, +1);

    private void Move(SelectedMetricViewModel? item, int delta)
    {
        if (item is null)
            return;
        var index = Selected.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Selected.Count)
            return;
        Selected.Move(index, target);
        CurrentSelected = item;
        PushSelection();
    }

    [RelayCommand]
    private void Remove(SelectedMetricViewModel? item)
    {
        if (item is null)
            return;
        Selected.Remove(item);
        SetOptionSelected(item.Id, false);
        PushSelection();
    }

    [RelayCommand]
    private void RestoreDefaultMetrics()
    {
        Selected.Clear();
        foreach (var d in _monitor.Metrics.Where(m => m.RecommendedOrder >= 0).OrderBy(m => m.RecommendedOrder))
            Selected.Add(CreateSelected(d, null));

        var ids = Selected.Select(s => s.Id).ToHashSet();
        foreach (var o in _options)
            SetOptionSelected(o.Id, ids.Contains(o.Id));
        PushSelection();
    }

    /// <summary>Constructs a row without letting its initial label assignment push a partial list to the store.</summary>
    private SelectedMetricViewModel CreateSelected(MetricDescriptor descriptor, string? customLabel)
    {
        var wasSyncing = _syncing;
        _syncing = true;
        try
        {
            return new SelectedMetricViewModel(descriptor, customLabel, PushSelection);
        }
        finally
        {
            _syncing = wasSyncing;
        }
    }

    private void SetOptionSelected(string id, bool value)
    {
        var option = _options.FirstOrDefault(o => o.Id == id);
        if (option is null)
            return;
        option.Suppress = true;
        option.IsSelected = value;
        option.Suppress = false;
    }

    private void PushSelection()
    {
        if (_syncing)
            return;
        _syncing = true;
        try
        {
            var list = Selected
                .Select(s => new MetricSelection
                {
                    Id = s.Id,
                    CustomLabel = string.IsNullOrWhiteSpace(s.CustomLabel) ? null : s.CustomLabel.Trim(),
                })
                .ToList();
            _store.Update(s => s.Metrics = list, SettingsChange.Metrics);
        }
        finally
        {
            _syncing = false;
        }
    }

    // --------------------------------------------------------------- appearance

    [ObservableProperty] public partial LayoutMode Layout { get; set; }
    [ObservableProperty] public partial int Columns { get; set; }
    [ObservableProperty] public partial double Scale { get; set; }
    [ObservableProperty] public partial double BackgroundOpacity { get; set; }
    [ObservableProperty] public partial string AccentColor { get; set; } = "#3B82F6";
    [ObservableProperty] public partial bool ShowHeader { get; set; }
    [ObservableProperty] public partial bool ShowSparklines { get; set; }
    [ObservableProperty] public partial bool ShowBars { get; set; }

    partial void OnLayoutChanged(LayoutMode value) => Push(s => s.Layout = value);
    partial void OnColumnsChanged(int value) => Push(s => s.Columns = Math.Clamp(value, 1, 4));
    partial void OnScaleChanged(double value) => Push(s => s.Scale = Math.Round(value, 2));
    partial void OnBackgroundOpacityChanged(double value) => Push(s => s.BackgroundOpacity = Math.Round(value, 2));
    partial void OnAccentColorChanged(string value) => Push(s => s.AccentColor = value);
    partial void OnShowHeaderChanged(bool value) => Push(s => s.ShowHeader = value);
    partial void OnShowSparklinesChanged(bool value) => Push(s => s.ShowSparklines = value);
    partial void OnShowBarsChanged(bool value) => Push(s => s.ShowBars = value);

    // ---------------------------------------------------------------- behaviour

    [ObservableProperty] public partial bool AlwaysOnTop { get; set; }
    [ObservableProperty] public partial bool ClickThrough { get; set; }
    [ObservableProperty] public partial double UpdateIntervalMs { get; set; }
    [ObservableProperty] public partial double TemperatureWarning { get; set; }
    [ObservableProperty] public partial double TemperatureCritical { get; set; }
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial string? StartupError { get; set; }

    partial void OnAlwaysOnTopChanged(bool value) => Push(s => s.AlwaysOnTop = value);
    partial void OnClickThroughChanged(bool value) => Push(s => s.ClickThrough = value);
    partial void OnUpdateIntervalMsChanged(double value) => Push(s => s.UpdateIntervalMs = (int)value, SettingsChange.Behaviour);

    partial void OnTemperatureWarningChanged(double value)
    {
        if (TemperatureCritical < value)
            TemperatureCritical = value;
        Push(s => s.TemperatureWarning = (int)value, SettingsChange.Behaviour);
    }

    partial void OnTemperatureCriticalChanged(double value)
    {
        if (TemperatureWarning > value)
            TemperatureWarning = value;
        Push(s => s.TemperatureCritical = (int)value, SettingsChange.Behaviour);
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_syncing)
            return;
        var ok = StartupService.SetEnabled(value);
        StartupError = ok ? null : "Nie udało się zmienić zadania autostartu (sprawdź log.txt).";
        if (!ok)
        {
            _syncing = true;
            StartWithWindows = !value;
            _syncing = false;
        }
    }

    [RelayCommand]
    private void RestoreDefaultAppearance()
    {
        var d = new AppSettings();
        Layout = d.Layout;
        Columns = d.Columns;
        Scale = d.Scale;
        BackgroundOpacity = d.BackgroundOpacity;
        AccentColor = d.AccentColor;
        ShowHeader = d.ShowHeader;
        ShowSparklines = d.ShowSparklines;
        ShowBars = d.ShowBars;
        AlwaysOnTop = d.AlwaysOnTop;
        ClickThrough = d.ClickThrough;
        UpdateIntervalMs = d.UpdateIntervalMs;
        TemperatureWarning = d.TemperatureWarning;
        TemperatureCritical = d.TemperatureCritical;
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Logger.Directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Logger.Directory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not open log folder", ex);
        }
    }

    public string HardwareSummary { get; private set; } = string.Empty;

    public string DriverStatus => _monitor.IsCpuDriverAvailable
        ? "Sterownik PawnIO: zainstalowany"
        : "Sterownik PawnIO: BRAK – czujniki CPU niedostępne (pobierz z pawnio.eu)";

    private void LoadFromSettings(AppSettings s)
    {
        _syncing = true;
        try
        {
            Layout = s.Layout;
            Columns = s.Columns;
            Scale = s.Scale;
            BackgroundOpacity = s.BackgroundOpacity;
            AccentColor = s.AccentColor;
            ShowHeader = s.ShowHeader;
            ShowSparklines = s.ShowSparklines;
            ShowBars = s.ShowBars;
            AlwaysOnTop = s.AlwaysOnTop;
            ClickThrough = s.ClickThrough;
            UpdateIntervalMs = s.UpdateIntervalMs;
            TemperatureWarning = s.TemperatureWarning;
            TemperatureCritical = s.TemperatureCritical;

            var hardware = _monitor.Metrics.Select(m => m.HardwareName).Distinct().Count();
            HardwareSummary = $"Wykryto {_monitor.Metrics.Count} czujników na {hardware} urządzeniach";
            StartWithWindows = StartupService.IsEnabled();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void Push(Action<AppSettings> mutate, SettingsChange change = SettingsChange.Appearance)
    {
        if (!_syncing)
            _store.Update(mutate, change);
    }

    // -------------------------------------------------------------- live values

    private void OnSnapshot(object? sender, HardwareSnapshot snapshot) =>
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () => UpdateLiveValues(snapshot));

    private void UpdateLiveValues(HardwareSnapshot snapshot)
    {
        foreach (var option in _options)
        {
            var v = snapshot.Get(option.Id);
            option.LiveValue = v is null ? "—" : MetricFormatter.FormatCompact(option.Descriptor.Kind, v.Value);
        }
    }

    private static int CategoryRank(HardwareCategory c) => c switch
    {
        HardwareCategory.Cpu => 0,
        HardwareCategory.Gpu => 1,
        HardwareCategory.Memory => 2,
        HardwareCategory.Storage => 3,
        HardwareCategory.Network => 4,
        HardwareCategory.Motherboard => 5,
        _ => 6,
    };

    public void Dispose() => _monitor.SnapshotUpdated -= OnSnapshot;
}
