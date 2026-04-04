using Microsoft.Win32;

namespace WinPanX2.Startup;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "WinPanX2";
    private const string LegacyRunValueName = "WinPan X.2";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) != null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (key == null) return;

        if (enabled)
        {
            key.SetValue(RunValueName, Application.ExecutablePath);
            key.DeleteValue(LegacyRunValueName, false);
        }
        else
        {
            key.DeleteValue(RunValueName, false);
            key.DeleteValue(LegacyRunValueName, false);
        }
    }
}
