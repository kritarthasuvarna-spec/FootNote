# Changelog (dev-facing)

Record of what was actually implemented, tested, fixed, or deferred per
release. User-facing notes live in `FootNote.App/Assets/PatchNotes.json`.

## 1.3.0 — 2026-09-25
- **New: search all notes.** A second global hotkey (default
  Ctrl+Shift+Alt+N, independently remappable in Settings) opens a floating
  search box. Enter runs the search over every live note's text and
  filename (`FootNote.Core/NotesSearch.cs`, filtering `NotesBackup.LoadAll()`
  — no new index needed), ranked filename-matches-first then by recency,
  capped at 50 results with a highlighted-match snippet. Clicking or
  pressing Enter on a result reveals and selects that file/folder in
  Explorer via a new `SHParseDisplayName`/`SHOpenFolderAndSelectItems`
  wrapper (`NativeMethods.RevealInExplorer`) and closes the box.
  - `HotkeyManager` now takes its RegisterHotKey ids via constructor
    instead of hardcoded consts, so two independent hotkeys can coexist.
  - Search window (`SearchBar.xaml`) opens with the pill-shaped field
    animating from a centered circular icon button out to full width
    (the icon rides along, visually sliding from center to its final
    left-inset position as the shape expands), holds briefly, then a
    short blink + typewriter reveal fills the placeholder — all skipped
    instantly the moment the user actually types.
  - Fixed a real bug found in live testing: Escape sometimes failed to
    close the search box (a known WPF quirk where an Alt-chorded hotkey
    can leave Escape reporting as `Key.System`/`SystemKey` instead of
    plain `Key.Escape`); now checks both.
- Fixed notes on **folders** not redisplaying after being saved:
  `AdsHelper.ReadHistory` was still using `File.ReadAllText` directly,
  which fails on a directory-rooted ADS stream without
  `FILE_FLAG_BACKUP_SEMANTICS` — the existence check (`StreamExists`) had
  already been patched for this same directory-handle limitation, but the
  actual content read never got the equivalent fix. Added a native
  `CreateFileW`-based read path for directories, files unaffected.

## 1.2.1 — 2026-09-07 (Store channel only, no GitHub release)
- Gave `.footnote` sidecar files a custom Explorer icon (reuses the app's
  own icon via a `DefaultIcon` registry entry on unpackaged builds, a
  declarative `windows.fileTypeAssociation` manifest extension on MSIX) —
  purely cosmetic, no change to double-click behavior; the files stay
  exactly as inert as before.
- Store MSIX identity bumped 1.2.0.0 → 1.2.1.0 for this change.

