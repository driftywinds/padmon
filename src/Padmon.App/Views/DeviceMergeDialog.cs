using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Padmon.Core.Diagnostics;
using Padmon.ViewModels;
using Windows.UI;

namespace Padmon.Views;

internal static class DeviceMergeDialog
{
    private const int MaxIdentities = 10;

    public static async Task<bool> ShowAsync(
        XamlRoot xamlRoot,
        KnownControllerViewModel controller,
        IReadOnlyList<KnownControllerViewModel> knownControllers)
    {
        var identities = BuildIdentities(controller, knownControllers);
        var nameBox = new TextBox
        {
            Text = controller.DisplayName,
            PlaceholderText = controller.Record.HardwareName,
            MaxLength = 80,
            MinWidth = 420
        };

        var namePanel = new StackPanel { Spacing = 5 };
        namePanel.Children.Add(new TextBlock
        {
            Text = "Tracked controller name",
            FontSize = 12,
            Opacity = 0.7
        });
        namePanel.Children.Add(nameBox);
        namePanel.Children.Add(new TextBlock
        {
            Text = "This name is used in the controller list, tester, and notifications.",
            FontSize = 12,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap
        });

        var searchBox = new TextBox
        {
            PlaceholderText = "Search identities by name or hardware",
            Margin = new Thickness(0, 4, 0, 6)
        };

        var identityList = new StackPanel { Spacing = 2 };
        var listScroller = new ScrollViewer
        {
            Content = identityList,
            MaxHeight = 300,
            MinHeight = 120,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var countText = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var limitText = new TextBlock
        {
            Text = $"Up to {MaxIdentities} identities can share one tracked controller.",
            FontSize = 12,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap
        };

        var updating = false;
        void UpdateCount()
        {
            countText.Text = $"{identities.Count(identity => identity.IsSelected)} of {MaxIdentities} identities";
        }

        void RebuildRows()
        {
            identityList.Children.Clear();
            var filter = searchBox.Text.Trim();

            foreach (var identity in identities)
            {
                if (filter.Length > 0 &&
                    !identity.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !identity.HardwareName.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !identity.Detail.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var check = new CheckBox
                {
                    IsChecked = identity.IsSelected,
                    IsEnabled = !identity.IsRoot,
                    Tag = identity,
                    MinWidth = 0,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var text = new StackPanel { Spacing = 1, Margin = new Thickness(0, 1, 0, 1) };
                text.Children.Add(new TextBlock
                {
                    Text = identity.DisplayName,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                text.Children.Add(new TextBlock
                {
                    Text = identity.Detail,
                    FontSize = 11.5,
                    Opacity = 0.62,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                var header = new Grid { ColumnSpacing = 8 };
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.Children.Add(text);

                if (identity.IsRoot)
                {
                    var badge = new Border
                    {
                        Padding = new Thickness(6, 2, 6, 2),
                        CornerRadius = new CornerRadius(6),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(90, 122, 162, 247)),
                        Background = new SolidColorBrush(Color.FromArgb(28, 122, 162, 247)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    badge.Child = new TextBlock
                    {
                        Text = "MAIN",
                        FontSize = 9,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 122, 162, 247))
                    };
                    Grid.SetColumn(badge, 1);
                    header.Children.Add(badge);
                }
                else if (!string.IsNullOrWhiteSpace(identity.GroupLabel))
                {
                    var badge = new TextBlock
                    {
                        Text = identity.GroupLabel,
                        FontSize = 10,
                        Opacity = 0.55,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(badge, 1);
                    header.Children.Add(badge);
                }

                check.Content = header;
                check.Checked += (_, _) =>
                {
                    if (updating)
                    {
                        return;
                    }

                    if (identities.Count(option => option.IsSelected) >= MaxIdentities &&
                        !identity.IsSelected)
                    {
                        updating = true;
                        check.IsChecked = false;
                        updating = false;
                        countText.Text = $"Limit reached: {MaxIdentities} identities";
                        return;
                    }

                    identity.IsSelected = true;
                    UpdateCount();
                };
                check.Unchecked += (_, _) =>
                {
                    if (updating || identity.IsRoot)
                    {
                        return;
                    }

                    identity.IsSelected = false;
                    UpdateCount();
                };

                identityList.Children.Add(new Border
                {
                    Padding = new Thickness(2, 1, 2, 1),
                    CornerRadius = new CornerRadius(6),
                    Child = check
                });
            }
        }

        searchBox.TextChanged += (_, _) => RebuildRows();
        RebuildRows();
        UpdateCount();

        var identitiesPanel = new StackPanel { Spacing = 4 };
        identitiesPanel.Children.Add(new TextBlock
        {
            Text = "Identities",
            FontSize = 12,
            Opacity = 0.7
        });
        identitiesPanel.Children.Add(searchBox);
        identitiesPanel.Children.Add(listScroller);
        identitiesPanel.Children.Add(countText);

        var panel = new StackPanel
        {
            Spacing = 14,
            Width = 500
        };
        panel.Children.Add(namePanel);
        panel.Children.Add(new TextBlock
        {
            Text = "Checked identities are grouped under this tracked controller. Uncheck one to separate it. Selecting a grouped entry moves only that identity.",
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480
        });
        panel.Children.Add(identitiesPanel);
        panel.Children.Add(limitText);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = controller.IsMerged ? "Manage controller identities" : "Merge controller identities",
            PrimaryButtonText = controller.IsMerged
                ? "Save changes"
                : identities.Count > 1
                    ? "Merge identities"
                    : "Save",
            SecondaryButtonText = controller.IsMerged ? "Separate all" : null,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = panel
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            if (!await ConfirmSeparateAsync(xamlRoot, controller))
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

        if (result != ContentDialogResult.Primary)
        {
            return false;
        }

        var name = string.IsNullOrWhiteSpace(nameBox.Text)
            ? controller.Record.HardwareName
            : nameBox.Text.Trim();

        var selectedKeys = identities
            .Where(identity => identity.IsSelected)
            .Select(identity => identity.StableKey)
            .Where(key => !string.Equals(key, controller.Record.StableKey, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (selectedKeys.Length + 1 > MaxIdentities)
        {
            await ShowMessageAsync(
                xamlRoot,
                "Too many identities",
                $"A tracked controller can group at most {MaxIdentities} identities.");
            return false;
        }

        var currentAliasKeys = controller.Record.Aliases
            .Select(alias => alias.StableKey)
            .ToHashSet(StringComparer.Ordinal);

        var changed = !string.Equals(name, controller.DisplayName, StringComparison.Ordinal) ||
                      !currentAliasKeys.SetEquals(selectedKeys);

        if (!changed)
        {
            return false;
        }

        try
        {
            if (!string.Equals(name, controller.DisplayName, StringComparison.Ordinal))
            {
                await App.Services.History.RenameAsync(controller.Record.StableKey, name);
            }

            if (currentAliasKeys.Count > 0)
            {
                await App.Services.History.UnmergeAsync(controller.Record.StableKey);
            }

            // Selecting a grouped entry moves that identity, not its whole
            // group. Detach each selected group root and remerge only the
            // identities that are actually checked.
            foreach (var key in selectedKeys)
            {
                var known = knownControllers.FirstOrDefault(
                    candidate => string.Equals(
                        candidate.Record.StableKey,
                        key,
                        StringComparison.Ordinal));

                if (known?.IsMerged == true)
                {
                    await App.Services.History.UnmergeAsync(key);
                }
            }

            if (selectedKeys.Length > 0)
            {
                PadmonLog.Info(
                    $"merging {selectedKeys.Length} identities into '{controller.Record.StableKey}'");
                await App.Services.History.MergeAsync(controller.Record.StableKey, selectedKeys);
            }

            return true;
        }
        catch (Exception exception)
        {
            PadmonLog.Error("merge failed", exception);
            await ShowMessageAsync(xamlRoot, "Could not update identities", exception.Message);
            return false;
        }
    }

    private static List<MergeIdentity> BuildIdentities(
        KnownControllerViewModel controller,
        IReadOnlyList<KnownControllerViewModel> knownControllers)
    {
        var identities = new List<MergeIdentity>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(
            string stableKey,
            string displayName,
            string hardwareName,
            string detail,
            bool isRoot,
            bool isSelected,
            string? groupLabel)
        {
            if (!seen.Add(stableKey))
            {
                return;
            }

            identities.Add(new MergeIdentity(
                stableKey,
                displayName,
                hardwareName,
                detail,
                isRoot,
                isSelected,
                groupLabel));
        }

        Add(
            controller.Record.StableKey,
            controller.DisplayName,
            controller.Record.HardwareName,
            $"Main identity · {controller.Record.HardwareName}",
            isRoot: true,
            isSelected: true,
            groupLabel: null);

        foreach (var alias in controller.Record.Aliases)
        {
            Add(
                alias.StableKey,
                alias.DisplayName,
                alias.HardwareName,
                $"Merged identity · {alias.HardwareName}",
                isRoot: false,
                isSelected: true,
                groupLabel: null);
        }

        foreach (var known in knownControllers)
        {
            var groupLabel = known.DisplayName;
            var record = known.Record;

            if (!string.Equals(record.StableKey, controller.Record.StableKey, StringComparison.Ordinal))
            {
                Add(
                    record.StableKey,
                    known.DisplayName,
                    record.HardwareName,
                    $"{record.HardwareName} · {record.VendorId:X4}:{record.ProductId:X4}",
                    isRoot: false,
                    isSelected: false,
                    groupLabel: known.IsMerged ? groupLabel : null);
            }

            foreach (var alias in record.Aliases)
            {
                Add(
                    alias.StableKey,
                    alias.DisplayName,
                    alias.HardwareName,
                    $"{alias.HardwareName} · in {groupLabel}",
                    isRoot: false,
                    isSelected: false,
                    groupLabel: groupLabel);
            }
        }

        return identities;
    }

    private static async Task<bool> ConfirmSeparateAsync(
        XamlRoot xamlRoot,
        KnownControllerViewModel controller)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Separate all identities?",
            Content = $"\"{controller.DisplayName}\" will be split back into its {controller.Record.DeviceCount} individual identities.",
            PrimaryButtonText = "Separate all",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
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

    private sealed class MergeIdentity(
        string stableKey,
        string displayName,
        string hardwareName,
        string detail,
        bool isRoot,
        bool isSelected,
        string? groupLabel)
    {
        public string StableKey { get; } = stableKey;

        public string DisplayName { get; } = displayName;

        public string HardwareName { get; } = hardwareName;

        public string Detail { get; } = detail;

        public bool IsRoot { get; } = isRoot;

        public bool IsSelected { get; set; } = isSelected;

        public string? GroupLabel { get; } = groupLabel;
    }
}
