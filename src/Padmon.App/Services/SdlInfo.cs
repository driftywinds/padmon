using System.Diagnostics;

namespace Padmon.Services;

internal static class SdlInfo
{
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "SDL3.dll");
            if (File.Exists(path))
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                var version = $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
                return info.FilePrivatePart > 0
                    ? $"{version}.{info.FilePrivatePart}"
                    : version;
            }
        }
        catch
        {
        }

        return "3.4.18";
    }
}
