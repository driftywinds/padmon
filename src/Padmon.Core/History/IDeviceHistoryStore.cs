using Padmon.Core.Devices;

namespace Padmon.Core.History;

public interface IDeviceHistoryStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<ConnectionRecordResult> RecordConnectionAsync(
        ControllerDevice device,
        DateTimeOffset connectedAt,
        CancellationToken cancellationToken = default);

    Task RecordDisconnectionAsync(
        string stableKey,
        DateTimeOffset disconnectedAt,
        CancellationToken cancellationToken = default);

    Task CloseOrphanedSessionsAsync(CancellationToken cancellationToken = default);

    Task RenameAsync(string stableKey, string? customName, CancellationToken cancellationToken = default);

    Task MergeAsync(
        string targetKey,
        IReadOnlyList<string> sourceKeys,
        CancellationToken cancellationToken = default);

    Task UnmergeAsync(string targetKey, CancellationToken cancellationToken = default);

    Task ForgetAsync(string stableKey, CancellationToken cancellationToken = default);

    Task ForgetAsync(
        IReadOnlyList<string> stableKeys,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ControllerRecord>> GetControllersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConnectionSession>> GetSessionsAsync(
        int limit = 200,
        CancellationToken cancellationToken = default);

    Task PruneSessionsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}
