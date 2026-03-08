using System;
using System.Collections.Generic;
using WinPanX2.Config;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private void ClearOpenedWindowTracking()
    {
        _windowFirstSeenTick.Clear();
        _windowLastSeenTick.Clear();
        _lastOpenedWindowPruneTick = Environment.TickCount64;
        _lastOpenedWindowTrackTick = _lastOpenedWindowPruneTick;
    }

    private void ClearWindowResolutionTracking()
    {
        _lastResolvedHwnd.Clear();
        _hwndToKeys.Clear();
        _trackedHwnds.Clear();
        _lastForegroundPid = 0;
        _lastLocationRecomputeTick = 0;
    }

    private bool TryGetWindowForSession(
        Func<WindowResolver.Snapshot> captureSnapshot,
        long nowTick,
        Dictionary<string, WindowInfo?>? openedModeCache,
        (string deviceId, int pid) key,
        int pid,
        out RECT rect,
        out IntPtr resolvedHwnd)
    {
        rect = default;
        resolvedHwnd = IntPtr.Zero;

        var mode = _config.BindingMode;
        var followActive = BindingModes.IsFollowMostRecent(mode);
        var followOpened = BindingModes.IsFollowMostRecentOpened(mode);

        // Sticky means we keep the first valid binding for this (device, pid)
        // until the window becomes invalid (closed/invisible), then we rebind.
        if (!followActive && !followOpened)
        {
            if (_boundHwnd.TryGetValue(key, out var bound) && bound != IntPtr.Zero)
            {
                if (TryGetValidRect(bound, out rect))
                {
                    resolvedHwnd = bound;
                    return true;
                }

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
            var snapshot = captureSnapshot();
            if (TryGetMostRecentlyOpenedWindow(snapshot, nowTick, openedModeCache, pid, out var best))
            {
                rect = best.Rect;
                resolvedHwnd = best.Handle;
                return true;
            }

            // Fallback: resolve without requiring foreground.
            var fallback = snapshot.ResolveForProcess(pid, preferForeground: false);
            if (fallback == null)
                return false;

            rect = fallback.Rect;
            resolvedHwnd = fallback.Handle;
            return true;
        }

        // Follow most recently active window.
        var resolved = captureSnapshot().ResolveForProcess(pid, preferForeground: true);
        if (resolved == null)
            return false;

        if (!followActive)
            _boundHwnd[key] = resolved.Handle;

        rect = resolved.Rect;
        resolvedHwnd = resolved.Handle;
        return true;
    }

    private bool TryGetMostRecentlyOpenedWindow(WindowResolver.Snapshot snapshot, long nowTick, Dictionary<string, WindowInfo?>? openedModeCache, int sessionPid, out WindowInfo bestWindow)
    {
        bestWindow = null!;

        var exe = snapshot.GetProcessName(sessionPid);
        if (string.IsNullOrWhiteSpace(exe))
            return false;

        if (openedModeCache != null && openedModeCache.TryGetValue(exe, out var cached))
        {
            if (cached == null)
                return false;

            bestWindow = cached;
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

        bestWindow = best;
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

    private bool TryMarkOpenedWindowSeen(IntPtr hWnd, long nowTick)
    {
        if (hWnd == IntPtr.Zero)
            return false;

        try
        {
            if (!IsEligibleOpenedWindow(hWnd))
                return false;

            _windowFirstSeenTick.TryAdd(hWnd, nowTick);
            _windowLastSeenTick[hWnd] = nowTick;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryRemoveOpenedWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return false;

        var removed = false;
        removed |= _windowLastSeenTick.TryRemove(hWnd, out _);
        removed |= _windowFirstSeenTick.TryRemove(hWnd, out _);
        return removed;
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

    private HashSet<(string deviceId, int pid)> ComputeDirtyKeys(long nowTick, bool sawForeground, bool sawOpenedLifecycle, IntPtr locationHwnd)
    {
        // Default: if we can't infer, recompute all.
        var dirty = new HashSet<(string deviceId, int pid)>();

        var mode = _config.BindingMode;
        var followActive = BindingModes.IsFollowMostRecent(mode);
        var followOpened = BindingModes.IsFollowMostRecentOpened(mode);

        // Opened lifecycle events can change which window is selected.
        if (followOpened && sawOpenedLifecycle)
        {
            foreach (var (deviceId, sessions) in _activeSessionCache)
            {
                foreach (var s in sessions)
                    dirty.Add((deviceId, s.ProcessId));
            }

            return dirty;
        }

        // Foreground changes only matter for FollowMostRecent.
        if (followActive && sawForeground)
        {
            var pid = TryGetWindowPid(NativeMethods.GetForegroundWindow());
            if (pid > 0)
            {
                AddPidKeys(dirty, pid);
                if (_lastForegroundPid > 0 && _lastForegroundPid != pid)
                    AddPidKeys(dirty, _lastForegroundPid);
                _lastForegroundPid = pid;
                return dirty;
            }

            // Fallback if we can't resolve pid.
            return dirty;
        }

        if (locationHwnd != IntPtr.Zero)
        {
            if (_hwndToKeys.TryGetValue(locationHwnd, out var keys))
            {
                foreach (var k in keys)
                    dirty.Add(k);
                return dirty;
            }

            var pid = TryGetWindowPid(locationHwnd);
            if (pid > 0)
            {
                AddPidKeys(dirty, pid);
                return dirty;
            }
        }

        // If we got here, we couldn't target. Recompute all.
        foreach (var (deviceId, sessions) in _activeSessionCache)
        {
            foreach (var s in sessions)
                dirty.Add((deviceId, s.ProcessId));
        }

        return dirty;
    }

    private static int TryGetWindowPid(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return 0;

        try
        {
            _ = NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            return (int)pid;
        }
        catch
        {
            return 0;
        }
    }

    private void AddPidKeys(HashSet<(string deviceId, int pid)> dirty, int pid)
    {
        foreach (var (deviceId, sessions) in _activeSessionCache)
        {
            foreach (var s in sessions)
            {
                if (s.ProcessId == pid)
                    dirty.Add((deviceId, pid));
            }
        }
    }

    private void UpdateResolutionTracking((string deviceId, int pid) key, IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        if (_lastResolvedHwnd.TryGetValue(key, out var prev) && prev == hwnd)
            return;

        if (prev != IntPtr.Zero)
        {
            if (_hwndToKeys.TryGetValue(prev, out var set))
            {
                set.Remove(key);
                if (set.Count == 0)
                    _hwndToKeys.Remove(prev);
            }
        }

        _lastResolvedHwnd[key] = hwnd;
        if (!_hwndToKeys.TryGetValue(hwnd, out var keys))
        {
            keys = new HashSet<(string deviceId, int pid)>();
            _hwndToKeys[hwnd] = keys;
        }

        keys.Add(key);
    }

    private void RefreshTrackedHwnds()
    {
        // Rebuild from known bindings + last resolved hwnds.
        _trackedHwnds.Clear();

        foreach (var kvp in _boundHwnd)
        {
            if (kvp.Value != IntPtr.Zero)
                _trackedHwnds.TryAdd(kvp.Value, 0);
        }

        foreach (var kvp in _lastResolvedHwnd)
        {
            if (kvp.Value != IntPtr.Zero)
                _trackedHwnds.TryAdd(kvp.Value, 0);
        }
    }
}
