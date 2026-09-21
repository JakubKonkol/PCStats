using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using PCStats.Models;

namespace PCStats.Services;

/// <summary>
/// Owns the live <see cref="AppSettings"/> instance, notifies listeners about changes and
/// persists to %AppData% with a short debounce. UI-thread only.
/// </summary>
public sealed class SettingsStore
{
    private static readonly string FilePath = Path.Combine(Logger.Directory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly DispatcherTimer _saveTimer;

    private SettingsStore(AppSettings settings)
    {
        Current = settings;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
    }

    public AppSettings Current { get; }

    /// <summary>Raised after every <see cref="Update"/>; the argument names what changed.</summary>
    public event EventHandler<SettingsChange>? Changed;

    public static SettingsStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded is not null)
                    return new SettingsStore(Sanitize(loaded));
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Settings file unreadable, using defaults", ex);
        }

        return new SettingsStore(new AppSettings());
    }

    public void Update(Action<AppSettings> mutate, SettingsChange change = SettingsChange.Appearance)
    {
        mutate(Current);
        Changed?.Invoke(this, change);
        ScheduleSave();
    }

    /// <summary>Persist without raising <see cref="Changed"/> (window position, etc.).</summary>
    public void UpdateSilently(Action<AppSettings> mutate)
    {
        mutate(Current);
        ScheduleSave();
    }

    public void SaveNow()
    {
        try
        {
            Directory.CreateDirectory(Logger.Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex)
        {
            Logger.Error("Saving settings failed", ex);
        }
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static AppSettings Sanitize(AppSettings s)
    {
        s.Columns = Math.Clamp(s.Columns, 1, 4);
        s.Scale = Math.Clamp(s.Scale, 0.6, 2.0);
        s.BackgroundOpacity = Math.Clamp(s.BackgroundOpacity, 0.1, 1.0);
        s.UpdateIntervalMs = Math.Clamp(s.UpdateIntervalMs, 250, 10_000);
        s.TemperatureWarning = Math.Clamp(s.TemperatureWarning, 40, 110);
        s.TemperatureCritical = Math.Clamp(s.TemperatureCritical, s.TemperatureWarning, 120);
        s.Metrics = s.Metrics.Where(m => !string.IsNullOrWhiteSpace(m.Id)).DistinctBy(m => m.Id).ToList();
        if (string.IsNullOrWhiteSpace(s.AccentColor))
            s.AccentColor = new AppSettings().AccentColor;
        return s;
    }
}

public enum SettingsChange
{
    /// <summary>Colours, scale, layout, toggles — tiles keep their history.</summary>
    Appearance,

    /// <summary>Selected metrics or their labels changed — tiles are rebuilt.</summary>
    Metrics,

    /// <summary>Polling interval or thresholds.</summary>
    Behaviour,
}
