using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Padmon.Services;
using Windows.Graphics;

namespace Padmon.Views;

public sealed partial class NotificationWindow : Window
{
    private readonly DispatcherQueueTimer _hideTimer;
    private readonly DispatcherQueueTimer _borderTimer;

    public NotificationWindow()
    {
        InitializeComponent();
        ThemeService.Apply(Root, App.Services.Settings.Theme, App.Services.Settings);

        AppWindow.Resize(new SizeInt32(390, 96));
        AppWindow.IsShownInSwitchers = false;
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromSeconds(6);
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => HideWindow();

        _borderTimer = DispatcherQueue.CreateTimer();
        _borderTimer.Interval = TimeSpan.FromMilliseconds(200);
        _borderTimer.IsRepeating = false;
        _borderTimer.Tick += (_, _) => ApplyPillShape();
    }

    public void ShowMessage(string title, string body)
    {
        TitleText.Text = title;
        BodyText.Text = body;
        PositionBottomRight();
        ApplyPillShape();

        AppWindow.Show();
        // Re-apply once the window is visible; Windows/WinUI can restore the frame on first show.
        ApplyPillShape();
        _borderTimer.Stop();
        _borderTimer.Start();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void PositionBottomRight()
    {
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = display.WorkArea;
        var x = workArea.X + workArea.Width - 410;
        var y = workArea.Y + workArea.Height - 120;
        AppWindow.Move(new PointInt32(x, y));
    }

    // Removes every source of an outline around the notification:
    //  1. strips the frame/caption/edge window styles so there is no non-client ring,
    //  2. turns the Windows 11 DWM border off and disables its corner rounding,
    //  3. clips the window to the card's rounded shape (client area only), so anything drawn
    //     outside that shape, including any border DWM still paints, is cut off.
    private void ApplyPillShape()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        style &= ~(WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox);
        _ = SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(style));

        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        exStyle &= ~(WsExClientEdge | WsExWindowEdge | WsExStaticEdge | WsExDlgModalFrame);
        _ = SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(exStyle));

        _ = SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

        var borderColor = unchecked((int)DwmColorNone);
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(int));

        var corners = DwmwcpDoNotRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corners, sizeof(int));

        var scale = GetDpiForWindow(hwnd) / 96.0;
        var diameter = (int)Math.Round(PillCornerRadius * 2 * scale);

        GetWindowRect(hwnd, out var windowRect);
        GetClientRect(hwnd, out var clientRect);
        var origin = new NativePoint { X = 0, Y = 0 };
        ClientToScreen(hwnd, ref origin);

        var left = origin.X - windowRect.Left;
        var top = origin.Y - windowRect.Top;
        var right = left + (clientRect.Right - clientRect.Left);
        var bottom = top + (clientRect.Bottom - clientRect.Top);
        var region = CreateRoundRectRgn(left, top, right + 1, bottom + 1, diameter, diameter);

        // The system owns the region after a successful call; only free it if the call failed.
        if (SetWindowRgn(hwnd, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    // Corner radius in DIPs, applied by the window clip (the XAML card itself is square).
    private const double PillCornerRadius = 16;

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpDoNotRound = 1;
    private const uint DwmColorNone = 0xFFFFFFFE;

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsCaption = 0x00C00000;
    private const long WsThickFrame = 0x00040000;
    private const long WsSysMenu = 0x00080000;
    private const long WsMinimizeBox = 0x00020000;
    private const long WsMaximizeBox = 0x00010000;
    private const long WsExDlgModalFrame = 0x00000001;
    private const long WsExWindowEdge = 0x00000100;
    private const long WsExClientEdge = 0x00000200;
    private const long WsExStaticEdge = 0x00020000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool redraw);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr obj);

    private void HideWindow()
    {
        _hideTimer.Stop();
        AppWindow.Hide();
    }

    private void Root_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.AppWindow.Show();
            mainWindow.Activate();
        }

        HideWindow();
    }
}