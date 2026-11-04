# Learning TagPrinter from the source

This guide describes TagPrinter 0.15. All technical documentation and source comments use English. The application still supports English and Spanish. Read this alongside the source; the XML comments explain function contracts in the editor.

## 1. What this application does

TagPrinter turns asset information into a printable label. A label can contain asset type, brand, model, serial, part number, MAC address and an optional QR/barcode. The user chooses physical media dimensions, design and printer. Completed output actions are recorded in SQLite. Reports can reload their original designs; Dashboard summarizes those recorded actions.

A label is a picture. A saved preference is JSON. A history event is a SQLite row. These are separate things, with different lifetimes.

## 2. Suggested reading order

1. `Program.cs`: application startup and the alternative self-test entry point.
2. `AppConfig.cs` and `WorkStore.cs`: installation defaults versus the last edited work.
3. `MainForm.cs`: controls, user events and preview updates.
4. `LabelRenderer.cs`: how physical measurements become a bitmap.
5. `CodeRenderer.cs`: the separate standalone-code export.
6. `MainForm.History.cs` and `HistoryStore.cs`: output completion and persistence.
7. `ReportsView.cs` and `DashboardView.cs`: display projections, searches and counting rules.
8. `RawPrinter.cs`, `Ui.cs`, `CollapsibleSection.cs`: native printing and reusable UI helpers.
9. `SelfTest.cs`: executable examples that verify behavior without printing.

See [architecture](ARCHITECTURE.md) for file responsibilities and [configuration](CONFIGURATION.md) for the stored data contracts.

## 3. C# concepts used here

| Concept | Meaning in this project |
|---|---|
| `class` | An object with behavior and state, such as a form or SQLite store. |
| `record` | A data object with value equality. WorkState holds a snapshot; LabelData holds renderer input. |
| `with` | Creates a copy of a record with selected values changed; the original remains unchanged. |
| `init` | A property can be assigned during object initialization, including a `with` expression. |
| `readonly` field | The field reference is assigned once; the referenced control can still change its Text or Checked value. |
| `partial` | Multiple files contribute to one class. Both MainForm files access the same instance fields. |
| `Action<T>` | A callback: a function supplied by another component. Reports uses it to ask MainForm to load a label. |
| `(_, e) => ...` | An event handler/lambda. `_` conventionally marks an unused argument; `e` carries event data. |
| `?` | In a type, the value may be null. `?.` safely skips access when its receiver is null. |
| `!` after a value | Suppresses a nullable warning; it does not perform a runtime null check. |
| `using var` | Disposes a resource when the current scope ends, including on exceptions. |
| `try/finally` | Guarantees cleanup when a native resource was opened successfully. |
| LINQ | A pipeline for filtering, ordering, grouping and projecting collections. |
| `///` | XML documentation: summaries, parameters, returns and failures shown by editor tooling. |

## 4. Follow one user edit

Suppose the user changes Model. Its TextChanged handler calls UpdatePreview. The ready flag prevents this while controls are only partly initialized. Once ready:

1. ScheduleSave restarts a 600 ms timer. Several quick edits become one delayed JSON write.
2. Data reads controls and builds LabelData; it trims asset strings for output.
3. LabelRenderer.Render creates a new bitmap or throws a useful validation error.
4. The form swaps the preview image and disposes the old bitmap.
5. If fitting fails, the form shows the error and disables printing.
6. When the timer fires, CaptureWork creates a WorkState and WorkStore.Save persists it.

No history event is created by editing or previewing. History describes completed output, not every keystroke.

The flags ready and translating are event guards, not worker-thread locks. Assigning a control programmatically can trigger the same event as a user click. Temporarily disabling reactive updates avoids rendering a half-restored label or accidentally overwriting saved values.

## 5. Understand units and selector indexes

| Value | Unit or meaning |
|---|---|
| Width/Height, WidthMm/HeightMm | Physical millimeters. |
| Dpi in LabelData/ConfigDefaults | Actual pixels per inch: 203 or 300. |
| Dpi in WorkState | Combo index: 0 for 203, 1 for 300. |
| Code in WorkState | 0 Code 128, 1 QR. |
| Mode | 0 Windows driver, 1 direct RAW ZPL. |
| Layout | 0 code below center, 1 below left, 2 below right, 3 beside text. |
| FontSizePt | Base body size in points; 72 points equal one inch. |
| FontPercent | Scale percentage; 100 means the configured base size. |
| ContentRotation | Clockwise degrees: 0, 90, 180 or 270. |
| Copies | Quantity requested in one submission, not number of database events. |

