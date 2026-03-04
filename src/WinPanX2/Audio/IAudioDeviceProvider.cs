using WinPanX2.Audio.Interop;

namespace WinPanX2.Audio;

internal interface IAudioDeviceProvider
{
    string GetDefaultRenderDeviceId();
    IEnumerable<string> GetActiveRenderDeviceIds();
    IMMDevice GetDeviceById(string deviceId);
}
