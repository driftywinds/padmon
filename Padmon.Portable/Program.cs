using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Padmon.Portable;

/// <summary>
/// Unpacks the embedded padmon build once per release and launches it.
/// Settings, history and logs stay in %LOCALAPPDATA%\padmon as usual.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var assembly = typeof(Program).Assembly;
            var buildId = assembly.ManifestModule.ModuleVersionId.ToString("N");

            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "padmon",
                "portable");

            var appDirectory = Path.Combine(root, buildId);
            var executable = Path.Combine(appDirectory, "Padmon.exe");

            if (!File.Exists(executable))
            {
                Extract(assembly, root, appDirectory);
                CleanOldBuilds(root, buildId);
            }

            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = appDirectory,
                UseShellExecute = false
            };

            foreach (var argument in args)
            {
                start.ArgumentList.Add(argument);
            }

            // Lets "start with Windows" point at this portable exe instead of
            // the versioned unpack folder.
            if (Environment.ProcessPath is { Length: > 0 } self)
            {
                start.Environment["PADMON_PORTABLE_EXE"] = self;
            }

            Process.Start(start);
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox(IntPtr.Zero, $"padmon could not start:\n\n{exception.Message}", "padmon", 0x10);
            return 1;
        }
    }

    private static void Extract(System.Reflection.Assembly assembly, string root, string appDirectory)
    {
        using var payload = assembly.GetManifestResourceStream("payload.zip")
            ?? throw new InvalidOperationException("This portable build has no embedded app.");

        Directory.CreateDirectory(root);
        var temporary = Path.Combine(root, $"{Path.GetFileName(appDirectory)}.tmp-{Environment.ProcessId}");

        try
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }

            using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(temporary);
            }

            try
            {
                Directory.Move(temporary, appDirectory);
            }
            catch (IOException) when (File.Exists(Path.Combine(appDirectory, "Padmon.exe")))
            {
                // Another launch finished unpacking first.
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporary))
                {
                    Directory.Delete(temporary, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private static void CleanOldBuilds(string root, string currentBuildId)
    {
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (string.Equals(Path.GetFileName(directory), currentBuildId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Still running or locked; try again next time.
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
