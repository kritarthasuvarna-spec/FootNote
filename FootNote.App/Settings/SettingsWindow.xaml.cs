using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;

namespace FootNote.App.Settings;

public partial class SettingsWindow : Window
{
    private static readonly string[] Swatches =
        ["#4F8EF7", "#8B5CF6", "#EC4899", "#F26B6B", "#F59E0B", "#22C55E", "#14B8A6", "#9B9BA8"];

    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        FootNote.App.DarkTitleBar.Apply(this);
        DataContext = _vm = vm;

        foreach (string hex in Swatches)
        {
            var b = new Button
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Tag = hex,
            };
            b.Click += (_, _) => _vm.SetAccent((string)b.Tag);
            SwatchPanel.Children.Add(b);
        }

        foreach (string hex in Swatches)
        {
            var b = new Button
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Tag = hex,
            };
            b.Click += (_, _) => _vm.SetBloomColor((string)b.Tag);
            BloomSwatchPanel.Children.Add(b);
        }

        SettingsService.Instance.SettingsChanged += UpdatePreviewEdge;
        Closed += (_, _) => SettingsService.Instance.SettingsChanged -= UpdatePreviewEdge;
        UpdatePreviewEdge();

        // Position/Appearance/Panel customization is Pro-only; Free shows a
        // short upsell note in their place instead of the controls.
        if (!FootNote.Core.ProFeatures.Current.Unlocked)
        {
            PositionSection.Visibility = Visibility.Collapsed;
            AppearanceSection.Visibility = Visibility.Collapsed;
            PanelSection.Visibility = Visibility.Collapsed;
            ProUpsellSection.Visibility = Visibility.Visible;
        }
    }

    private void UpdatePreviewEdge()
    {
        var s = SettingsService.Instance.Current;
        bool bottom = s.IsBottomEdge;
        PreviewBar.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        double r = s.CornerRadius / 2.0; // preview is ~half scale
        if (s.IsPill)
        {
            PreviewBar.CornerRadius = new CornerRadius(r);
            PreviewBar.Margin = new Thickness(40, bottom ? 0 : 8, 40, bottom ? 8 : 0);
        }
        else
        {
            PreviewBar.CornerRadius = bottom ? new CornerRadius(r, r, 0, 0) : new CornerRadius(0, 0, r, r);
            PreviewBar.Margin = new Thickness(14, 0, 14, 0);
        }
    }

    private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        HotkeyBox.Text = "Press a key combination…";

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore bare modifier presses — wait for the real key.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None)
            return;

        string? keyName = key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => key.ToString()[1..],
            >= Key.NumPad0 and <= Key.NumPad9 => key.ToString()[6..],
            >= Key.F1 and <= Key.F24 => key.ToString(),
            _ => null,
        };

        var mods = Keyboard.Modifiers;
        _vm.TrySetHotkey(
            mods.HasFlag(ModifierKeys.Control),
            mods.HasFlag(ModifierKeys.Shift),
            mods.HasFlag(ModifierKeys.Alt),
            keyName ?? "?");

        // Restore the bound display text (capture prompt replaced it).
        HotkeyBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
