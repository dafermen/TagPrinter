# Validation

## How to run

From Windows with the .NET 8 SDK:

```powershell
./scripts/Test.ps1
./scripts/Publish.ps1
```

Test builds Release and runs the managed --self-test path. Publish builds the self-contained executable and runs that executable's tests before packaging. Test paths isolate last-work settings and SQLite from live user data. No test submits printer jobs or opens the project website. A failed check throws and stops its script.

## Automated coverage

| Area | Checks |
|---|---|
| Full-label codes | 32 combinations: two sizes, two DPI values, four layouts, QR/Code128; generated pixels decode to the intended serial. |
| ZPL | Raster dimensions, framing and requested quantity. |
| Typography | Manual/automatic scale, fit rejection, margins, guide exclusion and exact offsets. |
| Standalone codes | PNG save/reopen, DPI, quiet zones, decoding and safe filenames. |
| Visibility | Field combinations, unchanged code payload, saved flags and empty-output rejection. |
| Config/compatibility | Catalog validation/stable IDs, old Title/ShowTitle, extra fields, asset heading and missing-new-field defaults. |
| UI state | English/Spanish restoration, section defaults/toggle, About hints, New asset and print summary. |
| SQLite | Append/reopen, repeated serials, complete snapshots, source foreign keys, literal/injection-like searches, actions and UTC bounds. |
| CSV/backup | UTF-8/semicolon round trip including quotes/newlines/JSON; consistent backup reopen and active-destination rejection. |
| Integration | Successful output creates events; failed exports do not; report sorting/loading preserves intended IDs and exact dimensions; missing printer cleared. |
| Text-only | No symbol, layout-independent pixels, optional/Unicode serial, empty-label rejection, history/CSV and no-serial recovery. |
| 0.15 | Four rotated QR decodes, unchanged physical dimensions, font/bold/rotation UI/snapshot recovery, latest-per-serial selection and recycle/restore across reopen. |

Screenshots use synthetic Monitor/Dell and laptop examples. They demonstrate presentation; assertions and decoded pixels provide functional checks.

## Documentation update validation

- Generate compiler XML documentation and treat malformed/missing public XML documentation diagnostics as errors.
- Compare C# syntax tokens with the previous committed source to verify comments do not change executable statements, literals, signatures or attributes.
- Audit explicit types, methods/constructors, properties and fields for nearby documentation, including private/local helpers and native declarations.
- Check local Markdown links and source references.
- Run the existing source and published-executable verification suites.

The project now generates its compiler XML file under build output; this is not committed as a generated artifact. The source audit confirmed identical C# syntax tokens in all 14 files and documentation on all 244 explicit declarations (types, methods/constructors, properties, fields and local helpers). This measures declaration coverage; complex local variables and algorithms are explained by adjacent comments and the student guide. All 43 relative Markdown targets were resolved. Build with XML diagnostics enforced completed with zero warnings/errors. Both scripts/Test.ps1 and scripts/Publish.ps1 completed successfully for this update. The source build and self-contained executable passed their existing isolated verification suites; no printer jobs were submitted. A final compiler XML check also completed with zero warnings/errors. The portable includes the English manuals, their images and compiler XML output.

## Limits of evidence

Automated checks do not verify a physical printer, media calibration, driver scaling, printhead condition or scanner performance on paper. RAW or driver submission success does not prove physical output. RFID writing/read-back is absent. Large-history performance, concurrent app instances and platform-specific font differences are not comprehensively tested.

## MIT licensing update

The root LICENSE uses the standard MIT grant and disclaimer with Copyright (c) 2026 Dario Meneses. README and project metadata identify MIT; the publishing script copies LICENSE into the portable. Third-party terms remain separate. The text matches the GitHub MIT template after substituting year/holder. scripts/Test.ps1 and scripts/Publish.ps1 both passed with zero build warnings/errors and no printer jobs. The generated ZIP contains a byte-for-byte identical LICENSE. Application version remains 0.15.0.

## Startup validation 0.16

Isolated checks cover malformed saved-work preservation and corrupted/healthy SQLite. Portable verification runs through the root launcher. Package mutation tests cover paths with spaces, missing app/runtime/native files, changed/missing manifest, same-length corrupt runtime, malformed/missing config, legitimate config edit and successful restoration. No printer work or real user data is used.

Result: source verification passed; final root-launcher/application self-tests passed; all 11 package scenarios passed; compiler XML build completed with zero warnings/errors. Every manifest entry was rehashed from the final ZIP; no verification files/databases were included. Tests did not print or write user settings/history.

## Configuration transfer/code size — 0.17

Isolated real-form tests check complete envelope equality, catalog persistence, pre-import live-state backup, unavailable printer/font behavior, reopening with the imported catalog, invalid/partial/version/range rejection, protected export targets, drafts and forced paired-write rollback. Symbol tests decode full labels and standalone QR/Code128 at 50/100/140 percent and 203/300 DPI, plus rotated output, overflow rejection and old-snapshot defaults. Explicit output records/reloads nondefault CodePercent. Screenshots use synthetic data.

Result: existing source suite passed during development; final root-launcher/published application suite passed with transfer, font fallback, history-scale recovery and rollback checks. All 11 package-integrity scenarios passed. Final ZIP hashes/manual images verified and test databases excluded. No printer jobs were submitted.
