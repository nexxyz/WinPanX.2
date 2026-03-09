using System;
using System.Collections.Generic;
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

        var best = FindMostRecentlyOpenedWindow(snapshot, exe, nowTick);

        if (openedModeCache != null)
            openedModeCache[exe] = best;

        if (best == null)
            return false;

        bestWindow = best;
        return true;
    }

    private WindowInfo? FindMostRecentlyOpenedWindow(WindowResolver.Snapshot snapshot, string exe, long nowTick)
    {
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

            var firstSeen = GetOrSetOpenedFirstSeen(w.Handle, nowTick);
            _windowLastSeenTick[w.Handle] = nowTick;

            var width = w.Rect.Right - w.Rect.Left;
            var height = w.Rect.Bottom - w.Rect.Top;
            var area = (long)width * height;
            var handleVal = w.Handle.ToInt64();

            if (!IsBetterOpenedWindowCandidate(firstSeen, handleVal, area, bestFirstSeen, bestHandle, bestArea))
                continue;

            bestFirstSeen = firstSeen;
            bestHandle = handleVal;
            bestArea = area;
            best = w;
        }

        return best;
    }

    private long GetOrSetOpenedFirstSeen(IntPtr hWnd, long nowTick)
    {
        if (_windowFirstSeenTick.TryGetValue(hWnd, out var firstSeen))
            return firstSeen;

        firstSeen = nowTick;
        _windowFirstSeenTick.TryAdd(hWnd, firstSeen);
        return firstSeen;
    }

    private static bool IsBetterOpenedWindowCandidate(long firstSeen, long handleVal, long area, long bestFirstSeen, long bestHandle, long bestArea)
    {
        if (firstSeen > bestFirstSeen)
            return true;

        if (firstSeen < bestFirstSeen)
            return false;

        // Tie-break: prefer the most recently created HWND (best-effort), then larger area.
        if (handleVal > bestHandle)
            return true;
        if (handleVal < bestHandle)
            return false;

        return area > bestArea;
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
}
