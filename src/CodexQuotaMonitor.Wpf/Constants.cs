namespace CodexQuotaMonitor.Wpf;

public static class Constants
{
    public const string AppName = "Codex Quota Monitor Native";
    public const string WindowTitlePrefix = "Codex Quota Monitor Native";
    public const string MutexName = "Local\\CodexQuotaMonitorNative";
    public const int DefaultQuotaIntervalSeconds = 180;

    // Three rows: the 5H and WK bars, then the "15:33 / 5d 7h" line with the state light.
    public const int DefaultWidth = 116;
    public const int DefaultHeight = 48;
    public const double DefaultRedThreshold = 15.0;
    public const double DefaultAmberThreshold = 30.0;
}
