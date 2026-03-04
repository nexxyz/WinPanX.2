using NAudio.CoreAudioApi;

namespace WinPanX2.Audio;

internal interface IAudioDeviceProvider
{
    string GetDefaultRenderDeviceId();
    IEnumerable<string> GetActiveRenderDeviceIds();
    MMDevice GetDeviceById(string deviceId);
}
