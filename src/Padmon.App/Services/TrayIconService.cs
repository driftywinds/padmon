using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Padmon.Services;

/// <summary>
/// Small, dependency-free notification-area icon. Keeping this on Win32
/// avoids a third-party tray control built against an older Windows App SDK.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint WmNull = 0x0000;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint TrayCallbackMessage = WmApp + 1;
    private const uint TrayIconId = 1;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint NiifInfo = 0x00000001;

    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;

    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint LrDefaultSize = 0x00000040;

    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmLeftAlign = 0x0000;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;

    private const int MenuOpen = 1;
    private const int MenuSettings = 2;
    private const int MenuExit = 3;

    private readonly IntPtr _windowHandle;
    private readonly Action _open;
    private readonly Action _settings;
    private readonly Action _exit;
    private readonly SubclassProc _subclassProc;
    private readonly uint _taskbarCreatedMessage;

    private NotifyIconData _data;
    private IntPtr _icon;
    private bool _ownsIcon;
    private bool _created;
    private bool _disposed;

    public TrayIconService(IntPtr windowHandle, Action open, Action settings, Action exit)
    {
        _windowHandle = windowHandle;
        _open = open;
        _settings = settings;
        _exit = exit;
        _subclassProc = WndProc;
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    }

    public void Create()
    {
        if (_created || _disposed || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        _icon = LoadTrayIcon();
        _ownsIcon = _icon != IntPtr.Zero;

        _data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _windowHandle,
            uID = TrayIconId,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _icon,
            szTip = "padmon",
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };

        SetWindowSubclass(_windowHandle, _subclassProc, (UIntPtr)TrayIconId, IntPtr.Zero);
        AddIcon();
        _created = true;
    }

    public void UpdateTooltip(string text)
    {
        if (_disposed || !_created)
        {
            return;
        }

        _data.szTip = text;
        _data.uFlags = NifTip;
        Shell_NotifyIcon(NimModify, ref _data);
    }

    public void ShowBalloon(string title, string body)
    {
        if (_disposed || !_created)
        {
            return;
        }

        _data.szInfoTitle = Truncate(title, 63);
        _data.szInfo = Truncate(body, 255);
        _data.dwInfoFlags = NiifInfo;
        _data.uVersion = 5000;
        _data.uFlags = NifInfo;
        Shell_NotifyIcon(NimModify, ref _data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_created)
        {
            Shell_NotifyIcon(NimDelete, ref _data);
            RemoveWindowSubclass(_windowHandle, _subclassProc, (UIntPtr)TrayIconId);
            _created = false;
        }

        if (_ownsIcon)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
            _ownsIcon = false;
        }
    }

    private void AddIcon()
    {
        _data.uFlags = NifMessage | NifIcon | NifTip;
        _data.hIcon = _icon;

        if (!Shell_NotifyIcon(NimAdd, ref _data))
        {
            Debug.WriteLine($"padmon could not add the tray icon: {Marshal.GetLastWin32Error()}");
        }
    }

    private IntPtr WndProc(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        IntPtr idSubclass,
        IntPtr refData)
    {
        if (message == _taskbarCreatedMessage)
        {
            AddIcon();
            return IntPtr.Zero;
        }

        if (message == TrayCallbackMessage)
        {
            var mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);

            if (mouseMessage == WmLButtonUp)
            {
                _open();
                return IntPtr.Zero;
            }

            if (mouseMessage == WmRButtonUp)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();

        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString, MenuOpen, "Open padmon");
            AppendMenu(menu, MfString, MenuSettings, "Settings");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, MenuExit, "Exit");

            GetCursorPos(out var point);
            SetForegroundWindow(_windowHandle);

            var command = TrackPopupMenu(
                menu,
                TpmLeftAlign | TpmBottomAlign | TpmRightButton | TpmReturnCmd,
                point.X,
                point.Y,
                0,
                _windowHandle,
                IntPtr.Zero);

            switch (command)
            {
                case MenuOpen:
                    _open();
                    break;
                case MenuSettings:
                    _settings();
                    break;
                case MenuExit:
                    _exit();
                    break;
            }

            PostMessage(_windowHandle, WmNull, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static IntPtr LoadTrayIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "padmon-tray.ico");

        if (File.Exists(path))
        {
            var icon = LoadImage(
                IntPtr.Zero,
                path,
                ImageIcon,
                0,
                0,
                LrLoadFromFile | LrDefaultSize);

            if (icon != IntPtr.Zero)
            {
                return icon;
            }
        }

        return LoadIcon(IntPtr.Zero, (IntPtr)32512);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private delegate IntPtr SubclassProc(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        IntPtr idSubclass,
        IntPtr refData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd,
        SubclassProc subclassProc,
        UIntPtr idSubclass,
        IntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr hWnd,
        SubclassProc subclassProc,
        UIntPtr idSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr instance,
        string name,
        uint type,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, int id, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        int reserved,
        IntPtr hWnd,
        IntPtr rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
