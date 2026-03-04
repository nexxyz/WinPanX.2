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

        var bindingItem = new ToolStripMenuItem("Follow Most Recently Active Window")
        {
            CheckOnClick = true,
            Checked = _config.BindingMode == "FollowMostRecent"
        };
        bindingItem.CheckedChanged += (_, _) =>
        {
            _config.BindingMode = bindingItem.Checked ? "FollowMostRecent" : "Sticky";
        };

        // Device mode submenu
        var applyToMenu = new ToolStripMenuItem("Apply To");

        var defaultDeviceItem = new ToolStripMenuItem("Default Device")
        {
            CheckOnClick = true,
            Checked = true
        };

        var allDevicesItem = new ToolStripMenuItem("All Active Devices")
        {
            CheckOnClick = true
        };

        defaultDeviceItem.CheckedChanged += (_, _) =>
        {
            if (!defaultDeviceItem.Checked) return;

            allDevicesItem.Checked = false;
            _engine.SetDeviceMode(Audio.DeviceMode.Default);
        };

        allDevicesItem.CheckedChanged += (_, _) =>
        {
            if (!allDevicesItem.Checked) return;

            defaultDeviceItem.Checked = false;
            _engine.SetDeviceMode(Audio.DeviceMode.All);
        };

        applyToMenu.DropDownItems.Add(defaultDeviceItem);
        applyToMenu.DropDownItems.Add(allDevicesItem);

        var openConfig = new ToolStripMenuItem("Open Config");
        openConfig.Click += (_, _) => OpenFile(Paths.ConfigFilePath);

        var openLog = new ToolStripMenuItem("Open Log");
        openLog.Click += (_, _) => OpenFile(Paths.LogFilePath);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Exit();

        menu.Items.Add(enableItem);
        menu.Items.Add(startupItem);
        menu.Items.Add(bindingItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(applyToMenu);
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
}
