using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WinPanX2.Audio.Interop;

namespace WinPanX2.Audio;

internal sealed class AudioSessionManager : IDisposable
{
    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _device;
    private IAudioSessionManager2? _sessionManager;
    public string DeviceId { get; private set; } = string.Empty;

    public void InitializeDefaultRenderDevice()
    {
        _deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

        _deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
        _device = device;

        // Store device ID
        _device.GetId(out var id);
        DeviceId = id;

        var iid = typeof(IAudioSessionManager2).GUID;
        const uint CLSCTX_ALL = 0x17;
        _device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var obj);

        _sessionManager = (IAudioSessionManager2)obj;
    }

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
        enumerator.GetCount(out var count);

        for (int i = 0; i < count; i++)
        {
            enumerator.GetSession(i, out var control);

            var control2 = (IAudioSessionControl2)control;
            control2.GetProcessId(out var pid);

            // Proper COM QueryInterface for IAudioChannelVolume
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

        return result;
    }

    public void Dispose()
    {
        if (_sessionManager != null) Marshal.ReleaseComObject(_sessionManager);
        if (_device != null) Marshal.ReleaseComObject(_device);
        if (_deviceEnumerator != null) Marshal.ReleaseComObject(_deviceEnumerator);
    }
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumerator
{
}
