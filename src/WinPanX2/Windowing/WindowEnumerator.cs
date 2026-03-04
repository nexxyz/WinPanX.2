using System;
using System.Collections.Generic;

namespace WinPanX2.Windowing;

internal static class WindowEnumerator
{
    public static List<WindowInfo> GetVisibleWindows()
    {
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            if (!NativeMethods.GetWindowRect(hWnd, out var rect))
                return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);

            if (rect.Right - rect.Left <= 0 || rect.Bottom - rect.Top <= 0)
                return true;

            windows.Add(new WindowInfo(hWnd, (int)pid, rect));

            return true;
        }, IntPtr.Zero);

        return windows;
    }
}
