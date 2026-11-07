# Current status

Application version: 0.23.0. Project: https://github.com/dafermen/TagPrinter. Local checkout: C:/Projects/TagPrinter.

The application supports six asset fields, independent visibility, optional QR/Code 128, text-only labels, media presets/custom dimensions, fonts/bold/fitting, margins/offsets, media orientation and content rotation. It restores last work, prints through Windows or RAW ZPL, exports label/code PNG and raster ZPL, and records successful actions in SQLite.

Reports supports search/date/action filters, sorting, snapshot recovery without automatic printing, active/recycle-bin CSV, consistent database backup and reversible multiple-row deletion. Assets is the second module with independent canonical records, administrative state/location/custodian and append-only changes. Dashboard groups current assets and separates global label-log totals.

The interface remains English/Spanish, initially English. Source comments and technical/user documentation are English. The current release includes both interface and asset-administration changes.

Source documentation covers classes/records, member functions, saved properties, meaningful state variables, local helpers and complex blocks. The student guide explains C# concepts, units, events, rendering, native resource ownership and data flow. Historical changes belong in CHANGELOG; manuals describe current behavior.

Tests use synthetic data in isolated files/databases; no real user records or printer jobs are used. The live database is outside the portable and is not replaced by application updates. The schema-2 upgrade derives canonical assets from active existing serial history after making a protected prior backup; it does not import external output files.

Pending: physical hardware validation, RFID and multi-device synchronization (deferred by the user). No physical-print confirmation, event editing, synchronization or database encryption is implemented. See ROADMAP and VALIDATION.

License: MIT for TagPrinter source and project documentation, Copyright (c) 2026 Dario Meneses. The repository root LICENSE is copied into portable packages. Third-party licensing remains separate; the embedded Fluent assets have their own upstream MIT notice.

0.16: root launcher, embedded/external integrity manifest, SHA-256 verification, mutable config/data preflight and startup logs. See STARTUP_VALIDATION.md.

0.17: full application configuration transfer and adjustable QR/barcode size. Configuration includes current asset data/preferences/catalog; history, driver setup, fonts and transient view state remain separate. See CONFIGURATION_TRANSFER.md.

0.18 adds whole-block text centering, permanent recycle-bin purge and informational Windows printer status. Automated tests do not require a physical printer. The physical-media/design-orientation improvement remains pending and was not changed in this release.

0.19 adds measured multiline asset headings, independent free-text labels, user-selected nonprinting feed arrows and automatic allocation of space left by the actual symbol size. See FLEXIBLE_LABELS.md. Physical printer orientation changes remain deferred.

0.20 replaces configurable paper arrows with a fixed front-view printer/paper-exit diagram, physical roll dimensions and a first-edge marker. Content rotation keeps physical size unchanged. Legacy direction values remain round-trippable but have no visual effect. See PAPER_EXIT_PREVIEW.md; hardware driver/media validation is still pending.

0.21 excludes Free text from all new inventory events, hides marked legacy Free text from reports without deletion, adds Ctrl+P on Labels and displays a transient correlated Windows job notice. Driver/RAW submission runs off-thread; queue status is not physical-print confirmation. See PRINT_JOB_PROGRESS.md.

0.22 adds transactional previewed CSV registration (no printing), bounded SQL report pages, validated backup restore with safety copy, daily/retained SQLite backups, configuration-transferable label profiles, identity-clearing duplication and serial warnings. See INVENTORY_TOOLS.md. Usage remains local-only.

0.23 adds visible Print · Ctrl+P, clean home text and embedded Fluent icon/text actions. Assets supports explicit records, stable identity, state/location/custodian/notes, movements, label loading and canonical Dashboard aggregation. Schema 2 and backup compatibility are covered in ASSETS.md. Migrated/automatically registered records need administrative review; no physical state is inferred.
