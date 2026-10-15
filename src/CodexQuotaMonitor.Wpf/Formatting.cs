namespace CodexQuotaMonitor.Wpf;

public static class Formatting
{
    public static string Countdown(long? epochSeconds)
    {
        if (!epochSeconds.HasValue)
        {
            return "reset --";
        }

        var seconds = epochSeconds.Value - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (seconds <= 0)
        {
            return "reset now";
        }

        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}d {span.Hours}h";
        }
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        }
        return $"{Math.Max(0, span.Minutes)}m";
    }

    public static string CompactError(Exception ex)
    {
        var text = ex.Message.Trim();
        return text.Length <= 180 ? text : text[..177] + "...";
    }

    public static string Truncate(string text, int maxChars)
    {
        var clean = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length <= maxChars)
        {
            return clean;
        }
        return clean[..Math.Max(0, maxChars - 3)].TrimEnd() + "...";
    }

    public static string RemainingText(double? remaining)
    {
        return remaining.HasValue ? $"{remaining.Value:0}" : "--";
    }

    // The overlay draws straight onto the taskbar, so the palette follows the taskbar's
    // own light/dark setting instead of assuming a card behind the text.
    public static bool LightTaskbar { get; } = ReadLightTaskbar();

    public static System.Windows.Media.Color PrimaryText => ColorFromHex(LightTaskbar ? "#1B2027" : "#F4F6F8");

    public static System.Windows.Media.Color DetailText => ColorFromHex(LightTaskbar ? "#4B5563" : "#A2ACB7");

    public static System.Windows.Media.Color GaugeTrack => ColorFromHex(LightTaskbar ? "#33000000" : "#6A56616C");

    public static System.Windows.Media.Color SeparatorLine => ColorFromHex(LightTaskbar ? "#59000000" : "#38FFFFFF");

    public static System.Windows.Media.Color Ok => ColorFromHex(LightTaskbar ? "#0F8A69" : "#2DD4A8");

    public static System.Windows.Media.Color Warn => ColorFromHex(LightTaskbar ? "#A45A00" : "#FFC857");

    public static System.Windows.Media.Color Fail => ColorFromHex(LightTaskbar ? "#C62A45" : "#FF6B81");

    public static System.Windows.Media.Color Idle => ColorFromHex(LightTaskbar ? "#5B6672" : "#8794A2");

    public static System.Windows.Media.Color ColorForRemaining(double? remaining, AppSettings settings)
    {
        if (!remaining.HasValue)
        {
            return Idle;
        }
        if (remaining.Value < settings.RedThreshold)
        {
            return Fail;
        }
        if (remaining.Value < settings.AmberThreshold)
        {
            return Warn;
        }
        return Ok;
    }

    public static System.Windows.Media.Color ColorFromHex(string hex)
    {
        return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
    }

    private static bool ReadLightTaskbar()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
