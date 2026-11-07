# SQLite history, Reports and Dashboard — 0.23

## Event contract

Each completed inventory output creates one independent event, even when its serial repeats. Free text output is excluded.

| Stable action | Meaning |
|---|---|
| ExportTag | Full-label PNG written successfully. |
| ExportCode | Standalone symbol PNG written successfully. |
| ExportZpl | Raster ZPL file written successfully. |
| PrintSubmitted | The application's print submission call completed. |
| Imported | CSV inventory registration; no label generated or printed. |

One submission with several copies is one event. It does not confirm physical printing or RFID. Preview edits, autosave, New label, report exports and backups do not create label events. Canceled/failed output is not recorded as successful.

Each event stores UTC time, six asset fields, full WorkState JSON (format, dimensions, visibility, fonts, rotation, margins, printer/copies), output path and optional source event ID. If an external output succeeds but SQLite fails, the app warns without retrying output. They are not one atomic transaction.

## Location and schema

The live file is %LOCALAPPDATA%/TagPrinter/TagPrinter.db, outside the portable. History begins with actions recorded since v0.13; old output files are not automatically imported. Tests/screenshots use separate synthetic databases.

SQLite user_version is 3. The events table holds id, created_utc, action, serial, asset_type, brand, model, part_number, mac_address, state_json, output_path, source_event_id and nullable asset_uid. Serial/date indexes assist queries; source_event_id links the loaded event and asset_uid links a canonical master. Event serials are intentionally not unique; nonempty master serials are normalized and unique. assets, asset_movements (with optional reason) and asset_catalog hold independent administration, audit and reusable vocabulary. See ASSETS.md for the protected version-1 upgrade.

The trash table holds event_id and deleted_utc. It preserves reversible membership without rewriting snapshots. Normal queries exclude its IDs; recycled queries include them. Get(id) can retrieve either scope. Connections enable WAL, foreign keys and a five-second busy timeout without pooling.

## Search, sorting and date scope

Search literal substrings in all six fields or one selected field. Percent and underscore are literal, not wildcard syntax. SQL field expressions are whitelisted and values are parameterized. SQLite case-insensitive matching here is ASCII-oriented.

Action/date conditions combine with text using AND. Dates start unchecked. From includes local midnight; To includes its complete day using an exclusive boundary at the next local midnight. Each boundary is converted to UTC. Display dates are local; CSV timestamps remain UTC.

Click column headers to sort. Numeric/date columns use typed values. Horizontal scrolling reveals additional columns. Global sorting and filters run in SQL before paging; Export view follows the displayed page/order.

## Load a label

Click its serial link or select a row and choose Load label. Blank serials display (No serial) but resolve through the numeric ID. Recovery creates no event and sends no print job.

Physical dimensions become Custom to preserve the old design despite catalog changes. Current UI language is retained. An unavailable saved printer is cleared; choose an installed printer before submitting. A later output is a new event with the loaded event ID as source. New label clears that reference.

New properties use compatible defaults: older records keep ShowCode=true, Arial 10 pt, heading emphasis and rotation zero. Legacy Title/ShowTitle migration applies when loading last-work JSON; history was introduced after the brand fields.

## Recycle and restore

Select rows and confirm Delete. A transaction inserts their IDs into trash; event contents and source links remain unchanged. Check Recycle bin to inspect and restore them. Duplicate deletion is harmless. Empty recycle bin can permanently remove recycled events after confirmation; active events cannot be edited or directly purged. Deleted events do not contribute to global label-log totals; recycling an event does not remove its master from Dashboard. Exported files and queued jobs are never removed by this action.

## CSV

Export view CSV exports currently shown rows in order. Export all CSV ignores search/date/group filters but respects active versus recycle-bin scope. Output is UTF-8 with BOM, semicolon separated, every value quoted, embedded quotes doubled. Newlines and delimiters round-trip.

Columns: Id, CreatedUtc, Action, Serial, AssetType, Brand, Model, PartNumber, MacAddress, Printer, Copies, CodeType, WidthMm, HeightMm, OutputPath, SourceEventId, StateJson.

Dates use ISO-8601 UTC; numbers use invariant formatting. CodeType is TextOnly for full-label actions with ShowCode=false, otherwise QR/Code128; standalone ExportCode always remains a symbol. Empty serials remain empty. Import identifier columns as text to preserve leading zeroes. Settings also imports six-core-field CSV; matching headers from this report can supply those fields. StateJson/admin data are not imported. See INVENTORY_TOOLS.md.

## Backup and manual restoration

Back up database uses SQLite's backup API to produce a consistent .db, including recycled events, while the app is open. It refuses the active database as destination. Keep backups outside the portable folder.

Use 05 Settings > Restore database for validated replacement and a safety copy. For offline manual recovery, close every app instance, preserve a copy of the complete existing data folder, then replace the database using a known valid backup. Do not combine unrelated -wal/-shm files with a restored .db. Backup does not include external PNG/ZPL files, installation config.json or last-work settings.json; preserve those separately when needed.

## Canonical asset counting — 0.23

Dashboard uses canonical master records, including explicitly registered no-serial assets, rather than deduplicating label snapshots. State/custodian/location edits are independent of print events. Label totals are separately aggregated from active non-Free-text history. Recycle-bin cleanup removes label events, not master assets; use Assets > Retire to change an asset's lifecycle state. See ASSETS.md.

## Configuration transfer and code scale — 0.17

Settings transfer does not import/export/change history or create output events. Back up the database separately. WorkState snapshots now store CodePercent (25–200, default 100); loading older events preserves their existing size. New exports record/recover the chosen scale.

## Permanent recycle-bin emptying — 0.18

Empty recycle bin deletes all recycled events after confirmation, independent of filters. Active records survive; source_event_id links to removed records become null within the same transaction. The editor drops a purged loaded-source reference. Exported files/queued jobs are untouched. CenterText is included in WorkState; older rows use false. See TEXT_TRASH_PRINTER_STATUS.md.

## Inventory-only events — 0.21

FreeTextMode=true work does not add new events for printing or any export. Last-work autosave/configuration transfer still preserves its draft. Query filters explicitly marked legacy Free text rows out of Reports, CSV selections and global label-log totals without deleting them; backups can still contain those older rows. Ordinary asset-field labels without a symbol remain inventory. PrintSubmitted means accepted by the spooler, not paper output; transient job monitoring does not rewrite the stored event. See PRINT_JOB_PROGRESS.md.

## Inventory tools — 0.22

Imported actions register inventory without printing/exporting. SQL pages and exact Dashboard groups precede row materialization; page export and whole-scope export remain distinct. Restoring replaces masters, movements, events and trash after staging/validation and safety backup. Automatic backups are daily with ten-copy automatic retention; pre-restore copies are protected. See INVENTORY_TOOLS.md.

Schema 2 adds assets, asset_movements and events.asset_uid while preserving immutable label snapshots. Backups/restores include all tables; older schema-1/2 backups are normalized in staging. Administrative CSV updates masters/audit without creating label events; see ASSET_OPERATIONS.md. Config/profile transfer does not transfer master records or audit history.

## Large history — 0.25

Reports uses background lightweight summary pages rather than complete WorkState objects. Exact serial uses indexed matching; substring/date/action/trash semantics remain compatible. Full CSV streams the original StateJson directly and stages the destination, so cancellation/failure preserves an existing file before replacement. Recovered labels still load the full snapshot by stable ID. Administrative audit is also paged. Derived indexes keep SQLite schema version 3; older snapshot restores create missing indexes in staging. See PERFORMANCE.md.
