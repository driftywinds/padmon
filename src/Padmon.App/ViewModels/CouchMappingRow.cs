using Padmon.Core.Devices;
using Padmon.Core.Settings;
using Padmon.Couch;

namespace Padmon.ViewModels;

public sealed class CouchMappingRow
{
    public CouchMappingRow(string id, string label, CouchBindingSettings binding)
    {
        Id = id;
        Label = label;
        Binding = binding;
    }

    public string Id { get; }

    public string Label { get; }

    public CouchBindingSettings Binding { get; set; }

    public string BindingText => CouchBindingText.Describe(Binding);
}

public static class CouchBindingText
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0x0D] = "Enter",
        [0x10] = "Shift",
        [0x11] = "Ctrl",
        [0x12] = "Alt",
        [0x13] = "Pause",
        [0x14] = "Caps Lock",
        [0x1B] = "Escape",
        [0x20] = "Space",
        [0x21] = "Page Up",
        [0x22] = "Page Down",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "Left",
        [0x26] = "Up",
        [0x27] = "Right",
        [0x28] = "Down",
        [0x2C] = "Print Screen",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0x5B] = "Left Windows",
        [0x5C] = "Right Windows",
        [0x5D] = "Menu",
        [0x6A] = "Multiply",
        [0x6B] = "Add",
        [0x6D] = "Subtract",
        [0x6E] = "Decimal",
        [0x6F] = "Divide",
        [0x90] = "Num Lock",
        [0x91] = "Scroll Lock",
        [0xA0] = "Left Shift",
        [0xA1] = "Right Shift",
        [0xA2] = "Left Ctrl",
        [0xA3] = "Right Ctrl",
        [0xA4] = "Left Alt",
        [0xA5] = "Right Alt",
        [0xA6] = "Browser Back",
        [0xA7] = "Browser Forward",
        [0xA8] = "Browser Refresh",
        [0xA9] = "Browser Stop",
        [0xAA] = "Browser Search",
        [0xAB] = "Browser Favorites",
        [0xAC] = "Browser Home",
        [0xAD] = "Volume Mute",
        [0xAE] = "Volume Down",
        [0xAF] = "Volume Up",
        [0xB0] = "Media Next",
        [0xB1] = "Media Previous",
        [0xB2] = "Media Stop",
        [0xB3] = "Media Play/Pause"
    };

    public static string Describe(CouchBindingSettings binding) => binding.Kind switch
    {
        CouchBindingKind.Keyboard => $"Key: {KeyName(binding.Code)}",
        CouchBindingKind.MouseButton => binding.Code switch
        {
            0 => "Left click",
            1 => "Right click",
            2 => "Middle click",
            _ => "Mouse button"
        },
        CouchBindingKind.HideWindow => "Hide padmon",
        CouchBindingKind.OnScreenKeyboard => "On-screen keyboard",
        _ => "None"
    };

    public static string KeyName(int code)
    {
        if (Names.TryGetValue(code, out var name))
        {
            return name;
        }

        if (code is >= 0x30 and <= 0x39)
        {
            return ((char)code).ToString();
        }

        if (code is >= 0x41 and <= 0x5A)
        {
            return ((char)code).ToString();
        }

        if (code is >= 0x60 and <= 0x69)
        {
            return $"Num {code - 0x60}";
        }

        if (code is >= 0x70 and <= 0x87)
        {
            return $"F{code - 0x6F}";
        }

        return $"0x{code:X2}";
    }

    public static CouchBindingSettings Clone(CouchBindingSettings binding) => new()
    {
        Kind = binding.Kind,
        Code = binding.Code
    };
}

public static class CouchMappingCatalog
{
    private static readonly (int Index, string Label)[] DefaultButtons =
    [
        (0, "A / Cross"),
        (1, "B / Circle"),
        (2, "X / Square"),
        (3, "Y / Triangle"),
        (4, "Back / Share"),
        (5, "Guide"),
        (6, "Start / Options"),
        (7, "L3"),
        (8, "R3"),
        (9, "LB / L1"),
        (10, "RB / R1"),
        (11, "D-Pad Up"),
        (12, "D-Pad Down"),
        (13, "D-Pad Left"),
        (14, "D-Pad Right")
    ];

    public static List<CouchMappingRow> Create(
        ControllerSnapshot? snapshot,
        CouchControlSettings settings)
    {
        var bindings = CouchDefaults.Merge(settings.Bindings);
        var rows = new List<CouchMappingRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (index, label) in DefaultButtons)
        {
            Add(rows, seen, bindings, CouchControlIds.Button(index), label);
        }

        if (snapshot is not null)
        {
            foreach (var button in snapshot.Buttons.OrderBy(button => button.Index))
            {
                if (button.Index >= DefaultButtons.Length)
                {
                    Add(
                        rows,
                        seen,
                        bindings,
                        CouchControlIds.Button(button.Index),
                        button.Label);
                }
            }

            foreach (var hat in snapshot.Hats)
            {
                Add(rows, seen, bindings, CouchControlIds.Hat(hat.Index, CouchControlIds.HatUp), $"D-Pad {hat.Index + 1} Up");
                Add(rows, seen, bindings, CouchControlIds.Hat(hat.Index, CouchControlIds.HatDown), $"D-Pad {hat.Index + 1} Down");
                Add(rows, seen, bindings, CouchControlIds.Hat(hat.Index, CouchControlIds.HatLeft), $"D-Pad {hat.Index + 1} Left");
                Add(rows, seen, bindings, CouchControlIds.Hat(hat.Index, CouchControlIds.HatRight), $"D-Pad {hat.Index + 1} Right");
            }
        }

        Add(rows, seen, bindings, CouchControlIds.TriggerLeft, "LT / L2");
        Add(rows, seen, bindings, CouchControlIds.TriggerRight, "RT / R2");

        return rows;
    }

    private static void Add(
        List<CouchMappingRow> rows,
        HashSet<string> seen,
        Dictionary<string, CouchBindingSettings> bindings,
        string id,
        string label)
    {
        if (!seen.Add(id))
        {
            return;
        }

        bindings.TryGetValue(id, out var binding);
        binding ??= new CouchBindingSettings { Kind = CouchBindingKind.None };
        rows.Add(new CouchMappingRow(id, label, binding));
    }
}
