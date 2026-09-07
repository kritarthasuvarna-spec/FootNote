# Partner Center Product Identity — FootNote

Official values from Partner Center's own "Product Identity" page
(Apps and games → Footnote: Comments for Files → Product Identity).
These are authoritative — if anything elsewhere in the repo disagrees
with this file, this file is right and the other thing needs fixing.

| Field | Value |
|---|---|
| Store product title | Footnote: Comments for Files |
| Package/Identity/Name | `Kritarth.FootnoteCommentsforFiles` |
| Package/Identity/Publisher | `CN=F94053C2-7FE8-4301-BE68-5CF640A88F39` |
| Package/Properties/PublisherDisplayName | `Kritarth` |
| Package Family Name (PFN) | `Kritarth.FootnoteCommentsforFiles_sbt8h0h1dyh2j` |
| Package SID | `S-1-15-2-3805253152-1051987152-1002314898-1568071414-2097222063-1291582212-2773698644` |

Used in:
- `FootNote.Msix/Package.appxmanifest` — `Identity` and `Properties` elements
- `FootNote.Msix/package-msix.ps1` — `$certSubject` (local test-signing cert,
  must match `Package/Identity/Publisher` exactly for a sideload test-install
  to work — Store submissions use Partner Center's own signing instead, so
  this only matters for local testing)

Package SID is the one value not currently used anywhere in this repo — kept
here since it may be needed later if the Store purchase integration (in
progress) ends up needing to identify the package for licensing checks.

## Add-on: Unlock Full Version

| Field | Value |
|---|---|
| Add-on Product ID (Partner Center) | `unlockfullversion` |
| Add-on type | Durable (one-time, doesn't expire) |
| Store ID (used in code) | `9NKD7WBDP1JP` |

Used in `FootNote.Pro/StorePurchase.cs` (`UnlockAddOnStoreId`). This is the
one thing that actually gates whether the Store-Paid channel unlocks — see
`FootNote.Pro/StoreProFeatures.cs`.
