namespace Padmon.Core.Settings;

public interface ISettingsStore
{
    PadmonSettings Current { get; }

    Task<PadmonSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(PadmonSettings settings, CancellationToken cancellationToken = default);
}
