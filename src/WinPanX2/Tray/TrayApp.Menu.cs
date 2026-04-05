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
        var widthLimitMenu = CreateWidthLimitMenuItem();
        var centerBiasMenu = CreateCenterBiasMenuItem();
        var applyAllDevicesItem = CreateApplyAllDevicesMenuItem();
        var openConfig = CreateOpenFileMenuItem("Open Config", Paths.ConfigFilePath);
        var openLog = CreateOpenFileMenuItem("Open Log", Paths.LogFilePath);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Exit();

        menu.Items.Add(enableItem);
        menu.Items.Add(startupItem);
        menu.Items.Add(followModeMenu);
        menu.Items.Add(widthLimitMenu);
        menu.Items.Add(centerBiasMenu);
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

    private ToolStripMenuItem CreateWidthLimitMenuItem()
    {
        var widthLimitMenu = new ToolStripMenuItem("Width Limit");

        var width50 = new ToolStripMenuItem("50%") { CheckOnClick = true };
        var width65 = new ToolStripMenuItem("65%") { CheckOnClick = true };
        var width80 = new ToolStripMenuItem("80%") { CheckOnClick = true };
        var width90 = new ToolStripMenuItem("90%") { CheckOnClick = true };
        var width100 = new ToolStripMenuItem("100%") { CheckOnClick = true };
        var widthCustom = new ToolStripMenuItem("Custom (from config)")
        {
            CheckOnClick = true,
            Enabled = false,
            Visible = false
        };
        var widthCustomSeparator = new ToolStripSeparator { Visible = false };

        void SetWidthLimit(double maxPan)
            => ApplyWidthLimitFromMenu(maxPan, width50, width65, width80, width90, width100, widthCustom, widthCustomSeparator);

        width50.Click += (_, _) => { if (_initializing) return; SetWidthLimit(0.5); };
        width65.Click += (_, _) => { if (_initializing) return; SetWidthLimit(0.65); };
        width80.Click += (_, _) => { if (_initializing) return; SetWidthLimit(0.8); };
        width90.Click += (_, _) => { if (_initializing) return; SetWidthLimit(0.9); };
        width100.Click += (_, _) => { if (_initializing) return; SetWidthLimit(1.0); };

        InitializeWidthLimitMenuChecks(width50, width65, width80, width90, width100, widthCustom, widthCustomSeparator);

        widthLimitMenu.DropDownItems.Add(width50);
        widthLimitMenu.DropDownItems.Add(width65);
        widthLimitMenu.DropDownItems.Add(width80);
        widthLimitMenu.DropDownItems.Add(width90);
        widthLimitMenu.DropDownItems.Add(width100);
        widthLimitMenu.DropDownItems.Add(widthCustomSeparator);
        widthLimitMenu.DropDownItems.Add(widthCustom);

        return widthLimitMenu;
    }

    private void ApplyWidthLimitFromMenu(
        double maxPan,
        ToolStripMenuItem width50,
        ToolStripMenuItem width65,
        ToolStripMenuItem width80,
        ToolStripMenuItem width90,
        ToolStripMenuItem width100,
        ToolStripMenuItem custom,
        ToolStripSeparator customSeparator)
    {
        maxPan = Math.Clamp(maxPan, 0.0, 1.0);

        _config.MaxPan = maxPan;
        UpdateWidthLimitMenuChecks(maxPan, width50, width65, width80, width90, width100, custom, customSeparator);

        _engine.ReapplyCurrentPositions();
        SaveConfig();
    }

    private void InitializeWidthLimitMenuChecks(
        ToolStripMenuItem width50,
        ToolStripMenuItem width65,
        ToolStripMenuItem width80,
        ToolStripMenuItem width90,
        ToolStripMenuItem width100,
        ToolStripMenuItem custom,
        ToolStripSeparator customSeparator)
    {
        var current = Math.Clamp(_config.MaxPan, 0.0, 1.0);
        UpdateWidthLimitMenuChecks(current, width50, width65, width80, width90, width100, custom, customSeparator);
    }

    private static void UpdateWidthLimitMenuChecks(
        double maxPan,
        ToolStripMenuItem width50,
        ToolStripMenuItem width65,
        ToolStripMenuItem width80,
        ToolStripMenuItem width90,
        ToolStripMenuItem width100,
        ToolStripMenuItem custom,
        ToolStripSeparator customSeparator)
    {
        width50.Checked = false;
        width65.Checked = false;
        width80.Checked = false;
        width90.Checked = false;
        width100.Checked = false;
        custom.Checked = false;
        custom.Visible = false;
        customSeparator.Visible = false;

        var presetIndex = TrayMenuPresets.DetectWidthLimitPreset(maxPan);
        if (presetIndex == 0)
        {
            width50.Checked = true;
            return;
        }

        if (presetIndex == 1)
        {
            width65.Checked = true;
            return;
        }

        if (presetIndex == 2)
        {
            width80.Checked = true;
            return;
        }

        if (presetIndex == 3)
        {
            width90.Checked = true;
            return;
        }

        if (presetIndex == 4)
        {
            width100.Checked = true;
            return;
        }

        custom.Text = $"Custom (from config: {(int)Math.Round(maxPan * 100.0)}%)";
        custom.Checked = true;
        custom.Visible = true;
        customSeparator.Visible = true;
    }

    private ToolStripMenuItem CreateCenterBiasMenuItem()
    {
        var centerBiasMenu = new ToolStripMenuItem("Center Bias");

        var off = new ToolStripMenuItem("Off") { CheckOnClick = true };
        var low = new ToolStripMenuItem("Low") { CheckOnClick = true };
        var medium = new ToolStripMenuItem("Medium") { CheckOnClick = true };
        var high = new ToolStripMenuItem("High") { CheckOnClick = true };
        var custom = new ToolStripMenuItem("Custom (from config)")
        {
            CheckOnClick = true,
            Enabled = false,
            Visible = false
        };
        var customSeparator = new ToolStripSeparator { Visible = false };

        void SetCenterBias(double centerBias)
            => ApplyCenterBiasFromMenu(centerBias, off, low, medium, high, custom, customSeparator);

        off.Click += (_, _) => { if (_initializing) return; SetCenterBias(0.0); };
        low.Click += (_, _) => { if (_initializing) return; SetCenterBias(0.3); };
        medium.Click += (_, _) => { if (_initializing) return; SetCenterBias(0.55); };
        high.Click += (_, _) => { if (_initializing) return; SetCenterBias(0.8); };

        InitializeCenterBiasMenuChecks(off, low, medium, high, custom, customSeparator);

        centerBiasMenu.DropDownItems.Add(off);
        centerBiasMenu.DropDownItems.Add(low);
        centerBiasMenu.DropDownItems.Add(medium);
        centerBiasMenu.DropDownItems.Add(high);
        centerBiasMenu.DropDownItems.Add(customSeparator);
        centerBiasMenu.DropDownItems.Add(custom);

        return centerBiasMenu;
    }

    private void ApplyCenterBiasFromMenu(
        double centerBias,
        ToolStripMenuItem off,
        ToolStripMenuItem low,
        ToolStripMenuItem medium,
        ToolStripMenuItem high,
        ToolStripMenuItem custom,
        ToolStripSeparator customSeparator)
    {
        centerBias = Math.Clamp(centerBias, 0.0, 1.0);

        _config.CenterBias = centerBias;
        UpdateCenterBiasMenuChecks(centerBias, off, low, medium, high, custom, customSeparator);

        _engine.ReapplyCurrentPositions();
        SaveConfig();
    }

    private void InitializeCenterBiasMenuChecks(
        ToolStripMenuItem off,
        ToolStripMenuItem low,
        ToolStripMenuItem medium,
        ToolStripMenuItem high,
        ToolStripMenuItem custom,
        ToolStripSeparator customSeparator)
    {
        var current = Math.Clamp(_config.CenterBias, 0.0, 1.0);
        UpdateCenterBiasMenuChecks(current, off, low, medium, high, custom, customSeparator);
    }

    private static void UpdateCenterBiasMenuChecks(
        double centerBias,
        ToolStripMenuItem off,
        ToolStripMenuItem low,
        ToolStripMenuItem medium,
        ToolStripMenuItem high,
        ToolStripMenuItem custom,
        ToolStripSeparator customSeparator)
    {
        off.Checked = false;
        low.Checked = false;
        medium.Checked = false;
        high.Checked = false;
        custom.Checked = false;
        custom.Visible = false;
        customSeparator.Visible = false;

        var presetIndex = TrayMenuPresets.DetectCenterBiasPreset(centerBias);
        if (presetIndex == 0)
        {
            off.Checked = true;
            return;
        }

        if (presetIndex == 1)
        {
            low.Checked = true;
            return;
        }

        if (presetIndex == 2)
        {
            medium.Checked = true;
            return;
        }

        if (presetIndex == 3)
        {
            high.Checked = true;
            return;
        }

        custom.Text = $"Custom (from config: {(int)Math.Round(centerBias * 100.0)}%)";
        custom.Checked = true;
        custom.Visible = true;
        customSeparator.Visible = true;
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
