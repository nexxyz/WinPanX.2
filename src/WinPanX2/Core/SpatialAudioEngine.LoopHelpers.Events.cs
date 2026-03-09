using System;
using System.Threading;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private readonly struct CoalescedEvents
    {
        public bool TopologyChangedRequested { get; }
        public bool ApplyNowRequested { get; }
        public bool SawLocationChange { get; }
        public bool SawForeground { get; }
        public bool SawOpenedLifecycle { get; }
        public IntPtr LastLocationHwnd { get; }

        public CoalescedEvents(
            bool topologyChangedRequested,
            bool applyNowRequested,
            bool sawLocationChange,
            bool sawForeground,
            bool sawOpenedLifecycle,
            IntPtr lastLocationHwnd)
        {
            TopologyChangedRequested = topologyChangedRequested;
            ApplyNowRequested = applyNowRequested;
            SawLocationChange = sawLocationChange;
            SawForeground = sawForeground;
            SawOpenedLifecycle = sawOpenedLifecycle;
            LastLocationHwnd = lastLocationHwnd;
        }
    }

    private CoalescedEvents DrainAndCoalesceEvents(long generation, long nowTickOuter, ref bool recomputeRequested)
    {
        var topologyChangedRequested = false;
        var applyNowRequested = false;
        var sawLocationChange = false;
        var sawForeground = false;
        var sawOpenedLifecycle = false;
        var lastLocationHwnd = IntPtr.Zero;

        while (_eventQueue.TryDequeue(out var ev))
        {
            if (ev.Generation != generation)
                continue;

            if (TryCoalesceNonWinEvent(ev, ref topologyChangedRequested, ref applyNowRequested, ref recomputeRequested))
                continue;

            if (ev.Type != EngineEventType.WinEvent)
                continue;

            recomputeRequested = true;
            CoalesceWinEvent(ev, nowTickOuter, ref sawForeground, ref sawLocationChange, ref lastLocationHwnd, ref sawOpenedLifecycle);
        }

        return new CoalescedEvents(
            topologyChangedRequested,
            applyNowRequested,
            sawLocationChange,
            sawForeground,
            sawOpenedLifecycle,
            lastLocationHwnd);
    }

    private bool TryCoalesceNonWinEvent(
        EngineEvent ev,
        ref bool topologyChangedRequested,
        ref bool applyNowRequested,
        ref bool recomputeRequested)
    {
        if (ev.Type == EngineEventType.DeviceTopologyChanged)
        {
            topologyChangedRequested = true;
            return true;
        }

        if (ev.Type == EngineEventType.ApplyCurrentPositionsRequested)
        {
            applyNowRequested = true;
            recomputeRequested = true;
            return true;
        }

        if (ev.Type == EngineEventType.DisplaySettingsChanged)
        {
            _mappingDirty = true;
            recomputeRequested = true;
            return true;
        }

        return false;
    }

    private void CoalesceWinEvent(
        EngineEvent ev,
        long nowTickOuter,
        ref bool sawForeground,
        ref bool sawLocationChange,
        ref IntPtr lastLocationHwnd,
        ref bool sawOpenedLifecycle)
    {
        if (ev.WinEventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
        {
            sawForeground = true;
            Interlocked.Increment(ref _winEventForegroundCount);
            return;
        }

        if (ev.WinEventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE)
        {
            sawLocationChange = true;
            lastLocationHwnd = ev.Hwnd;
            Interlocked.Increment(ref _winEventLocationCount);
            MarkOpenedWindowLocationSeenIfTracked(ev.Hwnd, nowTickOuter);
            return;
        }

        if (ev.WinEventType == NativeMethods.EVENT_OBJECT_CREATE)
        {
            sawOpenedLifecycle = true;
            Interlocked.Increment(ref _winEventCreateCount);
            TryMarkOpenedWindowSeen(ev.Hwnd, nowTickOuter);
            return;
        }

        if (ev.WinEventType == NativeMethods.EVENT_OBJECT_SHOW)
        {
            sawOpenedLifecycle = true;
            Interlocked.Increment(ref _winEventShowCount);
            TryMarkOpenedWindowSeen(ev.Hwnd, nowTickOuter);
            return;
        }

        if (ev.WinEventType == NativeMethods.EVENT_OBJECT_HIDE)
        {
            sawOpenedLifecycle = true;
            Interlocked.Increment(ref _winEventHideCount);
            TryRemoveOpenedWindow(ev.Hwnd);
            return;
        }

        if (ev.WinEventType == NativeMethods.EVENT_OBJECT_DESTROY)
        {
            sawOpenedLifecycle = true;
            Interlocked.Increment(ref _winEventDestroyCount);
            TryRemoveOpenedWindow(ev.Hwnd);
        }
    }

    private void MarkOpenedWindowLocationSeenIfTracked(IntPtr hWnd, long nowTick)
    {
        // For opened-window tracking, treat location changes as "seen" only
        // if we're already tracking the HWND (avoid ballooning the maps).
        if (hWnd == IntPtr.Zero)
            return;

        if (_windowLastSeenTick.ContainsKey(hWnd) || _windowFirstSeenTick.ContainsKey(hWnd))
            _windowLastSeenTick[hWnd] = nowTick;
    }
}
