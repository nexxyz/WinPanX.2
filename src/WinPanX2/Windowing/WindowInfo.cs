using System;

namespace WinPanX2.Windowing;

internal sealed class WindowInfo
{
    public IntPtr Handle { get; }
    public int ProcessId { get; }
    public RECT Rect { get; }

    public WindowInfo(IntPtr handle, int processId, RECT rect)
    {
        Handle = handle;
        ProcessId = processId;
        Rect = rect;
    }

    public int CenterX => Rect.Left + (Rect.Right - Rect.Left) / 2;
}

internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
