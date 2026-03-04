using WinPanX2.Audio;
using WinPanX2.Audio.Interop;

namespace WinPanX2.Tests;

internal sealed class MockAudioDeviceProvider : IAudioDeviceProvider
{
    private readonly List<string> _activeDevices = new();
    private string _defaultDevice = string.Empty;

    public void SetDefault(string id)
    {
        _defaultDevice = id;
        if (!_activeDevices.Contains(id))
            _activeDevices.Add(id);
    }

    public void SetActiveDevices(params string[] ids)
    {
        _activeDevices.Clear();
        _activeDevices.AddRange(ids);

        if (!_activeDevices.Contains(_defaultDevice) && _activeDevices.Count > 0)
            _defaultDevice = _activeDevices[0];
    }

    public string GetDefaultRenderDeviceId() => _defaultDevice;

    public IEnumerable<string> GetActiveRenderDeviceIds() => _activeDevices;

    public IMMDevice GetDeviceById(string deviceId)
    {
        // We never use IMMDevice in unit tests (engine won't actually activate WASAPI)
        return null!;
    }
}
