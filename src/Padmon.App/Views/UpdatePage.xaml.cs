using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Padmon.Core.Diagnostics;
using Padmon.Services;
using Windows.System;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Padmon.Views;

public sealed partial class UpdatePage : Page
{
    private readonly DispatcherQueueTimer _stallTimer;
    private UpdateCheckResult? _lastCheck;
    private UpdateCheckResult? _activeUpdate;
    private CancellationTokenSource? _downloadCancellation;
    private DateTimeOffset _lastDownloadProgress;
    private bool _checking;
    private bool _downloading;
    private bool _stallReported;

    public UpdatePage()
    {
        InitializeComponent();
        CurrentVersionText.Text = $"Version {UpdateService.CurrentVersion}";
        BuildKindText.Text = UpdateService.ModeLabel;

        _stallTimer = DispatcherQueue.CreateTimer();
        _stallTimer.Interval = TimeSpan.FromSeconds(1);
        _stallTimer.IsRepeating = true;
        _stallTimer.Tick += OnStallTick;

        if (UpdateService.IsStoreBuild)
        {
            PageSubtitle.Text = "padmon was installed as an MSIX package. Updates are delivered by Microsoft Store.";
            CheckButton.Visibility = Visibility.Collapsed;
            StoreCard.Visibility = Visibility.Visible;
        }
    }

    private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= 1180;

