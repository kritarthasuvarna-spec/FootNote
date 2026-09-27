using System.Runtime.InteropServices;

namespace FootNote.App;

/// <summary>
/// Resolves the file paths currently selected in an Explorer window via the
/// Shell.Application automation object (in-process automation — this is not a
/// shell extension and registers nothing).
///
/// Win11 tab handling: every open tab appears as its own entry in Shell.Windows(),
/// all sharing the top-level CabinetWClass HWND. The entry belonging to the
/// *active* tab is found by asking each entry's IShellBrowser for its own
/// ShellTabWindowClass HWND — only the active tab's is visible.
/// </summary>
internal static class ShellSelection
{
    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        void QueryService(ref Guid guidService, ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
    }

    // IShellBrowser derives from IOleWindow; only GetWindow (vtable slot 3) is needed.
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(out IntPtr phwnd);
        void ContextSensitiveHelp(bool fEnterMode);
    }

    /// <summary>ShellWindows collection, declared with the full vtable (dual interface) so
    /// FindWindowSW can be called — that's how the desktop's folder view is reached
    /// (the desktop isn't part of the normal Windows() enumeration).</summary>
    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"),
     InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        int Count { get; }
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object Item([MarshalAs(UnmanagedType.Struct)] object index);
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object _NewEnum();
        void Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass, out int plCookie);
        void RegisterPending(int lThreadId, [MarshalAs(UnmanagedType.Struct)] ref object? pvarloc,
            [MarshalAs(UnmanagedType.Struct)] ref object? pvarlocRoot, int swClass, out int plCookie);
        void Revoke(int lCookie);
        void OnNavigate(int lCookie, [MarshalAs(UnmanagedType.Struct)] ref object? pvarLoc);
        void OnActivated(int lCookie, bool fActive);
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object? FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object? pvarLoc,
                             [MarshalAs(UnmanagedType.Struct)] ref object? pvarLocRoot,
                             int swClass, out int phwnd, int swfwOptions);
        void OnCreated(int lCookie, [MarshalAs(UnmanagedType.IUnknown)] object punk);
        void ProcessAttachDetach(bool fAttach);
    }

    private const int SWC_DESKTOP = 8;
    private const int SWFO_NEEDDISPATCH = 1;

    private static Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");

    private static dynamic? _shell;

    private static dynamic? GetShell()
    {
        if (_shell is null)
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t is null) return null;
            _shell = Activator.CreateInstance(t);
        }
        return _shell;
    }

    /// <summary>
    /// Selected item paths in the active tab of the given Explorer window,
    /// or an empty list if it can't be determined. Never throws.
    /// </summary>
    public static List<string> GetSelectedPaths(IntPtr explorerHwnd)
    {
        var result = new List<string>();
        try
        {
            dynamic? shell = GetShell();
            if (shell is null) return result;

            long target = explorerHwnd.ToInt64();
            dynamic? activeTabDoc = null;
            dynamic? soleMatchDoc = null;
            int matches = 0;

            // Win11 tabbed Explorer keeps every open tab's shell-view pane
            // WS_VISIBLE at the same time (only z-order/clipping actually
            // distinguishes the one you're looking at) — IsWindowVisible
            // can't tell tabs apart. Keyboard focus can: whichever tab is
            // frontmost owns the focused child control on the window's UI
            // thread, so match tab HWNDs against that instead.
            IntPtr focused = NativeMethods.GetFocusedControl(explorerHwnd);

            dynamic windows = shell.Windows();
            foreach (dynamic w in windows)
            {
                try
                {
                    if ((long)w.HWND != target) continue;
                    matches++;
                    soleMatchDoc = w.Document;

                    IntPtr tabHwnd = GetTabHwnd(w);
                    if (tabHwnd != IntPtr.Zero && focused != IntPtr.Zero &&
                        (tabHwnd == focused || NativeMethods.IsChild(tabHwnd, focused)))
                    {
                        activeTabDoc = w.Document;
                        break;
                    }
                }
                catch { /* window vanished mid-enumeration */ }
            }

            // Single match (no tabs, or pre-Win11) → no active-tab test needed.
            dynamic? doc = activeTabDoc ?? (matches == 1 ? soleMatchDoc : null);
            if (doc is null) return result;

            dynamic items = doc.SelectedItems();
            if (items is null) return result;
            foreach (dynamic item in items)
            {
                try
                {
                    string? p = item.Path as string;
                    if (!string.IsNullOrEmpty(p)) result.Add(p);
                }
                catch { }
            }
        }
        catch
        {
            _shell = null; // stale RCW — rebuild next time
        }
        return result;
    }

    /// <summary>True if this window class is the desktop's icon surface.</summary>
    public static bool IsDesktopClass(string windowClass) =>
        windowClass is "Progman" or "WorkerW";

    /// <summary>Selected item paths on the desktop, or empty. Never throws.</summary>
    public static List<string> GetDesktopSelectedPaths()
    {
        var result = new List<string>();
        try
        {
            dynamic? shell = GetShell();
            if (shell is null) return result;

            var sw = (IShellWindows)shell.Windows();
            object? loc = null, root = null;
            object? view = sw.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH);
            if (view is null) return result;

            dynamic v = view;
            dynamic items = v.Document.SelectedItems();
            if (items is null) return result;
            foreach (dynamic item in items)
            {
                try
                {
                    string? p = item.Path as string;
                    if (!string.IsNullOrEmpty(p)) result.Add(p);
                }
                catch { }
            }
        }
        catch
        {
            _shell = null;
        }
        return result;
    }

    private static IntPtr GetTabHwnd(object shellWindow)
    {
        try
        {
            var sp = (IServiceProvider)shellWindow;
            sp.QueryService(ref SID_STopLevelBrowser, ref IID_IShellBrowser, out object browserObj);
            var browser = (IShellBrowser)browserObj;
            browser.GetWindow(out IntPtr hwnd);
            return hwnd;
        }
        catch { return IntPtr.Zero; }
    }
}
