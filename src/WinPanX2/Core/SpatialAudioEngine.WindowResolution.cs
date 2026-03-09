using System;
using System.Collections.Generic;
using WinPanX2.Config;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private void ClearWindowResolutionTracking()
    {
        _lastResolvedHwnd.Clear();
        _hwndToKeys.Clear();
        _trackedHwnds.Clear();
        _lastForegroundPid = 0;
        _lastLocationRecomputeTick = 0;
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
