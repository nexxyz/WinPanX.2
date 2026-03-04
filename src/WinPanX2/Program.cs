using WinPanX2.Logging;
using WinPanX2.Config;
using WinPanX2.Tray;

namespace WinPanX2;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        Directory.CreateDirectory(Paths.AppDirectory);

        Logger.Initialize(Paths.LogFilePath);
        Logger.Info("WinPan X.2 starting");

        var config = ConfigLoader.LoadOrCreate(Paths.ConfigFilePath);

        Application.Run(new TrayApp(config));

        Logger.Info("WinPan X.2 shutting down");
        Logger.Dispose();
    }
}
