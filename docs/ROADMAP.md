# Roadmap

## Completed

- 0.26: local maintenance/warranties, date reminders, explicit costs/currency, immutable revisions, paged lists/CSV and protected schema-4 backup/restore.

- 0.25: indexed/coalesced background reads, lightweight history projection, paged aggregate/audit views, bounded vocabulary/import lookup and atomic streaming/cancellable CSV; measured synthetic scale.

- 0.24: explicit serial recognition, inventory catalogs, guided workflows/reasons, administrative CSV, exact Dashboard navigation and durable/manual backup feedback.

- 0.23: discoverable print shortcut, offline Fluent actions and first canonical Assets module with movement audit, stable label links, current-asset Dashboard and protected schema-2 migration.

- 0.22: previewed transactional CSV inventory import, SQL-backed history pages, validated SQLite restore with safety copy, daily/retained backups, transferable design profiles, duplicate-asset command and inline serial warnings.

- 0.21: inventory-only history, Ctrl+P on Labels and correlated asynchronous Windows job progress with temporary notices.

- 0.20: fixed printer/paper-exit preview, first-edge marker and explicit distinction between physical stock dimensions and content rotation.

- 0.19: wrapped asset headings, dedicated free-text mode, paper-feed preview rails and automatic text-space reclamation when codes shrink.

- 0.18: text block centering, empty recycle bin and read-only Windows queue-status indicator.

- 0.17: full configuration export/import and QR/barcode size control.

- 0.16: startup payload integrity launcher, editable configuration validation, preserved corrupt-data diagnostics and isolated package mutation tests.

- 0.13: SQLite events and snapshots, Reports search/sort/load, CSV and backup, collapsible Asset details, footer About link.
- 0.14: inline right-side inclusion checkboxes and optional QR/barcode; text-only labels and blank-serial recovery.
- 0.15: installed-font selector, point size, bold, physical media orientation, complete-content rotation, recycle/restore and Dashboard.
- Documentation update: English source contracts/algorithm explanations, consolidated manuals, student walkthrough and troubleshooting.

## Pending

- Separate physical roll media size from reading/design orientation, with follow-up hardware validation (reported 100 × 50 media case).

1. Validate actual printing, calibration and scans with user hardware.
2. Define RFID identifiers/memory and implement write/read-back verification.
3. Synchronization is explicitly deferred: user confirmed local-only use for 0.22. Define server/API, access, offline/conflict rules when multiple devices are needed.

Future proposals require their own scope/authorization. Those performance follow-ups are implemented in 0.25. Millions-scale measurement, keyset pagination and broader indexed text search remain possible later work.

Potential later asset features: structured employee/site entities and hierarchy, procurement/attachments and permissions. These are proposals, not implemented or authorized automatically.

User accounts and permissions are explicitly deferred; keep current operation local without a login module. New module proposals awaiting selection: physical inventory reconciliation by scanner, asset photos/documents and batch label output. Recurring jobs, attachments and notifications can extend the delivered service module later.
