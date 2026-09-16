using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace FootNote.App;

/// <summary>
/// Per-user (HKCU, no admin) self-registration: Run-at-startup value and the
/// Apps &amp; Features uninstall entry. Idempotent — re-run on every launch so the
/// registry always points at the current install location.
/// </summary>
internal static class InstallHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FootNote";
    private const string FootnoteExtKeyPath = @"Software\Classes\.footnote";
    private const string FootnoteProgId = "FootNote.sidecarfile";
    private const string FootnoteProgIdKeyPath = @"Software\Classes\" + FootnoteProgId;
    private const string AppName = "FootNote";

    public static string ExePath => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

    /// <summary>Install folder currently recorded in Apps &amp; Features, or null.</summary>
    public static string? ReadRegisteredLocation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
            return key?.GetValue("InstallLocation") as string;
        }
        catch { return null; }
    }

    public static string StartMenuShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "FootNote.lnk");

    public static void RegisterAll(bool startWithWindows)
    {
        try
        {
            SetStartup(startWithWindows);
            if (MsixEnvironment.IsPackaged) return; // MSIX already owns the Apps & Features entry and Start Menu tile
            WriteUninstallEntry();
            CreateStartMenuShortcut();
            RegisterSidecarIcon();
        }
        catch { /* registry unavailable — app still functions this session */ }
    }

    /// <summary>Gives .footnote sidecar files the app's own icon in Explorer
    /// instead of Windows' generic blank one — purely cosmetic. Verified live:
    /// a bare DefaultIcon directly under ".footnote" with no ProgID is
    /// silently ignored by Explorer — the shell only resolves DefaultIcon
    /// through a ProgID chain (.ext → ProgID → ProgID\DefaultIcon). So this
    /// registers a minimal ProgID with ONLY a DefaultIcon subkey — no
    /// shell\open\command, no verbs — so double-click still falls through to
    /// Windows' normal "no app associated" prompt, unchanged.</summary>
    private static void RegisterSidecarIcon()
    {
        try
        {
            using var ext = Registry.CurrentUser.CreateSubKey(FootnoteExtKeyPath);
            if (ext.GetValue(null) as string != FootnoteProgId)
                ext.SetValue(null, FootnoteProgId);

            using var progId = Registry.CurrentUser.CreateSubKey(FootnoteProgIdKeyPath);
            using var iconKey = progId.CreateSubKey("DefaultIcon");
            string desired = $"{ExePath},0";
            if (iconKey.GetValue(null) as string != desired)
            {
                iconKey.SetValue(null, desired);
                NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch { }
    }

    /// <summary>Start Menu entry — makes the install feel standard rather than
    /// tray-only. Launching it while running just hits the second-instance message.</summary>
    public static void CreateStartMenuShortcut()
    {
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t is null) return;
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic sc = shell.CreateShortcut(StartMenuShortcutPath);
            sc.TargetPath = ExePath;
            sc.WorkingDirectory = Path.GetDirectoryName(ExePath);
            sc.IconLocation = ExePath;
            sc.Description = "FootNote — notes on your files";
            sc.Save();
        }
        catch { }
    }

    /// <summary>The one entry point for toggling startup, at install time or
    /// from the tray menu — routes to the packaged StartupTask API or the
    /// unpackaged Run-key write depending on how this process is running.</summary>
    public static void SetStartup(bool enabled)
    {
        if (MsixEnvironment.IsPackaged)
        {
            _ = MsixEnvironment.TrySetStartupAsync(enabled); // fire-and-forget, same as RegisterAll
            return;
        }
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled) key.SetValue(AppName, $"\"{ExePath}\"");
            else key.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch { }
    }

    private static void WriteUninstallEntry()
    {
        string dir = Path.GetDirectoryName(ExePath)!;
        string uninstaller = Path.Combine(dir, "Uninstall.exe");
        string version = typeof(InstallHelper).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", AppName);
        key.SetValue("DisplayIcon", ExePath);
        key.SetValue("InstallLocation", dir);
        key.SetValue("UninstallString", $"\"{uninstaller}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        try
        {
            long bytes = Directory.EnumerateFiles(dir).Sum(f => new FileInfo(f).Length);
            key.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
        }
        catch { }
    }
}
