using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StormRetroArch;

public sealed class StormTrayService : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 2026;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int WM_MEASUREITEM = 0x002C;
    private const int WM_DRAWITEM = 0x002B;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;

    private const uint MF_OWNERDRAW = 0x00000100;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    private const int CMD_RESTORE = 1001;
    private const int CMD_UPDATES = 1002;
    private const int CMD_ABOUT = 1003;
    private const int CMD_EXIT = 1005;

    private const uint ODS_SELECTED = 0x0001;
    private const int DT_LEFT = 0x00000000;
    private const int DT_VCENTER = 0x00000004;
    private const int DT_SINGLELINE = 0x00000020;
    private const int TRANSPARENT = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEASUREITEMSTRUCT
    {
        public int CtlType;
        public int CtlID;
        public int itemID;
        public int itemWidth;
        public int itemHeight;
        public IntPtr itemData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DRAWITEMSTRUCT
    {
        public int CtlType;
        public int CtlID;
        public int itemID;
        public int itemAction;
        public int itemState;
        public IntPtr hwndItem;
        public IntPtr hDC;
        public RECT rcItem;
        public IntPtr itemData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int crColor);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("user32.dll")]
    private static extern int FrameRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hDC, int iBkMode);

    [DllImport("gdi32.dll")]
    private static extern int SetTextColor(IntPtr hDC, int crColor);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hgdiobj);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFont(int nHeight, int nWidth, int nEscapement, int nOrientation,
        int fnWeight, uint fdwItalic, uint fdwUnderline, uint fdwStrikeOut, uint fdwCharSet,
        uint fdwOutputPrecision, uint fdwClipPrecision, uint fdwQuality, uint fdwPitchAndFamily, string lpszFace);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawText(IntPtr hDC, string lpString, int nCount, ref RECT lpRect, uint uFormat);

    private readonly Window _window;
    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _iconAdded;
    private HwndSource? _hwndSource;

    private readonly IntPtr _hFontNormal;
    private readonly IntPtr _hFontBold;

    public StormTrayService(Window window)
    {
        _window = window;
        _hFontNormal = CreateFont(-12, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        _hFontBold   = CreateFont(-12, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
    }

    public void Initialize()
    {
        _hwnd = new WindowInteropHelper(_window).EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppIcon.ico");
        if (File.Exists(icoPath))
        {
            _hIcon = LoadImage(IntPtr.Zero, icoPath, 1 /* IMAGE_ICON */, 16, 16, 0x00000010 /* LR_LOADFROMFILE */);
        }

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_TIP | (_hIcon != IntPtr.Zero ? NIF_ICON : 0),
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "STORM RETROARCH 2.1.0"
        };

        _iconAdded = Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int eventId = (int)(lParam.ToInt64() & 0xFFFF);
            if (eventId == WM_LBUTTONDBLCLK || eventId == WM_LBUTTONUP)
            {
                RestoreWindow();
                handled = true;
            }
            else if (eventId == WM_RBUTTONUP || eventId == WM_CONTEXTMENU)
            {
                ShowContextMenu();
                handled = true;
            }
        }
        else if (msg == WM_MEASUREITEM)
        {
            var mis = Marshal.PtrToStructure<MEASUREITEMSTRUCT>(lParam);
            mis.itemWidth = 220;
            mis.itemHeight = (mis.itemID == 0) ? 6 : 28;
            Marshal.StructureToPtr(mis, lParam, false);
            handled = true;
            return (IntPtr)1;
        }
        else if (msg == WM_DRAWITEM)
        {
            var dis = Marshal.PtrToStructure<DRAWITEMSTRUCT>(lParam);
            DrawMenuItem(dis);
            handled = true;
            return (IntPtr)1;
        }

        return IntPtr.Zero;
    }

    private void DrawMenuItem(DRAWITEMSTRUCT dis)
    {
        bool isSelected = (dis.itemState & ODS_SELECTED) != 0;
        int bg = isSelected ? 0x52262E : 0x261014; // BGR: #2E2652 vs #141026
        IntPtr bgBrush = CreateSolidBrush(bg);
        FillRect(dis.hDC, ref dis.rcItem, bgBrush);
        DeleteObject(bgBrush);

        if (dis.itemID == 0)
        {
            RECT sepRect = dis.rcItem;
            sepRect.Top += 2;
            sepRect.Bottom = sepRect.Top + 1;
            IntPtr sepBrush = CreateSolidBrush(0x502D32); // #322D50
            FillRect(dis.hDC, ref sepRect, sepBrush);
            DeleteObject(sepBrush);
            return;
        }

        string text = dis.itemID switch
        {
            CMD_RESTORE => "Развернуть STORM RETROARCH",
            CMD_UPDATES => "Проверить обновления",
            CMD_ABOUT => "О программе",
            CMD_EXIT => "Выход",
            _ => ""
        };

        SetBkMode(dis.hDC, TRANSPARENT);
        SetTextColor(dis.hDC, 0xFFFFFF);

        IntPtr fontToUse = (dis.itemID == CMD_RESTORE) ? _hFontBold : _hFontNormal;
        IntPtr oldFont = SelectObject(dis.hDC, fontToUse);

        RECT textRect = dis.rcItem;
        textRect.Left += 18;
        DrawText(dis.hDC, text, -1, ref textRect, DT_LEFT | DT_VCENTER | DT_SINGLELINE);

        SelectObject(dis.hDC, oldFont);
    }

    private void ShowContextMenu()
    {
        IntPtr hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        AppendMenu(hMenu, MF_OWNERDRAW, (IntPtr)CMD_RESTORE, null);
        AppendMenu(hMenu, MF_OWNERDRAW, (IntPtr)CMD_UPDATES, null);
        AppendMenu(hMenu, MF_OWNERDRAW, (IntPtr)CMD_ABOUT, null);
        AppendMenu(hMenu, MF_OWNERDRAW | MF_SEPARATOR, IntPtr.Zero, null);
        AppendMenu(hMenu, MF_OWNERDRAW, (IntPtr)CMD_EXIT, null);

        GetCursorPos(out POINT pt);
        SetForegroundWindow(_hwnd);

        int cmd = TrackPopupMenuEx(hMenu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, _hwnd, IntPtr.Zero);
        DestroyMenu(hMenu);

        switch (cmd)
        {
            case CMD_RESTORE:
                RestoreWindow();
                break;
            case CMD_UPDATES:
                _ = UpdateManager.CheckForUpdatesAsync(silent: false);
                break;
            case CMD_ABOUT:
                MainWindow.Instance?.ShowAboutModal();
                RestoreWindow();
                break;
            case CMD_EXIT:
                Dispose();
                Environment.Exit(0);
                break;
        }
    }

    public void RestoreWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Dispose()
    {
        if (_iconAdded)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _iconAdded = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }

        if (_hFontNormal != IntPtr.Zero) DeleteObject(_hFontNormal);
        if (_hFontBold != IntPtr.Zero) DeleteObject(_hFontBold);

        _hwndSource?.RemoveHook(WndProc);
    }
}
