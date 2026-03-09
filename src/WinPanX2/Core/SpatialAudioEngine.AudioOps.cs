using System;
using System.Collections.Generic;
using System.Threading;
using WinPanX2.Audio;
using WinPanX2.Logging;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private void ResetTouchedSessions()
    {
        try
        {
            var touched = _lastAppliedStereo.Keys;
            if (touched.Count == 0)
                return;

            var nowTick = Environment.TickCount64;

            foreach (var manager in _deviceManagers.Values)
            {
                ResetTouchedSessionsForManager(manager, nowTick);
            }
        }
        catch { }
    }

    private void ResetTouchedSessionsForManager(AudioSessionManager manager, long nowTick)
    {
        var sessions = manager.GetAllSessions();
        try
        {
            foreach (var session in sessions)
            {
                if (!session.HasStereoChannels())
                    continue;

                TryRestoreTouchedSession(manager.DeviceId, session, nowTick);
            }
        }
        finally
        {
            foreach (var session in sessions)
                session.Dispose();
        }
    }

    private void TryRestoreTouchedSession(string deviceId, AudioSessionWrapper session, long nowTick)
    {
        // Reset only sessions we actually modified.
        var tk = new TouchKey(deviceId, session.ProcessId, session.SessionInstanceId);
        if (!_lastAppliedStereo.ContainsKey(tk) && session.SessionInstanceId != null)
            tk = new TouchKey(deviceId, session.ProcessId, null);

        if (!_lastAppliedStereo.ContainsKey(tk))
            return;

        if (tk.SessionInstanceId == null)
        {
            var pname = ProcessHelper.GetProcessNameCached(session.ProcessId, nowTick);
            if (!string.IsNullOrWhiteSpace(pname)
                && _touchProcessName.TryGetValue(tk, out var touchedName)
                && !string.Equals(touchedName, pname, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        if (_originalStereo.TryGetValue(tk, out var original))
            session.SetStereo(original.Left, original.Right);
        else
            session.SetStereo(1f, 1f);
    }

    // Force immediate recalculation without smoothing
    private void ApplyCurrentPositions()
    {
        var windowSnapshot = WindowResolver.CaptureSnapshot();
        var nowTick = Environment.TickCount64;
        var mapping = GetCurrentMapping(nowTick);

        var openedModeCache = CreateOpenedModeCacheIfNeeded();
        var isOpenedMode = openedModeCache != null;

        var activeSessionExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (deviceId, manager) in _deviceManagers)
        {
            ApplyCurrentPositionsForDevice(deviceId, manager, windowSnapshot, nowTick, mapping, openedModeCache, activeSessionExes);
        }

        if (activeSessionExes.Count > 0)
        {
            // Step 7: opened-window tracking is primarily WinEvent-driven.
            // For FollowMostRecentOpened, we still backfill from a full snapshot
            // when applying positions explicitly (e.g. on Start / mode changes).
            if (isOpenedMode)
            {
                TrackOpenedWindows(windowSnapshot, nowTick, activeSessionExes);
                _lastOpenedWindowTrackTick = nowTick;
            }
        }
    }

    private void ApplyCurrentPositionsForDevice(
        string deviceId,
        AudioSessionManager manager,
        WindowResolver.Snapshot windowSnapshot,
        long nowTick,
        VirtualDesktopMapper.Mapping mapping,
        Dictionary<string, WindowInfo?>? openedModeCache,
        HashSet<string> activeSessionExes)
    {
        var sessions = manager.GetActiveSessions();

        try
        {
            foreach (var session in sessions)
            {
                ApplyCurrentPositionForSession(deviceId, session, windowSnapshot, nowTick, mapping, openedModeCache, activeSessionExes);
            }
        }
        finally
        {
            foreach (var session in sessions)
                session.Dispose();
        }
    }

    private void ApplyCurrentPositionForSession(
        string deviceId,
        AudioSessionWrapper session,
        WindowResolver.Snapshot windowSnapshot,
        long nowTick,
        VirtualDesktopMapper.Mapping mapping,
        Dictionary<string, WindowInfo?>? openedModeCache,
        HashSet<string> activeSessionExes)
    {
        if (!session.HasStereoChannels())
            return;

        var processName = ProcessHelper.GetProcessNameCached(session.ProcessId, nowTick);
        var key = (deviceId, session.ProcessId);

        if (processName != null && _excludedSet.Contains(processName))
        {
            HandleExcludedSession(session, key);
            return;
        }

        if (!string.IsNullOrWhiteSpace(processName))
            activeSessionExes.Add(processName);

        _smoothedPanLastSeenTick[key] = nowTick;

        if (!TryGetWindowForSession(() => windowSnapshot, nowTick, openedModeCache, key, session.ProcessId, out var rect, out _))
            return;

        var normalized = ComputeNormalizedForRect(rect, mapping);
        var (left, right) = ComputeStereoForNormalized(normalized);

        TryApplyStereo(session, key, processName, left, right);

        _smoothedPan[key] = normalized;
        _smoothedPanLastUpdateTick[key] = nowTick;
    }

    private void PrePositionAllSessions(CancellationToken token, HashSet<(string deviceId, int pid)>? activeKeysToSkip = null)
    {
        try
        {
            var nowTick = Environment.TickCount64;
            var mapping = GetCurrentMapping(nowTick);

            WindowResolver.Snapshot? windowSnapshot = null;
            WindowResolver.Snapshot CaptureSnapshot() => windowSnapshot ??= WindowResolver.CaptureSnapshot();

            var openedModeCache = CreateOpenedModeCacheIfNeeded();

            foreach (var (deviceId, manager) in _deviceManagers)
            {
                if (token.IsCancellationRequested)
                    return;

                PrePositionDeviceSessions(token, nowTick, mapping, CaptureSnapshot, openedModeCache, deviceId, manager, activeKeysToSkip);
            }

            RefreshTrackedHwnds();
        }
        catch (Exception ex)
        {
            Logger.Error($"Pre-position failed: {ex.Message}");
        }
    }

    private void PrePositionDeviceSessions(
        CancellationToken token,
        long nowTick,
        VirtualDesktopMapper.Mapping mapping,
        Func<WindowResolver.Snapshot> captureSnapshot,
        Dictionary<string, WindowInfo?>? openedModeCache,
        string deviceId,
        AudioSessionManager manager,
        HashSet<(string deviceId, int pid)>? activeKeysToSkip)
    {
        var sessions = manager.GetAllSessions();
        try
        {
            foreach (var session in sessions)
            {
                if (token.IsCancellationRequested)
                    return;

                PrePositionOneSession(nowTick, mapping, captureSnapshot, openedModeCache, deviceId, session, activeKeysToSkip);
            }
        }
        finally
        {
            foreach (var session in sessions)
                session.Dispose();
        }
    }

    private void PrePositionOneSession(
        long nowTick,
        VirtualDesktopMapper.Mapping mapping,
        Func<WindowResolver.Snapshot> captureSnapshot,
        Dictionary<string, WindowInfo?>? openedModeCache,
        string deviceId,
        AudioSessionWrapper session,
        HashSet<(string deviceId, int pid)>? activeKeysToSkip)
    {
        if (!session.HasStereoChannels())
            return;

        var processName = ProcessHelper.GetProcessNameCached(session.ProcessId, nowTick);
        var key = (deviceId, session.ProcessId);

        // Don't stomp currently-active sessions; those are driven by the smoothed main loop.
        if (activeKeysToSkip != null && activeKeysToSkip.Contains(key))
            return;

        if (processName != null && _excludedSet.Contains(processName))
        {
            // Excluded sessions should be left untouched.
            HandleExcludedSession(session, key);
            return;
        }

        if (!TryResolveRectForPrePosition(captureSnapshot, nowTick, openedModeCache, key, session.ProcessId, out var rect))
            return;

        var normalized = ComputeNormalizedForRect(rect, mapping);
        var (left, right) = ComputeStereoForNormalized(normalized);

        TryApplyStereo(session, key, processName, left, right);

        _smoothedPan[key] = normalized;
        _smoothedPanLastSeenTick[key] = nowTick;
        _smoothedPanLastUpdateTick[key] = nowTick;
    }

    private bool TryResolveRectForPrePosition(
        Func<WindowResolver.Snapshot> captureSnapshot,
        long nowTick,
        Dictionary<string, WindowInfo?>? openedModeCache,
        (string deviceId, int pid) key,
        int pid,
        out RECT rect)
    {
        // Prefer the last known hwnd rect to avoid enumerating all windows.
        if (_lastResolvedHwnd.TryGetValue(key, out var lastHwnd)
            && lastHwnd != IntPtr.Zero
            && TryGetValidRect(lastHwnd, out rect))
        {
            return true;
        }

        if (_boundHwnd.TryGetValue(key, out var bound)
            && bound != IntPtr.Zero
            && TryGetValidRect(bound, out rect))
        {
            return true;
        }

        return TryGetWindowForSession(captureSnapshot, nowTick, openedModeCache, key, pid, out rect, out _);
    }

    private int GetActiveSessionCountApprox()
    {
        try
        {
            var count = 0;
            foreach (var kvp in _activeSessionCache)
                count += kvp.Value.Count;
            return count;
        }
        catch
        {
            return 0;
        }
    }
}
