namespace CodexQuotaMonitor.Wpf;

public static class FullscreenDetector
{
    // These cover the whole monitor without being a fullscreen application.
    private static readonly string[] ShellClasses =
        ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow"];

    public static bool IsFullscreen(string windowClass, NativeMethods.RECT window, System.Drawing.Rectangle screen)
    {
        if (ShellClasses.Contains(windowClass, StringComparer.Ordinal))
        {
            return false;
        }

        // Covering the monitor means covering the strip the taskbar occupies as well; a
        // maximised window stops above it.
        return window.Left <= screen.Left &&
            window.Top <= screen.Top &&
            window.Right >= screen.Right &&
            window.Bottom >= screen.Bottom;
    }
}
