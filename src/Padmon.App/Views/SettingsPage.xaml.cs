using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Padmon.Services;
using Padmon.ViewModels;

namespace Padmon.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = new SettingsViewModel(App.Services.SettingsStore);
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; private set; }

    private void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        var nativeRequested = App.Services.Notifications.PreferNative;
        var delivery = App.Services.Notifications.ShowTest();

        ViewModel.NotificationTestStatus = delivery switch
        {
            NotificationDeliveryKind.Native =>
                "Sent as a native Windows notification. Check the bottom-right corner and Notification Center.",
            NotificationDeliveryKind.Tray when nativeRequested =>
                "Windows blocked the native toast; padmon showed a tray balloon instead. A tray balloon is not stored in Notification Center. Check the padmon.log native toast line for the exact reason.",
            NotificationDeliveryKind.Tray =>
                "Sent as a tray balloon. Enable native notifications to use the Windows notification system and Notification Center.",
            _ =>
                "Windows blocked the native toast and no tray icon was available; padmon used the in-app card."
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        ViewModel = new SettingsViewModel(App.Services.SettingsStore);
        Bindings.Update();
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
