using System.Windows.Forms;

namespace WinPanX2.Windowing;

internal static class VirtualDesktopMapper
{
    internal readonly struct Mapping
    {
        public double VirtualCenterX { get; }
        public double HalfWidth { get; }

        public Mapping(double virtualCenterX, double halfWidth)
        {
            VirtualCenterX = virtualCenterX;
            HalfWidth = halfWidth;
        }
    }

    public static Mapping Capture()
    {
        var virtualBounds = SystemInformation.VirtualScreen;
        var virtualCenterX = virtualBounds.Left + virtualBounds.Width / 2.0;
        var halfWidth = virtualBounds.Width / 2.0;

        // Avoid division by zero if the API reports something unexpected.
        if (halfWidth <= 0)
            halfWidth = 1.0;

        return new Mapping(virtualCenterX, halfWidth);
    }

    public static double MapToNormalized(int centerX)
    {
        return MapToNormalized(centerX, Capture());
    }

    public static double MapToNormalized(int centerX, Mapping mapping)
    {
        var normalized = (centerX - mapping.VirtualCenterX) / mapping.HalfWidth;

        if (normalized < -1) normalized = -1;
        if (normalized > 1) normalized = 1;

        return normalized;
    }
}
