# FootNote — What's In The Build (v1.2.0)

## What it does

Attach a short text note (up to 500 characters) to any file or folder on
Windows. Select a file, press a hotkey, type a note. Selecting a file that
already has a note shows it automatically in an overlay bar.

Entry points: a global hotkey (default **Shift+Alt+N**, remappable), or
automatic display when a noted file is selected in Explorer or on the
desktop. No context menu entry, no shell extension, no Explorer icon
overlay.

## Storage

- No database. Each note lives inside or beside the file itself.
- **Local NTFS drives:** written into an NTFS alternate data stream
  (`filename:FootNote.txt`).
- **Cloud-synced folders (Google Drive, OneDrive) and non-NTFS drives
  (FAT32/exFAT):** written to a hidden companion file (`report.xlsx` →
  `report.xlsx.footnote`).
- Routing between the two backends is automatic, per file.
- Every save appends to a JSON version history capped at 20 entries. Only
  the latest entry is shown in the UI.
- Local index (`%LocalAppData%\FootNote\index.json`): file paths only, no
  note text — a cache, not a source of truth.
- Local backup (`%LocalAppData%\FootNote\notes-backup.json.gz`): a
  gzip-compressed mirror of every note ever saved.

## UI surfaces

- **Overlay bar** — read, edit, and confirm-delete states; docks to a
  screen edge; slide in/out animation.
- **Settings window** — position (screen edge, monitor), appearance (accent
  color, panel style/color/corner radius/size, translucency), ink bloom
  (on/off toggle + color picker, on by default), behavior (auto-hide
  timing, slide animation on/off), hotkey remap with conflict detection.
- **Tutorial** — first-run interactive walkthrough with a demo animation.
- **Recover Notes** — lists notes from the local backup that no longer have
  a live copy; one-click restore.
- **What's New / Patch Notes** — changelog shown after an update.
- **Setup wizard** — per-user install, no admin prompt, detects and updates
  an existing install in place.
- **Uninstaller** — removes the app and strips every note it created (both
  ADS and companion files) back out of the user's files.

## Ink bloom

A soft colored glow behind the note card. One global setting — on/off
toggle plus a color picker (hex box + swatches: Ink `#4F8EF7` default,
Moss `#5FB88A`, Ember `#E0714E`) in Settings → Panel. Not per-note; there
is no per-note color tag or preset picker.

## Brand mark

A "Bleeding Drop" icon — a radial-gradient circle with a soft blurred
trailing ellipse. Used as the app icon, the tray icon, and in the overlay
bar header, Settings preview, Tutorial, and Setup welcome screen.

## Distribution

- One free build, distributed via GitHub Releases.
- Repo `kritarthasuvarna-spec/FootNote`, public, MIT licensed.
- Installer: `FootNote-Setup-vX.Y.Z.exe` (self-contained single-file,
  ~75MB) plus a portable `.zip`. No admin rights required.
- Current version: 1.2.0, tagged as a GitHub pre-release.

## Tech stack

- .NET 8, WPF, WinForms interop (tray `NotifyIcon`, screen enumeration).
- `build.ps1` publishes three projects: `FootNote.App` (framework-dependent,
  single-file), `FootNote.Uninstaller` (self-contained, trimmed), and
  `FootNote.Setup` (self-contained, single-file, with the app and
  uninstaller zipped and embedded as its payload).
- No server, no accounts, no telemetry. One outbound network call: a
  passive GitHub-releases version check on startup.
