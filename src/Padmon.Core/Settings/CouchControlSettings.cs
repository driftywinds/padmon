namespace Padmon.Core.Settings;

public enum CouchBindingKind
{
    None = 0,
    Keyboard = 1,
    MouseButton = 2,
    HideWindow = 3,
    OnScreenKeyboard = 4
}

public sealed class CouchBindingSettings
{
    public CouchBindingKind Kind { get; set; }

    /// <summary>
    /// Virtual-key code for Keyboard, mouse button index for MouseButton.
    /// </summary>
    public int Code { get; set; }
}

public sealed class CouchControlSettings
{
    public bool Enabled { get; set; }

    public string? ControllerStableKey { get; set; }

    /// <summary>Mouse pixels per second at full stick deflection.</summary>
    public int CursorSpeed { get; set; } = 900;

    public double Acceleration { get; set; }

    /// <summary>Raw SDL axis dead zone, 0..32767.</summary>
    public int DeadZone { get; set; } = 6000;

    public int ScrollDeadZone { get; set; } = 5000;

    /// <summary>Scroll wheel notches per second at full deflection.</summary>
    public double ScrollSpeed { get; set; } = 8;

    public bool SwapSticks { get; set; }

    public bool PauseWhenPadmonFocused { get; set; } = true;

    /// <summary>
    /// Control id to binding. Missing ids use the built-in Gopher-style
    /// defaults; an explicit None binding means "do nothing".
    /// </summary>
    public Dictionary<string, CouchBindingSettings> Bindings { get; set; } = [];
}
