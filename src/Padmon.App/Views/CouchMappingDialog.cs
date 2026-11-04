using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Padmon.Core.Settings;
using Padmon.ViewModels;
using Windows.System;

namespace Padmon.Views;

internal static class CouchMappingDialog
{
    public static async Task<CouchBindingSettings?> ShowAsync(
        XamlRoot xamlRoot,
        string controlLabel,
        CouchBindingSettings current)
    {
        var options = new List<ActionOption>
        {
            new("None", CouchBindingKind.None),
            new("Keyboard key", CouchBindingKind.Keyboard),
            new("Mouse: left click", CouchBindingKind.MouseButton, 0),
            new("Mouse: right click", CouchBindingKind.MouseButton, 1),
            new("Mouse: middle click", CouchBindingKind.MouseButton, 2),
            new("Hide padmon", CouchBindingKind.HideWindow),
            new("On-screen keyboard", CouchBindingKind.OnScreenKeyboard)
        };

        var selectedIndex = current.Kind switch
        {
            CouchBindingKind.Keyboard => 1,
            CouchBindingKind.MouseButton => current.Code switch
            {
                0 => 2,
                1 => 3,
                2 => 4,
                _ => 2
            },
            CouchBindingKind.HideWindow => 5,
            CouchBindingKind.OnScreenKeyboard => 6,
            _ => 0
        };

        var actionPicker = new ComboBox
        {
            ItemsSource = options,
            DisplayMemberPath = nameof(ActionOption.Label),
            SelectedIndex = selectedIndex,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var capturedKey = current.Kind == CouchBindingKind.Keyboard ? current.Code : 0;
        var keyBox = new TextBox
        {
            IsReadOnly = true,
            PlaceholderText = "Click here, then press a key",
            Text = capturedKey == 0 ? string.Empty : CouchBindingText.KeyName(capturedKey)
        };

        keyBox.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.None)
            {
                return;
            }

            capturedKey = (int)args.Key;
            keyBox.Text = CouchBindingText.KeyName(capturedKey);
            args.Handled = true;
        };

        var keyPanel = new StackPanel { Spacing = 6 };
        keyPanel.Children.Add(new TextBlock
        {
            Text = "Press the key this control should send.",
            FontSize = 12,
            Opacity = 0.7
        });
        keyPanel.Children.Add(keyBox);

        void UpdateKeyPanel()
        {
            keyPanel.Visibility = (actionPicker.SelectedItem as ActionOption)?.Kind == CouchBindingKind.Keyboard
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        actionPicker.SelectionChanged += (_, _) => UpdateKeyPanel();
        UpdateKeyPanel();

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = controlLabel,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(actionPicker);
        panel.Children.Add(keyPanel);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Map controller control",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            Content = panel
        };

        dialog.Opened += (_, _) =>
        {
            if (keyPanel.Visibility == Visibility.Visible)
            {
                keyBox.Focus(FocusState.Programmatic);
            }
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            actionPicker.SelectedItem is not ActionOption selected)
        {
            return null;
        }

        return selected.Kind switch
        {
            CouchBindingKind.Keyboard => new CouchBindingSettings
            {
                Kind = CouchBindingKind.Keyboard,
                Code = capturedKey == 0 ? (int)VirtualKey.Space : capturedKey
            },
            CouchBindingKind.MouseButton => new CouchBindingSettings
            {
                Kind = CouchBindingKind.MouseButton,
                Code = selected.Code
            },
            CouchBindingKind.HideWindow => new CouchBindingSettings
            {
                Kind = CouchBindingKind.HideWindow
            },
            CouchBindingKind.OnScreenKeyboard => new CouchBindingSettings
            {
                Kind = CouchBindingKind.OnScreenKeyboard
            },
            _ => new CouchBindingSettings
            {
                Kind = CouchBindingKind.None
            }
        };
    }

    private sealed record ActionOption(string Label, CouchBindingKind Kind, int Code = 0);
}
