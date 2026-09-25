using System.IO;
using System.Windows;
using System.Windows.Threading;
using FootNote.App.Settings;
using FootNote.Core;

namespace FootNote.App;

/// <summary>
/// Coordinator. Owns all components and the one evaluation routine that decides
/// what the overlay bar shows for the current Explorer selection.
/// </summary>
public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private IndexStore _index = null!;
    private OverlayBar _bar = null!;
    private TrayIcon _tray = null!;
    private ToastManager _toasts = null!;
    private HotkeyManager _hotkey = null!;
    private HotkeyManager _searchHotkey = null!;
    private SearchBar? _searchBar;
    private ExplorerWatcher _watcher = null!;
    private DispatcherTimer _debounce = null!;
    private SettingsWindow? _settingsWindow;
    private string _appliedHotkey = "";
    private string _appliedSearchHotkey = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if FOOTNOTE_PRO
        ProFeatures.Current = FootNote.Pro.ProFeaturesFactory.Create();
        // Blocking, not fire-and-forget: TrayIcon and Settings are built
        // synchronously right after this and both read Unlocked immediately
        // (e.g. to decide whether "Recover Notes…" belongs in the tray
        // menu) — showing the wrong menu for a moment and then silently
        // changing it under the user is worse than a brief startup delay.
        // The Owner build's InitializeAsync completes instantly (no Store
        // call); only the Store channel actually waits on anything here.
        ProFeatures.Current.InitializeAsync().GetAwaiter().GetResult();
#endif

        // Uninstall mode: this exe hosts the uninstall wizard (launched by the
        // Uninstall.exe stub) so the wizard UI costs no extra WPF payload.
        if (e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            Logger.Init("Uninstall", Logger.UninstallLogPath);
            UninstallRunner.KillOtherInstances();
            if (e.Args.Any(a => a.Equals("/S", StringComparison.OrdinalIgnoreCase)))
            {
                var runner = new UninstallRunner();
                runner.Run();
                runner.ScheduleInstallFolderDeletion();
                Shutdown(0);
                return;
            }
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            new UninstallWindow().Show();
            return;
        }

        _mutex = new Mutex(true, "FootNote.App.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // Dying silently here reads as "the app is broken" — say something.
            System.Windows.MessageBox.Show(
                "FootNote is already running — look for the icon in the system tray.\n\n" +
                "To run this copy instead, exit the running one first (tray icon → Exit).",
                "FootNote", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Rebrand migration MUST run before anything (Logger, IndexStore, Settings)
        // touches the data folders — otherwise a freshly-created empty new folder
        // would make the migration think there's nothing to move.
        bool migratedFolders = RebrandMigration.MigrateDataFolders();
        RebrandMigration.CleanupLegacyRegistry();

        Logger.Init("App");
        Logger.Info($"FootNote {typeof(App).Assembly.GetName().Version?.ToString(3)} starting from {InstallHelper.ExePath}");
        if (migratedFolders) Logger.Info("migrated local data from a previous FileTag install");
        UninstallRunner.CancelPendingDeletionHere();
        ListenForExitRequests(); // lets Setup update in place with a graceful stop

        _index = new IndexStore();
        // Detect a new/changed install location before re-registering over it,
        // so the user gets visible feedback that "running the exe" worked.
        string? previousLocation = InstallHelper.ReadRegisteredLocation();
        string currentLocation = Path.GetDirectoryName(InstallHelper.ExePath)!;
        InstallHelper.RegisterAll(_index.StartWithWindows);
        SettingsService.Instance.Save(); // materialize defaults on first run

        _bar = new OverlayBar();
        _bar.SaveRequested += OnSaveRequested;
        _bar.DeleteConfirmed += OnDeleteConfirmed;
        _bar.Dismissed += path => _dismissedPath = path;

        _tray = new TrayIcon(_index, ExitApp, OpenSettings, OpenTutorial, OpenWhatsNew, OpenRecoverNotes);
        _toasts = new ToastManager(_tray);

        _hotkey = new HotkeyManager();
        _hotkey.Pressed += OnHotkey;
        ApplyHotkeyFromSettings(warnOnFailure: true);
        DebugLog.Write($"startup: hotkey registered={_hotkey.Registered}");
        Logger.Info($"hotkey {SettingsService.Instance.Current.HotkeyDisplay} registered={_hotkey.Registered}");

        _searchHotkey = new HotkeyManager(0xF120, 0xF121);
        _searchHotkey.Pressed += OnSearchHotkey;
        ApplySearchHotkeyFromSettings(warnOnFailure: true);
        DebugLog.Write($"startup: search hotkey registered={_searchHotkey.Registered}");
        Logger.Info($"search hotkey {SettingsService.Instance.Current.SearchHotkeyDisplay} registered={_searchHotkey.Registered}");

        Logger.Info($"cloud sync roots: {string.Join(" | ", CloudFolderDetector.GetRoots())}");

        SettingsService.Instance.SettingsChanged += () => ApplyHotkeyFromSettings(warnOnFailure: false);
        SettingsService.Instance.SettingsChanged += () => ApplySearchHotkeyFromSettings(warnOnFailure: false);

        // Selection events arrive in bursts from any thread; collapse them into
        // one evaluation ~120ms after the burst ends.
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Evaluate(); };
        _watcher = new ExplorerWatcher(() => Dispatcher.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); }));

        if (!_index.FirstRunShown)
        {
            // A balloon is too easy to miss — show the interactive tutorial.
            OpenTutorial();
            _index.FirstRunShown = true;
        }
        else if (!string.Equals(previousLocation, currentLocation, StringComparison.OrdinalIgnoreCase))
        {
            // Not a first run, but the app moved (fresh extract, new folder):
            // without this, launching the exe looks like "nothing happened".
            _tray.ShowBalloon("FootNote is running",
                $"Now installed at {currentLocation}. Look for the icon in the system tray.");
        }

