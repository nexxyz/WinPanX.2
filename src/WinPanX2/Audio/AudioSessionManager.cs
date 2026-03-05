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

    private static void CheckHR(int hr)
    {
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    public void InitializeForDevice(IMMDevice device)
    {
        if (_sessionManager != null)
        {
            try { Marshal.ReleaseComObject(_sessionManager); } catch { }
            _sessionManager = null;
        }

        if (_device != null)
        {
            try { Marshal.ReleaseComObject(_device); } catch { }
            _device = null;
        }

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
        var hr = _device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var obj);
        CheckHR(hr);

        _sessionManager = (IAudioSessionManager2)obj
            ?? throw new InvalidOperationException("IAudioSessionManager2 activation returned null.");
    }

    public List<AudioSessionWrapper> GetActiveSessions()
    {
        var result = new List<AudioSessionWrapper>();

        if (_sessionManager == null)
            return result;

        var hrEnum = _sessionManager.GetSessionEnumerator(out var enumerator);
        CheckHR(hrEnum);
        try
        {
            CheckHR(enumerator.GetCount(out var count));

            for (int i = 0; i < count; i++)
            {
                var hrSession = enumerator.GetSession(i, out var control);
                if (hrSession < 0 || control == null)
                    continue;
                try
                {
                    var control2 = (IAudioSessionControl2)control;

                    var hrState = control2.GetState(out var state);
                    if (hrState < 0 || state == AudioSessionState.Expired)
                        continue;

                    CheckHR(control2.GetProcessId(out var pid));

                    var unkControl = Marshal.GetIUnknownForObject(control2);
                    var iidVolume = typeof(IAudioChannelVolume).GUID;
                    var hrVolume = Marshal.QueryInterface(unkControl, ref iidVolume, out var volumePtr);
                    Marshal.Release(unkControl);

                    if (hrVolume != 0 || volumePtr == IntPtr.Zero)
                        continue;

                    var volume = (IAudioChannelVolume)Marshal.GetObjectForIUnknown(volumePtr);
                    Marshal.Release(volumePtr);

                    result.Add(new AudioSessionWrapper((int)pid, volume));
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
        {
            try { Marshal.ReleaseComObject(_sessionManager); } catch { }
            _sessionManager = null;
        }

        if (_device != null)
        {
            try { Marshal.ReleaseComObject(_device); } catch { }
            _device = null;
        }
    }
}
