namespace Padmon.Core.Settings;

public sealed class PadmonSettings
{
    public const string DefaultCustomThemeBackground = "#111214";
    public const string DefaultCustomThemeForeground = "#1A1B1F";
    public const string DefaultCustomThemeText = "#ECEDEF";
    public const string DefaultCustomThemeHighlight = "#7AA2F7";

    public bool BackgroundMonitoring { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool NotifyOnConnect { get; set; } = true;

    public bool NotifyOnDisconnect { get; set; } = true;

    public bool NotifyOnlyWhenBackground { get; set; }

    public bool UseNativeNotifications { get; set; }

    /// <summary>
    /// Absolute desktop coordinates of the in-app notification card's top-left
    /// corner. Null means use the default bottom-right position.
    /// </summary>
    public int? NotificationPositionX { get; set; }

    public int? NotificationPositionY { get; set; }

    /// <summary>
    /// Show the controller diagram tester (hardwaretester-style) instead of the classic tester cards.
    /// </summary>
    public bool ShowControllerDiagram { get; set; } = true;

    public int HistoryRetentionDays { get; set; } = 90;

    public string Theme { get; set; } = "System";

    public bool UseSystemAccent { get; set; }

    public string CustomThemeBackground { get; set; } = DefaultCustomThemeBackground;

    public string CustomThemeForeground { get; set; } = DefaultCustomThemeForeground;

    public string CustomThemeText { get; set; } = DefaultCustomThemeText;

    public string CustomThemeHighlight { get; set; } = DefaultCustomThemeHighlight;
}