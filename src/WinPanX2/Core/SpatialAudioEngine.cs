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
    private readonly ConcurrentDictionary<(string deviceId, int pid), IntPtr> _windowCache = new();

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
        if (IsEnabled) return;

        try
        {
            InitializeDeviceManagers();

            if (_deviceProvider is CoreAudioDeviceProvider realProvider)
            {
                realProvider.TopologyChanged += OnDeviceTopologyChanged;
                realProvider.RegisterNotifications();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Audio init failed: {ex.Message}");
            return;
        }

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => Loop(_cts.Token), _cts.Token);

        IsEnabled = true;
        Logger.Info("Spatial engine started");
    }

    public void Stop()
    {
        if (!IsEnabled) return;

        _cts?.Cancel();
        try { _loopTask?.Wait(1000); } catch { }

        ResetAllSessions();

        if (_deviceProvider is CoreAudioDeviceProvider realProvider)
        {
            realProvider.TopologyChanged -= OnDeviceTopologyChanged;
            realProvider.UnregisterNotifications();
        }

        foreach (var manager in _deviceManagers.Values)
            manager.Dispose();

        _deviceManagers.Clear();

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

                        if (!_windowCache.TryGetValue(key, out var hwnd) || hwnd == IntPtr.Zero)
                        {
                            var resolved = WindowResolver.ResolveForProcess(
                                session.ProcessId,
                                _config.BindingMode == "FollowMostRecent");

                            if (resolved == null)
                                continue;

                            hwnd = resolved.Handle;
                            _windowCache[key] = hwnd;
                        }

                        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
                        {
                            _windowCache.TryRemove(key, out _);
                            continue;
                        }

                        var centerX = rect.Left + (rect.Right - rect.Left) / 2;
                        var normalized = VirtualDesktopMapper.MapToNormalized(centerX);

                        var prev = _smoothedPan.GetOrAdd(key, normalized);
                        var alpha = Math.Clamp(_config.SmoothingFactor, 0.0, 1.0);
                        var smoothed = prev + (normalized - prev) * alpha;

                        _smoothedPan[key] = smoothed;

                        var angle = (smoothed + 1.0) * Math.PI / 4.0;
                        var left = (float)Math.Cos(angle);
                        var right = (float)Math.Sin(angle);

                        if (Math.Abs(smoothed - prev) > 0.01)
                            session.SetStereo(left, right);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Engine loop error: {ex.Message}");
            }

            Thread.Sleep(_config.PollingIntervalMs);
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
    }

    public void SetDeviceMode(DeviceMode mode)
    {
        if (_deviceMode == mode)
            return;

        var wasEnabled = IsEnabled;

        if (wasEnabled)
            Stop();

        _smoothedPan.Clear();
        _windowCache.Clear();

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
        _deviceManagers.Clear();

        if (_deviceMode == DeviceMode.Default)
        {
            var deviceId = _deviceProvider.GetDefaultRenderDeviceId();
            var device = _deviceProvider.GetDeviceById(deviceId);

            var manager = new AudioSessionManager();
            manager.InitializeForDevice(device);

            _deviceManagers[deviceId] = manager;
        }
        else if (_deviceMode == DeviceMode.All)
        {
            foreach (var deviceId in _deviceProvider.GetActiveRenderDeviceIds())
            {
                var device = _deviceProvider.GetDeviceById(deviceId);

                var manager = new AudioSessionManager();
                manager.InitializeForDevice(device);

                _deviceManagers[deviceId] = manager;
            }
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
                _windowCache[key] = resolved.Handle;
            }
        }
    }

    private void OnDeviceTopologyChanged()
    {
        if (!IsEnabled)
            return;

        Stop();
        _smoothedPan.Clear();
        _windowCache.Clear();
        Start();
    }
}
