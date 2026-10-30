using CommunityToolkit.Mvvm.ComponentModel;
using Padmon.Core.Settings;
using Padmon.Services;

namespace Padmon.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private readonly PadmonSettings _settings;

    public SettingsViewModel(ISettingsStore store)
    {
        _store = store;
        _settings = store.Current;
    }

    public bool BackgroundMonitoring
    {
        get => _settings.BackgroundMonitoring;
        set
        {
            if (_settings.BackgroundMonitoring == value)
            {
                return;
            }

            _settings.BackgroundMonitoring = value;
            OnPropertyChanged();
            _ = SaveAsync();
        }
    }

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set
        {
            if (_settings.StartWithWindows == value)
            {
                return;
            }

            if (!StartupService.TrySetEnabled(value))
            {
                return;
            }

            _settings.StartWithWindows = value;
            OnPropertyChanged();
            _ = SaveAsync();
        }
    }

    public bool NotifyOnConnect
    {
        get => _settings.NotifyOnConnect;
        set
        {
            if (_settings.NotifyOnConnect == value)
            {
                return;
            }

            _settings.NotifyOnConnect = value;
            OnPropertyChanged();
            _ = SaveAsync();
        }
    }

    public bool NotifyOnDisconnect
    {
        get => _settings.NotifyOnDisconnect;
        set
        {
            if (_settings.NotifyOnDisconnect == value)
            {
                return;
            }

            _settings.NotifyOnDisconnect = value;
            OnPropertyChanged();
            _ = SaveAsync();
        }
    }

    public bool NotifyOnlyWhenBackground
    {
        get => _settings.NotifyOnlyWhenBackground;
        set
        {
            if (_settings.NotifyOnlyWhenBackground == value)
            {
                return;
            }

            _settings.NotifyOnlyWhenBackground = value;
            OnPropertyChanged();
            _ = SaveAsync();
        }
    }

    public int RetentionIndex
    {
        get => _settings.HistoryRetentionDays switch
        {
            <= 30 => 0,
            <= 90 => 1,
            <= 365 => 2,
            _ => 3
        };
        set
        {
            var days = value switch
            {
                0 => 30,
                1 => 90,
                2 => 365,
                _ => 3650
            };

            if (_settings.HistoryRetentionDays == days)
            {
                return;
            }

            _settings.HistoryRetentionDays = days;
            OnPropertyChanged();
            _ = SaveAsync();
        }
    }

    public int ThemeIndex
    {
        get => _settings.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
        set
        {
            var theme = value switch
            {
                1 => "Light",
                2 => "Dark",
                _ => "System"
            };

            if (string.Equals(_settings.Theme, theme, StringComparison.Ordinal))
            {
                return;
            }

            _settings.Theme = theme;
            OnPropertyChanged();
            ApplyTheme(theme);
            _ = SaveAsync();
        }
    }

    private static void ApplyTheme(string theme)
    {
        if (App.MainWindow is MainWindow window)
        {
            window.ApplyTheme(theme);
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            await _store.SaveAsync(_settings);
        }
        catch
        {
        }
    }
}
