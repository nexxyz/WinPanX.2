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
    private readonly ConcurrentDictionary<(string deviceId, int pid), long> _smoothedPanLastSeenTick = new();
    private long _lastSmoothedPanPruneTick;
    private HashSet<string> _excludedSet = new(StringComparer.OrdinalIgnoreCase);
    private string _excludedSignature = string.Empty;
    // Window handles are resolved fresh each loop to ensure dynamic tracking

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public bool IsEnabled { get; private set; }

    public void ReapplyCurrentPositions() => ApplyCurrentPositions();

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

            _excludedSet = new HashSet<string>(
                _config.ExcludedProcesses ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);

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

        _lastSmoothedPanPruneTick = Environment.TickCount64;

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
        try
        {
            _loopTask?.Wait(); // Ensure loop fully exits before proceeding
        }
        catch { }

        ResetAllSessions();
        _smoothedPan.Clear();
        _smoothedPanLastSeenTick.Clear();

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
        const int SmoothedPanPruneIntervalMs = 5000;
        const int SmoothedPanEntryTtlMs = 60_000;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var nowTick = Environment.TickCount64;
                var shouldPrune = nowTick - _lastSmoothedPanPruneTick >= SmoothedPanPruneIntervalMs;

                // Refresh exclusion set only if config changed
                var currentList = _config.ExcludedProcesses ?? new List<string>();
                var signature = string.Join('|',
                    currentList
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

                if (!string.Equals(signature, _excludedSignature, StringComparison.Ordinal))
                {
                    _excludedSet = new HashSet<string>(currentList, StringComparer.OrdinalIgnoreCase);
                    _excludedSignature = signature;
                    Logger.Debug($"ExcludedProcesses updated: [{signature}]");
                }

                foreach (var (deviceId, manager) in _deviceManagers)
                {
                    var sessions = manager.GetActiveSessions();

                    try
                    {
                        foreach (var session in sessions)
                        {
                            if (!session.HasStereoChannels())
                                continue;

                            var processName = ProcessHelper.GetProcessName(session.ProcessId);
                            if (processName != null && _excludedSet.Contains(processName))
                            {
                                Logger.Debug($"Excluded process: {processName}");
                                session.SetStereo(1f, 1f);
                                continue;
                            }

                            var key = (deviceId, session.ProcessId);
                            _smoothedPanLastSeenTick[key] = nowTick;

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

                            var biased = ApplyCenterBias(smoothed, _config.CenterBias);
                            biased *= Math.Clamp(_config.MaxPan, 0.0, 1.0);

                            var angle = (biased + 1.0) * Math.PI / 4.0;
                            var left = (float)Math.Cos(angle);
                            var right = (float)Math.Sin(angle);

                            session.SetStereo(left, right);
                        }
                    }
                    finally
                    {
                        foreach (var session in sessions)
                            session.Dispose();
                    }
                }

                if (shouldPrune)
                {
                    foreach (var kvp in _smoothedPanLastSeenTick)
                    {
                        if (nowTick - kvp.Value <= SmoothedPanEntryTtlMs)
                            continue;

                        _smoothedPanLastSeenTick.TryRemove(kvp.Key, out _);
                        _smoothedPan.TryRemove(kvp.Key, out _);
                    }

                    _lastSmoothedPanPruneTick = nowTick;
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
                try
                {
                    foreach (var session in sessions)
                    {
                        if (session.HasStereoChannels())
                            session.SetStereo(1f, 1f);
                    }
                }
                finally
                {
                    foreach (var session in sessions)
                        session.Dispose();
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
        _smoothedPanLastSeenTick.Clear();

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
            var nowTick = Environment.TickCount64;

            try
            {
                foreach (var session in sessions)
                {
                    if (!session.HasStereoChannels())
                        continue;

                    var processName = ProcessHelper.GetProcessName(session.ProcessId);
                    if (processName != null && _excludedSet.Contains(processName))
                    {
                        session.SetStereo(1f, 1f);
                        continue;
                    }

                    var resolved = WindowResolver.ResolveForProcess(
                        session.ProcessId,
                        _config.BindingMode == "FollowMostRecent");

                    if (resolved == null)
                        continue;

                    var normalized = VirtualDesktopMapper.MapToNormalized(resolved.CenterX);

                    var biased = ApplyCenterBias(normalized, _config.CenterBias);
                    biased *= Math.Clamp(_config.MaxPan, 0.0, 1.0);

                    var angle = (biased + 1.0) * Math.PI / 4.0;
                    var left = (float)Math.Cos(angle);
                    var right = (float)Math.Sin(angle);

                    session.SetStereo(left, right);

                    var key = (deviceId, session.ProcessId);
                    _smoothedPan[key] = normalized;
                    _smoothedPanLastSeenTick[key] = nowTick;
                }
            }
            finally
            {
                foreach (var session in sessions)
                    session.Dispose();
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
        _smoothedPanLastSeenTick.Clear();

        Start();
    }

    private static double ApplyCenterBias(double normalized, double bias)
    {
        var x = Math.Clamp(normalized, -1.0, 1.0);
        var b = Math.Clamp(bias, 0.0, 1.0);
        if (b <= 0.0)
            return x;

        var ax = Math.Abs(x);

        // Combine two effects:
        // - Nonlinear curve pulls midpoints toward center
        // - Max magnitude cap prevents hard panning even at screen edges
        // Tuned so the tray presets (Medium/Strong) are clearly noticeable.
        var exp = 1.0 + (b * 6.0);          // b=1 -> exp=7 (very strong pull)
        var maxMag = 1.0 - (b * 0.8);       // b=1 -> 0.2 (hard cap)

        var y = Math.Pow(ax, exp) * maxMag;
        return x < 0 ? -y : y;
    }
}
