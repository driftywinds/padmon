using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Padmon.Core.Diagnostics;

namespace Padmon.Services;

public enum InstallMode
{
    PortableLauncher,
    PortableFolder,
    Installed
}

public sealed record UpdateCheckResult(
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseUrl,
    string? ReleaseNotes,
    string? AssetName,
    string? AssetUrl,
    bool HasRelease,
    bool IsNewer,
    bool CanInstall,
    string? Error = null);

public sealed class UpdateService
{
    private const string ReleasesApi = "https://api.github.com/repos/driftywinds/padmon/releases/latest";
    private static readonly string PortableExecutable =
        Environment.GetEnvironmentVariable("PADMON_PORTABLE_EXE") ?? string.Empty;

    public static string CurrentVersion { get; } = ReadCurrentVersion();

    public static InstallMode Mode { get; } = DetectInstallMode();

    public static string ModeLabel => Mode switch
    {
        InstallMode.PortableLauncher => "Portable single-file build",
        InstallMode.PortableFolder => "Portable folder build",
        _ => "Installed build"
    };

    public static bool IsPortableLauncher => Mode == InstallMode.PortableLauncher;

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = CurrentVersion;

        try
        {
            using var client = CreateClient();
            using var response = await client.GetAsync(ReleasesApi, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(
                    current,
                    null,
                    null,
                    null,
                    null,
                    null,
                    HasRelease: false,
                    IsNewer: false,
                    CanInstall: false);
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString()
                : null;
            var releaseUrl = root.TryGetProperty("html_url", out var urlElement)
                ? urlElement.GetString()
                : null;
            var notes = root.TryGetProperty("body", out var bodyElement)
                ? bodyElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(tag))
            {
                return new UpdateCheckResult(
                    current,
                    null,
                    releaseUrl,
                    notes,
                    null,
                    null,
                    HasRelease: false,
                    IsNewer: false,
                    CanInstall: false);
            }

            var latest = NormalizeVersion(tag);
            var parsedCurrent = ParseVersion(current);
            var parsedLatest = ParseVersion(latest);
            var newer = parsedCurrent is not null &&
                        parsedLatest is not null &&
                        parsedLatest > parsedCurrent;

            var asset = FindAsset(root, IsPortableLauncher);

            return new UpdateCheckResult(
                current,
                latest,
                releaseUrl,
                notes,
                asset?.Name,
                asset?.Url,
                HasRelease: true,
                IsNewer: newer,
                CanInstall: newer && asset is not null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            PadmonLog.Error("update check failed", exception);
            return new UpdateCheckResult(
                current,
                null,
                null,
                null,
                null,
                null,
                HasRelease: false,
                IsNewer: false,
                CanInstall: false,
                Error: exception.Message);
        }
    }

    public static async Task InstallAsync(
        UpdateCheckResult update,
        CancellationToken cancellationToken = default)
    {
        if (!update.CanInstall ||
            string.IsNullOrWhiteSpace(update.AssetUrl) ||
            string.IsNullOrWhiteSpace(update.LatestVersion))
        {
            throw new InvalidOperationException("No installable update is available.");
        }

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "padmon",
            "updates",
            update.LatestVersion);
        Directory.CreateDirectory(root);

