using System;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private enum EngineEventType
    {
        RecomputeRequested,
        ApplyCurrentPositionsRequested,
        WinEvent,
        DisplaySettingsChanged,
        DeviceTopologyChanged
    }

    private readonly struct EngineEvent
    {
        public long Generation { get; }
        public EngineEventType Type { get; }
        public uint WinEventType { get; }
        public IntPtr Hwnd { get; }

        public EngineEvent(long generation, EngineEventType type, uint winEventType, IntPtr hwnd)
        {
            Generation = generation;
            Type = type;
            WinEventType = winEventType;
            Hwnd = hwnd;
        }
    }

    private readonly record struct TouchKey(string DeviceId, int Pid, string? SessionInstanceId);

    private readonly struct StereoPair
    {
        public float Left { get; }
        public float Right { get; }

        public StereoPair(float left, float right)
        {
            Left = left;
            Right = right;
        }
    }
}
