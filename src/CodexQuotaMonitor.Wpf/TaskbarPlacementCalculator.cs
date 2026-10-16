namespace CodexQuotaMonitor.Wpf;

public static class TaskbarPlacementCalculator
{
    public const uint EdgeLeft = 0;
    public const uint EdgeTop = 1;
    public const uint EdgeRight = 2;
    public const uint EdgeBottom = 3;

    // Gap between the overlay and the notification area, matching the tray spacing.
    public const int TrayGap = 8;

    public static TaskbarPlacement Compute(
        uint edge,
        NativeMethods.RECT taskbar,
        int preferredWidth,
        int fallbackHeight,
        int screenWidth,
        int screenHeight,
        int trayLeftEdge = 0,
        bool pinLeft = false)
    {
        if (edge is EdgeTop or EdgeBottom && taskbar.Width > 0 && taskbar.Height > 0)
        {
            var width = Math.Min(Math.Max(1, preferredWidth), taskbar.Width);

            // The tray side (default) sits immediately left of the notification area so a
            // left-aligned taskbar keeps its Start button clear; the left side is the taskbar's
            // own far end, which is the empty one on a Windows 11 centre-aligned taskbar. The
            // tray anchor also falls back to the taskbar's left edge when the tray geometry is
            // unknown or leaves no room.
            var x = pinLeft
                ? taskbar.Left + TrayGap
                : trayLeftEdge > taskbar.Left + width + TrayGap
                    ? trayLeftEdge - width - TrayGap
                    : taskbar.Left;

            return Clamp(new TaskbarPlacement(x, taskbar.Top, width, taskbar.Height), screenWidth, screenHeight);
        }

        // A vertical taskbar has no meaningful horizontal "taskbar height". Keep the
        // compact overlay at the lower-left of the primary screen in that layout.
        if (edge is EdgeLeft or EdgeRight)
        {
            return Fallback(preferredWidth, fallbackHeight, screenWidth, screenHeight);
        }

        return Fallback(preferredWidth, fallbackHeight, screenWidth, screenHeight);
    }

    public static TaskbarPlacement Fallback(int preferredWidth, int height, int screenWidth, int screenHeight)
    {
        return Clamp(
            new TaskbarPlacement(0, Math.Max(0, screenHeight - height), preferredWidth, height),
            screenWidth,
            screenHeight);
    }

    private static TaskbarPlacement Clamp(TaskbarPlacement placement, int screenWidth, int screenHeight)
    {
        var width = Math.Clamp(placement.Width, 1, Math.Max(1, screenWidth));
        var height = Math.Clamp(placement.Height, 1, Math.Max(1, screenHeight));
        var x = Math.Clamp(placement.X, 0, Math.Max(0, screenWidth - width));
        var y = Math.Clamp(placement.Y, 0, Math.Max(0, screenHeight - height));
        return new TaskbarPlacement(x, y, width, height);
    }
}
