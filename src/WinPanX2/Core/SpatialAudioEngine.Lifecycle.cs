using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinPanX2.Audio;
using WinPanX2.Config;
using WinPanX2.Logging;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private void ClearRuntimeStateAfterStop(bool clearOpenedTracking, bool clearActiveSessionCache)
    {
        _smoothedPan.Clear();
        _smoothedPanLastSeenTick.Clear();
        _smoothedPanLastUpdateTick.Clear();
        _boundHwnd.Clear();
        _lastAppliedStereo.Clear();
        _originalStereo.Clear();
        _touchProcessName.Clear();
        ClearWindowResolutionTracking();
        ClearMappingCache();

        if (clearOpenedTracking)
            ClearOpenedWindowTracking();

        if (clearActiveSessionCache)
            ClearActiveSessionCache();
    }

    public SpatialAudioEngine(AppConfig config, IAudioDeviceProvider? provider = null)
    {
        _config = config;
        _deviceProvider = provider ?? new CoreAudioDeviceProvider();
    }

    public bool IsEnabled { get; private set; }

    public void ReapplyCurrentPositions()
    {
        if (!IsEnabled)
            return;

        // Avoid cross-thread touching of engine state (UI thread -> loop thread).
        EnqueueEvent(EngineEventType.ApplyCurrentPositionsRequested, 0, IntPtr.Zero);
    }

    public void ClearWindowBindings() => _boundHwnd.Clear();

    public void Start()
    {
        lock (_lifecycleLock)
        {
            StartLocked();
        }
    }

    public void Stop()
    {
        lock (_lifecycleLock)
        {
            StopLocked();
        }
    }

    private void StartLocked()
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
            ClearActiveSessionCache();

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

            try
            {
                _winEventHook?.Dispose();
                _winEventHook = new WinEventHook(OnWinEvent);
            }
            catch (Exception ex)
            {
                Logger.Error($"WinEvent hook setup failed: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Audio init failed: {ex}");
            return;
        }

        var gen = Interlocked.Increment(ref _generation);
        Volatile.Write(ref _currentGeneration, gen);

        // Clear any stale, pre-start events.
        DrainEventQueue();

        _cts = new CancellationTokenSource();

        _lastSmoothedPanPruneTick = Environment.TickCount64;

        IsEnabled = true;
        Logger.Info("Spatial engine started");

        _openedModeEnabled = IsFollowMostRecentOpenedMode();

        // Pre-position all sessions (including inactive) once at startup so new audio
        // starts at the correct spatial position immediately.
        PrePositionAllSessions(CancellationToken.None);

        _loopTask = Task.Run(() => Loop(_cts.Token, gen), _cts.Token);

        RequestRecompute();
    }

    private void StopLocked()
    {
        Logger.Info($"Stop() called. IsEnabled before: {IsEnabled}");

        if (!IsEnabled)
        {
            Logger.Info("Stop() exiting early because IsEnabled is already false.");
            return;
        }

        // Invalidate any in-flight work and wake the loop.
        var gen = Interlocked.Increment(ref _generation);
        Volatile.Write(ref _currentGeneration, gen);

        try
        {
            _winEventHook?.Dispose();
            _winEventHook = null;
        }
        catch { }

        _cts?.Cancel();
        try
        {
            _workSignal.Set();
        }
        catch { }

        try
        {
            _loopTask?.Wait(); // Ensure loop fully exits before proceeding
        }
        catch { }

        // Drop any queued work from the prior generation.
        DrainEventQueue();

        ResetTouchedSessions();
        ClearRuntimeStateAfterStop(clearOpenedTracking: true, clearActiveSessionCache: true);

        if (_deviceProvider is CoreAudioDeviceProvider realProvider)
        {
            realProvider.TopologyChanged -= OnDeviceTopologyChanged;
            realProvider.UnregisterNotifications();
        }

        IsEnabled = false;
        _activeSessionCountApprox = 0;

        // Prevent event enqueues while stopped.
        Volatile.Write(ref _currentGeneration, 0);
        Logger.Info("Spatial engine stopped");
    }

    private void HandleDeviceTopologyChangedInLoop(CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        Logger.Info("[Topology] Change detected; rebuilding device managers");

        // Match previous behavior: stop affecting sessions as best-effort.
        ResetTouchedSessions();

        foreach (var manager in _deviceManagers.Values)
            manager.Dispose();

        _deviceManagers.Clear();
        ClearActiveSessionCache();

        ClearRuntimeStateAfterStop(clearOpenedTracking: true, clearActiveSessionCache: false);

        InitializeDeviceManagers();

        _lastSmoothedPanPruneTick = Environment.TickCount64;

        PrePositionAllSessions(token);
        ApplyCurrentPositions();
    }

    public void Dispose()
    {
        Stop();

        try
        {
            _workSignal.Dispose();
        }
        catch { }

        foreach (var manager in _deviceManagers.Values)
            manager.Dispose();

        _deviceManagers.Clear();
    }

    public void SetDeviceMode(DeviceMode mode)
    {
        lock (_lifecycleLock)
        {
            Logger.Debug($"SetDeviceMode: {_deviceMode} -> {mode}, IsEnabled={IsEnabled}");
            if (_deviceMode == mode)
                return;

            var wasEnabled = IsEnabled;

            if (wasEnabled)
                StopLocked();

            ClearRuntimeStateAfterStop(clearOpenedTracking: false, clearActiveSessionCache: false);

            _deviceMode = mode;

            if (wasEnabled)
            {
                StartLocked();
                // Immediately recalculate spatial positions
                ApplyCurrentPositions();
            }
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
}
