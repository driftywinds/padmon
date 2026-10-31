using Padmon.Core.Devices;
using Padmon.Core.History;
using Padmon.Formatting;

namespace Padmon.ViewModels;

public sealed class KnownControllerViewModel
{
    public KnownControllerViewModel(ControllerRecord record)
    {
        Record = record;
    }

    public ControllerRecord Record { get; }

    public string DisplayName => Record.DisplayName;

    public string HardwareName => Record.DeviceCount > 1
        ? $"{Record.HardwareName} · {Record.DeviceCount} devices"
        : Record.HardwareName;

    public bool IsMerged => Record.DeviceCount > 1;

    public string MergedLabel { get; } = "MERGED";

    public string MergeActionText => IsMerged ? "Separate" : "Merge";

    public string Initials => InitialsOf(Record.DisplayName);

    public string KindLabel => Record.Kind switch
    {
        ControllerKind.Gamepad => "Gamepad",
        ControllerKind.Joystick => "Joystick",
        ControllerKind.Wheel => "Wheel",
        ControllerKind.ArcadeStick => "Arcade stick",
        ControllerKind.FlightStick => "Flight stick",
        ControllerKind.DancePad => "Dance pad",
        ControllerKind.Guitar => "Guitar",
        ControllerKind.DrumKit => "Drum kit",
        ControllerKind.ArcadePad => "Arcade pad",
        ControllerKind.Throttle => "Throttle",
        _ => "Controller"
    };

    public string LastSeenText => $"Last seen {RelativeTime.Format(Record.LastSeen)}";

    public string FirstSeenText => $"First seen {RelativeTime.Date(Record.FirstSeen)}";

    public string SessionText => Record.SessionCount == 1
        ? "1 session"
        : $"{Record.SessionCount} sessions";

    public string MetaText => $"{KindLabel} · {SessionText}";

    public IReadOnlyList<ControllerAlias> Aliases => Record.Aliases;

    public static string InitialsOf(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return "?";
        }

        return words.Length == 1
            ? words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant()
            : $"{words[0][0]}{words[1][0]}".ToUpperInvariant();
    }
}

public sealed class ConnectionSessionViewModel
{
    public ConnectionSessionViewModel(ConnectionSession session, string displayName)
    {
        Session = session;
        DisplayName = displayName;
    }

    public ConnectionSession Session { get; }

    public string DisplayName { get; }

    public bool IsLive => Session.DisconnectedAt is null;

    public string WhenText => $"Connected {RelativeTime.Format(Session.ConnectedAt)}";

    public string ConnectionText => Session.Connection switch
    {
        ConnectionKind.Usb => "USB",
        ConnectionKind.Bluetooth => "Bluetooth",
        ConnectionKind.Virtual => "Virtual",
        _ => "Unknown transport"
    };

    public string DetailText => IsLive
        ? $"{ConnectionText} · connected now"
        : $"{ConnectionText} · {RelativeTime.SessionDuration(Session)}";

    public string Initials => KnownControllerViewModel.InitialsOf(DisplayName);
}
