using System.Windows.Forms;

namespace WinPanX2.Windowing;

internal static class VirtualDesktopMapper
{
    public static double MapToNormalized(int centerX)
    {
        var virtualBounds = SystemInformation.VirtualScreen;

        var virtualCenterX = virtualBounds.Left + virtualBounds.Width / 2.0;
        var halfWidth = virtualBounds.Width / 2.0;

        var normalized = (centerX - virtualCenterX) / halfWidth;

        if (normalized < -1) normalized = -1;
        if (normalized > 1) normalized = 1;

        return normalized;
    }
}