For a 50 mm width at 203 DPI, pixels = round(50 × 203 / 25.4) = 400. Never compare a millimeter directly with a pixel count. In LabelRenderer, w/h are image width/height, m is the margin, dx/dy are offsets and content is the allowed content rectangle, all in pixels.

## 6. How drawing and text fitting work

The renderer gathers visible, nonempty lines. Asset type becomes the heading; brand is the heading if type is absent. Headings use 1.3 times the body base point size. Visibility controls printed text; it does not remove stored values.

textW/textH reserve a text area. codeArea reserves space for the symbol. If no text is visible, the symbol can use the full content area. If ShowCode is false, the encoder is skipped and text uses the full area; serial is then optional. Completely blank text-only output is rejected.

Each text line has a rectangle in textRects. Fits measures every line using its real font, width and height. Auto fitting starts at 100 percent and shrinks to a minimum of 50 percent. Manual mode uses FontPercent. A label that still cannot fit is rejected instead of silently clipping. Increasing FontSizePt can therefore be partly offset by automatic fitting; disable automatic fitting when testing an exact requested size.

For code drawing, matrix is the encoder grid. scale is an integer pixels-per-module factor. Fractional module widths could blur bars, so the renderer paints solid rectangles. Very small labels can still produce one-pixel modules; a real printed scan remains necessary.

Rotation is performed on the completed content, including code and text. A 90-degree turn first renders into swapped temporary dimensions, then rotates the bitmap back into the selected physical media dimensions. The recursive call sets rotation to zero, so recursion ends after one extra render. Offsets are applied in the pre-rotation coordinate system. There is no independent text-only rotation control.

## 7. Follow an export or print

Full-label export renders without guides, writes PNG or raster ZPL, then calls RecordGeneration. Export Code uses CodeRenderer independently of the full-label design and always requires a serial.

Windows printing creates a PrintDocument. PaperSize uses hundredths of an inch, unlike the renderer bitmap. PrintPage removes the driver's hard-margin coordinate offset and draws the bitmap at the requested physical size. remaining tracks copies and controls HasMorePages.

Direct printing builds ZPL from the raster and sends ASCII bytes through RawPrinter. ToZpl packs eight dark/white pixels into each byte, emits hexadecimal pairs and wraps the raster in printer commands. stride is the rounded-up byte count per image row. This is a raster payload, not user-provided ZPL text.

RawPrinter uses P/Invoke to reach winspool.drv. It opens a handle, starts a document, starts a page and writes bytes. Each successful scope has a finally cleanup. A successful write means submission, not verified paper output or RFID encoding.

File output/spooler calls and SQLite cannot share one atomic transaction. If output succeeds but recording fails, the app warns and does not repeat the print automatically. Repeating it might print duplicates.

## 8. Understand the three stored data sources

- config.json beside the EXE defines presets and startup defaults. It belongs to the installation.
- settings.json in the legacy InventoryLabels user folder stores the last editable work. Its location remains stable for compatibility.
- TagPrinter.db in the TagPrinter user folder stores completed actions and their full WorkState snapshots.

WorkStore writes a sibling temporary file and replaces settings.json. This reduces incomplete writes but is not a multi-instance locking scheme. Missing new JSON fields retain their property defaults. Legacy Title/ShowTitle migrates only when Brand/ShowBrand is absent.

An event stores searchable columns and state_json. Indexed metadata supports queries; JSON preserves exact design details. source_event_id links a later action to the event loaded before it. IDs are stable numeric database keys; serials are not unique event keys.

## 9. SQL, dates, CSV and the recycle bin

HistoryStore.Query accepts a HistoryQuery. Conditions combine with AND. Field names come from a fixed whitelist; user text is passed as a parameter. Percent and underscore are escaped so a user search is literal. SQLite NOCASE behavior here is ASCII-oriented; do not assume full Unicode linguistic matching.

