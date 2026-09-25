using System.Text.Json.Serialization;

namespace FootNote.App.Settings;

/// <summary>
/// POCO matching %APPDATA%\FootNote\settings.json. SchemaVersion exists so
/// future fields can migrate safely instead of resetting the file.
/// </summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 2;

    // --- Position -----------------------------------------------------------
    /// <summary>"Top" or "Bottom".</summary>
    public string ScreenEdge { get; set; } = "Bottom";

    /// <summary>-1 = Auto (same monitor as the active Explorer window); otherwise a display index.</summary>
    public int MonitorIndex { get; set; } = -1;

    // --- Appearance -----------------------------------------------------------
    /// <summary>Hex color for accent elements (Save button etc.).</summary>
    public string AccentColor { get; set; } = "#4F8EF7";

    // --- Panel appearance (bounded: presets and sliders, not a theme editor) ---
    /// <summary>"Bar" (full-width) or "Pill" (floating, centered).</summary>
    public string BarStyle { get; set; } = "Bar";

    /// <summary>Panel background — separate from accent, which governs buttons/badge only.</summary>
    public string PanelColor { get; set; } = "#1E1E2B";

    /// <summary>0–24 px: sharp rectangle to full pill.</summary>
    public int CornerRadius { get; set; } = 12;

    /// <summary>"Compact" or "Comfortable" padding preset.</summary>
    public string SizePreset { get; set; } = "Comfortable";

    /// <summary>"Small", "Medium", or "Large".</summary>
    public string FontScale { get; set; } = "Medium";

    /// <summary>Real Windows acrylic blur behind the panel. Off by default (perf cost).</summary>
    public bool Translucency { get; set; }

    /// <summary>Soft colored glow behind every note card.</summary>
    public bool BloomEnabled { get; set; } = true;

    /// <summary>Hex color for the bloom, independent of the accent color.</summary>
    public string BloomColor { get; set; } = "#4F8EF7";

    [JsonIgnore] public bool IsPill => BarStyle.Equals("Pill", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public double FontScaleFactor => FontScale.ToLowerInvariant() switch
    { "small" => 0.88, "large" => 1.15, _ => 1.0 };
    [JsonIgnore] public bool IsCompact => SizePreset.Equals("Compact", StringComparison.OrdinalIgnoreCase);

    // --- Behavior --------------------------------------------------------------
    /// <summary>Seconds before the read-mode bar hides itself (2–15).</summary>
    public int AutoHideSeconds { get; set; } = 7;

    /// <summary>True = never auto-hide; bar stays until dismissed or deselected.</summary>
    public bool StayUntilDismissed { get; set; }

    public bool SlideAnimation { get; set; } = true;

    // --- Hotkey -----------------------------------------------------------------
    public bool HotkeyCtrl { get; set; }
    public bool HotkeyShift { get; set; } = true;
    public bool HotkeyAlt { get; set; } = true;
    /// <summary>"A"–"Z", "0"–"9", or "F1"–"F24".</summary>
    public string HotkeyKey { get; set; } = "N";

    [JsonIgnore]
    public string HotkeyDisplay
    {
        get
        {
            var parts = new List<string>(4);
            if (HotkeyCtrl) parts.Add("Ctrl");
            if (HotkeyShift) parts.Add("Shift");
            if (HotkeyAlt) parts.Add("Alt");
            parts.Add(HotkeyKey);
            return string.Join("+", parts);
        }
    }

    // --- Search hotkey ------------------------------------------------------
    public bool SearchHotkeyCtrl { get; set; } = true;
    public bool SearchHotkeyShift { get; set; } = true;
    public bool SearchHotkeyAlt { get; set; } = true;
    /// <summary>"A"–"Z", "0"–"9", or "F1"–"F24".</summary>
    public string SearchHotkeyKey { get; set; } = "N";

    [JsonIgnore]
    public string SearchHotkeyDisplay
    {
        get
        {
            var parts = new List<string>(4);
            if (SearchHotkeyCtrl) parts.Add("Ctrl");
            if (SearchHotkeyShift) parts.Add("Shift");
            if (SearchHotkeyAlt) parts.Add("Alt");
            parts.Add(SearchHotkeyKey);
            return string.Join("+", parts);
        }
    }

    [JsonIgnore]
    public bool IsBottomEdge => !ScreenEdge.Equals("Top", StringComparison.OrdinalIgnoreCase);
}
