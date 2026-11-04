using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Padmon.Services;
using Padmon.ViewModels;

namespace Padmon.Views;

public sealed partial class CouchControlPage : Page
{
    private bool _syncing;

    public CouchControlPage()
    {
        InitializeComponent();
    }

    private static CouchControlService Couch => App.Services.Couch;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        Couch.StateChanged += OnCouchStateChanged;
        App.Services.Monitor.Controllers.CollectionChanged += OnControllersChanged;

        ControllerPicker.ItemsSource = App.Services.Monitor.Controllers;
        ControllerPicker.IsEnabled = CouchControlService.IsAvailable;
        AvailabilityBar.IsOpen = !CouchControlService.IsAvailable;

        if (!CouchControlService.IsAvailable)
        {
            EnableToggle.IsEnabled = false;
            StatusText.Text = Couch.Status;
            return;
        }

        SyncFromService();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Couch.StateChanged -= OnCouchStateChanged;
        App.Services.Monitor.Controllers.CollectionChanged -= OnControllersChanged;
        base.OnNavigatedFrom(e);
    }

    private void OnCouchStateChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(SyncFromService);
    }

    private void OnControllersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(SyncControllerSelection);
    }

    private void SyncFromService()
    {
        _syncing = true;
        try
        {
            EnableToggle.IsOn = Couch.Enabled;
            StatusText.Text = Couch.Status;
            SyncControllerSelection();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SyncControllerSelection()
    {
        var key = App.Services.Settings.CouchControl.ControllerStableKey;
        var selected = App.Services.Monitor.Controllers.FirstOrDefault(
            controller => string.Equals(controller.StableKey, key, StringComparison.Ordinal));

        if (selected is null && string.IsNullOrWhiteSpace(key))
        {
            selected = App.Services.Monitor.Controllers.FirstOrDefault();
        }

        if (!ReferenceEquals(ControllerPicker.SelectedItem, selected))
        {
            ControllerPicker.SelectedItem = selected;
        }
    }

    private void EnableToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        Couch.SetEnabled(EnableToggle.IsOn);
        SyncFromService();
    }

    private void ControllerPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || ControllerPicker.SelectedItem is not ControllerViewModel controller)
        {
            return;
        }

        Couch.SetController(controller.StableKey);
    }
}
