using WinPanX2.Audio.Interop;

namespace WinPanX2.Audio;

internal sealed class CoreAudioDeviceProvider : IAudioDeviceProvider
{
    private readonly IMMDeviceEnumerator _enumerator;
    private IMMNotificationClient? _notificationClient;

    public event Action? TopologyChanged;

    public CoreAudioDeviceProvider()
    {
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
    }

    public void RegisterNotifications()
    {
        if (_notificationClient != null)
            return;

        _notificationClient = new DeviceNotificationClient(() =>
        {
            TopologyChanged?.Invoke();
        });

        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    public void UnregisterNotifications()
    {
        if (_notificationClient == null)
            return;

        _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
        _notificationClient = null;
    }

    public string GetDefaultRenderDeviceId()
    {
        _enumerator.GetDefaultAudioEndpoint(
            EDataFlow.eRender,
            ERole.eMultimedia,
            out var device);

        device.GetId(out var id);
        return id;
    }

    public IEnumerable<string> GetActiveRenderDeviceIds()
    {
        _enumerator.EnumAudioEndpoints(
            EDataFlow.eRender,
            DEVICE_STATE.ACTIVE,
            out var collection);

        collection.GetCount(out var count);

        for (uint i = 0; i < count; i++)
        {
            collection.Item(i, out var device);
            device.GetId(out var id);
            yield return id;
        }
    }

    public IMMDevice GetDeviceById(string deviceId)
    {
        _enumerator.GetDevice(deviceId, out var device);
        return device;
    }
}
