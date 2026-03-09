using System.Threading;

namespace WinPanX2.Core;

internal sealed partial class SpatialAudioEngine
{
    private int GetProbeIntervalMs()
        => _deviceMode == Audio.DeviceMode.All ? Timing.ProbeIntervalAllDevicesModeMs : Timing.ProbeIntervalDefaultModeMs;

    private bool ShouldExitLoop(CancellationToken token, long generation, int waitResult)
    {
        if (token.IsCancellationRequested)
            return true;

        if (waitResult == 0)
            return true;

        return Volatile.Read(ref _currentGeneration) != generation;
    }
}
