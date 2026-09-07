# Sanity Check — AppTier gating + build layout (2026-09-06)

## What was checked

1. Does the Free build actually lack the gated features, and does the
   Pro/Owner build actually have them — verified live, not just by reading
   code.
2. Does the reorganized `build.ps1` produce correct, non-colliding output
   for both tiers.

## 1. Feature gating

| Check | Method | Result |
|---|---|---|
| Settings → Position/Appearance/Panel hidden in Free | Launched fresh Free build, opened Settings, screenshotted | **Pass** — those three sections collapse to a single "Panel position, accent color, panel style, and ink bloom color are Pro features" card. Behavior and Hotkey sections remain, editable. |
| Settings → Position/Appearance/Panel shown in Pro | Same, Pro/Owner build | **Pass** — all three sections present and functional (screenshotted: accent swatches, panel style radios, corner radius slider, bloom color picker all visible). |
| Recover Notes hidden from tray menu in Free | Code inspection (`TrayIcon.cs`: `if (ProFeatures.Current.Unlocked) menu.Items.Add(...)`) | **Pass by construction** — same `Unlocked` flag already proven `false` in Free via the Settings check above, so the identical guard here necessarily behaves the same. Not separately re-screenshotted this pass (already confirmed in the prior session). |
| No backup file written in Free | Code inspection (`NullProFeatures.RecordSave`/`RecordDelete` are empty method bodies; `StorageRouter.Save`/`Delete` call only `ProFeatures.Current.RecordSave`/`RecordDelete`, never `NotesBackup` directly) | **Pass by construction** — a no-op method cannot write a file. Did not attempt a live delete-and-recheck test against `%LocalAppData%\FootNote\notes-backup.json.gz` this pass, since that path is the real production install's data, not a scratch copy — re-verifying live would require the same backup/restore discipline used elsewhere in this project, and the code-path proof here is already conclusive without that risk. |

**One caveat carried over from the original implementation, not re-litigated here:** Free-tier settings (accent color, panel style, etc.) are hidden in the UI but not forcibly reset — if `settings.json` already contains customized values (e.g. from a prior Pro session), those values still apply visually even though the picker is gone. Enforcing hard defaults at runtime for Free was scoped out as a deliberate follow-up, not an oversight.

## 2. Build output layout

Ran both tiers fresh through the reorganized `build.ps1`:

```
.\build.ps1 -Version 1.2.0 -Tier Free
.\build.ps1 -Version 1.2.0 -Tier Pro
```

| Check | Result |
|---|---|
| Both builds succeed | **Pass** — 0 errors each |
| Outputs land in separate, predictable folders | **Pass** — `dist\Free\v1.2.0\` and `dist\Owner\v1.2.0\`, no filename collision even though both are version 1.2.0 |
| Transient build state doesn't leak into `dist\` | **Pass** — `dist\_work\` (staging for the App/Uninstaller/Setup publish steps) is created fresh and deleted at the end of every run; confirmed absent after both builds completed |
| Old, pre-reorg artifacts don't clutter the working tree | **Pass** — everything from before this layout existed (all FileTag-era builds, FootNote v1.0.0–v1.1.5, v5.6.0–v5.7.3) moved into `dist\_archive\`, untouched but out of the way |

## Current `dist\` layout

```
dist\
  Free\v1.2.0\FootNote-Setup-v1.2.0.exe
  Free\v1.2.0\FootNote-v1.2.0-win-x64.zip
  Owner\v1.2.0\FootNote-Setup-v1.2.0-Owner.exe
  Owner\v1.2.0\FootNote-v1.2.0-Owner-win-x64.zip
  _archive\            (everything built before this session, kept for reference)
  _work\                (transient — doesn't exist between builds)
```

`dist\` itself stays gitignored, as before — this reorganization is purely a
local-disk convenience, nothing here affects what's tracked in git.

## Outcome

Both the gating logic and the reorganized build pipeline check out. No
regressions found. Nothing was pushed or committed — this was verification
only, plus the local folder cleanup requested alongside it.

## Not yet built (unchanged from before this check)

- Version-history browser UI (genuinely new Pro feature)
- MSIX packaging for the Store track
- `Windows.Services.Store` purchase integration
- The `FootNote.Pro` project split (deliberately not created yet — see the
  reasoning in the prior turn: nothing to relocate until there's a
  genuinely new, never-public feature to protect)
