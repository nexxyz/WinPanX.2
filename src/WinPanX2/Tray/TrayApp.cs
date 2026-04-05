using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Drawing;
using WinPanX2.Config;
using WinPanX2.Core;
using WinPanX2.Startup;
using WinPanX2.Logging;
using Microsoft.Win32;

namespace WinPanX2.Tray;

internal partial class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Form _menuAnchor;
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

        _menuAnchor = CreateMenuAnchor();

        _notifyIcon.ContextMenuStrip = BuildMenu();
        if (_notifyIcon.ContextMenuStrip != null)
        {
            _notifyIcon.ContextMenuStrip.Closed += (_, _) =>
                PostMessage(_menuAnchor.Handle, WmNull, IntPtr.Zero, IntPtr.Zero);
        }

        SubscribeRuntimeEvents();

        // Show the context menu on left click as well.
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            ShowContextMenu();
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

        try { _menuAnchor.Dispose(); } catch { }

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

    private void ShowContextMenu()
    {
        var menu = _notifyIcon.ContextMenuStrip;
        if (menu == null)
            return;

        SetForegroundWindow(_menuAnchor.Handle);
        menu.Show(Cursor.Position);
    }

    private static Form CreateMenuAnchor()
    {
        var form = new Form
        {
            ShowInTaskbar = false,
            Opacity = 0,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(1, 1)
        };

        form.Load += (_, _) => form.Hide();
        form.Show();
        form.Hide();
        return form;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WmNull = 0x0000;
}
