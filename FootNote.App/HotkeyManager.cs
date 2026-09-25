using System.Windows.Interop;

namespace FootNote.App;

/// <summary>
/// Global hotkey via RegisterHotKey on a message-only window. Remappable at
/// runtime (Settings → Hotkey): <see cref="Apply"/> re-registers live, and
/// <see cref="Probe"/> lets the settings UI conflict-check a combo before
/// accepting it.
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly int HotkeyId;
    private readonly int ProbeId;

    private readonly HwndSource _source;
    private (uint mods, uint vk)? _current;

    public event Action? Pressed;
    public bool Registered { get; private set; }

    /// <summary>Each live instance needs its own RegisterHotKey ids, since two
    /// instances (e.g. the note hotkey and the search hotkey) share no state.</summary>
    public HotkeyManager(int hotkeyId = 0xF11E, int probeId = 0xF11F)
    {
        HotkeyId = hotkeyId;
        ProbeId = probeId;
        var p = new HwndSourceParameters("FootNoteHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>Registers the combo, replacing any previous one. On failure the
    /// previous registration is restored. Returns whether the new combo took.</summary>
    public bool Apply(bool ctrl, bool shift, bool alt, string key)
    {
        uint vk = VkFromKey(key);
        if (vk == 0) return false;
        uint mods = Mods(ctrl, shift, alt);

        if (_current is { } cur)
        {
            if (cur.mods == mods && cur.vk == vk && Registered) return true; // no-op
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        }

        if (NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, mods | NativeMethods.MOD_NOREPEAT, vk))
        {
            _current = (mods, vk);
            Registered = true;
            return true;
        }

        // Revert to the previous working combo so the app isn't left hotkey-less.
        if (_current is { } prev)
            Registered = NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, prev.mods | NativeMethods.MOD_NOREPEAT, prev.vk);
        else
            Registered = false;
        return false;
    }

    /// <summary>True if the combo could be registered right now (or already is ours).</summary>
    public bool Probe(bool ctrl, bool shift, bool alt, string key)
    {
        uint vk = VkFromKey(key);
        if (vk == 0) return false;
        uint mods = Mods(ctrl, shift, alt);
        if (_current is { } cur && cur.mods == mods && cur.vk == vk && Registered) return true;

        if (!NativeMethods.RegisterHotKey(_source.Handle, ProbeId, mods, vk)) return false;
        NativeMethods.UnregisterHotKey(_source.Handle, ProbeId);
        return true;
    }

    private static uint Mods(bool ctrl, bool shift, bool alt) =>
        (ctrl ? NativeMethods.MOD_CONTROL : 0) |
        (shift ? NativeMethods.MOD_SHIFT : 0) |
        (alt ? NativeMethods.MOD_ALT : 0);

    /// <summary>"A"–"Z", "0"–"9", "F1"–"F24" → virtual-key code; 0 if unsupported.</summary>
    public static uint VkFromKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        key = key.ToUpperInvariant();
        if (key.Length == 1 && (char.IsAsciiLetter(key[0]) || char.IsAsciiDigit(key[0])))
            return key[0];
        if (key.Length is 2 or 3 && key[0] == 'F'
            && int.TryParse(key[1..], out int f) && f is >= 1 and <= 24)
            return (uint)(0x70 + f - 1); // VK_F1
        return 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (Registered) NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _source.Dispose();
    }
}
