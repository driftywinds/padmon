namespace Padmon.Core;

public static class PadmonPaths
{
    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    public static string DatabaseFile { get; } = Path.Combine(DataDirectory, "padmon.db");

    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "logs");

    public static void EnsureDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    private static string ResolveDataDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable("PADMON_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return Path.GetFullPath(overridden);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "padmon");
    }
}
