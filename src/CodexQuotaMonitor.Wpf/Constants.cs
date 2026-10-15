namespace CodexQuotaMonitor.Wpf;

public static class Constants
{
    public const string AppName = "Codex Quota Monitor Native";
    public const string WindowTitlePrefix = "Codex Quota Monitor Native";
    public const string MutexName = "Local\\CodexQuotaMonitorNative";
    public const int DefaultQuotaIntervalSeconds = 180;

    // Each metric cell keeps the width it had when the card still had a third slot;
    // the card just got narrower instead of the cells growing.
    public const int CellWidth = 81;
    public const int CardChrome = 10;
    public const int DefaultWidth = 2 * CellWidth + CardChrome;
    public const int DefaultHeight = 48;
    public const double DefaultRedThreshold = 15.0;
    public const double DefaultAmberThreshold = 30.0;
}
