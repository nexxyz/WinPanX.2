using System.Diagnostics;
using WinPanX2.Config;
using WinPanX2.Core;
using WinPanX2.Startup;

namespace WinPanX2.Tray;

internal class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly SpatialAudioEngine _engine;
    private readonly AppConfig _config;
    private bool _initializing;

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

            followOriginalItem.Checked = mode == "Sticky";
            followMostRecentItem.Checked = mode == "FollowMostRecent";
            followMostRecentlyOpenedItem.Checked = mode == "FollowMostRecentOpened";

            // Ensure mode change applies immediately (e.g. clear Sticky bindings).
            _engine.ClearWindowBindings();
            _engine.ReapplyCurrentPositions();

            SaveConfig();
        }

        followOriginalItem.Click += (_, _) => SetFollowMode("Sticky");
        followMostRecentItem.Click += (_, _) => SetFollowMode("FollowMostRecent");
        followMostRecentlyOpenedItem.Click += (_, _) => SetFollowMode("FollowMostRecentOpened");

        // Initialize checks based on current config
        followOriginalItem.Checked = _config.BindingMode != "FollowMostRecent" && _config.BindingMode != "FollowMostRecentOpened";
        followMostRecentItem.Checked = _config.BindingMode == "FollowMostRecent";
        followMostRecentlyOpenedItem.Checked = _config.BindingMode == "FollowMostRecentOpened";

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
        _engine.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Application.Exit();
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
