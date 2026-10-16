namespace CodexQuotaMonitor.Wpf;

using System.Globalization;

public static class Formatting
{
    // Every row uses the same text size, derived from the row height so a taller or
    // shorter taskbar keeps the three rows balanced.
    public static double TextSize(double rowHeight) => Math.Clamp(rowHeight * 0.78, 8.5, 12.0);

    // A window carries no trustworthy reset time when it is full (the server only starts
    // counting the 5h window from first use) or when its recorded reset time already passed.
    public static bool WindowIsFresh(double? remainingPercent, long? resetsAt, DateTimeOffset now)
    {
        return (remainingPercent.HasValue && remainingPercent.Value >= 99.5) ||
            (resetsAt.HasValue && resetsAt.Value <= now.ToUnixTimeSeconds());
    }

    // What to print for a window that has no live countdown.
    public static string WindowReset(double? remainingPercent, long? resetsAt, DateTimeOffset now, bool weekly)
    {
        if (WindowIsFresh(remainingPercent, resetsAt, now))
        {
            return "已重置";
        }

        if (!resetsAt.HasValue)
        {
            return "--";
        }

        return weekly ? Countdown(resetsAt) : ResetClock(resetsAt);
    }

    // Local clock time the window resets, e.g. "15:20".
    public static string ResetClock(long? epochSeconds)
    {
        return epochSeconds.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(epochSeconds.Value).ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)
            : "--:--";
    }

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

    public static System.Windows.Media.Color GaugeTrack => ColorFromHex(LightTaskbar ? "#33000000" : "#6A56616C");

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

    // The right-click panel is drawn by us and always dark, so it has its own fixed palette
    // instead of following the taskbar's light/dark setting.
    public static class Popup
    {
        public static System.Windows.Media.Color Background => ColorFromHex("#F0141619");

        public static System.Windows.Media.Color Outline => ColorFromHex("#33FFFFFF");

        public static System.Windows.Media.Color Text => ColorFromHex("#F2F4F7");

        public static System.Windows.Media.Color Dim => ColorFromHex("#8B94A1");

        public static System.Windows.Media.Color Accent => ColorFromHex("#7FB2FF");

        public static System.Windows.Media.Color Hover => ColorFromHex("#1FFFFFFF");

        public static System.Windows.Media.Color Track => ColorFromHex("#2EFFFFFF");

        public static System.Windows.Media.Color Ok => ColorFromHex("#2DD4A8");

        public static System.Windows.Media.Color Warn => ColorFromHex("#FFC857");

        public static System.Windows.Media.Color Fail => ColorFromHex("#FF6B81");

        public static System.Windows.Media.Color Idle => ColorFromHex("#8794A2");

        public static System.Windows.Media.Color ColorForRemaining(double? remaining, AppSettings settings)
        {
            return remaining switch
            {
                null => Idle,
                var value when value < settings.RedThreshold => Fail,
                var value when value < settings.AmberThreshold => Warn,
                _ => Ok
            };
        }
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
