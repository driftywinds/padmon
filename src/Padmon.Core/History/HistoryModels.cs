using Padmon.Core.Devices;

namespace Padmon.Core.History;

public sealed record ControllerRecord(
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
    int SessionCount)
{
    public IReadOnlyList<ControllerAlias> Aliases { get; init; } = [];

    public int DeviceCount => 1 + Aliases.Count;

    public string DisplayName => string.IsNullOrWhiteSpace(CustomName) ? HardwareName : CustomName;
}

public sealed record ControllerAlias(
    string StableKey,
    string HardwareName,
    string? CustomName)
{
    public string DisplayName => string.IsNullOrWhiteSpace(CustomName) ? HardwareName : CustomName;
}

public sealed record ConnectionSession(
    long Id,
    string StableKey,
    DateTimeOffset ConnectedAt,
    DateTimeOffset? DisconnectedAt,
    ConnectionKind Connection,
    int? BatteryPercent);

public sealed record ConnectionRecordResult(ControllerRecord Controller, bool IsNew);
