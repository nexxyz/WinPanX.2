using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WinPanX2.Audio.Interop;
using NAudio.CoreAudioApi;

namespace WinPanX2.Audio;

internal sealed class AudioSessionManager : IDisposable
{
    private MMDevice? _device;
    private IAudioSessionManager2? _sessionManager;
    public string DeviceId { get; private set; } = string.Empty;

    public void InitializeForDevice(MMDevice device)
    {
        _device = device;

        if (_device == null)
        {
            DeviceId = "MOCK";
            return;
        }

        DeviceId = _device.ID;

        var naSessionManager = _device.AudioSessionManager;

        var unk = Marshal.GetIUnknownForObject(naSessionManager);
        try
        {
            var iid = typeof(IAudioSessionManager2).GUID;
            var hr = Marshal.QueryInterface(unk, ref iid, out var ptr);

            if (hr != 0 || ptr == IntPtr.Zero)
                throw new InvalidOperationException("Failed to acquire IAudioSessionManager2.");

            try
            {
                _sessionManager = (IAudioSessionManager2)Marshal.GetObjectForIUnknown(ptr);
            }
            finally
            {
                Marshal.Release(ptr);
            }
        }
        finally
        {
            Marshal.Release(unk);
        }
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

                    var unk = Marshal.GetIUnknownForObject(control2);
                    var iidVolume = typeof(IAudioChannelVolume).GUID;
                    var hr = Marshal.QueryInterface(unk, ref iidVolume, out var volumePtr);
                    Marshal.Release(unk);

                    if (hr != 0 || volumePtr == IntPtr.Zero)
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
