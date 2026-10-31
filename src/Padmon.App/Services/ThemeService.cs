using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Padmon.Core.Settings;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Padmon.Services;

internal static class ThemeService
{
    private static readonly ConditionalWeakTable<FrameworkElement, ResourceDictionary> Applied = new();

    public static void Apply(FrameworkElement root, string theme, PadmonSettings settings)
    {
        if (Applied.TryGetValue(root, out var previous))
        {
            root.Resources.MergedDictionaries.Remove(previous);
            Applied.Remove(root);
        }

        var dictionary = new ResourceDictionary();
        ElementTheme baseTheme;

        if (string.Equals(theme, "Custom", StringComparison.Ordinal) &&
            TryParse(settings.CustomThemeBackground, out var background) &&
            TryParse(settings.CustomThemeForeground, out var foreground) &&
            TryParse(settings.CustomThemeText, out var text) &&
            TryParse(settings.CustomThemeHighlight, out var highlight))
        {
            ApplyCustomPalette(dictionary, background, foreground, text, highlight);
            baseTheme = IsLight(background) ? ElementTheme.Light : ElementTheme.Dark;
        }
        else
        {
            dictionary["PadmonHighlightBrush"] = new SolidColorBrush(GetSystemAccent());
            baseTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        root.Resources.MergedDictionaries.Add(dictionary);
        Applied.Add(root, dictionary);
        ForceThemeRefresh(root, baseTheme);
    }

    private static void ApplyCustomPalette(
        ResourceDictionary dictionary,
        Color background,
        Color foreground,
        Color text,
        Color highlight)
    {
        var surfaceSecondary = Blend(foreground, text, 0.08);
        var surfaceTertiary = Blend(foreground, text, 0.04);
        var border = Blend(foreground, text, 0.16);
        var textSecondary = WithAlpha(text, 0xA6);
        var textTertiary = WithAlpha(text, 0x7A);
        var onAccent = IsLight(highlight) ? Color.FromArgb(255, 12, 14, 16) : Colors.White;

        dictionary["PadmonWindowBackgroundBrush"] = Brush(background);
        dictionary["SolidBackgroundFillColorBaseBrush"] = Brush(background);
        dictionary["SolidBackgroundFillColorSecondaryBrush"] = Brush(surfaceSecondary);
        dictionary["SolidBackgroundFillColorTertiaryBrush"] = Brush(surfaceTertiary);
        dictionary["LayerFillColorDefaultBrush"] = Brush(foreground);

        dictionary["CardBackgroundFillColorDefaultBrush"] = Brush(foreground);
        dictionary["CardBackgroundFillColorSecondaryBrush"] = Brush(surfaceSecondary);
        dictionary["CardStrokeColorDefaultBrush"] = Brush(border);
        dictionary["DividerStrokeColorDefaultBrush"] = Brush(border);

        dictionary["SubtleFillColorSecondaryBrush"] = Brush(surfaceSecondary);
        dictionary["SubtleFillColorTertiaryBrush"] = Brush(surfaceTertiary);
        dictionary["ControlFillColorDefaultBrush"] = Brush(surfaceSecondary);
        dictionary["ControlFillColorSecondaryBrush"] = Brush(surfaceTertiary);
        dictionary["ControlFillColorTertiaryBrush"] = Brush(background);
        dictionary["ControlStrokeColorDefaultBrush"] = Brush(border);

        dictionary["TextFillColorPrimaryBrush"] = Brush(text);
        dictionary["TextFillColorSecondaryBrush"] = Brush(textSecondary);
        dictionary["TextFillColorTertiaryBrush"] = Brush(textTertiary);

        dictionary["AccentFillColorDefaultBrush"] = Brush(highlight);
        dictionary["AccentTextFillColorPrimaryBrush"] = Brush(highlight);
        dictionary["TextOnAccentFillColorPrimaryBrush"] = Brush(onAccent);
        dictionary["PadmonHighlightBrush"] = Brush(highlight);
    }

    private static void ForceThemeRefresh(FrameworkElement root, ElementTheme target)
    {
        if (root.RequestedTheme == target)
        {
            root.RequestedTheme = target == ElementTheme.Dark
                ? ElementTheme.Light
                : ElementTheme.Dark;
        }

        root.RequestedTheme = target;
    }

    private static Color GetSystemAccent()
    {
        try
        {
            return new UISettings().GetColorValue(UIColorType.Accent);
        }
        catch
        {
            return Color.FromArgb(255, 0x21, 0xF6, 0xFA);
        }
    }

    private static bool TryParse(string? value, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hex = value.Trim().TrimStart('#');
        if (hex.Length != 6 ||
            !uint.TryParse(
                hex,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var number))
        {
            return false;
        }

        color = Color.FromArgb(
            255,
            (byte)((number >> 16) & 0xFF),
            (byte)((number >> 8) & 0xFF),
            (byte)(number & 0xFF));
        return true;
    }

    private static SolidColorBrush Brush(Color color) => new(color);

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Blend(Color from, Color to, double amount)
    {
        return Color.FromArgb(
            255,
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private static bool IsLight(Color color)
    {
        var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
        return luminance > 0.55;
    }
}
