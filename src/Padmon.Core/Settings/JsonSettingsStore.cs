using System.Text.Json;

namespace Padmon.Core.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;

    public JsonSettingsStore(string? path = null)
    {
        _path = path ?? PadmonPaths.SettingsFile;
    }

    public PadmonSettings Current { get; private set; } = new();

    public async Task<PadmonSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            Current = new PadmonSettings();
            return Current;
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            Current = await JsonSerializer.DeserializeAsync<PadmonSettings>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false) ?? new PadmonSettings();
        }
        catch (JsonException)
        {
            Current = new PadmonSettings();
        }
        catch (IOException)
        {
            Current = new PadmonSettings();
        }

        return Current;
    }

    public async Task SaveAsync(PadmonSettings settings, CancellationToken cancellationToken = default)
    {
        PadmonPaths.EnsureDataDirectory();

        var temporaryPath = $"{_path}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                settings,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, _path, true);
        Current = settings;
    }
}
