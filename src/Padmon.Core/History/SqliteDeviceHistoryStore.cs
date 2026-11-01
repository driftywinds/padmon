using System.Globalization;
using Microsoft.Data.Sqlite;
using Padmon.Core.Devices;

namespace Padmon.Core.History;

public sealed class SqliteDeviceHistoryStore : IDeviceHistoryStore, IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string _databasePath;
    private bool _initialized;

    public SqliteDeviceHistoryStore(string? databasePath = null)
    {
        _databasePath = databasePath ?? PadmonPaths.DatabaseFile;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public void Dispose()
    {
        _writeGate.Dispose();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL;";
            await pragma.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var timeout = connection.CreateCommand())
        {
            timeout.CommandText = "PRAGMA busy_timeout = 5000;";
            await timeout.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText =
                """
                CREATE TABLE IF NOT EXISTS controllers (
                    stable_key TEXT PRIMARY KEY NOT NULL,
                    hardware_name TEXT NOT NULL,
                    custom_name TEXT NULL,
                    serial TEXT NULL,
                    vendor_id INTEGER NOT NULL,
                    product_id INTEGER NOT NULL,
                    sdl_guid TEXT NULL,
                    kind INTEGER NOT NULL,
                    first_seen TEXT NOT NULL,
                    last_seen TEXT NOT NULL,
                    session_count INTEGER NOT NULL DEFAULT 0,
                    merged_into TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    stable_key TEXT NOT NULL,
                    connected_at TEXT NOT NULL,
                    disconnected_at TEXT NULL,
                    connection INTEGER NOT NULL,
                    battery_percent INTEGER NULL
                );

                CREATE INDEX IF NOT EXISTS ix_sessions_stable_key ON sessions(stable_key);
                CREATE INDEX IF NOT EXISTS ix_sessions_connected_at ON sessions(connected_at);
                """;
            await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await EnsureMergedIntoColumnAsync(connection, cancellationToken).ConfigureAwait(false);

        _initialized = true;
    }

    public async Task<ConnectionRecordResult> RecordConnectionAsync(
        ControllerDevice device,
        DateTimeOffset connectedAt,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            string? customName = null;
            var firstSeen = connectedAt;
            var sessionCount = 1;
            var isNew = true;

            await using (var select = connection.CreateCommand())
            {
                select.Transaction = (SqliteTransaction)transaction;
                select.CommandText =
                    """
                    SELECT custom_name, first_seen, session_count
                    FROM controllers
                    WHERE stable_key = $key;
                    """;
                select.Parameters.AddWithValue("$key", device.StableKey);

                await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    isNew = false;
                    customName = reader.IsDBNull(0) ? null : reader.GetString(0);
                    firstSeen = ParseTimestamp(reader.GetString(1));
                    sessionCount = reader.GetInt32(2) + 1;
                }
            }

            await using (var upsert = connection.CreateCommand())
            {
                upsert.Transaction = (SqliteTransaction)transaction;
                upsert.CommandText =
                    """
                    INSERT INTO controllers (
                        stable_key, hardware_name, custom_name, serial, vendor_id,
                        product_id, sdl_guid, kind, first_seen, last_seen, session_count)
                    VALUES (
                        $key, $hardwareName, $customName, $serial, $vendorId,
                        $productId, $sdlGuid, $kind, $firstSeen, $lastSeen, $sessionCount)
                    ON CONFLICT(stable_key) DO UPDATE SET
                        hardware_name = excluded.hardware_name,
                        serial = excluded.serial,
                        vendor_id = excluded.vendor_id,
                        product_id = excluded.product_id,
                        sdl_guid = excluded.sdl_guid,
                        kind = excluded.kind,
                        last_seen = excluded.last_seen,
                        session_count = excluded.session_count;
                    """;
                upsert.Parameters.AddWithValue("$key", device.StableKey);
                upsert.Parameters.AddWithValue("$hardwareName", device.Name);
                upsert.Parameters.AddWithValue("$customName", (object?)customName ?? DBNull.Value);
                upsert.Parameters.AddWithValue("$serial", (object?)device.Serial ?? DBNull.Value);
                upsert.Parameters.AddWithValue("$vendorId", device.VendorId);
                upsert.Parameters.AddWithValue("$productId", device.ProductId);
                upsert.Parameters.AddWithValue("$sdlGuid", (object?)device.SdlGuid ?? DBNull.Value);
                upsert.Parameters.AddWithValue("$kind", (int)device.Kind);
                upsert.Parameters.AddWithValue("$firstSeen", FormatTimestamp(firstSeen));
                upsert.Parameters.AddWithValue("$lastSeen", FormatTimestamp(connectedAt));
                upsert.Parameters.AddWithValue("$sessionCount", sessionCount);
                await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var session = connection.CreateCommand())
            {
                session.Transaction = (SqliteTransaction)transaction;
                session.CommandText =
                    """
                    INSERT INTO sessions (stable_key, connected_at, connection, battery_percent)
                    VALUES ($key, $connectedAt, $connection, $batteryPercent);
                    """;
                session.Parameters.AddWithValue("$key", device.StableKey);
                session.Parameters.AddWithValue("$connectedAt", FormatTimestamp(connectedAt));
                session.Parameters.AddWithValue("$connection", (int)device.Connection);
                session.Parameters.AddWithValue(
                    "$batteryPercent",
                    (object?)device.BatteryPercent ?? DBNull.Value);
                await session.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            var record = new ControllerRecord(
                device.StableKey,
                device.Name,
                customName,
                device.Serial,
                device.VendorId,
                device.ProductId,
                device.SdlGuid,
                device.Kind,
                firstSeen,
                connectedAt,
                sessionCount);

            return new ConnectionRecordResult(record, isNew);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task RecordDisconnectionAsync(
        string stableKey,
        DateTimeOffset disconnectedAt,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE sessions
                SET disconnected_at = $disconnectedAt
                WHERE id = (
                    SELECT id FROM sessions
                    WHERE stable_key = $key AND disconnected_at IS NULL
                    ORDER BY id DESC
                    LIMIT 1
                );
                """;
            command.Parameters.AddWithValue("$disconnectedAt", FormatTimestamp(disconnectedAt));
            command.Parameters.AddWithValue("$key", stableKey);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task CloseOrphanedSessionsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE sessions
                SET disconnected_at = connected_at
                WHERE disconnected_at IS NULL;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task RenameAsync(
        string stableKey,
        string? customName,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE controllers SET custom_name = $name WHERE stable_key = $key;";
            command.Parameters.AddWithValue(
                "$name",
                string.IsNullOrWhiteSpace(customName) ? DBNull.Value : customName.Trim());
            command.Parameters.AddWithValue("$key", stableKey);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task MergeAsync(
        string targetKey,
        IReadOnlyList<string> sourceKeys,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            foreach (var sourceKey in sourceKeys
                         .Where(key => !string.Equals(key, targetKey, StringComparison.Ordinal))
                         .Distinct(StringComparer.Ordinal))
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText =
                    """
                    UPDATE controllers
                    SET merged_into = $target
                    WHERE stable_key = $source;
                    """;
                command.Parameters.AddWithValue("$target", targetKey);
                command.Parameters.AddWithValue("$source", sourceKey);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task UnmergeAsync(
        string targetKey,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE controllers
                SET merged_into = NULL
                WHERE merged_into = $target;
                """;
            command.Parameters.AddWithValue("$target", targetKey);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task ForgetAsync(string stableKey, CancellationToken cancellationToken = default)
    {
        return ForgetAsync([stableKey], cancellationToken);
    }

    public async Task ForgetAsync(
        IReadOnlyList<string> stableKeys,
        CancellationToken cancellationToken = default)
    {
        var keys = stableKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (keys.Length == 0)
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            foreach (var key in keys)
            {
                await using (var sessions = connection.CreateCommand())
                {
                    sessions.Transaction = (SqliteTransaction)transaction;
                    sessions.CommandText = "DELETE FROM sessions WHERE stable_key = $key;";
                    sessions.Parameters.AddWithValue("$key", key);
                    await sessions.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await using (var controller = connection.CreateCommand())
                {
                    controller.Transaction = (SqliteTransaction)transaction;
                    controller.CommandText = "DELETE FROM controllers WHERE stable_key = $key;";
                    controller.Parameters.AddWithValue("$key", key);
                    await controller.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<ControllerRecord>> GetControllersAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT stable_key, hardware_name, custom_name, serial, vendor_id, product_id,
                   sdl_guid, kind, first_seen, last_seen, session_count, merged_into
            FROM controllers
            ORDER BY last_seen DESC;
            """;

        var rows = new List<ControllerRow>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new ControllerRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    (ushort)reader.GetInt32(4),
                    (ushort)reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    (ControllerKind)reader.GetInt32(7),
                    ParseTimestamp(reader.GetString(8)),
                    ParseTimestamp(reader.GetString(9)),
                    reader.GetInt32(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11)));
            }
        }

        var byKey = rows.ToDictionary(row => row.StableKey, StringComparer.Ordinal);
        var rootByKey = new Dictionary<string, string>(StringComparer.Ordinal);

        string ResolveRoot(string key)
        {
            if (rootByKey.TryGetValue(key, out var known))
            {
                return known;
            }

            var current = key;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            while (byKey.TryGetValue(current, out var row) &&
                   !string.IsNullOrWhiteSpace(row.MergedInto) &&
                   seen.Add(current))
            {
                current = row.MergedInto;
            }

            rootByKey[key] = current;
            return current;
        }

        var groups = rows
            .GroupBy(row => ResolveRoot(row.StableKey), StringComparer.Ordinal)
            .Select(group => group.ToArray())
            .Select(group =>
            {
                var rootKey = ResolveRoot(group[0].StableKey);
                var root = group.FirstOrDefault(row => string.Equals(row.StableKey, rootKey, StringComparison.Ordinal))
                    ?? group.OrderByDescending(row => row.LastSeen).First();

                var aliases = group
                    .Where(row => !string.Equals(row.StableKey, root.StableKey, StringComparison.Ordinal))
                    .OrderByDescending(row => row.LastSeen)
                    .Select(row => new ControllerAlias(row.StableKey, row.HardwareName, row.CustomName))
                    .ToArray();

                return new ControllerRecord(
                    root.StableKey,
                    root.HardwareName,
                    root.CustomName,
                    root.Serial,
                    root.VendorId,
                    root.ProductId,
                    root.SdlGuid,
                    root.Kind,
                    group.Min(row => row.FirstSeen),
                    group.Max(row => row.LastSeen),
                    group.Sum(row => row.SessionCount))
                {
                    Aliases = aliases
                };
            })
            .OrderByDescending(record => record.LastSeen)
            .ToArray();

        return groups;
    }

    private static async Task EnsureMergedIntoColumnAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var hasColumn = false;

        await using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(controllers);";
            await using var reader = await info.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(reader.GetString(1), "merged_into", StringComparison.OrdinalIgnoreCase))
                {
                    hasColumn = true;
                    break;
                }
            }
        }

        if (hasColumn)
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE controllers ADD COLUMN merged_into TEXT NULL;";
        await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record ControllerRow(
        string StableKey,
        string HardwareName,
        string? CustomName,
        string? Serial,
        ushort VendorId,
        ushort ProductId,
        string? SdlGuid,
        ControllerKind Kind,
        DateTimeOffset FirstSeen,
        DateTimeOffset LastSeen,
        int SessionCount,
        string? MergedInto);

    public async Task<IReadOnlyList<ConnectionSession>> GetSessionsAsync(
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, stable_key, connected_at, disconnected_at, connection, battery_percent
            FROM sessions
            ORDER BY connected_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5_000));

        var sessions = new List<ConnectionSession>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sessions.Add(new ConnectionSession(
                reader.GetInt64(0),
                reader.GetString(1),
                ParseTimestamp(reader.GetString(2)),
                reader.IsDBNull(3) ? null : ParseTimestamp(reader.GetString(3)),
                (ConnectionKind)reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5)));
        }

        return sessions;
    }

    public async Task PruneSessionsAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                DELETE FROM sessions
                WHERE disconnected_at IS NOT NULL AND connected_at < $olderThan;
                """;
            command.Parameters.AddWithValue("$olderThan", FormatTimestamp(olderThan));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
