using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Padmon.Core.Diagnostics;
using Padmon.ViewModels;

namespace Padmon.Views;

internal static class DeviceMergeDialog
{
    public static async Task<bool> ShowAsync(
        XamlRoot xamlRoot,
        KnownControllerViewModel controller,
        IReadOnlyList<KnownControllerViewModel> knownControllers)
    {
        if (controller.IsMerged)
        {
            return await SeparateAsync(xamlRoot, controller);
        }

        var others = knownControllers
            .Where(candidate => !string.Equals(
                candidate.Record.StableKey,
                controller.Record.StableKey,
                StringComparison.Ordinal))
            .Select(candidate => new MergeOption(
                candidate,
                $"{candidate.DisplayName} — {candidate.HardwareName}"))
            .ToArray();

        if (others.Length == 0)
        {
            await ShowMessageAsync(
                xamlRoot,
                "Nothing to merge",
                "Connect or identify another controller first.");
            return false;
        }

        var picker = new ComboBox
        {
            ItemsSource = others,
            DisplayMemberPath = nameof(MergeOption.Label),
            SelectedIndex = 0,
            MinWidth = 380,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Keep \"{controller.DisplayName}\" as the main entry.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Merge with",
            FontSize = 12,
            Opacity = 0.7
        });
        panel.Children.Add(picker);
        panel.Children.Add(new TextBlock
        {
            Text = "Sessions and future connections from either identity will be grouped here. You can separate them later.",
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Merge devices",
            PrimaryButtonText = "Merge",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = panel
        };

        var result = await dialog.ShowAsync();

        if (result != ContentDialogResult.Primary ||
            picker.SelectedItem is not MergeOption selected)
        {
            return false;
        }

        try
        {
            PadmonLog.Info(
                $"merging '{selected.Controller.Record.StableKey}' into '{controller.Record.StableKey}'");
            await App.Services.History.MergeAsync(
                controller.Record.StableKey,
                [selected.Controller.Record.StableKey]);
            return true;
        }
        catch (Exception exception)
        {
            PadmonLog.Error("merge failed", exception);
            await ShowMessageAsync(xamlRoot, "Could not merge devices", exception.Message);
            return false;
        }
    }

    private static async Task<bool> SeparateAsync(
        XamlRoot xamlRoot,
        KnownControllerViewModel controller)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Separate devices?",
            Content = $"\"{controller.DisplayName}\" will be split back into its {controller.Record.DeviceCount} individual identities.",
            PrimaryButtonText = "Separate",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        try
        {
            await App.Services.History.UnmergeAsync(controller.Record.StableKey);
            return true;
        }
        catch (Exception exception)
        {
            PadmonLog.Error("separate failed", exception);
            await ShowMessageAsync(xamlRoot, "Could not separate devices", exception.Message);
            return false;
        }
    }

    private static async Task ShowMessageAsync(XamlRoot xamlRoot, string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "Close"
        };

        await dialog.ShowAsync();
    }

    private sealed record MergeOption(KnownControllerViewModel Controller, string Label);
}
