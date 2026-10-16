using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexQuotaMonitor.Wpf;

public sealed class AppSettings
{
    [JsonPropertyName("quota_interval")]
    public int QuotaInterval { get; set; } = Constants.DefaultQuotaIntervalSeconds;

    [JsonPropertyName("red_threshold")]
    public double RedThreshold { get; set; } = Constants.DefaultRedThreshold;

    [JsonPropertyName("amber_threshold")]
    public double AmberThreshold { get; set; } = Constants.DefaultAmberThreshold;

    // Which end of the taskbar the overlay is parked at: "right" is immediately left of the
    // notification area (the tray side, and what every earlier build did), "left" is the
    // taskbar's own far left - the empty end on a Windows 11 centre-aligned taskbar.
    [JsonPropertyName("display_side")]
    public string DisplaySide { get; set; } = "right";

    [JsonIgnore]
    public bool DisplayLeft => string.Equals(DisplaySide, "left", StringComparison.OrdinalIgnoreCase);

    public AppSettings Clone() => new()
    {
        QuotaInterval = QuotaInterval,
        RedThreshold = RedThreshold,
        AmberThreshold = AmberThreshold,
        DisplaySide = DisplaySide
    };

    public void Normalize()
    {
        QuotaInterval = Math.Max(30, QuotaInterval);
        RedThreshold = Math.Clamp(RedThreshold, 0.0, 100.0);
        AmberThreshold = Math.Clamp(AmberThreshold, RedThreshold, 100.0);
        // Anything that is not "left" means the tray side, so a hand-edited or unknown value
        // still parks the card somewhere sensible.
        DisplaySide = DisplayLeft ? "left" : "right";
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    public static AppSettings Load(string path, SimpleLogger? logger = null)
    {
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                           ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex)
        {
            logger?.Warning($"failed to load settings from {path}: {ex.Message}");
            return new AppSettings();
        }
    }

    public static void Save(string path, AppSettings settings, SimpleLogger? logger = null)
    {
        try
        {
            settings.Normalize();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            logger?.Warning($"failed to save settings to {path}: {ex.Message}");
        }
    }

    public static AppSettings ApplyCliOverrides(AppSettings settings, CliOptions options)
    {
        var merged = settings.Clone();
        if (options.QuotaInterval.HasValue)
        {
            merged.QuotaInterval = options.QuotaInterval.Value;
        }
        merged.Normalize();
        return merged;
    }

    public static void EnsureDefault(string path, SimpleLogger? logger = null)
    {
        if (!File.Exists(path))
        {
            Save(path, new AppSettings(), logger);
        }
    }
}
