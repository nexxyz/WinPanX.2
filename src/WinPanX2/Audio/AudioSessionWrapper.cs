using System;
using System.Runtime.InteropServices;
using WinPanX2.Audio.Interop;

namespace WinPanX2.Audio;

internal sealed class AudioSessionWrapper : IDisposable
{
    public int ProcessId { get; }
    public IAudioChannelVolume ChannelVolume { get; }

    private bool _disposed;

    public AudioSessionWrapper(int pid, IAudioChannelVolume volume)
    {
        ProcessId = pid;
        ChannelVolume = volume;
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (ChannelVolume != null)
                Marshal.ReleaseComObject(ChannelVolume);
        }
        catch
        {
            // best-effort COM cleanup
        }
    }
}
