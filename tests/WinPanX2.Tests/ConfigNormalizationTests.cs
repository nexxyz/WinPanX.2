using WinPanX2.Config;

namespace WinPanX2.Tests;

public class ConfigNormalizationTests
{
    [Fact]
    public void Normalize_ClampsNumericValues()
    {
        var config = new AppConfig
        {
            PollingIntervalMs = -5,
            SmoothingFactor = 2.0,
            MaxPan = -0.2,
            CenterBias = 2.0
        };

        config.Normalize();

        Assert.Equal(5, config.PollingIntervalMs);
        Assert.Equal(1.0, config.SmoothingFactor);
        Assert.Equal(0.0, config.MaxPan);
        Assert.Equal(1.0, config.CenterBias);
    }

    [Fact]
    public void Normalize_SanitizesModeLogLevelAndExcludedProcesses()
    {
        var config = new AppConfig
        {
            LogLevel = "nope",
            BindingMode = "garbage",
            ExcludedProcesses = new List<string> { " discord ", "", "DISCORD", "  " }
        };

        config.Normalize();

        Assert.Equal("Info", config.LogLevel);
        Assert.Equal(BindingModes.Sticky, config.BindingMode);
        Assert.Single(config.ExcludedProcesses);
        Assert.Equal("discord", config.ExcludedProcesses[0], ignoreCase: true);
    }
}
