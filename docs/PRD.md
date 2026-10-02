# FootNote — Product Requirements Document

**Version covered:** 1.3.1 (GitHub, built and ready, not yet published) / MSIX identity 1.3.2.0 (Microsoft Store, built and ready, not yet submitted); live today: GitHub 1.3.0, Store 1.3.1.0 (Submission 5 with 1.3.2.0 staged)
**Date:** 2026-10-03
**Status:** Beta — feature-complete, used daily by one person on one machine, no crash telemetry yet

---

## 1. Product summary

FootNote is a Windows utility that attaches a short personal note directly to any file or folder, with no database, no account, and no separate app window to manage. The note lives inside or beside the file itself, so it survives renames, moves, and even syncs across machines through the user's existing cloud storage (Google Drive, OneDrive) — because the note travels with the file, not with a service FootNote runs itself.

**The problem it solves:** files accumulate context that doesn't fit in a filename — why a version was kept, what changed, what still needs doing, which draft is the real one. That context normally lives in someone's memory, a separate notes app, or a filename suffix like `_FINAL_v2_actually_final`. FootNote gives it a permanent, visible home attached to the file.

**Target user:** anyone managing a personal or shared file system where "which file is this, and why does it matter" is a recurring question — freelancers, generalists, small teams passing files around, anyone with a Downloads folder that's turned into an archive.

---

## 2. Core functionality

- **Attach a note:** select a file or folder in Explorer (or on the desktop), press a global hotkey (default **Shift+Alt+N**), type a note (up to 500 characters), save.
- **Auto-show on reselect:** selecting a file that already has a note displays it automatically in a small overlay window docked to a screen edge — no need to remember a shortcut to check it.
- **Version history:** every save appends to a history capped at 20 entries; only the latest is shown by default, older ones are recoverable (Pro).
- **Storage, fully automatic and per-file:**
  - Local NTFS drives → an NTFS alternate data stream on the file itself (invisible, `filename:FootNote.txt`).
  - Cloud-synced folders (Google Drive, OneDrive) or non-NTFS drives (FAT32/exFAT) → a hidden companion file (`report.xlsx` → `report.xlsx.footnote`), since alternate data streams don't survive most sync engines.
  - Routing between the two is automatic and invisible to the user.
- **No database, no source of truth outside the file/sidecar itself.** A local index (`index.json`, paths only) and a local gzip backup (`notes-backup.json.gz`, full text) exist purely as caches/recovery aids — losing either loses nothing the user wrote, by design.

---

## 3. Full feature list (current as of 1.3.0 / Submission 4)

