using System.Runtime.InteropServices;
using WinPanX2.Audio.Interop;
using WinPanX2.Logging;
using NAudio.CoreAudioApi;

namespace WinPanX2.Audio;

internal sealed class CoreAudioDeviceProvider : IAudioDeviceProvider
{
    // Hot-plug notifications via NAudio (topology only)
    private NAudio.CoreAudioApi.MMDeviceEnumerator? _notificationEnumerator;
    private NotificationClient? _notificationClient;

    // Notifications disabled; keep event for compatibility
    #pragma warning disable CS0067
    public event Action? TopologyChanged;

    internal void RaiseTopologyChanged()
    {
        TopologyChanged?.Invoke();
    }
    #pragma warning restore CS0067

    public CoreAudioDeviceProvider()
    {
    }

    public void RegisterNotifications()
    {
        if (_notificationEnumerator != null)
            return;

        _notificationEnumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        _notificationClient = new NotificationClient(this);
        _notificationEnumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    public void UnregisterNotifications()
    {
        if (_notificationEnumerator == null || _notificationClient == null)
            return;

        _notificationEnumerator.UnregisterEndpointNotificationCallback(_notificationClient);
        _notificationEnumerator.Dispose();
        _notificationEnumerator = null;
        _notificationClient = null;
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

// Only raises topology events. No COM session work here.
internal sealed class NotificationClient : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
{
    private readonly CoreAudioDeviceProvider _provider;

    public NotificationClient(CoreAudioDeviceProvider provider)
    {
        _provider = provider;
    }

    public void OnDeviceStateChanged(string deviceId, NAudio.CoreAudioApi.DeviceState newState)
        => _provider.RaiseTopologyChanged();

    public void OnDeviceAdded(string pwstrDeviceId)
        => _provider.RaiseTopologyChanged();

    public void OnDeviceRemoved(string deviceId)
        => _provider.RaiseTopologyChanged();

    public void OnDefaultDeviceChanged(NAudio.CoreAudioApi.DataFlow flow,
        NAudio.CoreAudioApi.Role role, string defaultDeviceId)
        => _provider.RaiseTopologyChanged();

    public void OnPropertyValueChanged(string pwstrDeviceId, NAudio.CoreAudioApi.PropertyKey key)
    {
        // ignore
    }
}
