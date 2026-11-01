using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Padmon.Services;
using Windows.Graphics;
using Windows.UI;

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
        // Re-apply once the window is visible; Windows can restore its border when first shown.
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

    // The WinUI window is a plain rectangle. Windows 11 draws its own 1px border (and rounds the
    // corners differently from the card), so the rectangle's edge and corners show up as a light
    // line around the pill. Match the DWM border to the card and clip the window itself to the
    // same rounded shape, so nothing outside the pill is ever painted.
    private void ApplyPillShape()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        var color = Root.Background is SolidColorBrush brush
            ? brush.Color
            : Color.FromArgb(255, 32, 32, 32);
        var borderColor = unchecked((int)(
            (uint)color.R |
            ((uint)color.G << 8) |
            ((uint)color.B << 16)));
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(int));

        var corners = DwmwcpDoNotRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corners, sizeof(int));

        var scale = GetDpiForWindow(hwnd) / 96.0;
        var diameter = (int)Math.Round(PillCornerRadius * 2 * scale);
        var size = AppWindow.Size;
        var region = CreateRoundRectRgn(0, 0, size.Width + 1, size.Height + 1, diameter, diameter);

        // The system owns the region after a successful call; only free it if the call failed.
        if (SetWindowRgn(hwnd, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    // The XAML fills the whole window with square corners; this radius (in DIPs) is applied only
    // by the window clip, so there is no inner edge for a lighter line to show along.
    private const double PillCornerRadius = 16;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpDoNotRound = 1;

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
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
