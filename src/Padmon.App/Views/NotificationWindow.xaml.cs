using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Padmon.Views;

public sealed partial class NotificationWindow : Window
{
    private readonly DispatcherQueueTimer _hideTimer;

    public NotificationWindow()
    {
        InitializeComponent();

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
    }

    public void ShowMessage(string title, string body)
    {
        TitleText.Text = title;
        BodyText.Text = body;
        PositionBottomRight();

        AppWindow.Show();
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