A selected From date includes its local midnight. A selected To date includes its whole day by querying strictly before the next local midnight. These boundaries are converted to UTC. Stored times and CSV are UTC; the table shows local time.

Reports creates a typed DataTable for display. current preserves original events; table.DefaultView owns current sorting/filtering. VisibleRows maps back by numeric ID so an export follows the displayed order and a click loads the intended snapshot even after sorting.

Deleting inserts IDs into trash; restoring removes those markers. The events remain, preserving provenance and enabling restoration. Normal reports/dashboard exclude recycled records. Backup includes them. No PNG, ZPL or queued printer job is deleted.

CSV quotes every cell, doubles embedded quotes and uses semicolon plus UTF-8 BOM. It includes JSON, which can itself contain quotes and newlines. When importing, explicitly treat serial, MAC and part number as text to retain leading zeroes.

## 10. Why Dashboard counts differ

Assume one monitor serial ABC123 is exported twice and submitted once with three copies. History has three actions; Dashboard has one unique serial, one submitted job and three requested copies. It does not claim three physical prints.

UniqueAssets removes blank serials, orders descending by ID, groups trimmed serials case-insensitively and keeps the first event in each group. Grouping then uses that representative type/brand/model. These grouping values are trimmed but not case-normalized, so brand spelling/case differences may create separate groups.

Search and date filters run before deduplication. Consequently the representative snapshot is the latest matching event, not necessarily the newest overall. Rows without serial are counted as actions separately because their physical asset identity is unknown. Double-click applies a type/brand/model equality filter to all active Reports history; it does not preserve Dashboard date/search restrictions. Reports filters original strings using DataView default comparison, whereas Dashboard trims group values and groups case-sensitively. Case/edge-space variants can therefore produce a different drill-down row count. This documents the current implementation rather than assuming both grouping engines are identical.

## 11. Tests and safe exercises

Run scripts/Test.ps1 before changing behavior. It builds Release and invokes --self-test with isolated paths. SelfTest uses synthetic files and real SQLite, reopens CSV/backups and decodes generated code pixels. It never invokes the print action.

Useful learning exercises: trace a change from Data to Render; calculate pixel dimensions for both supported resolutions; follow one event ID through sorting/reloading; compare filtered action counts with unique serials; inspect how with preserves a snapshot while changing rotation.

Do not use the normal user database for experiments. A passing decode of a bitmap cannot validate media calibration, driver scaling, printhead quality or RFID. See [validation](VALIDATION.md) and [troubleshooting](TROUBLESHOOTING.md).

## 12. Documentation conventions

Document responsibilities and invariants, not just syntax. Each method comment explains its purpose and inputs; returned owned resources explicitly require disposal. Keep non-obvious units and indexes near declarations. Explain why a complex block exists before its implementation. Simple loop counters need no repetitive comments; meaningful intermediate values are described beside the algorithm or in this guide.

When changing a persisted field, document defaults and old-data compatibility. When changing a query/count, update reporting rules and tests. Keep historical release details in CHANGELOG and the current behavior in the manuals.

## Startup checks (0.16)

Open the root TagPrinter.exe to validate the immutable app/ payload before loading it. Editable config.json stays at the portable root; last work and SQLite are checked separately without deleting damaged data. See [startup validation](STARTUP_VALIDATION.md) for manifests, logs, recovery and limits.

## Configuration transfer and symbol scaling — 0.18

Read [the transfer/scale walkthrough](CONFIGURATION_TRANSFER.md) with ConfigurationTransfer.cs and MainForm.Configuration.cs. Notice the distinction between validating a draft and requiring a printable label, between a single-file atomic replacement and a two-file rollback, and between continuous percentages and integer pixel modules.

## Centering, recycle-bin purge and printer status — 0.18

03 Text and margins adds Center text for the whole text block. Reports > Recycle bin adds irreversible Empty recycle bin with all-filter scope confirmation. The visible printer summary now includes an automatically refreshed Windows status indicator. CenterText persists in work/history/full transfer; live status does not. See [centering, recycle-bin purge and Windows status](TEXT_TRASH_PRINTER_STATUS.md).
