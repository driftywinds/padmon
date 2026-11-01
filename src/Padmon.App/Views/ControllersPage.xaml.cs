using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Padmon.Core.History;
using Padmon.Services;
using Padmon.ViewModels;

namespace Padmon.Views;

public sealed partial class ControllersPage : Page
{
    private int _historyLoadVersion;
    private IReadOnlyList<KnownControllerViewModel> _allKnownControllers = [];
    private bool _hasHistory;

    public ControllersPage()
    {
        ViewModel = App.Services.Monitor;
        InitializeComponent();

        ViewModel.Controllers.CollectionChanged += OnControllersChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.ErrorOccurred += OnMonitorError;
        Loaded += OnLoaded;

        UpdateState();
        UpdateMonitoringStatus();
    }

    public ControllerMonitor ViewModel { get; }

    public ObservableCollection<KnownControllerViewModel> PreviousControllers { get; } = [];

    private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout(e.NewSize.Width);
    }

    private void UpdateResponsiveLayout(double width)
    {
        var wide = width >= 1180 && _hasHistory;

        Grid.SetColumnSpan(PrimaryPanel, wide ? 1 : 2);
        Grid.SetRow(SecondaryPanel, wide ? 0 : 1);
        Grid.SetColumn(SecondaryPanel, wide ? 1 : 0);
        Grid.SetColumnSpan(SecondaryPanel, wide ? 1 : 2);
        BodyGrid.ColumnSpacing = wide ? 20 : 0;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ReloadHistoryAsync();
    }

    private void OnControllersChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        UpdateState();
        _ = ReloadHistoryAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ControllerMonitor.ConnectedCount))
        {
            UpdateState();
        }

        if (args.PropertyName is nameof(ControllerMonitor.IsMonitoring) or nameof(ControllerMonitor.Error))
        {
            UpdateMonitoringStatus();
        }
    }

    private void UpdateMonitoringStatus()
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.Error))
        {
            MonitoringText.Text = "Input issue";
            MonitoringDot.Fill = new SolidColorBrush(Colors.IndianRed);
            return;
        }

        if (ViewModel.IsMonitoring)
        {
            MonitoringText.Text = "Watching";
            MonitoringDot.Fill = new SolidColorBrush(Colors.SeaGreen);
            return;
        }

        MonitoringText.Text = "Starting";
        MonitoringDot.Fill = new SolidColorBrush(Colors.Goldenrod);
    }

    private void OnMonitorError(object? sender, string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private void UpdateState()
    {
        var hasControllers = ViewModel.Controllers.Count > 0;
        EmptyPanel.Visibility = hasControllers ? Visibility.Collapsed : Visibility.Visible;
        ConnectedPanel.Visibility = hasControllers ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task ReloadHistoryAsync()
    {
        var version = ++_historyLoadVersion;
        IReadOnlyList<ControllerRecord> records;

        try
        {
            records = await App.Services.History.GetControllersAsync();
        }
        catch
        {
            return;
        }

        if (version != _historyLoadVersion)
        {
            return;
        }

        var connectedKeys = ViewModel.Controllers
            .Select(controller => controller.StableKey)
            .ToHashSet(StringComparer.Ordinal);

        var allKnown = records
            .Select(record => new KnownControllerViewModel(record))
            .ToList();

        _allKnownControllers = allKnown;

        var known = allKnown
            .Where(controller => !connectedKeys.Contains(controller.Record.StableKey))
            .ToList();

        PreviousControllers.Clear();
        foreach (var controller in known)
        {
            PreviousControllers.Add(controller);
        }

        var hasHistory = PreviousControllers.Count > 0;
        _hasHistory = hasHistory;
        SecondaryPanel.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyCard.Visibility = hasHistory ? Visibility.Collapsed : Visibility.Visible;
        HistoryListCard.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;

        UpdateResponsiveLayout(PageRoot.ActualWidth);
    }

    private void ViewHistory_Click(object sender, RoutedEventArgs e)
    {
        ((MainWindow)App.MainWindow!).NavigateTo("history");
    }

    private async void MergeHistory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string stableKey })
        {
            return;
        }

        var controller = PreviousControllers.FirstOrDefault(
            candidate => string.Equals(candidate.Record.StableKey, stableKey, StringComparison.Ordinal));

        if (controller is null)
        {
            return;
        }

        if (await DeviceMergeDialog.ShowAsync(XamlRoot, controller, _allKnownControllers))
        {
            await ReloadHistoryAsync();
        }
    }

    private async void MoreInfo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string stableKey })
        {
            return;
        }

        var controller = ViewModel.Controllers.FirstOrDefault(
            candidate => string.Equals(candidate.StableKey, stableKey, StringComparison.Ordinal));

        if (controller is null)
        {
            return;
        }

        ControllerRecord? record = null;

        try
        {
            var records = await App.Services.History.GetControllersAsync();
            record = records.FirstOrDefault(candidate => string.Equals(
                candidate.StableKey,
                stableKey,
                StringComparison.Ordinal));
        }
        catch
        {
        }

        var panel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(2)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Everything padmon currently knows about this controller.",
            FontSize = 13,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });

        AddSectionHeader(panel, "IDENTITY");
        AddInfoRow(panel, "Display name", controller.DisplayName);
        AddInfoRow(panel, "Hardware name", controller.HardwareName);
        AddInfoRow(panel, "Stable key", controller.StableKey, mono: true);
        AddInfoRow(panel, "Vendor ID", $"0x{controller.Device.VendorId:X4} ({controller.Device.VendorId})");
        AddInfoRow(panel, "Product ID", $"0x{controller.Device.ProductId:X4} ({controller.Device.ProductId})");
        AddInfoRow(panel, "Serial", controller.Device.Serial);
        AddInfoRow(panel, "SDL GUID", controller.Device.SdlGuid, mono: true);
        AddInfoRow(panel, "Device path", controller.Device.Path, mono: true);

        AddSectionHeader(panel, "CONNECTION");
        AddInfoRow(panel, "Type", controller.KindLabel);
        AddInfoRow(panel, "Transport", controller.ConnectionLabel);
        AddInfoRow(panel, "Mapping", controller.DeviceBadge);
        AddInfoRow(panel, "Battery", controller.BatteryLabel);
        AddInfoRow(panel, "Instance ID", controller.Device.InstanceId.ToString(CultureInfo.CurrentCulture));

        AddSectionHeader(panel, "HISTORY");
        AddInfoRow(panel, "First seen", record is null
            ? "Not recorded"
            : record.FirstSeen.ToLocalTime().ToString("f", System.Globalization.CultureInfo.CurrentCulture));
        AddInfoRow(panel, "Last seen", record is null
            ? "Not recorded"
            : record.LastSeen.ToLocalTime().ToString("f", System.Globalization.CultureInfo.CurrentCulture));
        AddInfoRow(panel, "Sessions", record?.SessionCount.ToString(CultureInfo.CurrentCulture) ?? "Not recorded");

        AddSectionHeader(panel, "LIVE SAMPLE");
        AddInfoRow(panel, "Buttons", controller.Snapshot is null
            ? "Open the tester to sample"
            : controller.Snapshot.Buttons.Count.ToString(CultureInfo.CurrentCulture));
        AddInfoRow(panel, "Axes", controller.Snapshot is null
            ? "Open the tester to sample"
            : controller.Snapshot.Axes.Count.ToString(CultureInfo.CurrentCulture));
        AddInfoRow(panel, "Hats", controller.Snapshot is null
            ? "Open the tester to sample"
            : controller.Snapshot.Hats.Count.ToString(CultureInfo.CurrentCulture));
        AddInfoRow(panel, "Current input", controller.Snapshot is null
            ? "Open the tester to sample"
            : controller.PrimaryInputLabel);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"{controller.DisplayName} details",
            PrimaryButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 560,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        };

        await dialog.ShowAsync();
    }

    private static void AddSectionHeader(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = 0.6,
            Margin = new Thickness(0, 10, 0, 2)
        });
    }

    private static void AddInfoRow(StackPanel panel, string label, string? value, bool mono = false)
    {
        var grid = new Grid
        {
            ColumnSpacing = 16
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Opacity = 0.66,
            TextWrapping = TextWrapping.Wrap
        };

        var valueBlock = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
            FontSize = 13,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap
        };

        if (mono)
        {
            valueBlock.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            valueBlock.FontSize = 12;
        }

        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        panel.Children.Add(grid);
    }

    private void TestInputs_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string stableKey })
        {
            Frame.Navigate(typeof(ControllerDetailPage), stableKey);
        }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string stableKey })
        {
            return;
        }

        var controller = ViewModel.Controllers.FirstOrDefault(
            candidate => string.Equals(candidate.StableKey, stableKey, StringComparison.Ordinal));

        if (controller is null)
        {
            return;
        }

        var input = new TextBox
        {
            Text = controller.DisplayName,
            PlaceholderText = controller.HardwareName,
            MaxLength = 64,
            SelectionStart = controller.DisplayName.Length
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Rename controller",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = input
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var name = input.Text.Trim();
        await ViewModel.RenameAsync(
            stableKey,
            string.IsNullOrWhiteSpace(name) ? controller.HardwareName : name);
    }
}
