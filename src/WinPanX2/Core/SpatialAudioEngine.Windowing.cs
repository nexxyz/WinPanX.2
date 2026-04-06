using System;
using System.Collections.Generic;
using WinPanX2.Config;
using WinPanX2.Logging;
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
        var sessionExe = ProcessHelper.GetProcessNameCached(pid, nowTick);

        var mode = _config.BindingMode;
        var followActive = BindingModes.IsFollowMostRecent(mode);
        var followOpened = BindingModes.IsFollowMostRecentOpened(mode);

        // Sticky means we keep the first valid binding for this (device, pid)
        // until the window becomes invalid (closed/invisible), then we rebind.
        if (!followActive && !followOpened)
        {
            var stickyRebindReason = string.Empty;

            if (_stickyBoundHwndByPid.TryGetValue(pid, out var stickyBound) && stickyBound != IntPtr.Zero)
            {
                if (TryGetValidRect(stickyBound, out rect))
                {
                    _boundHwnd[key] = stickyBound;
                    if (!string.IsNullOrWhiteSpace(sessionExe))
                        _stickyBoundHwndByExe[sessionExe] = stickyBound;
                    resolvedHwnd = stickyBound;
                    return true;
                }

                _stickyBoundHwndByPid.TryRemove(pid, out _);
                stickyRebindReason = "invalid-pid-binding";
            }
            else
            {
                stickyRebindReason = "missing-pid-binding";
            }

            if (_boundHwnd.TryGetValue(key, out var bound) && bound != IntPtr.Zero)
            {
                if (TryGetValidRect(bound, out rect))
                {
                    _stickyBoundHwndByPid[pid] = bound;
                    if (!string.IsNullOrWhiteSpace(sessionExe))
                        _stickyBoundHwndByExe[sessionExe] = bound;
                    resolvedHwnd = bound;
                    return true;
                }

                _boundHwnd.TryRemove(key, out _);
                if (string.IsNullOrWhiteSpace(stickyRebindReason))
                    stickyRebindReason = "invalid-device-binding";
            }

            if (_lastResolvedHwnd.TryGetValue(key, out var lastResolved) && lastResolved != IntPtr.Zero)
            {
                if (TryGetValidRect(lastResolved, out rect))
                {
                    _boundHwnd[key] = lastResolved;
                    _stickyBoundHwndByPid[pid] = lastResolved;
                    if (!string.IsNullOrWhiteSpace(sessionExe))
                        _stickyBoundHwndByExe[sessionExe] = lastResolved;
                    resolvedHwnd = lastResolved;
                    Logger.Debug($"[Sticky] pid={pid} exe={sessionExe ?? ""} rebound via last-resolved hwnd=0x{lastResolved.ToInt64():X} reason={stickyRebindReason}");
                    return true;
                }

                if (string.IsNullOrWhiteSpace(stickyRebindReason))
                    stickyRebindReason = "invalid-last-resolved";
            }

            if (!string.IsNullOrWhiteSpace(sessionExe)
                && _stickyBoundHwndByExe.TryGetValue(sessionExe, out var exeBound)
                && exeBound != IntPtr.Zero)
            {
                if (TryGetValidRect(exeBound, out rect))
                {
                    _boundHwnd[key] = exeBound;
                    _stickyBoundHwndByPid[pid] = exeBound;
                    resolvedHwnd = exeBound;
                    Logger.Debug($"[Sticky] pid={pid} exe={sessionExe} rebound via exe-cache hwnd=0x{exeBound.ToInt64():X} reason={stickyRebindReason}");
                    return true;
                }

                _stickyBoundHwndByExe.TryRemove(sessionExe, out _);
                if (string.IsNullOrWhiteSpace(stickyRebindReason))
                    stickyRebindReason = "invalid-exe-binding";
            }

            var stickyResolved = captureSnapshot().ResolveForProcess(pid, preferForeground: false);
            if (stickyResolved == null)
                return false;

            _boundHwnd[key] = stickyResolved.Handle;
            _stickyBoundHwndByPid[pid] = stickyResolved.Handle;
            if (!string.IsNullOrWhiteSpace(sessionExe))
                _stickyBoundHwndByExe[sessionExe] = stickyResolved.Handle;

            rect = stickyResolved.Rect;
            resolvedHwnd = stickyResolved.Handle;
            Logger.Debug($"[Sticky] pid={pid} exe={sessionExe ?? ""} rebound via resolver hwnd=0x{stickyResolved.Handle.ToInt64():X} reason={stickyRebindReason} preferFg=false");
            return true;
        }

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
        {
            _boundHwnd[key] = resolved.Handle;
            _stickyBoundHwndByPid[pid] = resolved.Handle;
            if (!string.IsNullOrWhiteSpace(sessionExe))
                _stickyBoundHwndByExe[sessionExe] = resolved.Handle;
        }

        rect = resolved.Rect;
        resolvedHwnd = resolved.Handle;
        return true;
    }
}
