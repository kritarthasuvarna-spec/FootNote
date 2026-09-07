# Sanity Check #2 — MSIX packaging (2026-09-06)

Covers everything built since the last sanity check: the version-history
browser, and the MSIX packaging pipeline for both tiers.

## 1. Compile check after all recent edits

| Check | Result |
|---|---|
| `FootNote.App` builds, Free tier | **Pass** — 0 errors |
| `FootNote.App` builds, Pro tier (references `FootNote.Pro`) | **Pass** — 0 errors |

## 2. Found and fixed a real bug: build scripts clobbered each other

`build.ps1` wiped the entire version folder (`dist\<Tier>\v<Version>\`)
before writing its own output. That's fine in isolation, but
`package-msix.ps1` writes into that same folder — running `build.ps1`
after `package-msix.ps1` silently deleted the `.msix` and test cert that
were already there.

**Fix:** `build.ps1` now only removes the two files it's actually about to
write (`FootNote-Setup-v*.exe`, `FootNote-v*-win-x64.zip`), not the whole
directory. Verified by running both scripts back-to-back for both tiers
and confirming all four expected files survive in each folder:

```
dist\Free\v1.2.0\   FootNote-Setup-v1.2.0.exe, FootNote-v1.2.0-win-x64.zip,
                    FootNote-v1.2.0.msix, footnote-test-cert.cer
dist\Owner\v1.2.0\  FootNote-Setup-v1.2.0-Owner.exe, FootNote-v1.2.0-Owner-win-x64.zip,
                    FootNote-v1.2.0-Owner.msix, footnote-test-cert.cer
```

## 3. MSIX package integrity

| Check | Method | Result |
|---|---|---|
| Package builds for both tiers | Fresh run of `package-msix.ps1 -Tier Free` and `-Tier Pro` | **Pass** |
| Manifest is valid | `makeappx` runs manifest validation by default (no `/nv` flag passed) — a build failure here would have surfaced as a pack error | **Pass** — both packs succeeded |
| Package contents are correct | Extracted the Free `.msix` (it's a zip container) and inspected directly | **Pass** — `AppxManifest.xml` present with correct `Identity`/`Executable`/`EntryPoint`, `FootNote.App.exe` + self-contained .NET runtime DLLs present, `AppxSignature.p7x` present |
| Signature is structurally valid | `signtool verify /pa` on the Free package | **Pass, with the expected caveat** — reports exactly one error: "certificate chain terminated in a root certificate which is not trusted." That's correct behavior for a self-signed test cert that was deliberately never installed into Trusted Root (see below) — a malformed or corrupt signature would report a different error entirely. |

## 4. What was NOT tested, and why

- **Actually installing and running the app from inside the MSIX container.**
  Blocked on two system-level settings I don't change automatically:
  Developer Mode, and trusting the test certificate. Both are printed as
  manual instructions by `package-msix.ps1` rather than applied
  automatically, since they're security-relevant Windows settings, not
  app config.
- Because of the above, things that specifically depend on the MSIX
  runtime environment (registry/file virtualization, whether the
  `windows.startupTask` TODO actually matters in practice) remain
  unverified. The package's *static contents* are confirmed correct; its
  *runtime behavior once installed* is not yet.

## 5. Re-verified: Settings gating still correct after all recent changes

Launched the current Pro build fresh, opened Settings — Position,
Appearance, and Panel sections all present and functional (screenshotted:
accent swatches, bar style radios, corner radius slider all visible and
correctly bound), same as the first sanity check. Confirms the
`FootNote.Pro` project reference and the `FootNoteTier` build property
didn't regress anything from the earlier gating work while building the
MSIX pipeline.

## Outcome

One real bug found and fixed (build-script folder collision). Everything
else checks out. Real install (`v1.2.0`, the shipped GitHub release)
confirmed untouched and running normally throughout. Nothing committed or
pushed.
