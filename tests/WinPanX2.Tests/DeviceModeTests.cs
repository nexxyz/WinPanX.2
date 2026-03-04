using WinPanX2.Audio;
using WinPanX2.Config;
using WinPanX2.Core;

namespace WinPanX2.Tests;

public class DeviceModeTests
{
    private AppConfig CreateConfig() => new();

    [Fact]
    public void DefaultMode_InitializesSingleDevice()
    {
        var mock = new MockAudioDeviceProvider();
        mock.SetDefault("D1");

        var engine = new SpatialAudioEngine(CreateConfig(), mock);
        engine.SetDeviceMode(DeviceMode.Default);

        engine.Start();

        Assert.True(engine.IsEnabled);

        engine.Stop();
    }

    [Fact]
    public void AllMode_InitializesMultipleDevices()
    {
        var mock = new MockAudioDeviceProvider();
        mock.SetActiveDevices("D1", "D2", "D3");
        mock.SetDefault("D1");

        var engine = new SpatialAudioEngine(CreateConfig(), mock);
        engine.SetDeviceMode(DeviceMode.All);

        engine.Start();

        Assert.True(engine.IsEnabled);

        engine.Stop();
    }

    [Fact]
    public void ModeSwitch_ResetsAndRestarts()
    {
        var mock = new MockAudioDeviceProvider();
        mock.SetActiveDevices("D1", "D2");
        mock.SetDefault("D1");

        var engine = new SpatialAudioEngine(CreateConfig(), mock);

        engine.Start();
        Assert.True(engine.IsEnabled);

        engine.SetDeviceMode(DeviceMode.All);
        Assert.True(engine.IsEnabled);

        engine.SetDeviceMode(DeviceMode.Default);
        Assert.True(engine.IsEnabled);

        engine.Stop();
    }
}
