using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Padmon.ViewModels;

namespace Padmon.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<KnownControllerViewModel> KnownControllers { get; } = [];

    public ObservableCollection<ConnectionSessionViewModel> Sessions { get; } = [];

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        RetentionText.Text = $"{App.Services.Settings.HistoryRetentionDays} days";

        IReadOnlyList<Padmon.Core.History.ControllerRecord> records;
        IReadOnlyList<Padmon.Core.History.ConnectionSession> sessions;

        try
        {
            records = await App.Services.History.GetControllersAsync();
            sessions = await App.Services.History.GetSessionsAsync(100);
        }
        catch
        {
            return;
        }

        var byKey = records.ToDictionary(record => record.StableKey, StringComparer.Ordinal);

        KnownControllers.Clear();
        foreach (var record in records)
        {
            KnownControllers.Add(new KnownControllerViewModel(record));
        }

        Sessions.Clear();
        foreach (var session in sessions)
        {
            var name = byKey.TryGetValue(session.StableKey, out var record)
                ? record.DisplayName
                : "Unknown controller";

            Sessions.Add(new ConnectionSessionViewModel(session, name));
        }

        KnownEmptyCard.Visibility = KnownControllers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        KnownListCard.Visibility = KnownControllers.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SessionsEmptyCard.Visibility = Sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SessionsListCard.Visibility = Sessions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
