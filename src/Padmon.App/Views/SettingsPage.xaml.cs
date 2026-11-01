using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Padmon.Services;
using Padmon.ViewModels;
using Windows.UI;

namespace Padmon.Views;

public sealed partial class SettingsPage : Page
{
    private bool _syncingColors;

    public SettingsPage()
    {
        ViewModel = new SettingsViewModel(App.Services.SettingsStore);
        InitializeComponent();
        AboutVersionText.Text = $"Version {UpdateService.CurrentVersion}";
        AboutSdlText.Text = $"SDL {SdlInfo.Version}";
        if (AppEnvironment.IsPackaged)
        {
            StartWithWindowsDescription.Text =
                "Launch padmon when you sign in. Windows manages this startup task in Task Manager.";
        }
        SyncCustomColorInputs();
        UpdateCustomPanelVisibility();
    }

    public SettingsViewModel ViewModel { get; private set; }

    private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= 1180;

        Grid.SetColumnSpan(LeftSettings, wide ? 1 : 2);
        Grid.SetRow(RightSettings, wide ? 0 : 1);
        Grid.SetColumn(RightSettings, wide ? 1 : 0);
        Grid.SetColumnSpan(RightSettings, wide ? 1 : 2);
        SettingsBody.ColumnSpacing = wide ? 20 : 0;
    }

    private void GoToTester_Click(object sender, RoutedEventArgs e)
    {
        // Opens the tester for the first connected controller, or a simulated preview when
        // none is connected, so the tester UI can always be reached from here.
        Frame.Navigate(typeof(ControllerDetailPage), ControllerDetailPage.PreviewKey);
    }

    private void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        var nativeRequested = App.Services.Notifications.PreferNative;
        var delivery = App.Services.Notifications.ShowTest();

        ViewModel.NotificationTestStatus = !nativeRequested
            ? "Sent as an in-app card. Enable Windows notifications to use the shell notification instead."
            : delivery == NotificationDeliveryKind.Tray
                ? "A test Windows tray notification in the bottom-right corner has been sent."
                : "The Windows tray notification could not be created; padmon used the in-app card.";
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        ViewModel = new SettingsViewModel(App.Services.SettingsStore);
        Bindings.Update();
        SyncCustomColorInputs();
        UpdateCustomPanelVisibility();
    }

    private void ThemePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCustomPanelVisibility();
    }

    private void UpdateCustomPanelVisibility()
    {
        if (CustomColorsPanel is null)
        {
            return;
        }

        CustomColorsPanel.Visibility = ThemePicker.SelectedIndex == 3
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SyncCustomColorInputs()
    {
        _syncingColors = true;

        try
        {
            BackgroundColorBox.Text = ViewModel.CustomBackground;
            ForegroundColorBox.Text = ViewModel.CustomForeground;
            TextColorBox.Text = ViewModel.CustomText;
            HighlightColorBox.Text = ViewModel.CustomHighlight;

            SetPreview(BackgroundPreview, ViewModel.CustomBackground);
            SetPreview(ForegroundPreview, ViewModel.CustomForeground);
            SetPreview(TextPreview, ViewModel.CustomText);
            SetPreview(HighlightPreview, ViewModel.CustomHighlight);
        }
        finally
        {
            _syncingColors = false;
        }
    }

    private void ColorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingColors || sender is not TextBox box)
        {
            return;
        }

        if (!TryParseColor(box.Text, out var color))
        {
            return;
        }

        var normalized = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        if (ReferenceEquals(box, BackgroundColorBox))
        {
            ViewModel.CustomBackground = normalized;
            SetPreview(BackgroundPreview, color);
        }
        else if (ReferenceEquals(box, ForegroundColorBox))
        {
            ViewModel.CustomForeground = normalized;
            SetPreview(ForegroundPreview, color);
        }
        else if (ReferenceEquals(box, TextColorBox))
        {
            ViewModel.CustomText = normalized;
            SetPreview(TextPreview, color);
        }
        else if (ReferenceEquals(box, HighlightColorBox))
        {
            ViewModel.CustomHighlight = normalized;
            SetPreview(HighlightPreview, color);
        }
    }

    private static void SetPreview(Border preview, string hex)
    {
        if (TryParseColor(hex, out var color))
        {
            SetPreview(preview, color);
        }
    }

    private static void SetPreview(Border preview, Color color)
    {
        preview.Background = new SolidColorBrush(color);
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hex = value.Trim().TrimStart('#');
        if (hex.Length != 6 ||
            !uint.TryParse(
                hex,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var number))
        {
            return false;
        }

        color = Color.FromArgb(
            255,
            (byte)((number >> 16) & 0xFF),
            (byte)((number >> 8) & 0xFF),
            (byte)(number & 0xFF));
        return true;
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Clear session history?",
            Content = "This removes past connection sessions. Known controllers and their names stay.",
            PrimaryButtonText = "Clear history",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await App.Services.History.PruneSessionsAsync(DateTimeOffset.UtcNow.AddDays(1));

        var done = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "History cleared",
            Content = "Past sessions have been removed.",
            CloseButtonText = "OK"
        };

        await done.ShowAsync();
    }
}
