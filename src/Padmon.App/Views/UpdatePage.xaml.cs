using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Padmon.Core.Diagnostics;
using Padmon.Services;
using Windows.System;

namespace Padmon.Views;

public sealed partial class UpdatePage : Page
{
    private UpdateCheckResult? _lastCheck;
    private bool _checking;

    public UpdatePage()
    {
        InitializeComponent();
        CurrentVersionText.Text = $"Version {UpdateService.CurrentVersion}";
        BuildKindText.Text = UpdateService.ModeLabel;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await CheckAsync();
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        await CheckAsync();
    }

    private async Task CheckAsync()
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        CheckButton.IsEnabled = false;
        CheckButton.Content = "Checking…";
        StatusBar.IsOpen = false;
        AvailableCard.Visibility = Visibility.Collapsed;
        UpToDateCard.Visibility = Visibility.Collapsed;
        NoReleaseCard.Visibility = Visibility.Collapsed;

        try
        {
            var result = await UpdateService.CheckAsync();
            _lastCheck = result;
            CurrentVersionText.Text = $"Version {result.CurrentVersion}";

            if (result.Error is not null)
            {
                StatusBar.Severity = InfoBarSeverity.Error;
                StatusBar.Title = "Could not check for updates";
                StatusBar.Message = result.Error;
                StatusBar.IsOpen = true;
                return;
            }

            if (!result.HasRelease)
            {
                NoReleaseCard.Visibility = Visibility.Visible;
                return;
            }

            if (!result.IsNewer)
            {
                UpToDateCard.Visibility = Visibility.Visible;
                return;
            }

            LatestVersionText.Text = $"Version {result.LatestVersion}";
            ReleaseNotesText.Text = string.IsNullOrWhiteSpace(result.ReleaseNotes)
                ? "No release notes were provided."
                : result.ReleaseNotes;
            ReleaseLinkButton.IsEnabled = !string.IsNullOrWhiteSpace(result.ReleaseUrl);
            InstallButton.IsEnabled = result.CanInstall;
            AvailableCard.Visibility = Visibility.Visible;

            if (!result.CanInstall)
            {
                StatusBar.Severity = InfoBarSeverity.Warning;
                StatusBar.Title = "Update available, but no compatible download";
                StatusBar.Message = "The release does not contain the expected Windows x64 asset.";
                StatusBar.IsOpen = true;
            }
        }
        catch (Exception exception)
        {
            PadmonLog.Error("update page check failed", exception);
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Title = "Could not check for updates";
            StatusBar.Message = exception.Message;
            StatusBar.IsOpen = true;
        }
        finally
        {
            _checking = false;
            CheckButton.IsEnabled = true;
            CheckButton.Content = "Check for updates";
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_lastCheck is not { CanInstall: true } update)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Install padmon {update.LatestVersion}?",
            Content = "padmon will download the update, close, install it, and restart. Your settings and history are kept.",
            PrimaryButtonText = "Install and restart",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        InstallButton.IsEnabled = false;
        CheckButton.IsEnabled = false;
        InstallButton.Content = "Downloading…";
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.Title = "Downloading update";
        StatusBar.Message = "Downloading the release from GitHub. padmon will restart when it is ready.";
        StatusBar.IsOpen = true;

        try
        {
            await UpdateService.InstallAsync(update);
            InstallButton.Content = "Restarting…";
            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Title = "Update downloaded";
            StatusBar.Message = "padmon will restart and finish installing the update.";
            StatusBar.IsOpen = true;
            await Task.Delay(600);
            Environment.Exit(0);
        }
        catch (Exception exception)
        {
            PadmonLog.Error("update install failed", exception);
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Title = "Could not install the update";
            StatusBar.Message = exception.Message;
            StatusBar.IsOpen = true;
            InstallButton.IsEnabled = true;
            InstallButton.Content = "Download and install";
            CheckButton.IsEnabled = true;
        }
    }

    private async void ReleaseLink_Click(object sender, RoutedEventArgs e)
    {
        if (_lastCheck?.ReleaseUrl is not { Length: > 0 } url ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        await Launcher.LaunchUriAsync(uri);
    }
}
