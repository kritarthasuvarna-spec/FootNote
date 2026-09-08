# FootNote — Microsoft Store Submission Report (2026-09-07)

Everything done in this work session, end to end: the build split, the MSIX
package, the Partner Center submission, and the git commit.

## 1. AppTier / Channel build split

Added a compile-time gate for paid features — a project reference, not a
runtime flag, so there is nothing to flip or recompile your way around from
public source.

- **`FootNote.Core/ProFeatures.cs`** (new, public): `IProFeatures` interface
  + `NullProFeatures` (the Free-build default — every Pro feature is simply
  absent) + a static `ProFeatures.Current` seam.
- **`FootNote.Pro/`** (new project, gitignored, never committed):
  - `LiveProFeatures` — Owner build, always unlocked, no Store calls.
  - `StoreProFeatures` / `StorePurchase.cs` — Store-Paid build, backed by
    `Windows.Services.Store` (real purchase flow, real license check).
  - `HistoryWindow` — the version-history browser UI (lists a note's saved
    history newest-first, "Restore this version").
- **`FootNote.App.csproj`**: `FootNoteTier` MSBuild property (`Free` default,
  `Pro` adds the `FOOTNOTE_PRO` define and references `FootNote.Pro`).
- **`FootNote.Pro.csproj`**: `FootNoteChannel` property (`Owner` default,
  `Store` adds `FOOTNOTE_STORE_CHANNEL`).
- Gated behind `ProFeatures.Current.Unlocked` throughout the Free app:
  - `TrayIcon.cs` — "Recover Notes…" only shown when unlocked.
  - `OverlayBar` — "History" button only shown for an existing note on an
    unlocked build.
  - `SettingsWindow` — Position/Appearance/Panel sections replaced with a
    one-line upsell notice when locked.
  - `StorageRouter.cs` — `RecordSave`/`RecordDelete` now route through
    `ProFeatures.Current` instead of calling `NotesBackup` directly, so the
    **Free build no longer gets local backup/recovery at all** — that's now
    a genuine Pro-only feature, matching the Store listing.
- **`MsixEnvironment.cs`** (new): packaged-vs-unpackaged detection +
  MSIX-native startup toggle via `Windows.ApplicationModel.StartupTask`
  (replaces the Run-key write, which is meaningless inside MSIX's registry
  virtualization). `InstallHelper.cs` branches on this so a packaged install
  doesn't write a redundant Start Menu shortcut or Apps & Features entry —
  MSIX already owns both.

## 2. MSIX packaging pipeline

- **`FootNote.Msix/Package.appxmanifest`** — final identity, copied verbatim
  from Partner Center's own Product Identity page:
  - `Package/Identity/Name`: `Kritarth.FootnoteCommentsforFiles`
  - `Package/Identity/Publisher`: `CN=F94053C2-7FE8-4301-BE68-5CF640A88F39`
  - `Package/Properties/DisplayName`: `Footnote: Comments for Files` (must
    match the reserved Store name exactly — this was wrong on the first
    submission attempt and caused a rejection, fixed and reverified)
  - `Package/Identity/Version`: `1.0.0.0` (deliberately reset from the
    GitHub build's `1.2.0` — a fresh, Store-only version number; **the two
    are independent and this does not affect the GitHub/free build**, which
    still defaults to `1.2.0` in `build.ps1`)
  - `runFullTrust` capability declared (required for any Desktop
    Bridge/Win32-in-MSIX app) — flagged by Partner Center as a restricted
    capability needing manual Microsoft approval; justification text was
    written and submitted (see §4).
- **`FootNote.Msix/package-msix.ps1`** — takes `-Tier` and `-Channel`,
  publishes the app, packs the MSIX, self-signs it with a local test
  certificate (for sideload testing only — Partner Center does its own
  signing for the real submission).
- **`tools/icongen/Program.cs`** extended to also render:
  - 8 MSIX Store tile assets (Square44–310, Wide310x150, StoreLogo,
    SplashScreen) — transparent background, OS composites its own tile color.
  - 4 Store *listing* logos (Poster art 720×1080/1440×2160, Box art
    1080×1080/2160×2160) — opaque dark background baked in, since these are
    marketing images on the product page, not OS-composited tiles.
