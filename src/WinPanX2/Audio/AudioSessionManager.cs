using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WinPanX2.Audio.Interop;
// no NAudio session usage

namespace WinPanX2.Audio;

internal sealed class AudioSessionManager : IDisposable
{
    private IMMDevice? _device;
    private IAudioSessionManager2? _sessionManager;
    public string DeviceId { get; private set; } = string.Empty;

    public void InitializeForDevice(IMMDevice device)
    {
        _device = device;

        if (_device == null)
        {
            DeviceId = "MOCK";
            return;
        }

        _device.GetId(out var id);
        DeviceId = id;

        // Acquire IMMDevice from MMDevice, then Activate IAudioSessionManager2
        var iid = typeof(IAudioSessionManager2).GUID;
        const uint CLSCTX_ALL = 0x17;

        _device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var obj);
        _sessionManager = (IAudioSessionManager2)obj;
    }

    public List<AudioSessionWrapper> GetActiveSessions()
    {
        var result = new List<AudioSessionWrapper>();

        if (_sessionManager == null)
            return result;

        _sessionManager.GetSessionEnumerator(out var enumerator);
        try
        {
            enumerator.GetCount(out var count);

            for (int i = 0; i < count; i++)
            {
                enumerator.GetSession(i, out var control);
                try
                {
                    var control2 = (IAudioSessionControl2)control;
                    control2.GetProcessId(out var pid);

                    var unkControl = Marshal.GetIUnknownForObject(control2);
                    var iidVolume = typeof(IAudioChannelVolume).GUID;
                    var hrVolume = Marshal.QueryInterface(unkControl, ref iidVolume, out var volumePtr);
                    Marshal.Release(unkControl);

                    if (hrVolume != 0 || volumePtr == IntPtr.Zero)
                        continue;

                    var volume = (IAudioChannelVolume)Marshal.GetObjectForIUnknown(volumePtr);
                    Marshal.Release(volumePtr);

                    var wrapper = new AudioSessionWrapper((int)pid, control2, volume);

                    if (wrapper.IsActive())
                        result.Add(wrapper);
                }
                finally
                {
                    if (control != null)
                        Marshal.ReleaseComObject(control);
                }
            }
        }
        finally
        {
            if (enumerator != null)
                Marshal.ReleaseComObject(enumerator);
        }

        return result;
    }

    public void Dispose()
    {
        if (_sessionManager != null)
            Marshal.ReleaseComObject(_sessionManager);
    }
}
