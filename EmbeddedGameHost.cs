using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StormRetroArch;

/// <summary>
/// High-performance WPF HwndHost container for embedding the Libretro emulation window
/// directly inside the STORM RETROARCH interface.
/// </summary>
public class EmbeddedGameHost : HwndHost
{
    private IntPtr _hostHwnd = IntPtr.Zero;
    private IntPtr _gameHwnd = IntPtr.Zero;

    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_SYSMENU = 0x00080000;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOPARENTNOTIFY = 0x00000004;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x, int y,
        int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    private static long GetWindowLong(IntPtr hWnd, int nIndex)
    {
        if (IntPtr.Size == 8)
            return (long)GetWindowLongPtr64(hWnd, nIndex);
        return GetWindowLong32(hWnd, nIndex);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static void SetWindowLong(IntPtr hWnd, int nIndex, long dwNewLong)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hWnd, nIndex, (IntPtr)dwNewLong);
        else
            SetWindowLong32(hWnd, nIndex, (int)dwNewLong);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    public IntPtr GameHwnd => _gameHwnd;

    public EmbeddedGameHost()
    {
        SizeChanged += (s, e) => ResizeEmbeddedGame();
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        int width = (int)Math.Max(1, ActualWidth > 0 ? ActualWidth : 800);
        int height = (int)Math.Max(1, ActualHeight > 0 ? ActualHeight : 600);

        _hostHwnd = CreateWindowEx(
            0,
            "static",
            "",
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            0, 0,
            width, height,
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        return new HandleRef(this, _hostHwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DetachGameWindow();
        if (_hostHwnd != IntPtr.Zero)
        {
            DestroyWindow(_hostHwnd);
            _hostHwnd = IntPtr.Zero;
        }
    }

    public void AttachGameWindow(IntPtr gameHwnd)
    {
        if (gameHwnd == IntPtr.Zero || _hostHwnd == IntPtr.Zero) return;

        _gameHwnd = gameHwnd;

        long currentStyle = GetWindowLong(_gameHwnd, GWL_STYLE);
        long newStyle = (currentStyle & ~WS_POPUP & ~WS_CAPTION & ~WS_THICKFRAME & ~WS_MINIMIZEBOX & ~WS_MAXIMIZEBOX & ~WS_SYSMENU) 
                      | WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS;

        SetWindowLong(_gameHwnd, GWL_STYLE, newStyle);
        SetWindowLong(_gameHwnd, GWL_EXSTYLE, WS_EX_NOPARENTNOTIFY);

        SetParent(_gameHwnd, _hostHwnd);

        ResizeEmbeddedGame();
        SetFocus(_gameHwnd);
    }

    public void DetachGameWindow()
    {
        if (_gameHwnd != IntPtr.Zero)
        {
            try
            {
                SetParent(_gameHwnd, IntPtr.Zero);
            }
            catch { }
            _gameHwnd = IntPtr.Zero;
        }
    }

    public void ResizeEmbeddedGame()
    {
        if (_gameHwnd != IntPtr.Zero && _hostHwnd != IntPtr.Zero)
        {
            int w = (int)Math.Max(1, ActualWidth);
            int h = (int)Math.Max(1, ActualHeight);
            MoveWindow(_gameHwnd, 0, 0, w, h, true);
        }
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        if (_gameHwnd != IntPtr.Zero)
        {
            int w = (int)Math.Max(1, rcBoundingBox.Width);
            int h = (int)Math.Max(1, rcBoundingBox.Height);
            MoveWindow(_gameHwnd, 0, 0, w, h, true);
        }
    }
}
