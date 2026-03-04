using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using WinPanX2.Audio;
using WinPanX2.Config;
using WinPanX2.Logging;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal sealed class SpatialAudioEngine : IDisposable
{
    private readonly AppConfig _config;
    private readonly IAudioDeviceProvider _deviceProvider;
    private DeviceMode _deviceMode = DeviceMode.Default;

    private readonly Dictionary<string, AudioSessionManager> _deviceManagers = new();
    private readonly ConcurrentDictionary<(string deviceId, int pid), double> _smoothedPan = new();
    // Window handles are resolved fresh each loop to ensure dynamic tracking

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public bool IsEnabled { get; private set; }

    public SpatialAudioEngine(AppConfig config, IAudioDeviceProvider? provider = null)
    {
        _config = config;
        _deviceProvider = provider ?? new CoreAudioDeviceProvider();
    }

    public void Start()
    {
        Logger.Info($"Start() called. IsEnabled before: {IsEnabled}");

        if (IsEnabled)
        {
            Logger.Info("Start() exiting early because IsEnabled is already true.");
            return;
        }

        try
        {
            // Always rebuild device managers on Start to ensure fresh session attachment
            foreach (var manager in _deviceManagers.Values)
                manager.Dispose();

            _deviceManagers.Clear();

            InitializeDeviceManagers();

            if (_deviceProvider is CoreAudioDeviceProvider realProvider)
            {
                try
                {
                    realProvider.TopologyChanged += OnDeviceTopologyChanged;
                    realProvider.RegisterNotifications();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Notification registration failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Audio init failed: {ex}");
            return;
        }

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => Loop(_cts.Token), _cts.Token);

        IsEnabled = true;
        Logger.Info("Spatial engine started");

        // Immediately spatialize any currently active sessions
        ApplyCurrentPositions();
    }

    public void Stop()
    {
        Logger.Info($"Stop() called. IsEnabled before: {IsEnabled}");

        if (!IsEnabled)
        {
            Logger.Info("Stop() exiting early because IsEnabled is already false.");
            return;
        }

        _cts?.Cancel();
        try { _loopTask?.Wait(1000); } catch { }

        ResetAllSessions();
        _smoothedPan.Clear();

        if (_deviceProvider is CoreAudioDeviceProvider realProvider)
        {
            realProvider.TopologyChanged -= OnDeviceTopologyChanged;
            realProvider.UnregisterNotifications();
        }

        IsEnabled = false;
        Logger.Info("Spatial engine stopped");
    }

    private void Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                foreach (var (deviceId, manager) in _deviceManagers)
                {
                    var sessions = manager.GetActiveSessions();

                    foreach (var session in sessions)
                    {
                        if (!session.HasStereoChannels())
                            continue;

                        var key = (deviceId, session.ProcessId);

                        var resolved = WindowResolver.ResolveForProcess(
                            session.ProcessId,
                            _config.BindingMode == "FollowMostRecent");

                        if (resolved == null)
                            continue;

                        var hwnd = resolved.Handle;

                        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
                            continue;

                        var centerX = rect.Left + (rect.Right - rect.Left) / 2;
                        var normalized = VirtualDesktopMapper.MapToNormalized(centerX);

                        var prev = _smoothedPan.GetOrAdd(key, normalized);
                        var alpha = Math.Clamp(_config.SmoothingFactor, 0.0, 1.0);
                        var smoothed = prev + (normalized - prev) * alpha;

                        _smoothedPan[key] = smoothed;

                        var angle = (smoothed + 1.0) * Math.PI / 4.0;
                        var left = (float)Math.Cos(angle);
                        var right = (float)Math.Sin(angle);

                        session.SetStereo(left, right);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Engine loop error: {ex.Message}");
            }

            // Cancellation-aware wait instead of Thread.Sleep
            token.WaitHandle.WaitOne(_config.PollingIntervalMs);
        }
    }

    private void ResetAllSessions()
    {
        try
        {
            foreach (var manager in _deviceManagers.Values)
            {
                var sessions = manager.GetActiveSessions();
                foreach (var session in sessions)
                {
                    if (session.HasStereoChannels())
                        session.SetStereo(1f, 1f);
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();

        foreach (var manager in _deviceManagers.Values)
            manager.Dispose();

        _deviceManagers.Clear();
    }

    public void SetDeviceMode(DeviceMode mode)
    {
        Logger.Debug($"SetDeviceMode: {_deviceMode} -> {mode}, IsEnabled={IsEnabled}");
        if (_deviceMode == mode)
            return;

        var wasEnabled = IsEnabled;

        if (wasEnabled)
            Stop();

        _smoothedPan.Clear();

        _deviceMode = mode;

        if (wasEnabled)
        {
            Start();
            // Immediately recalculate spatial positions
            ApplyCurrentPositions();
        }
    }

    private void InitializeDeviceManagers()
    {
        Logger.Debug($"Initializing device managers. Mode={_deviceMode}");
        _deviceManagers.Clear();

        if (_deviceMode == DeviceMode.Default)
        {
            Logger.Info("[Init] Resolving default render device ID...");
            var deviceId = _deviceProvider.GetDefaultRenderDeviceId();
            Logger.Debug($"Default device ID: {deviceId}");
            Logger.Info($"[Init] Default device ID: {deviceId}");

            Logger.Info("[Init] Resolving device by ID...");
            var device = _deviceProvider.GetDeviceById(deviceId);
            Logger.Info("[Init] Device resolved.");

            var manager = new AudioSessionManager();

            Logger.Info("[Init] Activating session manager...");
            manager.InitializeForDevice(device);
            Logger.Info("[Init] Session manager activated.");

            _deviceManagers[deviceId] = manager;
            Logger.Debug($"Managers count after default init: {_deviceManagers.Count}");
        }
        else if (_deviceMode == DeviceMode.All)
        {
            var ids = _deviceProvider.GetActiveRenderDeviceIds().ToList();
            Logger.Debug($"All mode device IDs count: {ids.Count}");

            foreach (var deviceId in ids)
            {
                Logger.Info($"[Init] Resolving device by ID (All mode): {deviceId}");
                Logger.Debug($"All mode device ID: {deviceId}");
                var device = _deviceProvider.GetDeviceById(deviceId);

                var manager = new AudioSessionManager();
                Logger.Info("[Init] Activating session manager (All mode)...");
                manager.InitializeForDevice(device);
                Logger.Info("[Init] Session manager activated (All mode).");

                _deviceManagers[deviceId] = manager;
            }

            Logger.Debug($"Managers count after all init: {_deviceManagers.Count}");
        }
    }

    // Force immediate recalculation without smoothing
    private void ApplyCurrentPositions()
    {
        foreach (var (deviceId, manager) in _deviceManagers)
        {
            var sessions = manager.GetActiveSessions();

            foreach (var session in sessions)
            {
                if (!session.HasStereoChannels())
                    continue;

                var resolved = WindowResolver.ResolveForProcess(
                    session.ProcessId,
                    _config.BindingMode == "FollowMostRecent");

                if (resolved == null)
                    continue;

                var normalized = VirtualDesktopMapper.MapToNormalized(resolved.CenterX);

                var angle = (normalized + 1.0) * Math.PI / 4.0;
                var left = (float)Math.Cos(angle);
                var right = (float)Math.Sin(angle);

                session.SetStereo(left, right);

                var key = (deviceId, session.ProcessId);
                _smoothedPan[key] = normalized;
            }
        }
    }

    private void OnDeviceTopologyChanged()
    {
        if (!IsEnabled)
            return;

        Stop();

        foreach (var manager in _deviceManagers.Values)
            manager.Dispose();

        _deviceManagers.Clear();
        _smoothedPan.Clear();

        Start();
    }
}
