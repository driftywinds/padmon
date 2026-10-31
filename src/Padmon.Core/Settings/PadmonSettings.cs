namespace Padmon.Core.Settings;

public sealed class PadmonSettings
{
    public bool BackgroundMonitoring { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool NotifyOnConnect { get; set; } = true;

    public bool NotifyOnDisconnect { get; set; } = true;

    public bool NotifyOnlyWhenBackground { get; set; }

    public bool UseNativeNotifications { get; set; }

    public int HistoryRetentionDays { get; set; } = 90;

    public string Theme { get; set; } = "System";

    public bool UseSystemAccent { get; set; }

    public string CustomThemeBackground { get; set; } = "#111214";

    public string CustomThemeForeground { get; set; } = "#1A1B1F";

    public string CustomThemeText { get; set; } = "#ECEDEF";

    public string CustomThemeHighlight { get; set; } = "#7AA2F7";
}