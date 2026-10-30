using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Padmon.Core.History;
using Padmon.Services;
using Padmon.ViewModels;

namespace Padmon.Views;

public sealed partial class ControllersPage : Page
{
    private int _historyLoadVersion;

    public ControllersPage()
    {
        ViewModel = App.Services.Monitor;
        InitializeComponent();

        ViewModel.Controllers.CollectionChanged += OnControllersChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.ErrorOccurred += OnMonitorError;
        Loaded += OnLoaded;

        UpdateState();
    }

    public ControllerMonitor ViewModel { get; }

    public ObservableCollection<KnownControllerViewModel> PreviousControllers { get; } = [];

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

        var known = records
            .Where(record => !connectedKeys.Contains(record.StableKey))
            .Select(record => new KnownControllerViewModel(record))
            .ToList();

        PreviousControllers.Clear();
        foreach (var controller in known)
        {
            PreviousControllers.Add(controller);
        }

        var hasHistory = PreviousControllers.Count > 0;
        HistoryEmptyCard.Visibility = hasHistory ? Visibility.Collapsed : Visibility.Visible;
        HistoryListCard.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ViewHistory_Click(object sender, RoutedEventArgs e)
    {
        ((MainWindow)App.MainWindow!).NavigateTo("history");
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
