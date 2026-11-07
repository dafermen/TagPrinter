# Troubleshooting — TagPrinter 0.27

## Windows asks for .NET

Use the self-contained Windows x64 ZIP from Releases, extract every file and start its TagPrinter.exe. Copying only the executable omits its runtime dependencies. Developer/framework-dependent builds require the matching runtime. Use the complete portable for normal distribution.

## No printer or wrong destination

Install the device's Windows driver, then use Refresh printers in section 04. Check the always-visible printer summary before submitting. Historical recovery clears a printer that is unavailable; choose a new destination explicitly. A successful spooler submission does not prove the device printed.

## Wrong size, orientation, offset or blank physical output

Compare app dimensions and DPI with actual media and driver settings. Calibrate the printer and review driver scaling/orientation. The app's horizontal/vertical control swaps physical dimensions; content rotation separately turns all text/code. Use the preview, then print one physical sample. Do not compensate for a media/driver mismatch by increasing copies.

Direct ZPL requires ZPL-compatible firmware; the application does not automatically detect printer command-language capabilities. Use the Windows-driver method when appropriate for the installed device.

## Text/code does not fit

Use a larger label, shorter visible fields, bottom layout or QR where suitable. Try automatic font fit, a smaller point size/scale, or hide unneeded fields. A code still requires serial even when printed serial text is hidden. Text-only output may omit serial, but cannot be entirely blank.

The font selector lists fonts on this computer. A missing historical font falls back to Arial in the editor. Automatic fitting can reduce the size you request; disable it to test a fixed scale and inspect overflow messages.

## Barcode cannot be scanned

Check serial contents (printable ASCII for Code 128), correct DPI, actual print sharpness and white quiet zones. Use a larger label or Export Code to inspect an independent symbol. Successful bitmap decoding in tests does not validate the printhead, media or driver scaling. Always scan a real sample before batches.

## Changes to config.json appear ignored

Existing last-work settings override startup defaults. The catalog still loads from the current installation. New asset clears asset values but does not reset formatting. Close the app before manual edits; see CONFIGURATION.md for supported properties and ranges. Do not add font/rotation keys to Defaults expecting unsupported settings to take effect.

## History or Dashboard looks different than expected

Check search, action, optional dates and Recycle bin in Reports. Dashboard counts current canonical asset records and excludes retired assets initially. Recycling label events changes label-log totals, not master counts. Its dates filter administrative updates. Repeated exports are multiple events linked to one master. Use Assets to review state/location/custodian. Dashboard links now carry their search/date/group scope; clear filters before comparing the whole registry. Check Include retired for lifecycle counts; explicitly registered blank-serial masters are counted too.

Dashboard filters apply before deduplication. Double-clicking a group opens full matching active history, not the Dashboard date/search subset. See HISTORY.md for exact counting rules.

## History write failed after output

The app can successfully create a file or submit printing and then fail to record SQLite history. Read the warning: do not automatically repeat a print. Inspect the output/queue and data-folder access before deciding whether another submission is needed.

## Backup and data recovery

Use Back up database for a consistent live SQLite copy. It includes recycled events, but not separate output files, config.json or last-work JSON. There is no restore-backup button. Close all instances and preserve the existing data folder before manual replacement; never mix unrelated WAL/SHM files. Keep backups outside the portable folder. See HISTORY.md.

## RFID expectations

This version prints visible labels on compatible printers. RFID chip encoding and read-back verification are not implemented. Printing successfully on RFID media does not mean its chip contains the serial.

## Package integrity or damaged startup data

Open the root EXE, not the app/ executable. Read the affected filenames and diagnostic log. Re-extract all program files into a fresh directory; preserve custom root config and user data. Malformed work or SQLite corruption is not automatically deleted. See [startup validation](STARTUP_VALIDATION.md).

## Administrative CSV or assignment rejected

Use the Assets CSV template with all business headers and stable English status keys. Keep exported UID/code/revision for the same local record; re-export after another edit. A changed preview target rejects the entire import. Assign needs a custodian and destination; Return/Repair need a destination. Catalog dropdowns accept typed new values within field limits. See ASSET_OPERATIONS.md.

## Last backup shows a failure or missing file

Open 05 Settings for the full error/path and use Back up now. Check destination access/free space and preserve the last successful snapshot. A missing metadata record initially shows Not recorded yet. A successful local backup is not off-machine protection or a tested restore; keep an independent copy.

## Large inventory search or export

Use Exact serial for known serials and narrower date/state filters for broad queries. Substring searches and full aggregates can still take time; the UI shows loading and discards obsolete requests. Initial index creation/structural checking can lengthen first startup. CSV Cancel interrupts reading and cleans the staged file before closing; an open destination in another program can prevent final replacement while preserving the prior file. See PERFORMANCE.md.

## Maintenance and warranties

If records are missing, check the selected subtab, asset scope, state/deadline filter and Include retired assets. All assets removes per-asset scope; Clear filters keeps it. Reminder totals deliberately ignore only the deadline selector and are not page totals. Check the Windows date and refresh if crossing midnight. Reminders do not run while the app is closed.

An amount needs a three-letter currency; Completed needs a completion date no earlier than an entered start. Invalid/stale saves leave the editor open with input intact. Reload a stale record before retrying. Coverage remains linked after serial edits. Job state never automatically changes assignment/physical asset state.

Schema-4 restore errors can indicate corrupt fields, inconsistent metadata or broken revision history. Preserve the files/error and use a known-good complete backup. Do not remove service/audit tables or lower user_version to force older binaries to open current data. Use the protected matching pre-upgrade copy for rollback. See [service recovery](SERVICES.md).

## Physical inventory

Configure the scanner as a keyboard with Enter suffix and focus the reading box. Existing label codes encode Serial; Asset code is an explicit separate mode. Oversized/control-bearing input is rejected. Reused serial conflicts require the intended TP code. Unrecognized input does not create an asset automatically. Wrong-location comparison uses frozen evidence; editing a current master does not rewrite past count results.

Pending is not Missing until Close count. Canceled sessions never assert missing assets. Duplicate readings do not inflate Found. Undo retains audit; closed/canceled sessions are read-only. Resolve a paused queue using Retry or an explicit Discard before closing/restoring. Full buffers preserve rejected input in the reading box. Committed session data survives restart; uncommitted memory input cannot survive forced termination. See [count recovery and evidence](PHYSICAL_INVENTORY.md).
