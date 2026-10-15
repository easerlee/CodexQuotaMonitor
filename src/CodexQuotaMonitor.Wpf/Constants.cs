namespace CodexQuotaMonitor.Wpf;

public static class Constants
{
    public const string AppName = "Codex Quota Monitor Native";
    public const string WindowTitlePrefix = "Codex Quota Monitor Native";
    public const string MutexName = "Local\\CodexQuotaMonitorNative";
    public const int DefaultQuotaIntervalSeconds = 180;

    // Each cell keeps the drawable width it had; the gaps around the indicator line are
    // wider now, so the card is simply that little bit wider too.
    public const int CellSlotWidth = 78;
    public const int CardChrome = 10;
    public const int SeparatorGapLeft = 8;
    public const int SeparatorGapRight = 10;
    public const int DefaultWidth = 2 * CellSlotWidth + CardChrome;
    public const int DefaultHeight = 48;
    public const double DefaultRedThreshold = 15.0;
    public const double DefaultAmberThreshold = 30.0;
}
