using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using Padmon.Core.Diagnostics;
using WinHttp = Windows.Web.Http;

namespace Padmon.Services;

public enum InstallMode
{
    PortableLauncher,
    PortableFolder,
    Installed,
    MicrosoftStore
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

public sealed record UpdateDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? Percent => TotalBytes is > 0
        ? Math.Clamp(BytesReceived * 100.0 / TotalBytes.Value, 0, 100)
        : null;
}

public sealed class UpdateService
{
    private const string ReleasesApi = "https://api.github.com/repos/driftywinds/padmon/releases/latest";

    // The updater appends one line per step to a single log in the updates folder.
    private const long MaxUpdateLogBytes = 1024 * 1024;

    // A staging folder touched this recently may belong to an update that is still
    // downloading or waiting for the old process to exit, so startup leaves it alone.
    private static readonly TimeSpan RecentPayloadWindow = TimeSpan.FromMinutes(10);

    private static readonly string PortableExecutable =
        Environment.GetEnvironmentVariable("PADMON_PORTABLE_EXE") ?? string.Empty;

    /// <summary>
    /// Root of the downloaded update payloads. Every release is staged in a folder named
    /// after its version, and the whole payload becomes disposable once the release has
    /// been copied into place. Only the updater log stays behind after a successful update.
    /// </summary>
    public static string UpdatesRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "padmon",
        "updates");

    public static string CurrentVersion { get; } = ReadCurrentVersion();

    public static InstallMode Mode { get; } = DetectInstallMode();

    public static string ModeLabel => Mode switch
    {
        InstallMode.PortableLauncher => "Portable single-file build",
        InstallMode.PortableFolder => "Portable folder build",
        InstallMode.MicrosoftStore => "Microsoft Store build",
        _ => "Installed build"
    };

    public static bool IsPortableLauncher => Mode == InstallMode.PortableLauncher;

    public static bool IsStoreBuild => Mode == InstallMode.MicrosoftStore;

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = CurrentVersion;

        try
        {
            string json;

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
                json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception) when (IsTlsCredentialFailure(exception))
            {
                // Some Windows sessions (restricted tokens, unusual proxy/certificate
                // policies) cannot acquire Schannel credentials from the .NET client.
                // WinHTTP uses a different credential path and often succeeds there.
                PadmonLog.Warn("update check: Schannel credentials unavailable, trying WinHTTP");
                json = await GetReleaseJsonViaWinHttpAsync(cancellationToken).ConfigureAwait(false)
                    ?? string.Empty;
                if (json.Length == 0)
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
            }

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
                CanInstall: newer && asset is not null && !IsStoreBuild);
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
                Error: DescribeError(exception));
        }
    }

    private static async Task<string?> GetReleaseJsonViaWinHttpAsync(CancellationToken cancellationToken)
    {
        using var client = new WinHttp.HttpClient();
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("padmon-updater");
        client.DefaultRequestHeaders.Accept.TryParseAdd("application/vnd.github+json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        using var response = await client
            .GetAsync(new Uri(ReleasesApi))
            .AsTask(timeout.Token)
            .ConfigureAwait(false);

        if (response.StatusCode == WinHttp.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadAsStringAsync()
            .AsTask(timeout.Token)
            .ConfigureAwait(false);
    }

    private static bool IsTlsCredentialFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
            {
                return true;
            }

            if (current is Win32Exception { NativeErrorCode: unchecked((int)0x8009030E) })
            {
                return true;
            }
        }

        return false;
    }

    private static string DescribeError(Exception exception)
    {
        if (IsTlsCredentialFailure(exception))
        {
            return "Windows could not create a secure connection to GitHub (Schannel error 0x8009030E). "
                + "This is usually a restricted network session, proxy, or certificate policy. "
                + "Check your connection and try again.";
        }

        if (exception is HttpRequestException or System.Runtime.InteropServices.COMException)
        {
            return "Could not reach GitHub. Check your internet connection and try again.";
        }

        return exception.Message;
    }

    public static async Task InstallAsync(
        UpdateCheckResult update,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var downloaded = await DownloadAsync(update, progress, cancellationToken).ConfigureAwait(false);
        await InstallDownloadedAsync(update, downloaded, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string> DownloadAsync(
        UpdateCheckResult update,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureInstallable(update);

        var root = Path.Combine(UpdatesRoot, update.LatestVersion!);
        Directory.CreateDirectory(root);

        var downloaded = Path.Combine(root, update.AssetName ?? "update.bin");
        var partial = downloaded + ".part";

        try
        {
            using var client = CreateClient();
            using var response = await client.GetAsync(
                       update.AssetUrl,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;

            await using var source = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            var buffer = new byte[81920];
            var bytesReceived = 0L;
            var lastReport = 0L;
            progress?.Report(new UpdateDownloadProgress(0, total));

            await using (var destination = File.Create(partial))
            {
                while (true)
                {
                    var read = await source
                        .ReadAsync(buffer, cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    await destination
                        .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    bytesReceived += read;

                    var now = Environment.TickCount64;
                    if (now - lastReport >= 100)
                    {
                        lastReport = now;
                        progress?.Report(new UpdateDownloadProgress(bytesReceived, total));
                    }
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new UpdateDownloadProgress(bytesReceived, total));
            File.Move(partial, downloaded, overwrite: true);
            return downloaded;
        }
        catch
        {
            try
            {
                File.Delete(partial);
            }
            catch
            {
            }

            throw;
        }
    }

    public static async Task InstallDownloadedAsync(
        UpdateCheckResult update,
        string downloaded,
        CancellationToken cancellationToken = default)
    {
        EnsureInstallable(update);

        if (!File.Exists(downloaded))
        {
            throw new FileNotFoundException("The downloaded update file is missing.", downloaded);
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
            var root = Path.Combine(UpdatesRoot, update.LatestVersion!);
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

        // The script and log sit beside the version folders instead of inside them, so
        // deleting the payload after the install does not take the diagnostics with it.
        var script = Path.Combine(UpdatesRoot, "install.ps1");
        var log = Path.Combine(UpdatesRoot, "update.log");
        TrimUpdateLog(log);
        await File.WriteAllTextAsync(
            script,
            BuildUpdaterScript(
                sourcePath,
                targetDirectory,
                restartPath,
                isPortableLauncher,
                UpdatesRoot,
                log),
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

    private static void EnsureInstallable(UpdateCheckResult update)
    {
        if (!update.CanInstall ||
            string.IsNullOrWhiteSpace(update.AssetUrl) ||
            string.IsNullOrWhiteSpace(update.LatestVersion))
        {
            throw new InvalidOperationException("No installable update is available.");
        }

        if (IsStoreBuild)
        {
            throw new InvalidOperationException(
                "This copy of padmon is installed from the Microsoft Store. Use the Store to update it.");
        }
    }

    /// <summary>
    /// Removes update payloads that are no longer needed. The updater already does this as
    /// soon as a release is copied into place; this sweep runs on startup to clear folders
    /// left behind by earlier builds (or by updates that failed part-way), so the updates
    /// folder never keeps multiple copies of the app.
    /// </summary>
    public static void CleanUpStaleUpdatePayloads()
    {
        try
        {
            if (!Directory.Exists(UpdatesRoot))
            {
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(UpdatesRoot))
            {
                var name = Path.GetFileName(directory);

                // Never delete the folder the running app lives in, even if an unusual
                // PADMON_PORTABLE_EXE setup points into the updates folder.
                if (IsUnder(AppContext.BaseDirectory, directory) ||
                    (!string.IsNullOrWhiteSpace(PortableExecutable) && IsUnder(PortableExecutable, directory)))
                {
                    continue;
                }

                try
                {
                    // A recent payload may still belong to an update that is downloading or
                    // waiting for the old process to exit, so leave it for the next launch.
                    if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(directory) < RecentPayloadWindow)
                    {
                        continue;
                    }

                    Directory.Delete(directory, recursive: true);
                    PadmonLog.Info($"removed stale update payload {name}");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    PadmonLog.Warn($"could not remove stale update payload {name}: {exception.Message}");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            PadmonLog.Warn($"could not inspect the updates folder: {exception.Message}");
        }
    }

    private static void TrimUpdateLog(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length > MaxUpdateLogBytes)
            {
                info.Delete();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            PadmonLog.Warn($"could not trim the update log: {exception.Message}");
        }
    }

    private static string BuildUpdaterScript(
        string source,
        string targetDirectory,
        string restartPath,
        bool isPortable,
        string updatesRoot,
        string log)
    {
        var oldWorkingDirectory = Path.GetDirectoryName(restartPath) ?? targetDirectory;

        return $$"""
$ErrorActionPreference = 'Stop'
$updatesRoot = '{{EscapePath(updatesRoot)}}'
$restartPath = '{{EscapePath(restartPath)}}'
$log = '{{EscapePath(log)}}'
function Write-UpdateLog($message) {
    Add-Content -LiteralPath $log -Value "$(Get-Date -Format o) $message"
}

function Remove-UpdatePayloads {
    # The release is already copied into place, so the download and the extracted
    # copy are disposable. Keeping only the log is what stops old versions piling up.
    Get-ChildItem -LiteralPath $updatesRoot -Directory -ErrorAction SilentlyContinue |
        ForEach-Object {
            if ($restartPath -like "$($_.FullName)\*") {
                Write-UpdateLog "kept $($_.Name) because the app runs from it"
                return
            }

            try {
                Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop
                Write-UpdateLog "removed update payload $($_.Name)"
            }
            catch {
                Write-UpdateLog "could not remove update payload $($_.Name): $_"
            }
        }
}

try {
    Write-UpdateLog 'waiting for padmon to exit'
    Wait-Process -Id {{Environment.ProcessId}} -Timeout 30 -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800

    {{(isPortable
        ? $"Copy-Item -LiteralPath '{EscapePath(source)}' -Destination '{EscapePath(restartPath)}' -Force"
        : $"Copy-Item -Path '{EscapePath(source)}\\*' -Destination '{EscapePath(targetDirectory)}' -Recurse -Force")}}

    Remove-UpdatePayloads

    Start-Process -FilePath $restartPath -WorkingDirectory '{{EscapePath(oldWorkingDirectory)}}'
    Write-UpdateLog 'update installed'
}
catch {
    Write-UpdateLog "update failed: $_"
    try {
        Start-Process -FilePath $restartPath -WorkingDirectory '{{EscapePath(oldWorkingDirectory)}}'
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
        if (AppEnvironment.IsPackaged)
        {
            return InstallMode.MicrosoftStore;
        }

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
