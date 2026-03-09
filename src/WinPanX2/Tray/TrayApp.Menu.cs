using System;
using System.Diagnostics;
using WinPanX2.Config;
using WinPanX2.Core;
using WinPanX2.Startup;

namespace WinPanX2.Tray;

internal partial class TrayApp
{
    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var enableItem = CreateEnableMenuItem();
        var startupItem = CreateStartupMenuItem();
        var followModeMenu = CreateFollowModeMenuItem();
        var depthMenu = CreatePanningWidthMenuItem();
        var applyAllDevicesItem = CreateApplyAllDevicesMenuItem();
        var openConfig = CreateOpenFileMenuItem("Open Config", Paths.ConfigFilePath);
        var openLog = CreateOpenFileMenuItem("Open Log", Paths.LogFilePath);

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

    private ToolStripMenuItem CreateEnableMenuItem()
    {
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
        return enableItem;
    }

    private ToolStripMenuItem CreateStartupMenuItem()
    {
        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled()
        };
        startupItem.CheckedChanged += (_, _) =>
        {
            StartupManager.SetEnabled(startupItem.Checked);
        };
        return startupItem;
    }

    private ToolStripMenuItem CreateApplyAllDevicesMenuItem()
    {
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

        return applyAllDevicesItem;
    }

    private ToolStripMenuItem CreateFollowModeMenuItem()
    {
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

        return followModeMenu;
    }

    private ToolStripMenuItem CreatePanningWidthMenuItem()
    {
        // Panning width submenu (UI is inverted vs CenterBias)
        // Higher width => wider panning range.
        var depthMenu = new ToolStripMenuItem("Panning Width");

        var depthLow = new ToolStripMenuItem("Low") { CheckOnClick = true };
        var depthMedium = new ToolStripMenuItem("Medium") { CheckOnClick = true };
        var depthHigh = new ToolStripMenuItem("High") { CheckOnClick = true };
        var depthMax = new ToolStripMenuItem("Max") { CheckOnClick = true };

        void SetDepth(double depth)
            => ApplyDepthFromMenu(depth, depthLow, depthMedium, depthHigh, depthMax);

        depthLow.Click += (_, _) => { if (_initializing) return; SetDepth(0.2); };
        depthMedium.Click += (_, _) => { if (_initializing) return; SetDepth(0.45); };
        depthHigh.Click += (_, _) => { if (_initializing) return; SetDepth(0.7); };
        depthMax.Click += (_, _) => { if (_initializing) return; SetDepth(1.0); };

        InitializeDepthMenuChecks(depthLow, depthMedium, depthHigh, depthMax);

        depthMenu.DropDownItems.Add(depthLow);
        depthMenu.DropDownItems.Add(depthMedium);
        depthMenu.DropDownItems.Add(depthHigh);
        depthMenu.DropDownItems.Add(depthMax);

        return depthMenu;
    }

    private void ApplyDepthFromMenu(double depth, ToolStripMenuItem low, ToolStripMenuItem medium, ToolStripMenuItem high, ToolStripMenuItem max)
    {
        depth = Math.Clamp(depth, 0.0, 1.0);

        // CenterBias: 0 = no bias (widest), 1 = strongest bias (narrowest)
        _config.CenterBias = 1.0 - depth;
        UpdateDepthMenuChecks(depth, low, medium, high, max);

        _engine.ReapplyCurrentPositions();
        SaveConfig();
    }

    private void InitializeDepthMenuChecks(ToolStripMenuItem low, ToolStripMenuItem medium, ToolStripMenuItem high, ToolStripMenuItem max)
    {
        var currentDepth = DepthFromCenterBias(_config.CenterBias);
        UpdateDepthMenuChecks(currentDepth, low, medium, high, max);
    }

    private static double DepthFromCenterBias(double centerBias)
        => 1.0 - Math.Clamp(centerBias, 0.0, 1.0);

    private static void UpdateDepthMenuChecks(double depth, ToolStripMenuItem low, ToolStripMenuItem medium, ToolStripMenuItem high, ToolStripMenuItem max)
    {
        low.Checked = false;
        medium.Checked = false;
        high.Checked = false;
        max.Checked = false;

        if (depth >= 0.85)
            max.Checked = true;
        else if (depth >= 0.55)
            high.Checked = true;
        else if (depth >= 0.25)
            medium.Checked = true;
        else
            low.Checked = true;
    }

    private ToolStripMenuItem CreateOpenFileMenuItem(string label, string path)
    {
        var open = new ToolStripMenuItem(label);
        open.Click += (_, _) => OpenFile(path);
        return open;
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
}
