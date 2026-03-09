using System;
using System.Collections.Generic;
using System.Threading;
using WinPanX2.Audio;
using WinPanX2.Logging;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private bool ShouldThrottleLocationStorm(WaitHandle[] handles, long nowTickOuter, CoalescedEvents events)
    {
        if (!events.SawLocationChange)
            return false;
        if (events.SawForeground)
            return false;
        if (events.SawOpenedLifecycle)
            return false;
        if (events.TopologyChangedRequested)
            return false;

        var dt = nowTickOuter - _lastLocationRecomputeTick;
        if (dt >= 0 && dt < Timing.LocationStormMinIntervalMs)
        {
            var delay = (int)(Timing.LocationStormMinIntervalMs - dt);
            WaitHandle.WaitAny(handles, delay);
            return true;
        }

        _lastLocationRecomputeTick = nowTickOuter;
        return false;
    }

    private (HashSet<string> activeSessionExes, bool isOpenedMode) RecomputeActiveSessions(
        long nowTickOuter,
        long nowTick,
        VirtualDesktopMapper.Mapping mapping,
        CoalescedEvents events,
        Func<WindowResolver.Snapshot> captureSnapshot)
    {
        var activeSessionExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        UpdateExcludedSetIfChanged();

        var ctx = BuildRecomputeContext(nowTickOuter, nowTick, mapping, events, captureSnapshot);

        foreach (var (deviceId, sessions) in _activeSessionCache)
        {
            ProcessActiveSessionsForDevice(deviceId, sessions, activeSessionExes, in ctx);
        }

        return (activeSessionExes, ctx.IsOpenedMode);
    }

    private readonly struct RecomputeContext
    {
        public long NowTickOuter { get; }
        public long NowTick { get; }
        public VirtualDesktopMapper.Mapping Mapping { get; }
        public Func<WindowResolver.Snapshot> CaptureSnapshot { get; }
        public Dictionary<string, WindowInfo?>? OpenedModeCache { get; }
        public bool IsOpenedMode { get; }
        public HashSet<(string deviceId, int pid)>? DirtyKeys { get; }
        public bool CanUseSingleHwndRect { get; }
        public IntPtr LastLocationHwnd { get; }
        public IntPtr ForegroundHwnd { get; }
        public int ForegroundPid { get; }

        public RecomputeContext(
            long nowTickOuter,
            long nowTick,
            VirtualDesktopMapper.Mapping mapping,
            Func<WindowResolver.Snapshot> captureSnapshot,
            Dictionary<string, WindowInfo?>? openedModeCache,
            bool isOpenedMode,
            HashSet<(string deviceId, int pid)>? dirtyKeys,
            bool canUseSingleHwndRect,
            IntPtr lastLocationHwnd,
            IntPtr foregroundHwnd,
            int foregroundPid)
        {
            NowTickOuter = nowTickOuter;
            NowTick = nowTick;
            Mapping = mapping;
            CaptureSnapshot = captureSnapshot;
            OpenedModeCache = openedModeCache;
            IsOpenedMode = isOpenedMode;
            DirtyKeys = dirtyKeys;
            CanUseSingleHwndRect = canUseSingleHwndRect;
            LastLocationHwnd = lastLocationHwnd;
            ForegroundHwnd = foregroundHwnd;
            ForegroundPid = foregroundPid;
        }
    }

    private RecomputeContext BuildRecomputeContext(
        long nowTickOuter,
        long nowTick,
        VirtualDesktopMapper.Mapping mapping,
        CoalescedEvents events,
        Func<WindowResolver.Snapshot> captureSnapshot)
    {
        // If this wake is primarily a window move for a known HWND, avoid
        // enumerating all windows. We'll just GetWindowRect on that HWND.
        var canUseSingleHwndRect = events.SawLocationChange
                                   && events.LastLocationHwnd != IntPtr.Zero
                                   && !events.SawForeground
                                   && !events.SawOpenedLifecycle
                                   && !events.TopologyChangedRequested;

        var foregroundHwnd = IntPtr.Zero;
        var foregroundPid = 0;
        if (events.SawForeground && IsFollowMostRecentMode())
        {
            // Fast path for FollowMostRecent: foreground window drives binding.
            foregroundHwnd = NativeMethods.GetForegroundWindow();
            foregroundPid = TryGetWindowPid(foregroundHwnd);
        }

        HashSet<(string deviceId, int pid)>? dirtyKeys = null;
        if (!events.TopologyChangedRequested)
            dirtyKeys = ComputeDirtyKeys(nowTickOuter, events.SawForeground, events.SawOpenedLifecycle, events.LastLocationHwnd);

        var openedModeCache = CreateOpenedModeCacheIfNeeded();
        var isOpenedMode = openedModeCache != null;

        return new RecomputeContext(
            nowTickOuter,
            nowTick,
            mapping,
            captureSnapshot,
            openedModeCache,
            isOpenedMode,
            dirtyKeys,
            canUseSingleHwndRect,
            events.LastLocationHwnd,
            foregroundHwnd,
            foregroundPid);
    }

    private void ProcessActiveSessionsForDevice(
        string deviceId,
        List<AudioSessionWrapper> sessions,
        HashSet<string> activeSessionExes,
        in RecomputeContext ctx)
    {
        foreach (var session in sessions)
        {
            ProcessOneActiveSession(deviceId, session, activeSessionExes, in ctx);
        }
    }

    private void ProcessOneActiveSession(
        string deviceId,
        AudioSessionWrapper session,
        HashSet<string> activeSessionExes,
        in RecomputeContext ctx)
    {
        var key = (deviceId, session.ProcessId);
        if (ctx.DirtyKeys != null && ctx.DirtyKeys.Count > 0 && !ctx.DirtyKeys.Contains(key))
            return;

        if (!session.HasStereoChannels())
            return;

        var processName = ProcessHelper.GetProcessNameCached(session.ProcessId, ctx.NowTick);
        if (processName != null && _excludedSet.Contains(processName))
        {
            Logger.Debug($"Excluded process: {processName}");
            HandleExcludedSession(session, key);
            return;
        }

        if (!string.IsNullOrWhiteSpace(processName))
            activeSessionExes.Add(processName);

        _smoothedPanLastSeenTick[key] = ctx.NowTick;

        if (!TryResolveRectForSession(in ctx, key, session.ProcessId, out var rect, out var resolvedHwnd))
            return;

        UpdateResolutionTracking(key, resolvedHwnd);
        ApplyPanForSession(session, key, processName, rect, in ctx);
    }

    private bool TryResolveRectForSession(
        in RecomputeContext ctx,
        (string deviceId, int pid) key,
        int pid,
        out RECT rect,
        out IntPtr resolvedHwnd)
    {
        rect = default;
        resolvedHwnd = IntPtr.Zero;

        if (ctx.CanUseSingleHwndRect)
        {
            // Fast path: locationchange for a known, tracked hwnd.
            if (_lastResolvedHwnd.TryGetValue(key, out var lastHwnd)
                && lastHwnd == ctx.LastLocationHwnd
                && TryGetValidRect(ctx.LastLocationHwnd, out rect))
            {
                resolvedHwnd = ctx.LastLocationHwnd;
                return true;
            }

            if (_boundHwnd.TryGetValue(key, out var bound)
                && bound == ctx.LastLocationHwnd
                && TryGetValidRect(ctx.LastLocationHwnd, out rect))
            {
                resolvedHwnd = ctx.LastLocationHwnd;
                return true;
            }
        }

        // Fast path: FollowMostRecent and this pid owns the foreground window.
        if (ctx.ForegroundPid != 0
            && ctx.ForegroundPid == pid
            && TryGetValidRect(ctx.ForegroundHwnd, out rect))
        {
            resolvedHwnd = ctx.ForegroundHwnd;
            return true;
        }

        // Fallback: resolve using snapshot (captures only if needed).
        return TryGetWindowForSession(ctx.CaptureSnapshot, ctx.NowTick, ctx.OpenedModeCache, key, pid, out rect, out resolvedHwnd);
    }

    private void ApplyPanForSession(
        AudioSessionWrapper session,
        (string deviceId, int pid) key,
        string? processName,
        RECT rect,
        in RecomputeContext ctx)
    {
        var normalized = ComputeNormalizedForRect(rect, ctx.Mapping);
        var prev = GetOrSeedSmoothedPan(key, session, normalized, ctx.NowTick);
        var smoothed = SmoothPan(key, prev, normalized, ctx.NowTick);
        _smoothedPan[key] = smoothed;

        var (left, right) = ComputeStereoForNormalized(smoothed);
        TryApplyStereo(session, key, processName, left, right);
    }

    private void BackfillOpenedWindowsIfNeeded(
        HashSet<string> activeSessionExes,
        bool isOpenedMode,
        bool isHousekeepingDue,
        Func<WindowResolver.Snapshot> captureSnapshot,
        long nowTick)
    {
        if (activeSessionExes.Count == 0)
            return;

        // Opened-window tracking is primarily driven by WinEvents.
        // Safety net: in FollowMostRecentOpened mode, occasionally backfill using a snapshot.
        if (!isOpenedMode || !isHousekeepingDue)
            return;

        TrackOpenedWindows(captureSnapshot(), nowTick, activeSessionExes);
        _lastOpenedWindowTrackTick = nowTick;
    }
}
