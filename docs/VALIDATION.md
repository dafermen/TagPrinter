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

## Centering, recycle-bin purge and Windows status — 0.18

New isolated checks measure rendered dark-pixel centroids at 203/300 DPI, decode centered text/code output, restore centering from history/full transfer, purge related recycled parent/child rows while preserving active rows, prevent restoration, clear loaded provenance before a new output, map Windows status bits/virtual queues and exercise asynchronous selection changes/timeouts/disposal using injected tasks. No native printer status query or printer submission is required by these tests.

Result: source suite and final root-launcher/application verification passed. All 11 package-integrity cases passed; XML documentation build had zero warnings/errors. Centered output, permanent deletion boundaries and simulated status/timeout/disposal scenarios passed without native hardware status queries or printer submissions. Final package payload hashes and documentation links were checked. Physical printer status remains a hardware/driver validation task.

## Flexible labels — 0.19

New isolated tests cover reclaimed space at both DPIs/layout axes/code types, actual text pixels in the released QR region, preservation of a wrapped heading final character, free-text isolation and blank rejection, output history, configuration transfer/restart and identical PNG bytes with feed rails on/off. Existing code-decode/rotation and package mutation checks remain required. No hardware printing is performed.

Result: source suite and final root-launcher/published application suite passed, including the newline round trip. All 11 package-integrity scenarios passed. Compiler XML build completed with zero warnings/errors. The final ZIP payload hashes, absence of test databases and 71 local documentation links were checked. No printer jobs were submitted; physical printing remains unverified.

## Fixed paper-exit preview — 0.20

Isolated PaperFeedTests checks 0/90/180/270 rotation on unchanged 100 × 50 media, fixed printer-header pixels and paper bounds, removal of the direction selector, exact preview/export raster equality, English/Spanish screenshots and old-direction restart compatibility. Existing tests also compare output with diagram enabled/disabled. No physical printer or native printer queries are used.

Result: scripts/Test.ps1 and the final root-launcher/published application suite passed. All 11 package-integrity scenarios passed. The XML documentation build completed with zero warnings/errors. All 470 immutable payload hashes were checked from the final ZIP, synthetic databases were excluded and 77 local documentation links resolved. No printer jobs were submitted; physical-driver behavior remains unverified.

## Inventory-only history, Ctrl+P and print progress — 0.21

PrintingTests uses fake submission/queue tasks and isolated real forms/SQLite. It checks actual event row counts for Free text PNG/ZPL/code/print, old marked rows hidden without deletion, exact title/ID correlation, duplicate/invalid shortcut guards, captured state/provenance across editor changes, queued/printing/removed/error/completed/unobserved states, timeout, expiry and disposal. Existing scene/render/configuration/history/integrity suites remain enabled. No native job queries or printer submissions are used by tests.

Result: source checks and final root-launcher/published application checks passed, including held-key repeat after completion, free-text mode changes during submission, missing printer, submission failure, tracking deadline and deleting-vs-deleted states. All 11 package-integrity cases passed; XML documentation build had zero warnings/errors. Final ZIP: 470 immutable payload hashes verified, no test databases/verification files and 82 local Markdown targets resolved. No native printer jobs or job-status queries were used in tests; real driver/queue behavior remains a hardware validation task.

## Inventory tools — 0.22

InventoryToolsTests uses isolated SQLite/CSV/forms: quoted text/newlines/leading zeros, active and within-batch duplicate skipping, forced trigger failure and rollback, disjoint SQL pages/group counts, UI last-page bounds, daily backup/ten-file retention, replacement restore/protected prior state/corrupt rejection, profile persistence/full configuration transfer and identity-clearing duplication. Existing rendering, print simulation, Free text exclusion and integrity suites remain enabled.

Result: scripts/Test.ps1 and the final root-launcher/published suite passed. The final suite also rejects a structurally healthy SQLite file with invalid label snapshots, preserving live inventory. All 11 package-integrity scenarios passed. XML documentation build completed with zero warnings/errors; 470 immutable ZIP payload hashes and 88 local documentation links were checked. No test databases/verification files were distributed and no real print jobs were submitted. Synchronization remains pending by explicit user choice.

## Fluent UI and canonical assets — 0.23

FluentUiTests checks actual glyph differences and translated icon/text/shortcut presentation. AssetTests checks canonical uniqueness across outputs, stable links after serial changes/historical loading, required assignment data, actual editor save binding, CSV identity isolation, global asset pagination/sorting, invalid asset-backup rejection, stale/duplicate guards, no-serial links, Free text exclusion, literal search, retired/review-state Dashboard scope, protected Unicode-aware version-1 migration and full asset/audit backup restoration. Existing source and portable suites remain enabled without real print jobs. Verification windows render off-screen without activation/taskbar entries; a regression check rejects visible test-window bounds.

Result: scripts/Test.ps1 and the root-launcher/published suite passed. All 11 package-integrity scenarios passed. Compiler XML validation completed with zero warnings/errors. The final ZIP verifies all 470 immutable payload hashes, includes the Fluent license/notices and English manuals/screenshots, and excludes synthetic databases/verification files. All 104 local repository documentation links resolve. Tests use isolated settings and SQLite and submit no real printer jobs. Physical hardware and synchronization remain outside this release.

