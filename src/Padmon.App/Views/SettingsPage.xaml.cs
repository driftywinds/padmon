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

        ViewModel.NotificationTestStatus = !nativeRequested
            ? "Sent as an in-app card. Enable Windows notifications to use the shell notification instead."
            : delivery == NotificationDeliveryKind.Tray
                ? "Shown as a Windows tray notification in the bottom-right corner. No administrator rights are required."
                : "The Windows tray notification could not be created; padmon used the in-app card.";
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
