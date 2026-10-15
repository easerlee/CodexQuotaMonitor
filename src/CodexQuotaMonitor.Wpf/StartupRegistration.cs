using Microsoft.Win32;

namespace CodexQuotaMonitor.Wpf;

public static class StartupRegistration
{
    public const string DefaultValueName = "CodexQuotaMonitorNative";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled(string valueName = DefaultValueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(valueName) is string value && value.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled, SimpleLogger logger, string valueName = DefaultValueName)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                logger.Warning("autostart update failed: Run key unavailable");
                return false;
            }

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe))
                {
                    logger.Warning("autostart update failed: executable path unknown");
                    return false;
                }

                key.SetValue(valueName, $"\"{exe}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.Warning($"autostart update failed: {ex.Message}");
            return false;
        }
    }
}
