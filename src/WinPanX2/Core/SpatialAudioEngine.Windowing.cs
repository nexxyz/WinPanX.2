using System;
using System.Collections.Generic;
using WinPanX2.Config;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
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
}