#if !FOOTNOTE_STORE_CHANNEL
        // Store policy 10.2.5: a Store-distributed app must only update
        // through the Store — no checking GitHub, no linking out to it.
        _ = UpdateChecker.CheckAsync(_tray);
#endif
        _ = Task.Run(MigrateAndBackfillNotes);

        if (Environment.GetEnvironmentVariable("FOOTNOTE_OPEN_SETTINGS") == "1")
            OpenSettings(); // debug/test aid
        if (Environment.GetEnvironmentVariable("FOOTNOTE_OPEN_WHATSNEW") == "1")
            OpenWhatsNew();
        if (Environment.GetEnvironmentVariable("FOOTNOTE_OPEN_RECOVER") == "1")
            OpenRecoverNotes();
    }

    /// <summary>Runs once per launch on a background thread, per already-tagged file:
    /// (1) rebrand migration — moves a legacy FileTag-named stream/sidecar to its
    /// FootNote-named equivalent, and (2) mirrors the current history into the
    /// notes-backup safety net, so notes that predate that feature (or were never
    /// re-saved since) still get a recovery snapshot.</summary>
    private void MigrateAndBackfillNotes()
    {
        int migrated = 0;
        foreach (string path in _index.GetPaths())
        {
            try
            {
                if (StorageRouter.MigrateLegacy(path)) migrated++;
                var history = StorageRouter.ReadHistory(path);
                if (history?.Latest is not null) NotesBackup.RecordSave(path, history);
            }
            catch { }
        }
        if (migrated > 0) Logger.Info($"migrated {migrated} note(s) from legacy FileTag storage");
    }

    private void ApplyHotkeyFromSettings(bool warnOnFailure)
    {
        var s = SettingsService.Instance.Current;
        if (s.HotkeyDisplay == _appliedHotkey && _hotkey.Registered) return;
        bool ok = _hotkey.Apply(s.HotkeyCtrl, s.HotkeyShift, s.HotkeyAlt, s.HotkeyKey);
        if (ok) _appliedHotkey = s.HotkeyDisplay;
        else if (warnOnFailure) _toasts.HotkeyConflict(s.HotkeyDisplay);
    }

    private void ApplySearchHotkeyFromSettings(bool warnOnFailure)
    {
        var s = SettingsService.Instance.Current;
        if (s.SearchHotkeyDisplay == _appliedSearchHotkey && _searchHotkey.Registered) return;
        bool ok = _searchHotkey.Apply(s.SearchHotkeyCtrl, s.SearchHotkeyShift, s.SearchHotkeyAlt, s.SearchHotkeyKey);
        if (ok) _appliedSearchHotkey = s.SearchHotkeyDisplay;
        else if (warnOnFailure) _toasts.HotkeyConflict(s.SearchHotkeyDisplay);
    }

    private void OnSearchHotkey()
    {
        try
        {
            if (_searchBar is null)
            {
                _searchBar = new SearchBar();
                _searchBar.RevealFailed += () => _toasts.RevealInExplorerFailed();
            }
            if (_searchBar.IsVisible) _searchBar.HideAndClear();
            else _searchBar.ShowFresh();
        }
        catch (Exception ex) { DebugLog.Write("search hotkey EX: " + ex); }
    }

    private RecoverNotesWindow? _recoverNotes;

    private void OpenRecoverNotes()
    {
        if (_recoverNotes is { IsLoaded: true }) { _recoverNotes.Activate(); return; }
        _recoverNotes = new RecoverNotesWindow(path => _index.AddPath(path));
        _recoverNotes.Show();
        _recoverNotes.Activate();
    }

    private PatchNotesWindow? _patchNotes;

    private void OpenWhatsNew()
    {
        if (_patchNotes is { IsLoaded: true }) { _patchNotes.Activate(); return; }
        _patchNotes = new PatchNotesWindow();
        _patchNotes.Show();
        _patchNotes.Activate();
    }

    private TutorialWindow? _tutorial;

    private void OpenTutorial()
    {
        if (_tutorial is { IsLoaded: true }) { _tutorial.Activate(); return; }
        _tutorial = new TutorialWindow();
        _tutorial.Show();
        _tutorial.Activate();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(new SettingsViewModel(_hotkey.Probe, _searchHotkey.Probe));
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Decide what the bar should show for the current foreground Explorer selection.</summary>
    private void Evaluate()
    {
        try
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == _bar.Handle) return; // user is interacting with the bar itself

            string cls = NativeMethods.GetWindowClass(fg);
            List<string>? sel = null;
            if (cls == "CabinetWClass") sel = ShellSelection.GetSelectedPaths(fg);
            else if (ShellSelection.IsDesktopClass(cls)) sel = ShellSelection.GetDesktopSelectedPaths();

            if (_bar.IsEditing)
            {
                // Unsaved typing is protected; an untouched editor is abandoned
                // once the selection moves to a different item.
                bool sameItem = sel is { Count: 1 }
                    && string.Equals(sel[0], _bar.CurrentPath, StringComparison.OrdinalIgnoreCase);
                if (_bar.IsEditingDirty || sameItem || sel is null) return;
            }

            if (_bar.IsConfirmingDelete)
            {
                // Same racy re-evaluation that affected the ✕ button: clicking
                // Delete (even though it never activates the bar) can still
                // trigger a UIA event that fires this method again almost
                // immediately. Without this guard, that re-evaluation sees the
                // same still-selected, still-commented file and calls ShowRead,
                // silently resetting the confirm prompt back to Read state —
                // so the first Delete click looked like it did nothing.
                bool sameItem = sel is { Count: 1 }
                    && string.Equals(sel[0], _bar.CurrentPath, StringComparison.OrdinalIgnoreCase);
                if (sameItem || sel is null) return;
            }

            if (sel is not null)
            {
                DebugLog.Write($"evaluate: fg={fg} cls={cls} sel=[{string.Join("; ", sel)}]");

                // A file the user just closed via ✕ stays suppressed only while
                // it's still the selection that triggered the close — once the
                // selection actually moves elsewhere, it's fair game again.
                bool stillDismissedItem = sel.Count == 1
                    && string.Equals(sel[0], _dismissedPath, StringComparison.OrdinalIgnoreCase);
                if (!stillDismissedItem) _dismissedPath = null;

                // Exactly one file or folder (multi-select is ambiguous). A file
                // mid-delete-grace-period still has its comment on disk, but must not
                // be shown or re-indexed as commented — that's what "reappears after
                // deleting" would look like to the user.
                bool pendingDelete = sel.Count == 1
                    && string.Equals(sel[0], _pendingDeletePath, StringComparison.OrdinalIgnoreCase);
                if (!pendingDelete && !stillDismissedItem && sel.Count == 1 && IsFileOrFolder(sel[0])
                    && !SidecarHelper.IsSidecar(sel[0]) && StorageRouter.HasComment(sel[0]))
                {
                    var note = StorageRouter.ReadLatest(sel[0]);
                    DebugLog.Write($"evaluate: note={(note is null ? "null" : "ok")} -> ShowRead");
                    if (note is not null)
                    {
                        _index.AddPath(sel[0]); // keep the cache warm as a side effect
                        _bar.ShowRead(sel[0], note, fg);
                        return;
                    }
                }
            }
            _bar.HideBar();
        }
        catch (Exception ex) { DebugLog.Write("evaluate EX: " + ex); }
    }

    private void OnHotkey()
    {
        try
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            string cls = NativeMethods.GetWindowClass(fg);
            DebugLog.Write($"hotkey: fg={fg} cls={cls}");

            // Allow the hotkey while the bar itself has focus (re-enter edit).
            if (fg == _bar.Handle && _bar.CurrentPath is not null)
            {
                _bar.ShowEdit(_bar.CurrentPath, StorageRouter.ReadLatest(_bar.CurrentPath), IntPtr.Zero);
                return;
            }

            // 1. Foreground selection: Explorer window or the desktop.
            List<string> sel = new();
            IntPtr anchor = IntPtr.Zero;
            if (cls == "CabinetWClass") { sel = ShellSelection.GetSelectedPaths(fg); anchor = fg; }
            else if (ShellSelection.IsDesktopClass(cls)) { sel = ShellSelection.GetDesktopSelectedPaths(); anchor = fg; }
            DebugLog.Write($"hotkey: sel=[{string.Join("; ", sel)}]");

            if (sel.Count > 1) { _toasts.SelectSingleFile(sel.Count); return; } // multi-select is ambiguous

            string? path = sel.Count == 1 ? sel[0] : null;
            if (path is not null && SidecarHelper.IsSidecar(path)) return;    // never comment a sidecar
            if (path is not null && !IsFileOrFolder(path))
            {
                // Virtual/unresolvable item — log what it was and keep the
                // hotkey alive via the clipboard/picker chain, not a dead end.
                Logger.Warn($"hotkey: selection not resolvable as file/folder: \"{path}\"");
                path = null;
            }

            // 2. Nothing selected anywhere → a file copied to the clipboard counts.
            path ??= TryClipboardFile();

            // 3. Last resort: never a dead key — let the user pick any file.
            if (path is null)
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "FootNote — choose a file to comment",
                    CheckFileExists = true,
                };
                if (dlg.ShowDialog() != true) return;
                path = dlg.FileName;
            }

            if (!IsFileOrFolder(path) || SidecarHelper.IsSidecar(path)) return;

            CancelPendingDeleteFor(path); // re-editing revives a pending delete

            // Existing comment that can't be parsed/read → warn, don't overwrite blindly.
            var existing = StorageRouter.ReadLatest(path);
            if (existing is null && StorageRouter.HasComment(path))
            {
                _toasts.DataDamaged();
                return;
            }

            // Any drive works now: NTFS gets an ADS stream, everything else
            // (FAT32 sticks, cloud-sync folders) gets a hidden sidecar file.
            _bar.ShowEdit(path, existing, anchor);
            _tutorial?.HotkeyVerified(); // completes the tutorial's verified step
        }
        catch (Exception ex) { DebugLog.Write("hotkey EX: " + ex); }
    }

    private static bool IsFileOrFolder(string path) =>
        File.Exists(path) || Directory.Exists(path);

    /// <summary>Exactly one existing file/folder on the clipboard (CF_HDROP), else null.</summary>
    private static string? TryClipboardFile()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsFileDropList()) return null;
            var files = System.Windows.Clipboard.GetFileDropList();
            if (files.Count == 1 && files[0] is string p && IsFileOrFolder(p)) return p;
        }
        catch { /* clipboard is flaky by nature */ }
        return null;
    }

    // ---- delete with undo grace period -------------------------------------
    // Confirming Delete hides the comment immediately, but the underlying
    // stream/sidecar deletion is held ~5s; Undo (the toast) just cancels it —
    // nothing has left the disk yet.
    private DispatcherTimer? _undoTimer;
    private string? _pendingDeletePath;

    /// <summary>Path the user just explicitly closed via the ✕ button. Clicking
    /// the bar (even though it never takes foreground) can still trigger a UIA
    /// event that fires Evaluate() again almost immediately — without this,
    /// that re-evaluation sees the same file still selected and still
    /// commented, and shows the bar right back, so the first Close click
    /// appears to do nothing. Cleared as soon as the selection actually moves
    /// to something else, so reselecting the same file later works normally.</summary>
    private string? _dismissedPath;

    private void OnDeleteConfirmed(string path)
    {
        CommitPendingDelete(); // a previous pending delete commits first
        _pendingDeletePath = path;
        // Index entry goes NOW, not when the grace period closes — keeps the
        // uninstaller's live "N tagged files" count accurate during the window.
        _index.RemovePath(path);
        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _undoTimer.Tick += (_, _) => { _undoTimer!.Stop(); CommitPendingDelete(); };
        _undoTimer.Start();
        _bar.HideBar();
        _tray.ShowBalloon("Comment deleted", "Click here to Undo", onClick: UndoPendingDelete);
        Logger.Info($"delete pending (5s undo): {path}");
    }

    private void CommitPendingDelete()
    {
        _undoTimer?.Stop();
        if (_pendingDeletePath is null) return;
        string p = _pendingDeletePath;
        _pendingDeletePath = null;
        try
        {
            StorageRouter.Delete(p);
            _index.RemovePath(p);
            Logger.Info($"delete committed: {p}");
        }
        catch (Exception ex) { Logger.Error($"delete failed: {p} — {ex.Message}"); }
    }

    private void UndoPendingDelete()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_pendingDeletePath is null) return; // window expired
            Logger.Info($"delete undone: {_pendingDeletePath}");
            _undoTimer?.Stop();
            _index.AddPath(_pendingDeletePath); // restore the index entry with the note
            _pendingDeletePath = null;
            _tray.ShowBalloon("FootNote", "Comment restored.");
        });
    }

    private void CancelPendingDeleteFor(string path)
    {
        if (string.Equals(_pendingDeletePath, path, StringComparison.OrdinalIgnoreCase))
        {
            _undoTimer?.Stop();
            _index.AddPath(path);
            _pendingDeletePath = null;
            Logger.Info($"pending delete cancelled by new activity: {path}");
        }
    }

    private void OnSaveRequested(string path, string text)
    {
        try
        {
            CancelPendingDeleteFor(path); // saving over a pending delete keeps the new comment
            StorageRouter.Save(path, text); // empty text deletes the comment
            if (string.IsNullOrWhiteSpace(text))
            {
                _index.RemovePath(path);
                Logger.Info($"comment removed: {path}");
                _bar.CompleteSave(null);
            }
            else
            {
                _index.AddPath(path);
                Logger.Info($"comment saved ({StorageRouter.RouteFor(path).GetType().Name}): {path}");
                _bar.CompleteSave(StorageRouter.ReadLatest(path));
            }
        }
        catch (Exception ex)
        {
            _toasts.SaveFailed(ex.Message);
        }
    }

    public const string ExitEventName = "FootNote.App.ExitRequest";
    private EventWaitHandle? _exitEvent;

    private void ListenForExitRequests()
    {
        try
        {
            _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            var t = new Thread(() =>
            {
                _exitEvent.WaitOne();
                Logger.Info("graceful exit requested (updater)");
                Dispatcher.BeginInvoke(ExitApp);
            }) { IsBackground = true, Name = "FootNote.ExitListener" };
            t.Start();
        }
        catch { /* updater falls back to killing the process */ }
    }

    private void ExitApp()
    {
        CommitPendingDelete(); // don't lose an intended delete on exit
        Logger.Info("exit via tray menu");
        _watcher?.Dispose();
        _hotkey?.Dispose();
        _searchHotkey?.Dispose();
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
