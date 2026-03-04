using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WinPanX2.Audio.Interop;
using NAudio.CoreAudioApi;

namespace WinPanX2.Audio;

internal sealed class AudioSessionManager : IDisposable
{
    private MMDevice? _device;
    // No custom session manager; use NAudio's AudioSessionManager
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

        // Activation handled by NAudio; no custom session manager required
    }

    public List<AudioSessionWrapper> GetActiveSessions()
    {
        var result = new List<AudioSessionWrapper>();

        var sessions = _device?.AudioSessionManager.Sessions;
        if (sessions == null)
            return result;

        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];

            var unkControl = Marshal.GetIUnknownForObject(session);
            try
            {
                var iidControl2 = typeof(IAudioSessionControl2).GUID;
                var hrControl = Marshal.QueryInterface(unkControl, ref iidControl2, out var ptrControl2);

                if (hrControl != 0 || ptrControl2 == IntPtr.Zero)
                    continue;

                try
                {
                    var control2 = (IAudioSessionControl2)Marshal.GetObjectForIUnknown(ptrControl2);
                    control2.GetProcessId(out var pid);

                    var iidVolume = typeof(IAudioChannelVolume).GUID;
                    var hrVolume = Marshal.QueryInterface(unkControl, ref iidVolume, out var ptrVolume);

                    if (hrVolume != 0 || ptrVolume == IntPtr.Zero)
                        continue;

                    try
                    {
                        var volume = (IAudioChannelVolume)Marshal.GetObjectForIUnknown(ptrVolume);
                        var wrapper = new AudioSessionWrapper((int)pid, control2, volume);

                        if (wrapper.IsActive())
                            result.Add(wrapper);
                    }
                    finally
                    {
                        Marshal.Release(ptrVolume);
                    }
                }
                finally
                {
                    Marshal.Release(ptrControl2);
                }
            }
            finally
            {
                Marshal.Release(unkControl);
            }
        }

        return result;
    }

    public void Dispose()
    {
        // No custom session manager to release
    }
}
