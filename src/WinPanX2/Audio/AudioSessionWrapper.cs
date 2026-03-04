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
        Control2.GetState(out var state);
        return state != AudioSessionState.Expired;
    }

    public bool HasStereoChannels()
    {
        ChannelVolume.GetChannelCount(out var count);
        return count >= 2;
    }

    public void SetStereo(float left, float right)
    {
        var ctx = Guid.Empty;
        ChannelVolume.SetChannelVolume(0, left, ctx);
        ChannelVolume.SetChannelVolume(1, right, ctx);
    }
}
