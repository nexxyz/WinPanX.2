using WinPanX2.Audio.Interop;

namespace WinPanX2.Audio;

internal sealed class DeviceNotificationClient : IMMNotificationClient
{
    private readonly Action _onDeviceTopologyChanged;

    public DeviceNotificationClient(Action onDeviceTopologyChanged)
    {
        _onDeviceTopologyChanged = onDeviceTopologyChanged;
    }

    public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId)
    {
        if (flow == EDataFlow.eRender)
            _onDeviceTopologyChanged();
        return 0;
    }

    public int OnDeviceAdded(string pwstrDeviceId)
    {
        _onDeviceTopologyChanged();
        return 0;
    }

    public int OnDeviceRemoved(string deviceId)
    {
        _onDeviceTopologyChanged();
        return 0;
    }

    public int OnDeviceStateChanged(string deviceId, DEVICE_STATE newState)
    {
        _onDeviceTopologyChanged();
        return 0;
    }

    public int OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        return 0;
    }
}
