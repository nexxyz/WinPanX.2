using System.Runtime.InteropServices;
using WinPanX2.Audio.Interop;
using WinPanX2.Logging;
using NAudio.CoreAudioApi;

namespace WinPanX2.Audio;

internal sealed class CoreAudioDeviceProvider : IAudioDeviceProvider
{
    // Notifications temporarily disabled to stabilize All-mode switching
    // private MMDeviceEnumerator? _notificationEnumerator;
    // private NAudio.CoreAudioApi.Interfaces.IMMNotificationClient? _notificationClient;

    // Notifications disabled; keep event for compatibility
    #pragma warning disable CS0067
    public event Action? TopologyChanged;
    #pragma warning restore CS0067

    public CoreAudioDeviceProvider()
    {
    }

    public void RegisterNotifications()
    {
        // Notifications disabled for now
        return;
    }

    public void UnregisterNotifications()
    {
        // Notifications disabled for now
        return;
    }

    public string GetDefaultRenderDeviceId()
    {
        // Use NAudio for default device resolution (stable)
        var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(
            NAudio.CoreAudioApi.DataFlow.Render,
            NAudio.CoreAudioApi.Role.Multimedia);

        return device.ID;
    }

    public IEnumerable<string> GetActiveRenderDeviceIds()
    {
        var result = new List<string>();

        using (var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator())
        {
            var devices = enumerator.EnumerateAudioEndPoints(
                NAudio.CoreAudioApi.DataFlow.Render,
                NAudio.CoreAudioApi.DeviceState.Active);

            foreach (var device in devices)
            {
                result.Add(device.ID);
            }
        }

        Logger.Debug($"NAudio enumerated {result.Count} active render devices.");

        return result;
    }

    public IMMDevice GetDeviceById(string deviceId)
    {
        var clsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        var type = Type.GetTypeFromCLSID(clsid, throwOnError: true)!;

        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;

        try
        {
            enumerator.GetDevice(deviceId, out var device);
            return device;
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }
}
