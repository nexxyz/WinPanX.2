using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    private readonly ConcurrentDictionary<(string deviceId, int pid), IntPtr> _boundHwnd = new();

    // Used only for FollowMostRecentOpened mode.
    private readonly ConcurrentDictionary<IntPtr, long> _windowFirstSeenTick = new();
    private readonly ConcurrentDictionary<IntPtr, long> _windowLastSeenTick = new();
    private long _lastOpenedWindowPruneTick;
    private long _lastOpenedWindowTrackTick;
    private long _lastSmoothedPanPruneTick;
    private HashSet<string> _excludedSet = new(StringComparer.OrdinalIgnoreCase);
    private string _excludedSignature = string.Empty;
    // Window handles are resolved fresh each loop to ensure dynamic tracking

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public bool IsEnabled { get; private set; }

    public void ReapplyCurrentPositions() => ApplyCurrentPositions();

    public void ClearWindowBindings() => _boundHwnd.Clear();

    private void ClearOpenedWindowTracking()
    {
        _windowFirstSeenTick.Clear();
        _windowLastSeenTick.Clear();
        _lastOpenedWindowPruneTick = Environment.TickCount64;
        _lastOpenedWindowTrackTick = _lastOpenedWindowPruneTick;
    }

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
        _boundHwnd.Clear();
        ClearOpenedWindowTracking();

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
        const int OpenedWindowPruneIntervalMs = 5000;
        const int OpenedWindowEntryTtlMs = 120_000;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var windowSnapshot = WindowResolver.CaptureSnapshot();
                var mapping = VirtualDesktopMapper.Capture();

                var nowTick = Environment.TickCount64;
                var shouldPrune = nowTick - _lastSmoothedPanPruneTick >= SmoothedPanPruneIntervalMs;
                var shouldPruneOpened = nowTick - _lastOpenedWindowPruneTick >= OpenedWindowPruneIntervalMs;

                var activeSessionExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                Dictionary<string, WindowInfo?>? openedModeCache = null;
                var isOpenedMode = string.Equals(_config.BindingMode, "FollowMostRecentOpened", StringComparison.Ordinal);
                if (isOpenedMode)
                    openedModeCache = new Dictionary<string, WindowInfo?>(StringComparer.OrdinalIgnoreCase);

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

                            if (!string.IsNullOrWhiteSpace(processName))
                                activeSessionExes.Add(processName);

                            var key = (deviceId, session.ProcessId);
                            _smoothedPanLastSeenTick[key] = nowTick;

                            if (!TryGetWindowForSession(windowSnapshot, nowTick, openedModeCache, key, session.ProcessId, out var rect))
                                continue;

                            var centerX = rect.Left + (rect.Right - rect.Left) / 2;
                            var normalized = VirtualDesktopMapper.MapToNormalized(centerX, mapping);

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
                        _boundHwnd.TryRemove(kvp.Key, out _);
                    }

                    _lastSmoothedPanPruneTick = nowTick;
                }

                // Track eligible windows continuously so FollowMostRecentOpened behaves
                // consistently even when enabled after windows already exist.
                if (activeSessionExes.Count > 0)
                {
                    // In Opened mode, track every tick for responsiveness.
                    // Otherwise, track at a lower cadence to reduce overhead.
                    var interval = isOpenedMode ? 0 : 250;
                    if (nowTick - _lastOpenedWindowTrackTick >= interval)
                    {
                        TrackOpenedWindows(windowSnapshot, nowTick, activeSessionExes);
                        _lastOpenedWindowTrackTick = nowTick;
                    }
                }

                if (shouldPruneOpened)
                {
                    PruneOpenedWindowTracking(nowTick, OpenedWindowEntryTtlMs);
                    _lastOpenedWindowPruneTick = nowTick;
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
        var windowSnapshot = WindowResolver.CaptureSnapshot();
        var mapping = VirtualDesktopMapper.Capture();
        var nowTick = Environment.TickCount64;

        Dictionary<string, WindowInfo?>? openedModeCache = null;
        var isOpenedMode = string.Equals(_config.BindingMode, "FollowMostRecentOpened", StringComparison.Ordinal);
        if (isOpenedMode)
            openedModeCache = new Dictionary<string, WindowInfo?>(StringComparer.OrdinalIgnoreCase);

        var activeSessionExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                        session.SetStereo(1f, 1f);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(processName))
                        activeSessionExes.Add(processName);

                    var key = (deviceId, session.ProcessId);
                    _smoothedPanLastSeenTick[key] = nowTick;

                    if (!TryGetWindowForSession(windowSnapshot, nowTick, openedModeCache, key, session.ProcessId, out var rect))
                        continue;

                    var centerX = rect.Left + (rect.Right - rect.Left) / 2;
                    var normalized = VirtualDesktopMapper.MapToNormalized(centerX, mapping);

                    var biased = ApplyCenterBias(normalized, _config.CenterBias);
                    biased *= Math.Clamp(_config.MaxPan, 0.0, 1.0);

                    var angle = (biased + 1.0) * Math.PI / 4.0;
                    var left = (float)Math.Cos(angle);
                    var right = (float)Math.Sin(angle);

                    session.SetStereo(left, right);

                    _smoothedPan[key] = normalized;
                }
            }
            finally
            {
                foreach (var session in sessions)
                    session.Dispose();
            }
        }

        if (activeSessionExes.Count > 0)
        {
            var interval = isOpenedMode ? 0 : 250;
            if (nowTick - _lastOpenedWindowTrackTick >= interval)
            {
                TrackOpenedWindows(windowSnapshot, nowTick, activeSessionExes);
                _lastOpenedWindowTrackTick = nowTick;
            }
        }
    }

    private bool TryGetWindowForSession(WindowResolver.Snapshot snapshot, long nowTick, Dictionary<string, WindowInfo?>? openedModeCache, (string deviceId, int pid) key, int pid, out RECT rect)
    {
        rect = default;

        var mode = _config.BindingMode;
        var followActive = string.Equals(mode, "FollowMostRecent", StringComparison.Ordinal);
        var followOpened = string.Equals(mode, "FollowMostRecentOpened", StringComparison.Ordinal);

        // Sticky means we keep the first valid binding for this (device, pid)
        // until the window becomes invalid (closed/invisible), then we rebind.
        if (!followActive && !followOpened)
        {
            if (_boundHwnd.TryGetValue(key, out var bound) && bound != IntPtr.Zero)
            {
                if (TryGetValidRect(bound, out rect))
                    return true;

                _boundHwnd.TryRemove(key, out _);
            }
        }

        // We resolve when:
        // - follow mode is enabled (always), OR
        // - sticky mode has no valid binding yet (first bind/rebind)
        // Even in Sticky mode, picking the foreground window for the initial bind
        // avoids accidentally binding to an arbitrary same-exe window.
        if (followOpened)
        {
            if (TryGetMostRecentlyOpenedWindowRect(snapshot, nowTick, openedModeCache, pid, out rect))
                return true;

            // Fallback: resolve without requiring foreground.
            var fallback = snapshot.ResolveForProcess(pid, preferForeground: false);
            if (fallback == null)
                return false;

            rect = fallback.Rect;
            return true;
        }

        // Follow most recently active window.
        var resolved = snapshot.ResolveForProcess(pid, preferForeground: true);
        if (resolved == null)
            return false;

        if (!followActive)
            _boundHwnd[key] = resolved.Handle;

        rect = resolved.Rect;
        return true;
    }

    private bool TryGetMostRecentlyOpenedWindowRect(WindowResolver.Snapshot snapshot, long nowTick, Dictionary<string, WindowInfo?>? openedModeCache, int sessionPid, out RECT rect)
    {
        rect = default;

        var exe = snapshot.GetProcessName(sessionPid);
        if (string.IsNullOrWhiteSpace(exe))
            return false;

        if (openedModeCache != null && openedModeCache.TryGetValue(exe, out var cached))
        {
            if (cached == null)
                return false;

            rect = cached.Rect;
            return true;
        }

        WindowInfo? best = null;
        long bestFirstSeen = long.MinValue;
        long bestHandle = long.MinValue;
        long bestArea = long.MinValue;

        foreach (var w in snapshot.Windows)
        {
            var wExe = snapshot.GetProcessName(w.ProcessId);
            if (wExe == null || !wExe.Equals(exe, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsEligibleOpenedWindow(w.Handle))
                continue;

            if (!_windowFirstSeenTick.TryGetValue(w.Handle, out var firstSeen))
            {
                firstSeen = nowTick;
                _windowFirstSeenTick.TryAdd(w.Handle, firstSeen);
            }

            _windowLastSeenTick[w.Handle] = nowTick;

            var width = w.Rect.Right - w.Rect.Left;
            var height = w.Rect.Bottom - w.Rect.Top;
            var area = (long)width * height;
            var handleVal = w.Handle.ToInt64();

            var pick = false;
            if (firstSeen > bestFirstSeen)
                pick = true;
            else if (firstSeen == bestFirstSeen)
            {
                // Tie-break: prefer the most recently created HWND (best-effort), then larger area.
                if (handleVal > bestHandle)
                    pick = true;
                else if (handleVal == bestHandle && area > bestArea)
                    pick = true;
            }

            if (pick)
            {
                bestFirstSeen = firstSeen;
                bestHandle = handleVal;
                bestArea = area;
                best = w;
            }
        }

        if (openedModeCache != null)
            openedModeCache[exe] = best;

        if (best == null)
            return false;

        rect = best.Rect;
        return true;
    }

    private void TrackOpenedWindows(WindowResolver.Snapshot snapshot, long nowTick, HashSet<string> activeSessionExes)
    {
        foreach (var w in snapshot.Windows)
        {
            var exe = snapshot.GetProcessName(w.ProcessId);
            if (exe == null || !activeSessionExes.Contains(exe))
                continue;

            if (!IsEligibleOpenedWindow(w.Handle))
                continue;

            _windowFirstSeenTick.TryAdd(w.Handle, nowTick);
            _windowLastSeenTick[w.Handle] = nowTick;
        }
    }

    private static bool IsEligibleOpenedWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return false;

        var owner = NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER);
        if (owner != IntPtr.Zero)
            return false;

        var exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
            return false;

        if ((exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0)
            return false;

        if (NativeMethods.TryIsCloaked(hWnd, out var cloaked) && cloaked)
            return false;

        return true;
    }

    private void PruneOpenedWindowTracking(long nowTick, int ttlMs)
    {
        // Remove any tracked windows that haven't been observed recently.
        // This keeps the dictionaries bounded even if windows come and go.
        var cutoff = nowTick - ttlMs;

        foreach (var kvp in _windowLastSeenTick)
        {
            if (kvp.Value >= cutoff)
                continue;

            _windowLastSeenTick.TryRemove(kvp.Key, out _);
            _windowFirstSeenTick.TryRemove(kvp.Key, out _);
        }
    }

    private static bool TryGetValidRect(IntPtr hWnd, out RECT rect)
    {
        rect = default;

        if (hWnd == IntPtr.Zero)
            return false;

        if (!NativeMethods.IsWindowVisible(hWnd))
            return false;

        if (!NativeMethods.GetWindowRect(hWnd, out rect))
            return false;

        if (rect.Right - rect.Left <= 0 || rect.Bottom - rect.Top <= 0)
            return false;

        return true;
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
