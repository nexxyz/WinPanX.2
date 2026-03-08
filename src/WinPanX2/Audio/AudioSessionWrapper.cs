using System;
using System.Runtime.InteropServices;
using WinPanX2.Audio.Interop;

namespace WinPanX2.Audio;

internal sealed class AudioSessionWrapper : IDisposable
{
    public int ProcessId { get; }
    public IAudioChannelVolume ChannelVolume { get; }
    public string? SessionInstanceId { get; }

    private bool _disposed;
    private uint? _channelCount;

    public AudioSessionWrapper(int pid, IAudioChannelVolume volume, string? sessionInstanceId)
    {
        ProcessId = pid;
        ChannelVolume = volume;
        SessionInstanceId = sessionInstanceId;
    }

    public bool HasStereoChannels()
    {
        if (_channelCount.HasValue)
            return _channelCount.Value >= 2;

        var hr = ChannelVolume.GetChannelCount(out var count);
        if (hr < 0)
        {
            _channelCount = 0;
            return false;
        }

        _channelCount = count;
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
