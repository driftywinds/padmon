using Windows.ApplicationModel;

namespace Padmon.Services;

/// <summary>
/// Runtime facts that differ between the portable/unpackaged build and the
/// Microsoft Store MSIX build. All checks are lazy and safe to call at startup.
/// </summary>
public static class AppEnvironment
{
    public static bool IsPackaged { get; } = DetectPackaged();

    public static string? PackageFullName { get; } = ReadPackageFullName();

    public static string? PackageFamilyName { get; } = ReadPackageFamilyName();

    /// <summary>
    /// Store packages get their own local data directory so uninstalling the
    /// package cleans up settings and history. An explicit PADMON_DATA_DIR
    /// override still wins for development and tests.
    /// </summary>
    public static void ConfigureDataDirectory()
    {
        if (!IsPackaged ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PADMON_DATA_DIR")))
        {
            return;
        }

        try
        {
            var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            if (!string.IsNullOrWhiteSpace(localFolder))
            {
                Environment.SetEnvironmentVariable("PADMON_DATA_DIR", localFolder);
            }
        }
        catch
        {
            // Fall back to %LOCALAPPDATA%\padmon if the package data folder is
            // unavailable. The app still works, just without cleanup on uninstall.
        }
    }

    private static bool DetectPackaged()
    {
        try
        {
            _ = Package.Current.Id.Name;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadPackageFullName()
    {
        try
        {
            return Package.Current.Id.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadPackageFamilyName()
    {
        try
        {
            return Package.Current.Id.FamilyName;
        }
        catch
        {
            return null;
        }
    }
}