## 1.2.0 — 2026-09-03
- New brand identity locked in (`docs/brand/Brand_Identity.md`): the
  "Bleeding Drop" mark — a radial-gradient circle with a soft blurred
  trailing ellipse — replaces the old 📝 emoji used as a placeholder icon.
  - Generated a proper multi-resolution `footnote.ico` (16/32/48/128/256)
    with a one-off render tool (`tools/icongen`, GDI+ — no SVG rasterizer
    was available locally). First pass used PNG-compressed frames for
    every size and rendered distorted in the system tray; fixed by
    switching sizes ≤48 to raw 32bpp DIB data (some icon loaders, including
    the one behind `Icon.ExtractAssociatedIcon`, don't handle small PNG
    frames well) and keeping PNG only for 128/256.
  - Added a shared `BrandMarkBrush` resource (both `FootNote.App` and
    `FootNote.Setup` App.xaml, since they're separate WPF applications) and
    replaced all four 📝 usages (overlay bar header, Settings preview,
    Tutorial, Setup welcome screen) with a small gradient `Ellipse` using it.
  - Fixed two leftover "look for the tag icon" strings (single-instance
    dialog, moved-install toast) — stale copy from the retired FileTag
    icon direction.
  - README hero now shows the wordmark SVG (light/dark variants committed
    under `docs/brand/`) instead of an emoji heading.
  Palette/typography from the brand doc already matched what's live
  (Ink `#4F8EF7`, dark-native surfaces, Source Serif italic reserved for
  comment text) — no further changes needed there.

## 1.1.5 — 2026-09-03
- Fixed the same root cause for the Delete confirmation, reported by the
  user right after 1.1.4 shipped (Edit → Discard → Delete could silently
  reset back to Read instead of showing the confirm prompt). `Evaluate()`
  only guarded against clobbering `IsEditing` state; it had no equivalent
  guard for `ConfirmDelete`, so the same racy UIA re-evaluation that
  affected the ✕ button could call `ShowRead` mid-confirmation and reset
  the bar out from under the confirm prompt.
  Added `OverlayBar.IsConfirmingDelete` and a matching guard in
  `Evaluate()`, mirroring the existing `IsEditing` one: while confirming a
  delete for the currently-selected file, re-evaluation is skipped
  entirely, the same shape as the 1.1.4 fix but generalized instead of
  tied to one specific button. Verified live: the confirm prompt now
  appears and stays on the very first Delete click, and Cancel still
  returns cleanly to Read mode.

## 1.1.4 — 2026-09-03
- Actually fixed the two-click Close bug (1.1.3's `SetWindowPos`/NOACTIVATE
  fix was real but wasn't the cause). Root-caused this time with
  `FOOTNOTE_DEBUG=1` logging and a precise repro (fresh app start, plain
  click to select a file with a note — not the hotkey — then click ✕):
  the log showed `CloseButton_Click` firing and `HideBar()` running
  correctly on the *first* click, but ~150ms later `Evaluate()` fired again
  (the click on the bar itself still triggers a UIA event `ExplorerWatcher`
  picks up, which restarts the 120ms debounce), saw the same file still
  selected and still commented, and called `ShowRead` right back —
  undoing the close before the user could perceive it as anything but
  "the first click didn't work."
  Fix: `OverlayBar` now raises a new `Dismissed` event from the ✕ handler
  with the closed path; `App.xaml.cs` tracks it as `_dismissedPath` and
  `Evaluate()` skips re-showing that exact path until the selection
  actually moves to something else (mirrors the existing
  `_pendingDeletePath` suppression pattern). Verified via the debug log:
  the racy re-evaluation still fires, but no longer reaches `ShowRead`;
  reselecting the same file afterward shows it normally again.

## 1.1.3 — 2026-09-03
- Fixed a real bug reported by the user: the first time you closed a note
  after starting FootNote, the Close button (and by extension any bar
  dismissal) needed two clicks instead of one — every later close in the
  same session worked in one click. Root cause: `SetNoActivate()` toggles
  `WS_EX_NOACTIVATE` via `SetWindowLong`, but per the Win32 docs a style set
  this way is cached and does not take effect until a `SetWindowPos` call —
  without one, the window's very first `Show()` after the style was set
  could still activate/steal focus once, before `Reposition()`'s own
  `SetWindowPos` call (which happens after `Show()`) ever flushed it. Fixed
  by having `SetNoActivate()` immediately follow its `SetWindowLong` call
  with a `SetWindowPos(..., SWP_FRAMECHANGED)` to force the style to apply
  right away, every time.
  Added `NativeMethods.cs`: `SWP_NOMOVE`, `SWP_NOSIZE`, `SWP_NOZORDER`,
  `SWP_FRAMECHANGED`.

## 1.1.2 — 2026-09-03
- Fixed a real layout bug reported by the user: enabling ink bloom made the
  whole bar taller. Root cause: the 220px/90px bloom ellipses were direct
  children of the bar's Grid, spanning its two Auto rows — a Grid inflates
  Auto rows to fit a spanning child's desired size, so once the ellipses
  became Visible they grew the rows (and the window, which is
  SizeToContent="Height") well past the card's normal height, even though
  the ellipses were later clipped to the card's rounded corner at render
  time. Fix: moved each bloom layer into its own zero-size `Canvas`
  (`Width="0" Height="0"`) — a Canvas never reports its children's size to
  its parent, so the ellipses can be visible and positioned via
  `Canvas.Left`/`Canvas.Top` without influencing the Grid's row sizing at
  all. Verified with a pixel-identical before/after: the card's top edge
  lands at the same y-coordinate in both bloom-on and bloom-off states.
- Ink bloom now defaults to on for new installs (`BloomEnabled = true`,
  already shipped in 1.1.1) — confirmed still in effect.

## 1.1.1 — 2026-09-03
- Fixed a real rendering bug: the ink bloom showed as a hard-edged blue
  rectangle with the desktop/Explorer visibly bleeding through the card,
  reported live by the user. Root cause: `RootBorder` carried both the
  slide animation's `RenderTransform` and the new corner-clip `Clip` while
  hosting `BlurEffect` descendants — WPF renders an Effect as hard-edged
  instead of blurred when an ancestor has both a Clip and a RenderTransform.
  Fix: moved `RenderTransform` onto a new unstyled `SlideWrapper` Border so
  `RootBorder` only carries the `Clip`, decoupling the two.
- Ink bloom flipped to on-by-default (`BloomEnabled = true`), with preset
  swatches added next to the existing hex box in Settings → Panel, matching
  the accent-color picker's pattern.

## 1.1.0 — 2026-09-03
- Ink bloom: two blurred radial-gradient layers behind the card content
  (220px/34px blur top-left, 90px/20px blur bottom-right), clipped to the
  card's own rounded corner via a RectangleGeometry clip recalculated on
  resize (Border doesn't clip children to its rounded corners on its own).
  Global, driven by two new settings (`BloomEnabled`, `BloomColor`) exposed
  in Settings → Panel, off by default. Originally built as a per-note color
  tag with 3 presets and a serif comment font; both were pulled per
  feedback in favor of a single global toggle, and the comment font was
  reverted to the app-wide Manrope.

## 1.0.0 — 2026-09-02
- First public release of FootNote. Fresh version history starting here;
  prior internal builds (including the FileTag-branded ones) are folded
  into this release rather than tracked individually.
- Core: dual storage backend (NTFS alternate data stream on local drives,
  hidden companion file on Google Drive/OneDrive/non-NTFS drives), version
  history per note, cloud sync detection, folder notes.
  Overlay bar, global hotkey (Shift+Alt+N, remappable, conflict-checked),
  delete with 5-second undo, Recover Notes backup/restore.
  Settings window with live-apply position/appearance/behavior options,
  interactive first-run tutorial.
  Per-user Setup wizard and matching uninstaller, plain-text logging
  across install/app/uninstall, dark titlebar and Manrope font throughout.
