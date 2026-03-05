using WinPanX2.Logging;
using WinPanX2.Config;
using WinPanX2.Tray;

namespace WinPanX2;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // CLI test harnesses
        if (args != null)
        {
            if (args.Contains("--test-all"))
            {
                RunAllDevicesHarness(args);
                return;
            }

            if (args.Contains("--test-sequence"))
            {
                RunLifecycleSequenceHarness(args);
                return;
            }
        }

        ApplicationConfiguration.Initialize();

        Directory.CreateDirectory(Paths.AppDirectory);

        Logger.Initialize(Paths.LogFilePath);
        Logger.Info("WinPan X.2 starting");

        var config = ConfigLoader.LoadOrCreate(Paths.ConfigFilePath);

        // Apply configured log level
        if (Enum.TryParse<WinPanX2.Logging.LogLevel>(config.LogLevel, true, out var level))
            Logger.MinimumLevel = level;
        else
            Logger.MinimumLevel = WinPanX2.Logging.LogLevel.Info;

        Application.Run(new TrayApp(config));

        Logger.Info("WinPan X.2 shutting down");
        Logger.Dispose();
    }

    private static void RunAllDevicesHarness(string[] args)
    {
        // Resolve log path (optional override)
        string logPath = Paths.LogFilePath;

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--log")
            {
                logPath = args[i + 1];
                break;
            }
        }

        Directory.CreateDirectory(Paths.AppDirectory);
        try
        {
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
        }
        catch { }

        Logger.Initialize(logPath);
        Logger.Info("WinPan X.2 starting (CLI --test-all)");

        var config = ConfigLoader.LoadOrCreate(Paths.ConfigFilePath);

        if (Enum.TryParse<WinPanX2.Logging.LogLevel>(config.LogLevel, true, out var level))
            Logger.MinimumLevel = level;
        else
            Logger.MinimumLevel = WinPanX2.Logging.LogLevel.Info;

        try
        {
            var engine = new WinPanX2.Core.SpatialAudioEngine(config);

            engine.SetDeviceMode(WinPanX2.Audio.DeviceMode.All);
            engine.Start();

            Thread.Sleep(2000);

            engine.Stop();
        }
        catch (Exception ex)
        {
            Logger.Error($"CLI --test-all failed: {ex}");
        }

        Logger.Info("CLI --test-all finished");
        Logger.Dispose();
    }

    private static void RunLifecycleSequenceHarness(string[] args)
    {
        string logPath = Paths.LogFilePath;

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--log")
            {
                logPath = args[i + 1];
                break;
            }
        }

        Directory.CreateDirectory(Paths.AppDirectory);
        try
        {
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
        }
        catch { }

        Logger.Initialize(logPath);
        Logger.Info("WinPan X.2 starting (CLI --test-sequence)");

        var config = ConfigLoader.LoadOrCreate(Paths.ConfigFilePath);

        if (Enum.TryParse<WinPanX2.Logging.LogLevel>(config.LogLevel, true, out var level))
            Logger.MinimumLevel = level;
        else
            Logger.MinimumLevel = WinPanX2.Logging.LogLevel.Info;

        try
        {
            var engine = new WinPanX2.Core.SpatialAudioEngine(config);

            Logger.Info("[SEQ] Start default mode");
            engine.Start();
            Thread.Sleep(1000);

            Logger.Info("[SEQ] Stop default mode");
            engine.Stop();
            Thread.Sleep(500);

            Logger.Info("[SEQ] Switch to All mode");
            engine.SetDeviceMode(WinPanX2.Audio.DeviceMode.All);

            Logger.Info("[SEQ] Start All mode");
            engine.Start();
            Thread.Sleep(1000);

            Logger.Info("[SEQ] Stop All mode");
            engine.Stop();
            Thread.Sleep(500);

            Logger.Info("[SEQ] Switch back to Default mode");
            engine.SetDeviceMode(WinPanX2.Audio.DeviceMode.Default);

            Logger.Info("[SEQ] Start Default mode again");
            engine.Start();
            Thread.Sleep(1000);

            Logger.Info("[SEQ] Final Stop");
            engine.Stop();
        }
        catch (Exception ex)
        {
            Logger.Error($"CLI --test-sequence failed: {ex}");
        }

        Logger.Info("CLI --test-sequence finished");
        Logger.Dispose();
    }
}
