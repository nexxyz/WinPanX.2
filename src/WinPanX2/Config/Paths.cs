namespace WinPanX2.Config;

internal static class Paths
{
    public static string AppDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinPanX.2");

    public static string ConfigFilePath =>
        Path.Combine(AppDirectory, "config.json");

    public static string LogFilePath =>
        Path.Combine(AppDirectory, "winpan-x.2.log");
}
