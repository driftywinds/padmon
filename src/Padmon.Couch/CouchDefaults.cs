using Padmon.Core.Settings;

namespace Padmon.Couch;

/// <summary>
/// Clean-room defaults modeled on Gopher360's documented couch mappings.
/// Buttons use SDL's semantic gamepad button indices, so Xbox, PlayStation,
/// and mapped DInput controllers share the same default layout.
/// </summary>
public static class CouchDefaults
{
    private const int VkBack = 0x08;
    private const int VkReturn = 0x0D;
    private const int VkSpace = 0x20;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkLWin = 0x5B;
    private const int VkF2 = 0x71;
    private const int VkBrowserBack = 0xA6;
    private const int VkBrowserForward = 0xA7;
    private const int VkBrowserRefresh = 0xA8;

    public static Dictionary<string, CouchBindingSettings> Merge(
        IReadOnlyDictionary<string, CouchBindingSettings>? overrides)
    {
        var bindings = CreateBindings();

        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                bindings[pair.Key] = pair.Value;
            }
        }

        return bindings;
    }

    public static Dictionary<string, CouchBindingSettings> CreateBindings() => new(StringComparer.Ordinal)
    {
        [CouchControlIds.Button(0)] = Mouse(0),                 // A / Cross
        [CouchControlIds.Button(1)] = Key(VkReturn),            // B / Circle
        [CouchControlIds.Button(2)] = Mouse(1),                 // X / Square
        [CouchControlIds.Button(3)] = Special(CouchBindingKind.HideWindow),
        [CouchControlIds.Button(4)] = Key(VkBrowserRefresh),    // Back / Share
        [CouchControlIds.Button(5)] = None(),
        [CouchControlIds.Button(6)] = Key(VkLWin),              // Start / Options
        [CouchControlIds.Button(7)] = Mouse(2),                 // L3
        [CouchControlIds.Button(8)] = Key(VkF2),                // R3
        [CouchControlIds.Button(9)] = Key(VkBrowserBack),       // LB
        [CouchControlIds.Button(10)] = Key(VkBrowserForward),   // RB
        [CouchControlIds.Button(11)] = Key(VkUp),
        [CouchControlIds.Button(12)] = Key(VkDown),
        [CouchControlIds.Button(13)] = Key(VkLeft),
        [CouchControlIds.Button(14)] = Key(VkRight),
        [CouchControlIds.TriggerLeft] = Key(VkSpace),
        [CouchControlIds.TriggerRight] = Key(VkBack)
    };

    private static CouchBindingSettings Key(int virtualKey) => new()
    {
        Kind = CouchBindingKind.Keyboard,
        Code = virtualKey
    };

    private static CouchBindingSettings Mouse(int button) => new()
    {
        Kind = CouchBindingKind.MouseButton,
        Code = button
    };

    private static CouchBindingSettings Special(CouchBindingKind kind) => new()
    {
        Kind = kind
    };

    private static CouchBindingSettings None() => new()
    {
        Kind = CouchBindingKind.None
    };
}