### Everyday use
- Global hotkey, remappable, with live conflict detection against other registered hotkeys.
- Overlay bar: read / edit / confirm-delete states, slide in/out animation, docks to a chosen screen edge.
- **Search all notes** *(new in 1.3.0)*: a second, independently remappable global hotkey (default **Ctrl+Shift+Alt+N**) opens a floating search box. Typing a query and pressing Enter searches every live note's text and filename, ranked filename-matches-first then by recency, capped at 50 results with a highlighted snippet. Clicking or pressing Enter on a result reveals and selects that exact file/folder in Explorer via `SHParseDisplayName`/`SHOpenFolderAndSelectItems`, then closes the search box.
- Settings window: position (screen edge, monitor), full appearance (accent color, panel style/color/corner radius/size, translucency), ink bloom (soft colored glow behind the note card — on/off + color picker, on by default), behavior (auto-hide timing, slide animation on/off), independent remapping for both hotkeys.
- Interactive first-run Tutorial with a demo animation, permanently re-accessible from the tray menu.
- What's New / Patch Notes shown after an update.
- Custom Explorer icon on `.footnote` sidecar files (the app's own "Bleeding Drop" mark) so they read as intentional, not as junk — purely cosmetic, no change to double-click behavior; files stay exactly as inert as before.

### Recovery and safety
- **Recover Notes** (Pro): lists notes from the local backup that no longer have a live copy (file moved/deleted after the note was written), one-click restore.
- **History** (Pro): browse and restore any earlier version of a note, not just the latest.
- Delete with a 5-second undo window.
- Uninstaller removes the app and strips every note it ever created (both ADS and companion files) back out of the user's own files — nothing is left behind, orphaned, or silently kept.

### Setup and distribution
- Per-user Setup wizard, no admin prompt, detects and updates an existing install in place.
- Portable `.zip` alternative alongside the installer.
- Passive GitHub-releases version check on startup — the only outbound network call the free/sideload build makes.

---

## 4. Editions and monetization

FootNote ships as one codebase with a compile-time tier/channel split (not a runtime flag — nothing to unlock by tampering with a public build):

| Build | Distribution | Unlock model | Gated features |
|---|---|---|---|
| **Free** | GitHub Releases | Always locked (Pro features simply absent) | No Recover Notes, no History, appearance customization replaced with an upsell notice |
| **Pro / Owner** | Sideload only, never distributed | Always unlocked, no Store calls | Everything unlocked (personal build) |
| **Pro / Store** | Microsoft Store | Real in-app purchase ("Unlock Full Version", $2.99 one-time) via `Windows.Services.Store` | Unlocked only after genuine purchase |

Features universal to every tier: hotkey note-attach, auto-show on reselect, search all notes, automatic storage routing, the overlay bar, Settings' position/behavior/hotkey sections, the Tutorial, and the uninstaller's full cleanup.

Pricing, availability, and age rating in Partner Center: Free base app, USD, all ~240 worldwide markets, public and discoverable; add-on priced at $2.99 one-time. No telemetry, no accounts, no personal data collected — reflected accurately in the Store's privacy questionnaire and `docs/Privacy_Policy.md`.

---

## 5. Distribution channels (state as of 2026-09-27)

- **GitHub Releases** (`kritarthasuvarna-spec/FootNote`, public, MIT licensed): **v1.3.0 is live**. **v1.3.1 is built and ready** (installer and portable zip in `dist/Release_1.3.1/`, release notes prepared) but not yet published.
- **Microsoft Store** ("Footnote: Comments for Files"): **MSIX identity 1.3.1.0 (Submission 4) is currently live**. It has the first Explorer multi-tab fix but not the focus-gap fix or the stray-copy protection. **MSIX identity 1.3.2.0** (app 1.3.1) is uploaded, validated and saved in **Submission 5** with the updated listing text, waiting only for "Submit for certification". This section goes stale the moment that happens; treat the Partner Center dashboard as the source of truth for exact live status.
- The GitHub build's own version number and the Store MSIX identity version are intentionally independent tracks (established convention, see `docs/Store_Submission_Report_2026-09-07.md`) — they're kept numerically aligned when a release covers both channels, but nothing requires that.

---

## 6. Tech stack

- .NET 8, WPF, WinForms interop (tray `NotifyIcon`, screen enumeration).
- `build.ps1` publishes the Free/GitHub artifacts (framework-dependent single-file app, trimmed self-contained uninstaller, self-contained Setup wizard with the app zipped in as its payload).
- `FootNote.Msix/package-msix.ps1` publishes the MSIX/Desktop Bridge package for Store or Owner-sideload use, self-signing with a local test certificate for sideload testing (Partner Center does its own signing for the real Store submission).
- No server, no accounts, no telemetry anywhere in the product.

---

## 7. Recently resolved (this development cycle)

- **Folder notes not redisplaying after save** — `AdsHelper.ReadHistory` was using a plain `File.ReadAllText` that can't open a stream rooted at a directory; fixed with a native `CreateFileW`-based read path using `FILE_FLAG_BACKUP_SEMANTICS`, mirroring the fix `HasComment` already had for the same directory-handle limitation.
- **Explorer multi-tab selection bug** — with more than one tab open in an Explorer window (a very common Windows 11 pattern), the app could silently read the wrong tab's selection, so the overlay bar failed to show an existing note and the hotkey couldn't tell what was selected. Root cause: the active-tab detection relied on `IsWindowVisible`, but Windows 11 keeps every open tab's shell-view pane marked visible simultaneously — only z-order actually distinguishes the one in view. Fixed by matching each tab's window handle against the window's actual focused control instead. Verified live against the real Shell.Application COM object with 3 tabs open, before and after.
- **Stray copies hijacking registration (1.3.1)** — every launch used to repoint the Start Menu shortcut, startup entry and Apps and Features entry at the running exe, so a stale test copy could take over. A copy now only registers if nothing valid is registered, the registered install is gone, or it is the registered copy. The Explorer tab detection also gained a z-order fallback for when keyboard focus is outside the file list.
- **No crash visibility** — an unhandled exception anywhere previously killed the process with zero trace. Added always-on crash logging (`AppDomain.UnhandledException` / `DispatcherUnhandledException` → the existing `Logger`), independent of the opt-in `FOOTNOTE_DEBUG` diagnostic flag.

---

## 8. Known limitations / non-goals

- Windows only — no mobile, no web, no macOS/Linux port planned.
- No notes storage hosted by FootNote itself; multi-device sync rides entirely on whatever cloud-sync folder the user already has (Google Drive, OneDrive). No sync for local-NTFS-drive notes across machines that don't share a cloud folder.
- Beta status: feature-complete and used daily, but tested by one person on one machine so far — no crash telemetry, edge cases still surface periodically (see Section 7).
- No per-note color tags or presets — ink bloom is a single global on/off + color setting, not a per-note property.
- No context-menu entry, shell extension, or Explorer icon overlay for noted files themselves (only the sidecar `.footnote` file gets a custom icon, and only when the sidecar backend is in use).