## Daily asset operations — 0.24

AssetOperationsTests checks read-only recognition and explicit loading, draft/master captions, catalog normalization/user-text preservation, actual catalog/action forms, assignment/return/repair reasons and stale guards, administrative CSV metadata/quotes/preview/unchanged/rollback/concurrency, full filtered export, exact Dashboard state/group/date navigation, protected schema-2 migration/restore, catalog validation and backup success/failure/missing-file/manual recovery. Visual review is backed by real usable grid-bound assertions. Existing render/configuration/print-simulation suites remain enabled.

Result: scripts/Test.ps1 and the root-launcher/published suite passed. All 11 package-integrity scenarios passed. Compiler XML build completed with zero warnings/errors. The final ZIP verifies all 470 immutable payload hashes and excludes verification files/databases. All 115 local repository documentation links resolve. Screenshots/manuals reflect the published executable, including English/Spanish and the minimum-size layout. Tests cover catalog/record races, reason-only movements, full transaction rollback, legacy migration/restore and malformed/missing backup metadata. No real printer jobs were submitted; hardware validation and synchronization remain pending.

Focused workflow iteration: after building Release, run `dotnet src/TagPrinter/bin/Release/net8.0-windows/TagPrinter.dll --self-test --asset-operations-only`. Full source/published suites remain required for release. An off-screen host runs the actual WinForms message loop so asynchronous continuations survive dialog closure.

## Large datasets — 0.25

PerformanceTests builds isolated 100000-row assets/history/audit/catalog collections. It verifies actual index plans, projection/filter/order parity, bounded pages and suggestions, native cancellation, one-worker/latest-only delivery, cross-thread-safe real controls, modal export success/failure/cancel, invariant/raw JSON CSV, preserved destinations on cancellation/source failure and sampled live managed-memory bounds. Timings are recorded as environment-specific evidence. Existing source and portable suites remain required without actual print jobs.

Result: scripts/Test.ps1 and the root-launcher/published suite passed. All 11 package-integrity scenarios passed. Compiler XML build completed with zero warnings/errors. The final ZIP verifies all 470 immutable payload hashes and excludes verification files/databases. All 123 local repository documentation links resolve. Screenshots/manuals reflect the published executable, including English/Spanish and the minimum-size layout. Tests additionally cover 100000-row collections, real query plans, bounded projections/groups/audit/vocabulary, native interruption, coalesced background delivery, modal export success/cancel/failure, invariant/raw CSV and sampled live-memory bounds. No real printer jobs were submitted; hardware validation and synchronization remain pending.

## Maintenance and warranties — 0.26

ServiceTests exercises real linked records, inclusive/leap/invalid calendar dates, explicit decimal currency, lifecycle/immutable-identity/no-op/stale guards, a failing audit insert with full rollback, 10000-row indexed/bounded pages, paged changes, retired scopes, serial-edit continuity, invariant/canceled CSV, complete backup/restore, malformed records/revision-chain rejection, protected schema-3 migration and staged old backup restore. Actual editors test retained validation/input and persisted bindings; real async lists and English/Spanish/minimum-size captures verify usable geometry. No accounts or printer jobs are created.

Result: scripts/Test.ps1 and the root-launcher/published suite passed, including ServiceTests and the existing 100000-record scale checks. All 11 package-integrity scenarios passed. Strict XML-documentation build completed with zero warnings/errors. The final ZIP verifies all 470 immutable payload hashes, synchronizes manuals/screenshots/licenses and excludes verification files/databases. All 140 local repository documentation links resolve. Service UI captures cover English, Spanish and minimum size; dates/reminders remain visible. Operation is local without application accounts. No real printer jobs were submitted; physical printing/RFID and synchronization remain pending.

## Physical inventory — 0.27

CountTests verifies location normalization/frozen scope, exact serial/code modes, leading zeros and literal input, duplicate-safe classification, reused-serial ambiguity, unknown/retired/wrong-location/unexpected outcomes, withdrawal/terminal/stale guards, 10000-asset indexed/bounded pages/actions, audit-write rollback, complete/corrupt backups, protected schema-4 migration/staged restore, quoted/canceled CSV, actual creation form, oversized scanner rejection, FIFO retained head/retry/order and normal close guard. Real async controls and English/Spanish/minimum-size captures verify UI geometry. No physical scanner/RFID/printer jobs or live data are used.

Result: scripts/Test.ps1 and the root-launcher/published suite passed, including CountTests, ServiceTests and the existing 100000-record scale checks. All 11 package-integrity scenarios passed. Strict XML-documentation build completed with zero warnings/errors. The final ZIP verifies all 470 immutable payload hashes, synchronizes manuals/screenshots/licenses and excludes verification files/databases. All 159 local repository documentation links resolve. Count UI captures cover English, Spanish, closed reconciliation, failed-queue retry and minimum size; captured/observed location headers and scanner input remain usable. Operation is local without application accounts. No real printer jobs were submitted; physical printing/RFID and synchronization remain pending.