- A real bug was caught and fixed before submission: **`package-msix.ps1`
  was never actually passing `-Channel` through**, so every "Pro" MSIX built
  so far had silently used the Owner channel (always unlocked) instead of
  Store (real purchase gate). Rebuilt correctly afterward — this would have
  given the paid unlock away for free to every Store customer if shipped.

## 3. Partner Center — Store listing content

All filled in via the live Partner Center session (English - United States):

- **Description** (~1900 words) and **Short description**.
- **Product features** (3 bullets: hotkey note-attach, local-only storage,
  Unlock Full Version's added features).
- **Keywords**: `file notes`, `sticky notes`, `productivity`, `file
  comments` (+ AI-suggested).
- **5 screenshots**, composited from real captures of the running app
  (1366×768 canvases, dark themed, on-brand):
  1. Client Proposal note — "Never forget why a file matters"
  2. Final Cut note — "Notes stay attached, right on the file"
  3. Q3 Budget note — "Know which version is the real one"
  4. Wedding Playlist note — "Catch mistakes before it's too late"
  5. Settings window — full appearance/position customization
- **Store logos**: 9:16 Poster art and 1:1 Box art uploaded (generated by
  icongen, on-brand "Bleeding Drop" mark on dark background).
- **Category**: Productivity. **Privacy**: "No, my product doesn't use any
  personal information" (accurate — no telemetry, no accounts, all storage
  local).

All demo screenshots were staged with realistic example files/notes and
manually cropped/composited to remove personal info (usernames, device
name, weather widget, OneDrive account name) before upload.

## 4. Partner Center — everything else

- **Pricing and availability**: Free, USD, all worldwide markets, public,
  discoverable — Complete.
- **Age ratings**: IARC questionnaire completed (utility app, no
  ratings-relevant content, no user content sharing, digital-goods purchase
  = yes for the add-on, no loot-box/gambling mechanics) — rating pending
  IARC's own confirmation email.
- **Submission Options**: `runFullTrust` justification written and saved,
  explaining FootNote is a Desktop Bridge app (classic WPF exe hosted via
  `Windows.FullTrustApplication`), not requesting elevation or any access
  beyond a normal desktop app.
- **Packages**: `FootNote-v1.0.0-Store.msix` uploaded and validated. (A
  duplicate-upload snag from a stuck "Analyzing package" state was cleaned
  up — resolved by keeping the single valid package rather than fighting
  the duplicate.)

## 5. Privacy policy

- **`docs/Privacy_Policy.md`** and **`docs/privacy-policy.html`** — states
  plainly that FootNote collects no personal data, notes stay local, and
  in-app purchases are handled entirely by Microsoft Store's own commerce
  system. Required by Partner Center because of the in-app purchase, even
  though the actual answer is "no personal data."

## 6. Submission status (as of last check)

**Main app ("Footnote: Comments for Files") — submitted, in certification.**
Pipeline: Submission ✓ → Pre-processing (in progress) → Certification →
Publishing. Typically a few hours, up to 3 business days.

**Add-on ("Unlock Full Version", $2.99 one-time) — fully staged, not yet
submittable.** Properties, Pricing, Age ratings, and Store listing (icon +
description) are all Complete, but Partner Center blocks its submission
until the parent app finishes publishing.

**Still outstanding (yours to do, not something I can do for you):**
- Tax and payout information in Partner Center — required before either
  product can actually charge money, though it doesn't block certification.
- Once the main app publishes: submit the add-on.
- If certification comes back with feedback (most likely the `runFullTrust`
  capability review), bring it back here to interpret/fix.

## 7. Git

- Reviewed the full diff before committing (nothing unexpected —
  `FootNote.Pro/` correctly absent from tracked files, no stray temp files).
- Committed as `43f2cdc`: *"Add Microsoft Store distribution: AppTier
  split, MSIX packaging, Pro gating"* — 35 files changed.
- Pushed to `origin/main` on GitHub (`kritarthasuvarna-spec/FootNote`).
- The GitHub/free build's own version (`1.2.0`, `build.ps1` default) was
  left untouched throughout — only the Store MSIX uses the fresh `1.0.0`.
