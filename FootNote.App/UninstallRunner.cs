using System.Diagnostics;
using System.IO;
using FootNote.Core;
using Microsoft.Win32;

namespace FootNote.App;

/// <summary>
/// The actual uninstall work, UI-free so the wizard and the /S test path share
/// it. Order (spec): strip comments (both backends) → registry cleanup →
/// local data → install folder (scheduled — this exe lives inside it).
/// The log goes to %TEMP%, never the install folder: a log inside the folder
/// being deleted would erase its own record on completion.
/// </summary>
internal sealed class UninstallRunner
{
    private readonly IndexStore _index = new();
    private readonly IReadOnlyCollection<string> _paths;

    public int Stripped { get; private set; }
    public int Skipped { get; private set; }
    public int TaggedFileCount => _paths.Count;

    public UninstallRunner()
    {
        _paths = _index.GetPaths();
    }

    public void Run(Action<int, int, string>? onProgress = null)
    {
        Logger.Info($"uninstall started — {_paths.Count} indexed path(s)");

        int i = 0;
        foreach (string path in _paths)
        {
            i++;
            onProgress?.Invoke(i, _paths.Count, path);
            try
            {
                bool had = StorageRouter.HasComment(path);
                StorageRouter.Delete(path);
                if (had) { Stripped++; Logger.Info($"stripped: {path}"); }
                else { Skipped++; Logger.Warn($"skipped (moved/deleted): {path}"); }
            }
            catch (Exception ex)
            {
                Skipped++;
                Logger.Warn($"skipped ({ex.Message}): {path}");
            }
        }

        SweepOrphanedSidecars(onProgress);

        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            run?.DeleteValue("FootNote", throwOnMissingValue: false);
            Logger.Info("startup entry removed");
        }
        catch (Exception ex) { Logger.Error("startup entry: " + ex.Message); }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FootNote", throwOnMissingSubKey: false);
            Logger.Info("Apps & Features entry removed");
        }
        catch (Exception ex) { Logger.Error("uninstall key: " + ex.Message); }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\.footnote", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\FootNote.sidecarfile", throwOnMissingSubKey: false);
            NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            Logger.Info("sidecar file icon association removed");
        }
        catch (Exception ex) { Logger.Error("sidecar icon key: " + ex.Message); }

        try
        {
            if (File.Exists(InstallHelper.StartMenuShortcutPath))
            {
                File.Delete(InstallHelper.StartMenuShortcutPath);
                Logger.Info("Start Menu shortcut removed");
            }
        }
        catch (Exception ex) { Logger.Warn("start menu shortcut: " + ex.Message); }

        try
        {
            // File-specific, never recursive: the data dir can coincide with the
            // install dir (default setup location) or contain user files.
            foreach (string f in new[] { "index.json", "debug.log", "notes-backup.json.gz" })
            {
                string p = Path.Combine(IndexStore.DataDirectory, f);
                if (File.Exists(p)) File.Delete(p);
            }
            try { Directory.Delete(IndexStore.DataDirectory); } catch { /* not empty — fine */ }
            Logger.Info("local index/config removed");
        }
        catch (Exception ex) { Logger.Warn("data dir: " + ex.Message); }

        try
        {
            string settingsDir = Path.GetDirectoryName(Settings.SettingsService.SettingsPath)!;
            bool existed = Directory.Exists(settingsDir);
            if (existed) Directory.Delete(settingsDir, recursive: true);
            Logger.Info($"settings/logs removed (dir: {settingsDir}, existed: {existed}, still-there: {Directory.Exists(settingsDir)})");
        }
        catch (Exception ex) { Logger.Warn("settings dir: " + ex.Message); }

        Logger.Info($"uninstall summary — stripped: {Stripped}, skipped: {Skipped}");
    }

    /// <summary>Marker whose presence authorizes the scheduled folder deletion.
    /// Launching or reinstalling FootNote into the folder removes it, which
    /// cancels a still-pending deletion instead of letting it eat the new files.</summary>
    public const string PendingDeletionSentinel = ".footnote-uninstall-pending";

    /// <summary>Safety net for notes the index never knew about (see
    /// <see cref="StorageRouter.SweepSidecars"/>).</summary>
    private void SweepOrphanedSidecars(Action<int, int, string>? onProgress)
    {
        foreach (string root in CloudFolderDetector.GetRoots())
        {
            Logger.Info($"sweeping sync root for orphaned notes: {root}");
            int n = StorageRouter.SweepSidecars(root, path =>
            {
                onProgress?.Invoke(_paths.Count, _paths.Count, path);
                Logger.Info($"stripped orphan: {path}");
            });
            Stripped += n;
            if (n > 0) Logger.Info($"swept {n} orphaned note(s) from {root}");
        }
    }

    /// <summary>The only files uninstall may delete from the install folder.
    /// A portable copy can live in a folder full of the user's other files —
    /// those are never FootNote's to touch, so cleanup is manifest-based and the
    /// folder itself is removed only if it is empty afterwards.</summary>
    private static readonly string[] OwnedFiles =
        ["FootNote.App.exe", "Uninstall.exe", "README.txt", "LICENSE.txt", "README.md",
         "debug.log", "index.json", "notes-backup.json.gz", PendingDeletionSentinel];

    /// <summary>Deletes FootNote's own files (and the folder, only if then empty)
    /// after this process exits — and only if the sentinel still exists at fire
    /// time (guards against a quick reinstall).</summary>
    public void ScheduleInstallFolderDeletion()
    {
        string installDir = AppContext.BaseDirectory.TrimEnd('\\');
        try
        {
            string sentinel = Path.Combine(installDir, PendingDeletionSentinel);
            File.WriteAllText(sentinel, DateTime.Now.ToString("o"));
            string dels = string.Join(" ", OwnedFiles.Select(f => $"\"{Path.Combine(installDir, f)}\""));
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c ping -n 4 127.0.0.1 >nul & if exist \"{sentinel}\" (del /f /q {dels} & rd \"{installDir}\")",
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            });
            Logger.Info($"cleanup of FootNote's own files scheduled in: {installDir} (folder removed only if empty)");
        }
        catch (Exception ex) { Logger.Error("folder cleanup: " + ex.Message); }
    }

    /// <summary>Called at normal app startup: cancels a pending deletion of the
    /// folder this app runs from (the user reinstalled or relaunched into it).</summary>
    public static void CancelPendingDeletionHere()
    {
        try
        {
            string sentinel = Path.Combine(AppContext.BaseDirectory, PendingDeletionSentinel);
            if (File.Exists(sentinel))
            {
                File.Delete(sentinel);
                Logger.Warn("cancelled a pending uninstall deletion of this folder");
            }
        }
        catch { }
    }

    /// <summary>Stops other running FootNote.App instances (not this one).</summary>
    public static void KillOtherInstances()
    {
        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("FootNote.App"))
        {
            try { if (p.Id != self) { p.Kill(); p.WaitForExit(3000); } }
            catch { }
            finally { p.Dispose(); }
        }
    }
}
