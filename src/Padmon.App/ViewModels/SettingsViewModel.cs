using CommunityToolkit.Mvvm.ComponentModel;
using Padmon.Core.Settings;
using Padmon.Services;

namespace Padmon.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private readonly PadmonSettings _settings;
    private string _notificationTestStatus = "Use Send test to check which notification style Windows allows.";

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

            _ = SetStartWithWindowsAsync(value);
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

    public bool ShowControllerDiagram
    {
        get => _settings.ShowControllerDiagram;
        set
        {
            if (_settings.ShowControllerDiagram == value)
            {
                return;
            }

            _settings.ShowControllerDiagram = value;
            OnPropertyChanged();
            _ = SaveAsync();
        }
    }

    public string NotificationTestStatus
    {
        get => _notificationTestStatus;
        set => SetProperty(ref _notificationTestStatus, value);
    }

    public bool HasCustomNotificationPosition =>
        _settings.NotificationPositionX is not null &&
        _settings.NotificationPositionY is not null;

    public string NotificationPositionStatus => HasCustomNotificationPosition
        ? "Custom position saved."
        : "Default: bottom-right corner.";

    public void SetNotificationPosition(int x, int y)
    {
        _settings.NotificationPositionX = x;
        _settings.NotificationPositionY = y;
        OnPropertyChanged(nameof(HasCustomNotificationPosition));
        OnPropertyChanged(nameof(NotificationPositionStatus));
        _ = SaveAsync();
    }

    public void ResetNotificationPosition()
    {
        _settings.NotificationPositionX = null;
        _settings.NotificationPositionY = null;
        OnPropertyChanged(nameof(HasCustomNotificationPosition));
        OnPropertyChanged(nameof(NotificationPositionStatus));
        _ = SaveAsync();
    }

    public bool NativeNotifications
    {
        get => _settings.UseNativeNotifications;
        set
        {
            if (_settings.UseNativeNotifications == value)
            {
                return;
            }

            _settings.UseNativeNotifications = value;
            App.Services.Notifications.PreferNative = value;
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
            "Custom" => 3,
            _ => 0
        };
        set
        {
            var theme = value switch
            {
                1 => "Light",
                2 => "Dark",
                3 => "Custom",
                _ => "System"
            };

            if (string.Equals(_settings.Theme, theme, StringComparison.Ordinal))
            {
                return;
            }

            _settings.Theme = theme;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomTheme));
            ApplyTheme(theme);
            _ = SaveAsync();
        }
    }

    public bool UseSystemAccent
    {
        get => _settings.UseSystemAccent;
        set
        {
            if (_settings.UseSystemAccent == value)
            {
                return;
            }

            _settings.UseSystemAccent = value;
            OnPropertyChanged();
            ApplyTheme(_settings.Theme);
            _ = SaveAsync();
        }
    }

    public bool IsCustomTheme => string.Equals(_settings.Theme, "Custom", StringComparison.Ordinal);

    public string CustomBackground
    {
        get => _settings.CustomThemeBackground;
        set => SetCustomColor(value, current => _settings.CustomThemeBackground = current,
            () => _settings.CustomThemeBackground);
    }

    public string CustomForeground
    {
        get => _settings.CustomThemeForeground;
        set => SetCustomColor(value, current => _settings.CustomThemeForeground = current,
            () => _settings.CustomThemeForeground);
    }

    public string CustomText
    {
        get => _settings.CustomThemeText;
        set => SetCustomColor(value, current => _settings.CustomThemeText = current,
            () => _settings.CustomThemeText);
    }

    public string CustomHighlight
    {
        get => _settings.CustomThemeHighlight;
        set => SetCustomColor(value, current => _settings.CustomThemeHighlight = current,
            () => _settings.CustomThemeHighlight);
    }

    public void ResetCustomTheme()
    {
        _settings.CustomThemeBackground = PadmonSettings.DefaultCustomThemeBackground;
        _settings.CustomThemeForeground = PadmonSettings.DefaultCustomThemeForeground;
        _settings.CustomThemeText = PadmonSettings.DefaultCustomThemeText;
        _settings.CustomThemeHighlight = PadmonSettings.DefaultCustomThemeHighlight;

        OnPropertyChanged(nameof(CustomBackground));
        OnPropertyChanged(nameof(CustomForeground));
        OnPropertyChanged(nameof(CustomText));
        OnPropertyChanged(nameof(CustomHighlight));
        ApplyTheme("Custom");
        _ = SaveAsync();
    }

    private async Task SetStartWithWindowsAsync(bool value)
    {
        if (!await StartupService.TrySetEnabledAsync(value))
        {
            // Revert the toggle if Windows refused the change (for example,
            // the packaged startup task was disabled by the user or policy).
            OnPropertyChanged(nameof(StartWithWindows));
            return;
        }

        _settings.StartWithWindows = value;
        OnPropertyChanged(nameof(StartWithWindows));
        await SaveAsync();
    }

    private void SetCustomColor(
        string value,
        Action<string> assign,
        Func<string> read)
    {
        if (string.Equals(read(), value, StringComparison.Ordinal))
        {
            return;
        }

        assign(value);
        OnPropertyChanged();
        ApplyTheme("Custom");
        _ = SaveAsync();
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