using System;
using System.Collections.Generic;
using WinPanX2.Logging;

namespace WinPanX2.Windowing;

internal sealed class WinEventHook : IDisposable
{
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly Action<uint, IntPtr> _onEvent;
    private readonly List<IntPtr> _hooks = new();
    private volatile bool _disposed;

    public WinEventHook(Action<uint, IntPtr> onEvent)
    {
        _onEvent = onEvent;
        _callback = OnWinEvent;

        // Use multiple single-event hooks to avoid capturing broad event ranges.
        // Out-of-context means user32 invokes the callback in our process.
        // Skip own process/thread reduces noise from our own UI.
        var flags = NativeMethods.WINEVENT_OUTOFCONTEXT
                    | NativeMethods.WINEVENT_SKIPOWNPROCESS
                    | NativeMethods.WINEVENT_SKIPOWNTHREAD;

        AddHook(NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, flags);
        AddHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, flags);

        // Used by FollowMostRecentOpened and general cleanup.
        AddHook(NativeMethods.EVENT_OBJECT_CREATE, flags);
        AddHook(NativeMethods.EVENT_OBJECT_SHOW, flags);
        AddHook(NativeMethods.EVENT_OBJECT_HIDE, flags);
        AddHook(NativeMethods.EVENT_OBJECT_DESTROY, flags);
    }

    private void AddHook(uint evt, uint flags)
    {
        try
        {
            var hook = NativeMethods.SetWinEventHook(
                evt,
                evt,
                IntPtr.Zero,
                _callback,
                0,
                0,
                flags);

            if (hook != IntPtr.Zero)
                _hooks.Add(hook);
            else
                Logger.Error($"SetWinEventHook failed for 0x{evt:X}");
        }
        catch (Exception ex)
        {
            Logger.Error($"SetWinEventHook exception for 0x{evt:X}: {ex.Message}");
        }
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hWnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (_disposed)
            return;

        if (hWnd == IntPtr.Zero)
            return;

        // Only react to real top-level window objects.
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != 0)
            return;

        try
        {
            _onEvent(eventType, hWnd);
        }
        catch
        {
            // Never throw from a WinEvent callback.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var hook in _hooks)
        {
            try
            {
                if (hook != IntPtr.Zero)
                    NativeMethods.UnhookWinEvent(hook);
            }
            catch
            {
                // best-effort
            }
        }

        _hooks.Clear();
    }
}
