using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using Padmon.Core.Settings;
using Padmon.Services;
using Padmon.ViewModels;

namespace Padmon.Views;

public sealed partial class CouchControlPage : Page
{
    private readonly DispatcherQueueTimer _saveTimer;
    private bool _syncing;
    private bool _syncingSettings;

    public CouchControlPage()
    {
        InitializeComponent();

        _syncingSettings = true;
        CursorSpeedSlider.Minimum = 200;
        ScrollSpeedSlider.Minimum = 1;
        _syncingSettings = false;

        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(300);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => Couch.SaveSettings();
    }

    public ObservableCollection<CouchMappingRow> MappingRows { get; } = [];

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
        SyncSettingsControls();
        RefreshMappingRows();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _saveTimer.Stop();
        Couch.SaveSettings();
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
        DispatcherQueue.TryEnqueue(() =>
        {
            SyncControllerSelection();
            RefreshMappingRows();
        });
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

    private void SyncSettingsControls()
    {
        _syncingSettings = true;
        try
        {
            var settings = Couch.Settings;
            CursorSpeedSlider.Value = settings.CursorSpeed;
            DeadZoneSlider.Value = settings.DeadZone;
            ScrollDeadZoneSlider.Value = settings.ScrollDeadZone;
            ScrollSpeedSlider.Value = settings.ScrollSpeed;
            AccelerationSlider.Value = settings.Acceleration;
            SwapSticksToggle.IsOn = settings.SwapSticks;
            PauseWhenFocusedToggle.IsOn = settings.PauseWhenPadmonFocused;
            UpdateSettingLabels();
        }
        finally
        {
            _syncingSettings = false;
        }
    }

    private void UpdateSettingLabels()
    {
        CursorSpeedText.Text = $"{Couch.Settings.CursorSpeed} px/s";
        DeadZoneText.Text = $"{Couch.Settings.DeadZone} raw units";
        ScrollDeadZoneText.Text = $"{Couch.Settings.ScrollDeadZone} raw units";
        ScrollSpeedText.Text =
            $"{Couch.Settings.ScrollSpeed.ToString("0.#", CultureInfo.InvariantCulture)} notches/sec";
        AccelerationText.Text =
            $"{Couch.Settings.Acceleration.ToString("0.0", CultureInfo.InvariantCulture)}×";
    }

    private void RefreshMappingRows()
    {
        var selected = ControllerPicker.SelectedItem as ControllerViewModel;
        var rows = CouchMappingCatalog.Create(selected?.Snapshot, Couch.Settings);

        MappingRows.Clear();
        foreach (var row in rows)
        {
            MappingRows.Add(row);
        }
    }

    private void QueueSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async void ChangeMapping_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
        {
            return;
        }

        var row = MappingRows.FirstOrDefault(
            candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));

        if (row is null)
        {
            return;
        }

        var updated = await CouchMappingDialog.ShowAsync(XamlRoot, row.Label, row.Binding);
        if (updated is null)
        {
            return;
        }

        Couch.Settings.Bindings[id] = updated;
        Couch.ReloadBindings();
        Couch.SaveSettings();
        RefreshMappingRows();
    }

    private void ResetMappings_Click(object sender, RoutedEventArgs e)
    {
        Couch.Settings.Bindings.Clear();
        Couch.ReloadBindings();
        Couch.SaveSettings();
        RefreshMappingRows();
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
        RefreshMappingRows();
    }

    private void CursorSpeed_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingSettings)
        {
            return;
        }

        Couch.Settings.CursorSpeed = (int)Math.Round(e.NewValue);
        UpdateSettingLabels();
        QueueSave();
    }

    private void DeadZone_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingSettings)
        {
            return;
        }

        Couch.Settings.DeadZone = (int)Math.Round(e.NewValue);
        UpdateSettingLabels();
        QueueSave();
    }

    private void ScrollDeadZone_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingSettings)
        {
            return;
        }

        Couch.Settings.ScrollDeadZone = (int)Math.Round(e.NewValue);
        UpdateSettingLabels();
        QueueSave();
    }

    private void ScrollSpeed_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingSettings)
        {
            return;
        }

        Couch.Settings.ScrollSpeed = e.NewValue;
        UpdateSettingLabels();
        QueueSave();
    }

    private void Acceleration_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingSettings)
        {
            return;
        }

        Couch.Settings.Acceleration = e.NewValue;
        UpdateSettingLabels();
        QueueSave();
    }

    private void SwapSticks_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncingSettings)
        {
            return;
        }

        Couch.Settings.SwapSticks = SwapSticksToggle.IsOn;
        QueueSave();
    }

    private void PauseWhenFocused_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncingSettings)
        {
            return;
        }

        Couch.Settings.PauseWhenPadmonFocused = PauseWhenFocusedToggle.IsOn;
        QueueSave();
    }
}
