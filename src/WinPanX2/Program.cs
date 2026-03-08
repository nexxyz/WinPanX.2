using WinPanX2.Logging;
using WinPanX2.Config;
using WinPanX2.Tray;
using System.Threading;
using System.Drawing;
using System.Windows.Forms;

namespace WinPanX2;

static class Program
{
    private const string SingleInstanceMutexName = "Local\\WinPanX2.X2";
    private static Mutex? _singleInstanceMutex;
    private static bool _singleInstanceHasHandle;

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

        if (!TryAcquireSingleInstance())
        {
            ShowAlreadyRunningNotification();
            Logger.Info("Another instance is already running; exiting.");
            Logger.Dispose();
            return;
        }

        var config = ConfigLoader.LoadOrCreate(Paths.ConfigFilePath);

        // Apply configured log level
        if (Enum.TryParse<WinPanX2.Logging.LogLevel>(config.LogLevel, true, out var level))
            Logger.MinimumLevel = level;
        else
            Logger.MinimumLevel = WinPanX2.Logging.LogLevel.Info;

        try
        {
            Application.Run(new TrayApp(config));
        }
        finally
        {
            ReleaseSingleInstance();
        }

        Logger.Info("WinPan X.2 shutting down");
        Logger.Dispose();
    }

    private static bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: false, name: SingleInstanceMutexName);
            try
            {
                _singleInstanceHasHandle = _singleInstanceMutex.WaitOne(0, false);
            }
            catch (AbandonedMutexException)
            {
                _singleInstanceHasHandle = true;
            }

            return _singleInstanceHasHandle;
        }
        catch
        {
            // If we can't create/open the mutex, don't block startup.
            return true;
        }
    }

    private static void ReleaseSingleInstance()
    {
        try
        {
            if (_singleInstanceHasHandle)
                _singleInstanceMutex?.ReleaseMutex();
        }
        catch { }

        try
        {
            _singleInstanceMutex?.Dispose();
        }
        catch { }

        _singleInstanceMutex = null;
        _singleInstanceHasHandle = false;
    }

    private static void ShowAlreadyRunningNotification()
    {
        try
        {
            using var notify = new NotifyIcon();
            try
            {
                notify.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            }
            catch
            {
                notify.Icon = SystemIcons.Application;
            }

            notify.Visible = true;
            notify.Text = "WinPan X.2";

            using var ctx = new ApplicationContext();
            using var timer = new System.Windows.Forms.Timer { Interval = 2500 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                notify.Visible = false;
                notify.Dispose();
                ctx.ExitThread();
            };

            notify.ShowBalloonTip(2000, "WinPan X.2", "Already running. This instance will exit.", ToolTipIcon.Info);
            timer.Start();
            Application.Run(ctx);
        }
        catch
        {
            // Fallback: no UI.
        }
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
