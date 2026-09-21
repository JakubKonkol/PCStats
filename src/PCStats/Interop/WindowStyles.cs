using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PCStats.Interop;

/// <summary>Extended window style helpers used by the widget window.</summary>
public static class WindowStyles
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    /// <summary>Lets mouse input fall through to whatever is underneath the window.</summary>
    public static void SetClickThrough(Window window, bool enabled) => SetFlag(window, WsExTransparent, enabled);

    /// <summary>Hides the window from Alt+Tab and keeps it from stealing focus from games.</summary>
    public static void MakeUnobtrusive(Window window)
    {
        SetFlag(window, WsExToolWindow, true);
        SetFlag(window, WsExNoActivate, true);
    }

    private static void SetFlag(Window window, int flag, bool enabled)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var style = (ulong)GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        var bit = (ulong)flag;
        style = enabled ? style | bit : style & ~bit;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr((long)style));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
