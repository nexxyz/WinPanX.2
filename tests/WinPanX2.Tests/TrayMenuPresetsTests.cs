using WinPanX2.Tray;

namespace WinPanX2.Tests;

public class TrayMenuPresetsTests
{
    [Fact]
    public void DetectWidthLimitPreset_ReturnsPresetIndex_ForKnownValues()
    {
        Assert.Equal(0, TrayMenuPresets.DetectWidthLimitPreset(0.5));
        Assert.Equal(1, TrayMenuPresets.DetectWidthLimitPreset(0.65));
        Assert.Equal(2, TrayMenuPresets.DetectWidthLimitPreset(0.8));
        Assert.Equal(3, TrayMenuPresets.DetectWidthLimitPreset(0.9));
        Assert.Equal(4, TrayMenuPresets.DetectWidthLimitPreset(1.0));
    }

    [Fact]
    public void DetectWidthLimitPreset_ReturnsCustom_ForNonPresetValue()
    {
        Assert.Equal(-1, TrayMenuPresets.DetectWidthLimitPreset(0.72));
    }

    [Fact]
    public void DetectCenterBiasPreset_ReturnsPresetIndex_ForKnownValues()
    {
        Assert.Equal(0, TrayMenuPresets.DetectCenterBiasPreset(0.0));
        Assert.Equal(1, TrayMenuPresets.DetectCenterBiasPreset(0.3));
        Assert.Equal(2, TrayMenuPresets.DetectCenterBiasPreset(0.55));
        Assert.Equal(3, TrayMenuPresets.DetectCenterBiasPreset(0.8));
    }

    [Fact]
    public void DetectCenterBiasPreset_ReturnsCustom_ForNonPresetValue()
    {
        Assert.Equal(-1, TrayMenuPresets.DetectCenterBiasPreset(0.42));
    }
}
