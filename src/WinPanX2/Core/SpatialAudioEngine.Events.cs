using System;
using System.Threading;
using WinPanX2.Windowing;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private void DrainEventQueue()
    {
        while (_eventQueue.TryDequeue(out _))
        {
        }
    }

    private void EnqueueEvent(EngineEventType type, uint winEventType, IntPtr hwnd)
    {
        var gen = Volatile.Read(ref _currentGeneration);
        if (gen <= 0)
            return;

        _eventQueue.Enqueue(new EngineEvent(gen, type, winEventType, hwnd));
        try
        {
            _workSignal.Set();
        }
        catch
        {
            // best-effort (can race with Dispose)
        }
    }

    private void RequestRecompute()
    {
        EnqueueEvent(EngineEventType.RecomputeRequested, 0, IntPtr.Zero);
    }

    public void NotifyDisplaySettingsChanged()
    {
        // Called on UI thread.
        if (!IsEnabled)
            return;

        EnqueueEvent(EngineEventType.DisplaySettingsChanged, 0, IntPtr.Zero);
    }

    private void OnWinEvent(uint eventType, IntPtr hWnd)
    {
        // Keep callback fast: filter noise and enqueue.
        if (!IsEnabled)
            return;

        // Only opened-window tracking needs these.
        if (eventType == NativeMethods.EVENT_OBJECT_CREATE
            || eventType == NativeMethods.EVENT_OBJECT_SHOW
            || eventType == NativeMethods.EVENT_OBJECT_HIDE
            || eventType == NativeMethods.EVENT_OBJECT_DESTROY)
        {
            if (!_openedModeEnabled)
                return;
        }

        // If no active sessions and we're not in opened mode, location updates don't matter.
        if (eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE)
        {
            if (_activeSessionCountApprox == 0 && !_openedModeEnabled)
                return;

            // Further narrowing: only wake on location changes for windows we currently track.
            if (!_trackedHwnds.ContainsKey(hWnd))
                return;
        }

        EnqueueEvent(EngineEventType.WinEvent, eventType, hWnd);
    }

    private void OnDeviceTopologyChanged()
    {
        if (!IsEnabled)
            return;

        EnqueueEvent(EngineEventType.DeviceTopologyChanged, 0, IntPtr.Zero);
    }
}
