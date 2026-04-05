namespace WinPanX2.Tray;

internal static class TrayMenuPresets
{
    public const double Epsilon = 0.01;

    public static readonly double[] WidthLimitValues =
    {
        0.5,
        0.65,
        0.8,
        0.9,
        1.0
    };

    public static readonly double[] CenterBiasValues =
    {
        0.0,
        0.3,
        0.55,
        0.8
    };

    public static int DetectWidthLimitPreset(double maxPan)
        => DetectPreset(Math.Clamp(maxPan, 0.0, 1.0), WidthLimitValues);

    public static int DetectCenterBiasPreset(double centerBias)
        => DetectPreset(Math.Clamp(centerBias, 0.0, 1.0), CenterBiasValues);

    private static int DetectPreset(double value, IReadOnlyList<double> presets)
    {
        for (var i = 0; i < presets.Count; i++)
        {
            if (Math.Abs(value - presets[i]) <= Epsilon)
                return i;
        }

        return -1;
    }
}
