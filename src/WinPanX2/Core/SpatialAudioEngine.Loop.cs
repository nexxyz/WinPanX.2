using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WinPanX2.Logging;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private void Loop(CancellationToken token, long generation)
    {
        var handles = new WaitHandle[] { token.WaitHandle, _workSignal };

        _lastHousekeepingTick = Environment.TickCount64;
        _lastSessionProbeTick = _lastHousekeepingTick;
        _lastNameCachePruneTick = _lastHousekeepingTick;
        _lastHealthLogTick = _lastHousekeepingTick;

        while (!token.IsCancellationRequested)
        {
            // Probe interval controls how quickly we detect new Active sessions.
            // Keep it relatively small for responsiveness; signature probing is lightweight.
            var probeIntervalMs = _deviceMode == Audio.DeviceMode.All ? Timing.ProbeIntervalAllDevicesModeMs : Timing.ProbeIntervalDefaultModeMs;
            var waitResult = WaitHandle.WaitAny(handles, probeIntervalMs);
            if (waitResult == 0)
                break;

            if (Volatile.Read(ref _currentGeneration) != generation)
                break;

            var nowTickOuter = Environment.TickCount64;

            // Update callback filters (volatile writes).
            _openedModeEnabled = IsFollowMostRecentOpenedMode();
            var isHousekeepingDue = nowTickOuter - _lastHousekeepingTick >= Timing.HousekeepingIntervalMs;
            var isProbeDue = nowTickOuter - _lastSessionProbeTick >= probeIntervalMs;

            var recomputeRequested = waitResult == 1; // wake by signal
            var applyNowRequested = false;
            var sawLocationChange = false;
            var sawForeground = false;
            var sawOpenedLifecycle = false;
            IntPtr lastLocationHwnd = IntPtr.Zero;

            // Drain and coalesce events (future-proofing: hook + notification storms)
            var topologyChangedRequested = false;

            while (_eventQueue.TryDequeue(out var ev))
            {
                if (ev.Generation != generation)
                    continue;

                if (ev.Type == EngineEventType.DeviceTopologyChanged)
                    topologyChangedRequested = true;
                else if (ev.Type == EngineEventType.ApplyCurrentPositionsRequested)
                {
                    applyNowRequested = true;
                    recomputeRequested = true;
                }
                else if (ev.Type == EngineEventType.DisplaySettingsChanged)
                {
                    _mappingDirty = true;
                    recomputeRequested = true;
                }
                else if (ev.Type == EngineEventType.WinEvent)
                {
                    recomputeRequested = true;

                    if (ev.WinEventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
                    {
                        sawForeground = true;
                        Interlocked.Increment(ref _winEventForegroundCount);
                    }
                    else if (ev.WinEventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE)
                    {
                        sawLocationChange = true;
                        lastLocationHwnd = ev.Hwnd;
                        Interlocked.Increment(ref _winEventLocationCount);
                        // For opened-window tracking, treat location changes as "seen" only
                        // if we're already tracking the HWND (avoid ballooning the maps).
                        if (ev.Hwnd != IntPtr.Zero)
                        {
                            var tick = nowTickOuter;
                            if (_windowLastSeenTick.ContainsKey(ev.Hwnd) || _windowFirstSeenTick.ContainsKey(ev.Hwnd))
                                _windowLastSeenTick[ev.Hwnd] = tick;
                        }
                    }
                    else if (ev.WinEventType == NativeMethods.EVENT_OBJECT_CREATE)
                    {
                        sawOpenedLifecycle = true;
                        Interlocked.Increment(ref _winEventCreateCount);
                        TryMarkOpenedWindowSeen(ev.Hwnd, nowTickOuter);
                    }
                    else if (ev.WinEventType == NativeMethods.EVENT_OBJECT_SHOW)
                    {
                        sawOpenedLifecycle = true;
                        Interlocked.Increment(ref _winEventShowCount);
                        TryMarkOpenedWindowSeen(ev.Hwnd, nowTickOuter);
                    }
                    else if (ev.WinEventType == NativeMethods.EVENT_OBJECT_HIDE)
                    {
                        sawOpenedLifecycle = true;
                        Interlocked.Increment(ref _winEventHideCount);
                        TryRemoveOpenedWindow(ev.Hwnd);
                    }
                    else if (ev.WinEventType == NativeMethods.EVENT_OBJECT_DESTROY)
                    {
                        sawOpenedLifecycle = true;
                        Interlocked.Increment(ref _winEventDestroyCount);
                        TryRemoveOpenedWindow(ev.Hwnd);
                    }
                }
            }

            if (topologyChangedRequested)
            {
                try
                {
                    HandleDeviceTopologyChangedInLoop(token);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Device topology rebuild failed: {ex.Message}");
                }
            }

            try
            {
                if (topologyChangedRequested)
                {
                    _activeSessionSignature = string.Empty;
                    _lastSessionProbeTick = nowTickOuter;
                    _lastHousekeepingTick = nowTickOuter;
                    recomputeRequested = true;
                }

                if (isProbeDue)
                {
                    var probeSig = ComputeActivePidSignatureProbe();
                    _lastSessionProbeTick = nowTickOuter;

                    if (!string.Equals(probeSig, _activeSessionSignature, StringComparison.Ordinal))
                    {
                        // Full refresh only when the active PID set changes.
                        RefreshActiveSessionCache();
                        _activeSessionSignature = probeSig;
                        _activeSessionCountApprox = GetActiveSessionCountApprox();
                        recomputeRequested = true;
                    }
                }

                if (isHousekeepingDue)
                {
                    // Backstop refresh to keep COM objects from going stale.
                    RefreshActiveSessionCache();
                    _activeSessionSignature = ComputeActivePidSignatureFromCache();
                    _activeSessionCountApprox = GetActiveSessionCountApprox();
                    _lastHousekeepingTick = nowTickOuter;

                    // Pre-position all sessions (inactive included) on housekeeping so
                    // window moves while paused still result in correct panning at resume.
                    var activeKeys = new HashSet<(string deviceId, int pid)>();
                    foreach (var kvp in _activeSessionCache)
                    {
                        foreach (var s in kvp.Value)
                            activeKeys.Add((kvp.Key, s.ProcessId));
                    }

                    PrePositionAllSessions(token, activeKeys);

                    var loc = Interlocked.Read(ref _winEventLocationCount);
                    var fg = Interlocked.Read(ref _winEventForegroundCount);
                    var create = Interlocked.Read(ref _winEventCreateCount);
                    var show = Interlocked.Read(ref _winEventShowCount);
                    var hide = Interlocked.Read(ref _winEventHideCount);
                    var destroy = Interlocked.Read(ref _winEventDestroyCount);

                    if (loc != _winEventLocationLogged || fg != _winEventForegroundLogged || create != _winEventCreateLogged || show != _winEventShowLogged || hide != _winEventHideLogged || destroy != _winEventDestroyLogged)
                    {
                        Logger.Debug($"[WinEvent] loc={loc} fg={fg} create={create} show={show} hide={hide} destroy={destroy}");
                        _winEventLocationLogged = loc;
                        _winEventForegroundLogged = fg;
                        _winEventCreateLogged = create;
                        _winEventShowLogged = show;
                        _winEventHideLogged = hide;
                        _winEventDestroyLogged = destroy;
                    }

                    if (nowTickOuter - _lastHealthLogTick >= Timing.HealthLogIntervalMs)
                    {
                        var activeCount = 0;
                        foreach (var kvp in _activeSessionCache)
                            activeCount += kvp.Value.Count;

                        Logger.Debug($"[Health] activeSessions={activeCount} smoothed={_smoothedPan.Count} bound={_boundHwnd.Count} lastResolved={_lastResolvedHwnd.Count} hwndMap={_hwndToKeys.Count} trackedHwnds={_trackedHwnds.Count} openedFirst={_windowFirstSeenTick.Count} openedLast={_windowLastSeenTick.Count} appliedStereo={_lastAppliedStereo.Count} originalStereo={_originalStereo.Count} nameCache={ProcessHelper.NameCacheCount}");
                        _lastHealthLogTick = nowTickOuter;
                    }
                }

                if (!recomputeRequested && !topologyChangedRequested)
                {
                    // Nothing to do besides periodic pruning.
                    var tick = nowTickOuter;

                    PruneSmoothedPanIfDue(tick, Timing.SmoothedPanPruneIntervalMs, Timing.SmoothedPanEntryTtlMs);
                    PruneOpenedWindowsIfDue(tick, Timing.OpenedWindowPruneIntervalMs, Timing.OpenedWindowEntryTtlMs);

                    if (tick - _lastNameCachePruneTick >= Timing.NameCachePruneIntervalMs)
                    {
                        ProcessHelper.PruneNameCache(tick);
                        _lastNameCachePruneTick = tick;
                    }

                    continue;
                }

                // If we are in a LOCATIONCHANGE storm, cap recompute rate to ~60-70Hz.
                if (sawLocationChange && !sawForeground && !sawOpenedLifecycle && !topologyChangedRequested)
                {
                    var dt = nowTickOuter - _lastLocationRecomputeTick;
                    if (dt >= 0 && dt < Timing.LocationStormMinIntervalMs)
                    {
                        var delay = (int)(Timing.LocationStormMinIntervalMs - dt);
                        WaitHandle.WaitAny(handles, delay);
                        continue;
                    }

                    _lastLocationRecomputeTick = nowTickOuter;
                }

                WindowResolver.Snapshot? windowSnapshot = null;
                WindowResolver.Snapshot CaptureSnapshot() => windowSnapshot ??= WindowResolver.CaptureSnapshot();

                var mapping = GetCurrentMapping(nowTickOuter);

                var nowTick = Environment.TickCount64;
                var shouldPrune = nowTick - _lastSmoothedPanPruneTick >= Timing.SmoothedPanPruneIntervalMs;
                var shouldPruneOpened = nowTick - _lastOpenedWindowPruneTick >= Timing.OpenedWindowPruneIntervalMs;

                if (applyNowRequested)
                {
                    ApplyCurrentPositions();
                    RefreshTrackedHwnds();
                    PruneSmoothedPanIfDue(nowTick, Timing.SmoothedPanPruneIntervalMs, Timing.SmoothedPanEntryTtlMs);
                    PruneOpenedWindowsIfDue(nowTick, Timing.OpenedWindowPruneIntervalMs, Timing.OpenedWindowEntryTtlMs);
                    continue;
                }

                var activeSessionExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // If this wake is primarily a window move for a known HWND, avoid
                // enumerating all windows. We'll just GetWindowRect on that HWND.
                var canUseSingleHwndRect = sawLocationChange
                                           && lastLocationHwnd != IntPtr.Zero
                                           && !sawForeground
                                           && !sawOpenedLifecycle
                                           && !topologyChangedRequested;

                var foregroundHwnd = IntPtr.Zero;
                var foregroundPid = 0;
                if (sawForeground && IsFollowMostRecentMode())
                {
                    // Fast path for FollowMostRecent: foreground window drives binding.
                    foregroundHwnd = NativeMethods.GetForegroundWindow();
                    foregroundPid = TryGetWindowPid(foregroundHwnd);
                }

                // Compute dirty session keys for targeted recompute.
                HashSet<(string deviceId, int pid)>? dirtyKeys = null;
                if (!topologyChangedRequested)
                {
                    dirtyKeys = ComputeDirtyKeys(nowTickOuter, sawForeground, sawOpenedLifecycle, lastLocationHwnd);
                }

                var openedModeCache = CreateOpenedModeCacheIfNeeded();
                var isOpenedMode = openedModeCache != null;

                UpdateExcludedSetIfChanged();

                foreach (var (deviceId, sessions) in _activeSessionCache)
                {
                    foreach (var session in sessions)
                    {
                        var key = (deviceId, session.ProcessId);
                        if (dirtyKeys != null && dirtyKeys.Count > 0 && !dirtyKeys.Contains(key))
                            continue;

                        if (!session.HasStereoChannels())
                            continue;

                        var processName = ProcessHelper.GetProcessNameCached(session.ProcessId, nowTick);
                        if (processName != null && _excludedSet.Contains(processName))
                        {
                            Logger.Debug($"Excluded process: {processName}");
                            HandleExcludedSession(session, key);
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(processName))
                            activeSessionExes.Add(processName);

                        _smoothedPanLastSeenTick[key] = nowTick;

                        RECT rect;
                        IntPtr resolvedHwnd;

                        // 1) Fast path: locationchange for a known, tracked hwnd.
                        if (canUseSingleHwndRect
                            && _lastResolvedHwnd.TryGetValue(key, out var lastHwnd)
                            && lastHwnd == lastLocationHwnd
                            && TryGetValidRect(lastLocationHwnd, out rect))
                        {
                            resolvedHwnd = lastLocationHwnd;
                        }
                        else if (canUseSingleHwndRect
                                 && _boundHwnd.TryGetValue(key, out var bound)
                                 && bound == lastLocationHwnd
                                 && TryGetValidRect(lastLocationHwnd, out rect))
                        {
                            resolvedHwnd = lastLocationHwnd;
                        }
                        // 2) Fast path: FollowMostRecent and this pid owns the foreground window.
                        else if (foregroundPid != 0
                                 && foregroundPid == session.ProcessId
                                 && TryGetValidRect(foregroundHwnd, out rect))
                        {
                            resolvedHwnd = foregroundHwnd;
                        }
                        // 3) Fallback: resolve using snapshot (captures only if needed).
                        else
                        {
                            if (!TryGetWindowForSession(CaptureSnapshot, nowTick, openedModeCache, key, session.ProcessId, out rect, out resolvedHwnd))
                                continue;
                        }

                        UpdateResolutionTracking(key, resolvedHwnd);

                        var normalized = ComputeNormalizedForRect(rect, mapping);

                        var prev = GetOrSeedSmoothedPan(key, session, normalized, nowTick);
                        var smoothed = SmoothPan(key, prev, normalized, nowTick);

                        _smoothedPan[key] = smoothed;

                        var (left, right) = ComputeStereoForNormalized(smoothed);

                        TryApplyStereo(session, key, processName, left, right);
                    }
                }

                // Update callback filter set (tracked HWNDs) from the current resolution map.
                RefreshTrackedHwnds();

                if (shouldPrune)
                    PruneSmoothedPanIfDue(nowTick, Timing.SmoothedPanPruneIntervalMs, Timing.SmoothedPanEntryTtlMs);

                if (activeSessionExes.Count > 0)
                {
                    // Step 7 prep: opened-window tracking is driven by WinEvents.
                    // Safety net: in FollowMostRecentOpened mode, occasionally backfill
                    // using a full snapshot on housekeeping.
                    if (isOpenedMode && isHousekeepingDue)
                    {
                        TrackOpenedWindows(CaptureSnapshot(), nowTick, activeSessionExes);
                        _lastOpenedWindowTrackTick = nowTick;
                    }
                }

                if (shouldPruneOpened)
                    PruneOpenedWindowsIfDue(nowTick, Timing.OpenedWindowPruneIntervalMs, Timing.OpenedWindowEntryTtlMs);
            }
            catch (Exception ex)
            {
                Logger.Error($"Engine loop error: {ex.Message}");
            }
        }
    }

    private void UpdateExcludedSetIfChanged()
    {
        // Refresh exclusion set only if config changed.
        var currentList = _config.ExcludedProcesses ?? new List<string>();
        var signature = string.Join('|', currentList.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

        if (string.Equals(signature, _excludedSignature, StringComparison.Ordinal))
            return;

        _excludedSet = new HashSet<string>(currentList, StringComparer.OrdinalIgnoreCase);
        _excludedSignature = signature;
        Logger.Debug($"ExcludedProcesses updated: [{signature}]");
    }

    private void PruneSmoothedPanIfDue(long nowTick, int pruneIntervalMs, int entryTtlMs)
    {
        if (nowTick - _lastSmoothedPanPruneTick < pruneIntervalMs)
            return;

        foreach (var kvp in _smoothedPanLastSeenTick)
        {
            if (nowTick - kvp.Value <= entryTtlMs)
                continue;

            _smoothedPanLastSeenTick.TryRemove(kvp.Key, out _);
            _smoothedPan.TryRemove(kvp.Key, out _);
            _smoothedPanLastUpdateTick.TryRemove(kvp.Key, out _);
            _boundHwnd.TryRemove(kvp.Key, out _);
        }

        _lastSmoothedPanPruneTick = nowTick;
    }

    private void PruneOpenedWindowsIfDue(long nowTick, int pruneIntervalMs, int entryTtlMs)
    {
        if (nowTick - _lastOpenedWindowPruneTick < pruneIntervalMs)
            return;

        PruneOpenedWindowTracking(nowTick, entryTtlMs);
        _lastOpenedWindowPruneTick = nowTick;
    }

    private string ComputeActivePidSignatureProbe()
    {
        try
        {
            var parts = new List<string>();
            foreach (var (deviceId, manager) in _deviceManagers)
            {
                List<int> pids;
                try
                {
                    pids = manager.GetActiveSessionPids();
                }
                catch
                {
                    continue;
                }

                if (pids.Count == 0)
                    continue;

                pids.Sort();
                parts.Add(deviceId + ":" + string.Join(',', pids));
            }

            parts.Sort(StringComparer.Ordinal);
            return string.Join("|", parts);
        }
        catch
        {
            return string.Empty;
        }
    }

    private string ComputeActivePidSignatureFromCache()
    {
        try
        {
            var parts = new List<string>();
            foreach (var (deviceId, sessions) in _activeSessionCache)
            {
                if (sessions.Count == 0)
                    continue;

                var pids = sessions.Select(s => s.ProcessId).OrderBy(p => p);
                parts.Add(deviceId + ":" + string.Join(',', pids));
            }

            parts.Sort(StringComparer.Ordinal);
            return string.Join("|", parts);
        }
        catch
        {
            return string.Empty;
        }
    }
}
