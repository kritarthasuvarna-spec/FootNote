using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace FootNote.App.Settings;

/// <summary>
/// Two-way bound to <see cref="SettingsWindow"/>. Every setter writes through
/// to <see cref="SettingsService"/> immediately (live-apply). Owns validation:
/// hotkey conflict checks (via a probe delegate supplied by the app) and value
/// ranges.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _service = SettingsService.Instance;
    private readonly Func<bool, bool, bool, string, bool> _probeHotkey;
    private readonly Func<bool, bool, bool, string, bool> _probeSearchHotkey;
    private AppSettings S => _service.Current;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(Func<bool, bool, bool, string, bool> probeHotkey,
        Func<bool, bool, bool, string, bool> probeSearchHotkey)
    {
        _probeHotkey = probeHotkey;
        _probeSearchHotkey = probeSearchHotkey;
        ResetToDefaultsCommand = new RelayCommand(() =>
        {
            _service.ResetToDefaults();
            RaiseAll();
        });
        MonitorOptions = BuildMonitorOptions();
    }

    // ---- Position ---------------------------------------------------------

    public bool EdgeBottom
    {
        get => S.IsBottomEdge;
        set { if (value) { S.ScreenEdge = "Bottom"; Changed(); RaiseAll(); } }
    }

    public bool EdgeTop
    {
        get => !S.IsBottomEdge;
        set { if (value) { S.ScreenEdge = "Top"; Changed(); RaiseAll(); } }
    }

    public IReadOnlyList<string> MonitorOptions { get; }

    public int MonitorSelectedIndex
    {
        get => Math.Clamp(S.MonitorIndex + 1, 0, MonitorOptions.Count - 1);
        set { S.MonitorIndex = value - 1; Changed(); Raise(); }
    }

    private static List<string> BuildMonitorOptions()
    {
        var list = new List<string> { "Auto (follow Explorer window)" };
        var screens = System.Windows.Forms.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var b = screens[i].Bounds;
            list.Add($"Monitor {i + 1} ({b.Width}×{b.Height}){(screens[i].Primary ? " — primary" : "")}");
        }
        return list;
    }

    // ---- Appearance -------------------------------------------------------

    public string AccentColor
    {
        get => S.AccentColor;
        set
        {
            string v = value?.Trim() ?? "";
            if (!v.StartsWith('#')) v = "#" + v;
            try
            {
                ColorConverter.ConvertFromString(v); // throws on invalid
                S.AccentColor = v;
                AccentError = "";
                Changed();
            }
            catch { AccentError = "Not a valid hex color"; }
            Raise();
            Raise(nameof(AccentBrush));
            Raise(nameof(AccentError));
        }
    }

    public string AccentError { get; private set; } = "";

    public System.Windows.Media.Brush AccentBrush
    {
        get
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(S.AccentColor)); }
            catch { return Brushes.SteelBlue; }
        }
    }

    /// <summary>Swatch click helper.</summary>
    public void SetAccent(string hex) => AccentColor = hex;

    /// <summary>Swatch click helper.</summary>
    public void SetBloomColor(string hex) => BloomColor = hex;

    // ---- Panel appearance ---------------------------------------------------

    public bool StyleBar
    {
        get => !S.IsPill;
        set { if (value) { S.BarStyle = "Bar"; Changed(); RaiseAll(); } }
    }

    public bool StylePill
    {
        get => S.IsPill;
        set { if (value) { S.BarStyle = "Pill"; Changed(); RaiseAll(); } }
    }

    public string PanelColor
    {
        get => S.PanelColor;
        set
        {
            string v = value?.Trim() ?? "";
            if (!v.StartsWith('#')) v = "#" + v;
            try
            {
                ColorConverter.ConvertFromString(v);
                S.PanelColor = v;
                Changed();
            }
            catch { /* invalid hex — ignore */ }
            Raise(); Raise(nameof(PanelBrush));
        }
    }

    public System.Windows.Media.Brush PanelBrush
    {
        get
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(S.PanelColor)); }
            catch { return Brushes.DarkSlateGray; }
        }
    }

    public int CornerRadius
    {
        get => S.CornerRadius;
        set { S.CornerRadius = Math.Clamp(value, 0, 24); Changed(); Raise(); Raise(nameof(CornerRadiusLabel)); }
    }

    public string CornerRadiusLabel => $"{S.CornerRadius}px";

    public int SizeSelectedIndex
    {
        get => S.IsCompact ? 1 : 0;
        set { S.SizePreset = value == 1 ? "Compact" : "Comfortable"; Changed(); Raise(); }
    }

    public int FontScaleSelectedIndex
    {
        get => S.FontScale.ToLowerInvariant() switch { "small" => 0, "large" => 2, _ => 1 };
        set { S.FontScale = value switch { 0 => "Small", 2 => "Large", _ => "Medium" }; Changed(); Raise(); }
    }

    public bool Translucency
    {
        get => S.Translucency;
        set { S.Translucency = value; Changed(); Raise(); }
    }

    public bool BloomEnabled
    {
        get => S.BloomEnabled;
        set { S.BloomEnabled = value; Changed(); Raise(); }
    }

    public string BloomColor
    {
        get => S.BloomColor;
        set
        {
            string v = value?.Trim() ?? "";
            if (!v.StartsWith('#')) v = "#" + v;
            try
            {
                ColorConverter.ConvertFromString(v);
                S.BloomColor = v;
                Changed();
            }
            catch { /* invalid hex — ignore */ }
            Raise(); Raise(nameof(BloomBrush));
        }
    }

    public System.Windows.Media.Brush BloomBrush
    {
        get
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(S.BloomColor)); }
            catch { return Brushes.SteelBlue; }
        }
    }

    // ---- Behavior -----------------------------------------------------------

    public int AutoHideSeconds
    {
        get => S.AutoHideSeconds;
        set { S.AutoHideSeconds = Math.Clamp(value, 2, 15); Changed(); Raise(); Raise(nameof(AutoHideLabel)); }
    }

    public string AutoHideLabel => $"{S.AutoHideSeconds}s";

    public bool StayUntilDismissed
    {
        get => S.StayUntilDismissed;
        set { S.StayUntilDismissed = value; Changed(); Raise(); Raise(nameof(AutoHideEnabled)); }
    }

    public bool AutoHideEnabled => !S.StayUntilDismissed;

    public bool SlideAnimation
    {
        get => S.SlideAnimation;
        set { S.SlideAnimation = value; Changed(); Raise(); }
    }

    // ---- Hotkey ---------------------------------------------------------------

    public string HotkeyDisplay => S.HotkeyDisplay;

    public string HotkeyStatus { get; private set; } = "";

    /// <summary>Called by the capture box. Validates before accepting.</summary>
    public void TrySetHotkey(bool ctrl, bool shift, bool alt, string key)
    {
        if (HotkeyManager.VkFromKey(key) == 0)
        {
            HotkeyStatus = "Unsupported key — use a letter, digit, or F-key.";
        }
        else if (!ctrl && !shift && !alt)
        {
            HotkeyStatus = "Add at least one modifier (Ctrl/Shift/Alt).";
        }
        else if (!_probeHotkey(ctrl, shift, alt, key))
        {
            HotkeyStatus = $"{FormatCombo(ctrl, shift, alt, key)} is already taken by another app.";
        }
        else
        {
            S.HotkeyCtrl = ctrl; S.HotkeyShift = shift; S.HotkeyAlt = alt; S.HotkeyKey = key;
            HotkeyStatus = "Applied.";
            Changed();
        }
        Raise(nameof(HotkeyDisplay));
        Raise(nameof(HotkeyStatus));
    }

    private static string FormatCombo(bool c, bool s, bool a, string k) =>
        string.Join("+", new[] { c ? "Ctrl" : null, s ? "Shift" : null, a ? "Alt" : null, k }
            .Where(x => x is not null));

    // ---- Search hotkey ----------------------------------------------------

    public string SearchHotkeyDisplay => S.SearchHotkeyDisplay;

    public string SearchHotkeyStatus { get; private set; } = "";

    /// <summary>Called by the search hotkey capture box. Validates before accepting.</summary>
    public void TrySetSearchHotkey(bool ctrl, bool shift, bool alt, string key)
    {
        if (HotkeyManager.VkFromKey(key) == 0)
        {
            SearchHotkeyStatus = "Unsupported key — use a letter, digit, or F-key.";
        }
        else if (!ctrl && !shift && !alt)
        {
            SearchHotkeyStatus = "Add at least one modifier (Ctrl/Shift/Alt).";
        }
        else if (!_probeSearchHotkey(ctrl, shift, alt, key))
        {
            SearchHotkeyStatus = $"{FormatCombo(ctrl, shift, alt, key)} is already taken by another app.";
        }
        else
        {
            S.SearchHotkeyCtrl = ctrl; S.SearchHotkeyShift = shift; S.SearchHotkeyAlt = alt; S.SearchHotkeyKey = key;
            SearchHotkeyStatus = "Applied.";
            Changed();
        }
        Raise(nameof(SearchHotkeyDisplay));
        Raise(nameof(SearchHotkeyStatus));
    }

    // ---- plumbing ----------------------------------------------------------------

    public ICommand ResetToDefaultsCommand { get; }

    private void Changed() => _service.NotifyChanged();

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void RaiseAll() => Raise(string.Empty);
}

/// <summary>Minimal ICommand for the one command this window needs.</summary>
public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
