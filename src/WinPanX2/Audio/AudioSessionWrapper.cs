using System;
using WinPanX2.Audio.Interop;

namespace WinPanX2.Audio;

internal sealed class AudioSessionWrapper
{
    public int ProcessId { get; }
    public IAudioSessionControl2 Control2 { get; }
    public IAudioChannelVolume ChannelVolume { get; }

    public AudioSessionWrapper(int pid, IAudioSessionControl2 control2, IAudioChannelVolume volume)
    {
        ProcessId = pid;
        Control2 = control2;
        ChannelVolume = volume;
    }

    public bool IsActive()
    {
        var hr = Control2.GetState(out var state);
        if (hr < 0)
            return false;
        return state != AudioSessionState.Expired;
    }

    public bool HasStereoChannels()
    {
        var hr = ChannelVolume.GetChannelCount(out var count);
        if (hr < 0)
            return false;
        return count >= 2;
    }

    public void SetStereo(float left, float right)
    {
        var ctx = Guid.Empty;
        var hrL = ChannelVolume.SetChannelVolume(0, left, ctx);
        var hrR = ChannelVolume.SetChannelVolume(1, right, ctx);

        // Fail silently per-session to avoid killing engine loop
        if (hrL < 0 || hrR < 0)
        {
            // Do not throw here; engine loop should remain resilient
        }
    }
}
