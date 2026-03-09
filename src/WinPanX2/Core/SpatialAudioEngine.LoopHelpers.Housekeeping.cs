using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WinPanX2.Logging;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private bool ProbeActiveSessions(long nowTickOuter)
    {
        var probeSig = ComputeActivePidSignatureProbe();
        _lastSessionProbeTick = nowTickOuter;

        if (string.Equals(probeSig, _activeSessionSignature, StringComparison.Ordinal))
            return false;

        // Full refresh only when the active PID set changes.
        RefreshActiveSessionCache();
        _activeSessionSignature = probeSig;
        _activeSessionCountApprox = GetActiveSessionCountApprox();
        return true;
    }

    private void PerformHousekeeping(CancellationToken token, long nowTickOuter)
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

        LogWinEventCountsIfChanged();
        LogHealthIfDue(nowTickOuter);
    }

    private void LogWinEventCountsIfChanged()
    {
        var loc = Interlocked.Read(ref _winEventLocationCount);
        var fg = Interlocked.Read(ref _winEventForegroundCount);
        var create = Interlocked.Read(ref _winEventCreateCount);
        var show = Interlocked.Read(ref _winEventShowCount);
        var hide = Interlocked.Read(ref _winEventHideCount);
        var destroy = Interlocked.Read(ref _winEventDestroyCount);

        if (loc == _winEventLocationLogged
            && fg == _winEventForegroundLogged
            && create == _winEventCreateLogged
            && show == _winEventShowLogged
            && hide == _winEventHideLogged
            && destroy == _winEventDestroyLogged)
            return;

        Logger.Debug($"[WinEvent] loc={loc} fg={fg} create={create} show={show} hide={hide} destroy={destroy}");
        _winEventLocationLogged = loc;
        _winEventForegroundLogged = fg;
        _winEventCreateLogged = create;
        _winEventShowLogged = show;
        _winEventHideLogged = hide;
        _winEventDestroyLogged = destroy;
    }

    private void LogHealthIfDue(long nowTickOuter)
    {
        if (nowTickOuter - _lastHealthLogTick < Timing.HealthLogIntervalMs)
            return;

        var activeCount = 0;
        foreach (var kvp in _activeSessionCache)
            activeCount += kvp.Value.Count;

        Logger.Debug($"[Health] activeSessions={activeCount} smoothed={_smoothedPan.Count} bound={_boundHwnd.Count} lastResolved={_lastResolvedHwnd.Count} hwndMap={_hwndToKeys.Count} trackedHwnds={_trackedHwnds.Count} openedFirst={_windowFirstSeenTick.Count} openedLast={_windowLastSeenTick.Count} appliedStereo={_lastAppliedStereo.Count} originalStereo={_originalStereo.Count} nameCache={ProcessHelper.NameCacheCount}");
        _lastHealthLogTick = nowTickOuter;
    }

    private void HandleIdle(long tick)
    {
        PruneSmoothedPanIfDue(tick, Timing.SmoothedPanPruneIntervalMs, Timing.SmoothedPanEntryTtlMs);
        PruneOpenedWindowsIfDue(tick, Timing.OpenedWindowPruneIntervalMs, Timing.OpenedWindowEntryTtlMs);

        if (tick - _lastNameCachePruneTick >= Timing.NameCachePruneIntervalMs)
        {
            ProcessHelper.PruneNameCache(tick);
            _lastNameCachePruneTick = tick;
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