        Grid.SetColumnSpan(UpdateMain, wide ? 1 : 2);
        Grid.SetRow(UpdateInfo, wide ? 0 : 1);
        Grid.SetColumn(UpdateInfo, wide ? 1 : 0);
        Grid.SetColumnSpan(UpdateInfo, wide ? 1 : 2);
        UpdateBody.ColumnSpacing = wide ? 20 : 0;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await CheckAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _downloadCancellation?.Cancel();
        _stallTimer.Stop();
        base.OnNavigatedFrom(e);
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        await CheckAsync();
    }

    private async Task CheckAsync()
    {
        if (UpdateService.IsStoreBuild)
        {
            StoreCard.Visibility = Visibility.Visible;
            CheckButton.Visibility = Visibility.Collapsed;
            return;
        }

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
        DownloadPanel.Visibility = Visibility.Collapsed;
        RetryDownloadButton.Visibility = Visibility.Collapsed;
        CancelDownloadButton.Visibility = Visibility.Collapsed;

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

        await DownloadAndInstallAsync(update);
    }

    private async Task DownloadAndInstallAsync(UpdateCheckResult update)
    {
        _downloadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;
        _activeUpdate = update;
        _downloading = true;
        _stallReported = false;
        _lastDownloadProgress = DateTimeOffset.UtcNow;

        InstallButton.IsEnabled = false;
        InstallButton.Content = "Downloading…";
        CheckButton.IsEnabled = false;
        DownloadPanel.Visibility = Visibility.Visible;
        DownloadProgressBar.IsIndeterminate = true;
        DownloadProgressBar.Value = 0;
        DownloadStatusText.Text = "Connecting to GitHub…";
        DownloadPercentText.Text = string.Empty;
        RetryDownloadButton.Visibility = Visibility.Visible;
        CancelDownloadButton.Visibility = Visibility.Visible;
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.Title = "Downloading update";
        StatusBar.Message = "Downloading the release from GitHub. Retry if the download stalls.";
        StatusBar.IsOpen = true;

        _stallTimer.Start();

        try
        {
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                if (!ReferenceEquals(_downloadCancellation, cancellation) ||
                    cancellation.IsCancellationRequested)
                {
                    return;
                }

                _lastDownloadProgress = DateTimeOffset.UtcNow;
                if (_stallReported)
                {
                    _stallReported = false;
                    StatusBar.Severity = InfoBarSeverity.Informational;
                    StatusBar.Title = "Downloading update";
                    StatusBar.Message = "Download resumed.";
                }

                UpdateDownloadProgress(value);
            });

            var downloaded = await UpdateService
                .DownloadAsync(update, progress, cancellation.Token)
                .ConfigureAwait(true);

            if (cancellation.IsCancellationRequested ||
                !ReferenceEquals(_downloadCancellation, cancellation))
            {
                return;
            }

            _downloading = false;
            _stallTimer.Stop();
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = 100;
            DownloadPercentText.Text = "100%";
            DownloadStatusText.Text = "Download complete. Installing update…";
            InstallButton.Content = "Installing…";
            StatusBar.Severity = InfoBarSeverity.Informational;
            StatusBar.Title = "Installing update";
            StatusBar.Message = "The update is downloaded. padmon will restart when installation is ready.";
            StatusBar.IsOpen = true;

            await UpdateService
                .InstallDownloadedAsync(update, downloaded, cancellation.Token)
                .ConfigureAwait(true);

            DownloadStatusText.Text = "Update installed. Restarting…";
            DownloadPercentText.Text = "100%";
            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Title = "Update downloaded";
            StatusBar.Message = "padmon will restart and finish installing the update.";
            StatusBar.IsOpen = true;
            await Task.Delay(600);
            Environment.Exit(0);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!ReferenceEquals(_downloadCancellation, cancellation))
            {
                return;
            }

            _downloading = false;
            _stallTimer.Stop();
            StatusBar.Severity = InfoBarSeverity.Warning;
            StatusBar.Title = "Download canceled";
            StatusBar.Message = "Press Retry download to start again.";
            StatusBar.IsOpen = true;
            ResetDownloadControls();
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(_downloadCancellation, cancellation))
            {
                return;
            }

            PadmonLog.Error("update install failed", exception);
            _downloading = false;
            _stallTimer.Stop();
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Title = "Could not install the update";
            StatusBar.Message = exception.Message;
            StatusBar.IsOpen = true;
            ResetDownloadControls();
        }
        finally
        {
            if (ReferenceEquals(_downloadCancellation, cancellation))
            {
                _downloadCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void UpdateDownloadProgress(UpdateDownloadProgress progress)
    {
        if (progress.TotalBytes is > 0)
        {
            var percent = progress.Percent ?? 0;
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = percent;
            DownloadPercentText.Text = $"{percent:0}%";
            DownloadStatusText.Text =
                $"{FormatBytes(progress.BytesReceived)} of {FormatBytes(progress.TotalBytes.Value)}";
        }
        else
        {
            DownloadProgressBar.IsIndeterminate = true;
            DownloadPercentText.Text = string.Empty;
            DownloadStatusText.Text = $"{FormatBytes(progress.BytesReceived)} downloaded";
        }
    }

    private void ResetDownloadControls()
    {
        InstallButton.IsEnabled = true;
        InstallButton.Content = "Download and install";
        CheckButton.IsEnabled = true;
        RetryDownloadButton.Visibility = Visibility.Visible;
        CancelDownloadButton.Visibility = Visibility.Collapsed;
    }

    private async void RetryDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_activeUpdate is not { CanInstall: true } update)
        {
            return;
        }

        _downloadCancellation?.Cancel();
        await DownloadAndInstallAsync(update);
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        _downloadCancellation?.Cancel();
    }

    private void OnStallTick(DispatcherQueueTimer sender, object args)
    {
        if (!_downloading || _stallReported)
        {
            return;
        }

        var idle = DateTimeOffset.UtcNow - _lastDownloadProgress;
        if (idle < TimeSpan.FromSeconds(12))
        {
            return;
        }

        _stallReported = true;
        DownloadStatusText.Text =
            $"No data received for {idle.TotalSeconds:0} seconds. Retry if it does not resume.";
        StatusBar.Severity = InfoBarSeverity.Warning;
        StatusBar.Title = "Download appears stalled";
        StatusBar.Message = "GitHub has not sent data for 12 seconds. Press Retry download to restart.";
        StatusBar.IsOpen = true;
        RetryDownloadButton.Visibility = Visibility.Visible;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";
        }

        if (bytes >= 1024L * 1024)
        {
            return $"{bytes / (1024.0 * 1024):0.0} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024.0:0} KB";
        }

        return $"{bytes} B";
    }

    private async void OpenStoreUpdates_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri("ms-windows-store://downloadsandupdates"));
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