        var downloaded = Path.Combine(root, update.AssetName ?? "update.bin");
        using (var client = CreateClient())
        using (var response = await client.GetAsync(
                   update.AssetUrl,
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = File.Create(downloaded);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        var target = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var isPortableLauncher = IsPortableLauncher;
        var restartPath = isPortableLauncher ? PortableExecutable : Path.Combine(target, "Padmon.exe");
        string sourcePath;
        string targetDirectory;

        if (isPortableLauncher)
        {
            sourcePath = downloaded;
            targetDirectory = Path.GetDirectoryName(PortableExecutable)!;
            EnsureWritable(PortableExecutable);
        }
        else
        {
            var staging = Path.Combine(root, "app");
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            await Task.Run(
                () => ZipFile.ExtractToDirectory(downloaded, staging),
                cancellationToken).ConfigureAwait(false);
            if (!File.Exists(Path.Combine(staging, "Padmon.exe")))
            {
                throw new InvalidOperationException("The release archive does not contain Padmon.exe.");
            }

            sourcePath = staging;
            targetDirectory = target;
            EnsureWritable(Path.Combine(target, "Padmon.exe"));
        }

        var script = Path.Combine(root, "install.ps1");
        var log = Path.Combine(root, "update.log");
        await File.WriteAllTextAsync(
            script,
            BuildUpdaterScript(sourcePath, targetDirectory, restartPath, isPortableLauncher, log),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken).ConfigureAwait(false);

        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        if (Process.Start(start) is null)
        {
            throw new InvalidOperationException("Could not start the updater.");
        }

        PadmonLog.Info($"update installer started for {update.LatestVersion}");
    }

    private static string BuildUpdaterScript(
        string source,
        string targetDirectory,
        string restartPath,
        bool isPortable,
        string log)
    {
        var oldWorkingDirectory = Path.GetDirectoryName(restartPath) ?? targetDirectory;

        return $$"""
$ErrorActionPreference = 'Stop'
$log = '{{EscapePath(log)}}'
function Write-UpdateLog($message) {
    Add-Content -LiteralPath $log -Value "$(Get-Date -Format o) $message"
}

try {
    Write-UpdateLog 'waiting for padmon to exit'
    Wait-Process -Id {{Environment.ProcessId}} -Timeout 30 -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800

    {{(isPortable
        ? $"Copy-Item -LiteralPath '{EscapePath(source)}' -Destination '{EscapePath(restartPath)}' -Force"
        : $"Copy-Item -Path '{EscapePath(source)}\\*' -Destination '{EscapePath(targetDirectory)}' -Recurse -Force")}}

    Start-Process -FilePath '{{EscapePath(restartPath)}}' -WorkingDirectory '{{EscapePath(oldWorkingDirectory)}}'
    Write-UpdateLog 'update installed'
}
catch {
    Write-UpdateLog "update failed: $_"
    try {
        Start-Process -FilePath '{{EscapePath(restartPath)}}' -WorkingDirectory '{{EscapePath(oldWorkingDirectory)}}'
    } catch {
    }

    exit 1
}

Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force
""";
    }

    private static void EnsureWritable(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The application directory is unavailable.");
        }

        var probe = Path.Combine(directory, $".padmon-write-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, string.Empty);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "padmon cannot write to its application folder. Move it to a writable folder or run it as administrator.",
                exception);
        }
        finally
        {
            try
            {
                File.Delete(probe);
            }
            catch
            {
            }
        }
    }

    private static InstallMode DetectInstallMode()
    {
        if (File.Exists(PortableExecutable))
        {
            return InstallMode.PortableLauncher;
        }

        var directory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        if (File.Exists(Path.Combine(directory, "padmon.installed")))
        {
            return InstallMode.Installed;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs");

        if (IsUnder(directory, programFiles) || IsUnder(directory, localPrograms))
        {
            return InstallMode.Installed;
        }

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\padmon");
            if (key is not null)
            {
                return InstallMode.Installed;
            }
        }
        catch
        {
        }

        return InstallMode.PortableFolder;
    }

    private static bool IsUnder(string path, string parent)
    {
        if (string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        var normalizedParent = parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("padmon-updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static (string Name, string Url)? FindAsset(JsonElement root, bool portable)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var preferred = portable ? "Padmon-portable.exe" : "padmon-win-x64.zip";

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (string.Equals(name, preferred, StringComparison.OrdinalIgnoreCase))
            {
                return (name, url);
            }
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var matches = portable
                ? name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                : name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

            if (matches)
            {
                return (name, url);
            }
        }

        return null;
    }

    private static string ReadCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        var informational = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational.Split('+')[0];
        }

        var version = assembly?.GetName().Version;
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string NormalizeVersion(string tag) =>
        tag.Trim().TrimStart('v', 'V');

    private static Version? ParseVersion(string value)
    {
        var core = value.Split('-', 2)[0];
        var parts = core.Split('.');

        if (parts.Length < 2 ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor))
        {
            return null;
        }

        var patch = parts.Length > 2 && int.TryParse(parts[2], out var parsedPatch)
            ? parsedPatch
            : 0;

        return new Version(major, minor, patch);
    }

    private static string EscapePath(string path) => path.Replace("'", "''", StringComparison.Ordinal);
}
