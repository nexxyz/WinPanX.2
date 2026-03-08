using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WinPanX2.Config;
using WinPanX2.Core;
using WinPanX2.Startup;
using WinPanX2.Logging;
using Microsoft.Win32;

namespace WinPanX2.Tray;

internal class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly SpatialAudioEngine _engine;
    private readonly AppConfig _config;
    private bool _initializing;
    private bool _shutdownStarted;

    public TrayApp(AppConfig config)
    {
        _config = config;
        _engine = new SpatialAudioEngine(config);
        _initializing = true;

        _engine.SetDeviceMode(_config.ApplyToAllDevices ? Audio.DeviceMode.All : Audio.DeviceMode.Default);

        Icon trayIcon;
        try
        {
            trayIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
                       ?? SystemIcons.Application;
        }
        catch
        {
            trayIcon = SystemIcons.Application;
        }

        _notifyIcon = new NotifyIcon
        {
            Icon = trayIcon,
            Visible = true,
            Text = "WinPan X.2"
        };

        _notifyIcon.ContextMenuStrip = BuildMenu();

        SubscribeRuntimeEvents();

        // Show the context menu on left click as well.
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            var menu = _notifyIcon.ContextMenuStrip;
            if (menu == null)
                return;

            menu.Show(Cursor.Position);
        };
         
        // Start spatial audio after full initialization
        _engine.Start();

        _initializing = false;
    }

    private void SubscribeRuntimeEvents()
    {
        try { Application.ApplicationExit += OnApplicationExit; } catch { }
        try { Application.ThreadException += OnThreadException; } catch { }
        try { AppDomain.CurrentDomain.ProcessExit += OnProcessExit; } catch { }
        try { AppDomain.CurrentDomain.UnhandledException += OnUnhandledException; } catch { }
        try { TaskScheduler.UnobservedTaskException += OnUnobservedTaskException; } catch { }
        try { SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged; } catch { }
    }

    private void UnsubscribeRuntimeEvents()
    {
        try { Application.ApplicationExit -= OnApplicationExit; } catch { }
        try { Application.ThreadException -= OnThreadException; } catch { }
        try { AppDomain.CurrentDomain.ProcessExit -= OnProcessExit; } catch { }
        try { AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException; } catch { }
        try { TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException; } catch { }
        try { SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; } catch { }
    }

    private void SafeShutdown(string reason, bool exitApplication)
    {
        if (_shutdownStarted)
            return;

        _shutdownStarted = true;
        UnsubscribeRuntimeEvents();

        try { Logger.Info($"[Shutdown] {reason}"); } catch { }

        try { _engine.Dispose(); } catch { }

        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        catch { }

        if (exitApplication)
        {
            try { Application.Exit(); } catch { }
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        try { _engine.NotifyDisplaySettingsChanged(); } catch { }
    }

    private void OnApplicationExit(object? sender, EventArgs e) => SafeShutdown("ApplicationExit", exitApplication: false);

    private void OnProcessExit(object? sender, EventArgs e) => SafeShutdown("ProcessExit", exitApplication: false);

    private void OnThreadException(object sender, ThreadExceptionEventArgs e)
    {
        try { Logger.Error($"UI thread exception: {e.Exception}"); } catch { }
        SafeShutdown("ThreadException", exitApplication: false);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try { Logger.Error($"Unhandled exception: {e.ExceptionObject}"); } catch { }
        SafeShutdown("UnhandledException", exitApplication: false);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try { Logger.Error($"Unobserved task exception: {e.Exception}"); } catch { }
        try { e.SetObserved(); } catch { }
        SafeShutdown("UnobservedTaskException", exitApplication: false);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var enableItem = new ToolStripMenuItem("Enable Spatial Audio")
        {
            CheckOnClick = true,
            Checked = false
        };
        enableItem.CheckedChanged += (_, _) =>
        {
            if (_initializing) return;
            if (enableItem.Checked) _engine.Start();
            else _engine.Stop();
        };

        // Reflect initial state without triggering logic
        enableItem.Checked = true;

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled()
        };
        startupItem.CheckedChanged += (_, _) =>
        {
            StartupManager.SetEnabled(startupItem.Checked);
        };

        var followModeMenu = new ToolStripMenuItem("Follow Mode");

        var followOriginalItem = new ToolStripMenuItem("Original window") { CheckOnClick = true };
        var followMostRecentItem = new ToolStripMenuItem("Most recently active window") { CheckOnClick = true };
        var followMostRecentlyOpenedItem = new ToolStripMenuItem("Most recently opened window") { CheckOnClick = true };

        void SetFollowMode(string mode)
        {
            if (_initializing) return;

            _config.BindingMode = mode;

            followOriginalItem.Checked = mode == BindingModes.Sticky;
            followMostRecentItem.Checked = mode == BindingModes.FollowMostRecent;
            followMostRecentlyOpenedItem.Checked = mode == BindingModes.FollowMostRecentOpened;

            // Ensure mode change applies immediately (e.g. clear Sticky bindings).
            _engine.ClearWindowBindings();
            _engine.ReapplyCurrentPositions();

            SaveConfig();
        }

        followOriginalItem.Click += (_, _) => SetFollowMode(BindingModes.Sticky);
        followMostRecentItem.Click += (_, _) => SetFollowMode(BindingModes.FollowMostRecent);
        followMostRecentlyOpenedItem.Click += (_, _) => SetFollowMode(BindingModes.FollowMostRecentOpened);

        // Initialize checks based on current config
        followOriginalItem.Checked = BindingModes.IsStickyLike(_config.BindingMode);
        followMostRecentItem.Checked = _config.BindingMode == BindingModes.FollowMostRecent;
        followMostRecentlyOpenedItem.Checked = _config.BindingMode == BindingModes.FollowMostRecentOpened;

        followModeMenu.DropDownItems.Add(followOriginalItem);
        followModeMenu.DropDownItems.Add(followMostRecentItem);
        followModeMenu.DropDownItems.Add(followMostRecentlyOpenedItem);

        var applyAllDevicesItem = new ToolStripMenuItem("Apply to all stereo output devices")
        {
            CheckOnClick = true,
            Checked = _config.ApplyToAllDevices
        };
        applyAllDevicesItem.CheckedChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.ApplyToAllDevices = applyAllDevicesItem.Checked;
            _engine.SetDeviceMode(_config.ApplyToAllDevices ? Audio.DeviceMode.All : Audio.DeviceMode.Default);
            SaveConfig();
        };

        // Panning width submenu (UI is inverted vs CenterBias)
        // Higher width => wider panning range.
        var depthMenu = new ToolStripMenuItem("Panning Width");

        var depthLow = new ToolStripMenuItem("Low") { CheckOnClick = true };
        var depthMedium = new ToolStripMenuItem("Medium") { CheckOnClick = true };
        var depthHigh = new ToolStripMenuItem("High") { CheckOnClick = true };
        var depthMax = new ToolStripMenuItem("Max") { CheckOnClick = true };

        void SetDepth(double depth)
        {
            depth = Math.Clamp(depth, 0.0, 1.0);

            // CenterBias: 0 = no bias (widest), 1 = strongest bias (narrowest)
            _config.CenterBias = 1.0 - depth;

            depthLow.Checked = false;
            depthMedium.Checked = false;
            depthHigh.Checked = false;
            depthMax.Checked = false;

            if (depth >= 0.85)
                depthMax.Checked = true;
            else if (depth >= 0.55)
                depthHigh.Checked = true;
            else if (depth >= 0.25)
                depthMedium.Checked = true;
            else
                depthLow.Checked = true;

            _engine.ReapplyCurrentPositions();
            SaveConfig();
        }

        depthLow.Click += (_, _) => { if (_initializing) return; SetDepth(0.2); };
        depthMedium.Click += (_, _) => { if (_initializing) return; SetDepth(0.45); };
        depthHigh.Click += (_, _) => { if (_initializing) return; SetDepth(0.7); };
        depthMax.Click += (_, _) => { if (_initializing) return; SetDepth(1.0); };

        // Initialize checks based on current config
        var currentDepth = 1.0 - Math.Clamp(_config.CenterBias, 0.0, 1.0);
        if (currentDepth >= 0.85)
            depthMax.Checked = true;
        else if (currentDepth >= 0.55)
            depthHigh.Checked = true;
        else if (currentDepth >= 0.25)
            depthMedium.Checked = true;
        else
            depthLow.Checked = true;

        depthMenu.DropDownItems.Add(depthLow);
        depthMenu.DropDownItems.Add(depthMedium);
        depthMenu.DropDownItems.Add(depthHigh);
        depthMenu.DropDownItems.Add(depthMax);

        var openConfig = new ToolStripMenuItem("Open Config");
        openConfig.Click += (_, _) => OpenFile(Paths.ConfigFilePath);

        var openLog = new ToolStripMenuItem("Open Log");
        openLog.Click += (_, _) => OpenFile(Paths.LogFilePath);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Exit();

        menu.Items.Add(enableItem);
        menu.Items.Add(startupItem);
        menu.Items.Add(followModeMenu);
        menu.Items.Add(depthMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(applyAllDevicesItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(openConfig);
        menu.Items.Add(openLog);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    private static void OpenFile(string path)
    {
        if (!File.Exists(path)) return;

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private void Exit()
    {
        SafeShutdown("MenuExit", exitApplication: true);
    }

    private void SaveConfig()
    {
        try
        {
            ConfigLoader.Save(Paths.ConfigFilePath, _config);
        }
        catch
        {
            // best-effort; config persistence should never break the tray app
        }
    }
}
