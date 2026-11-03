# Current status

Application version: 0.15.0. Project: https://github.com/dafermen/TagPrinter. Local checkout: C:/Projects/TagPrinter.

The application supports six asset fields, independent visibility, optional QR/Code 128, text-only labels, media presets/custom dimensions, fonts/bold/fitting, margins/offsets, media orientation and content rotation. It restores last work, prints through Windows or RAW ZPL, exports label/code PNG and raster ZPL, and records successful actions in SQLite.

Reports supports search/date/action filters, sorting, snapshot recovery without automatic printing, active/recycle-bin CSV, consistent database backup and reversible multiple-row deletion. Dashboard groups latest matching normalized serials by type/brand/model and separates action/job/copy counts.

The interface remains English/Spanish, initially English. Source comments and technical/user documentation are English. The documentation update changes comments/manuals only, enables XML documentation output and keeps application behavior/version unchanged.

Source documentation covers classes/records, member functions, saved properties, meaningful state variables, local helpers and complex blocks. The student guide explains C# concepts, units, events, rendering, native resource ownership and data flow. Historical changes belong in CHANGELOG; manuals describe current behavior.

Tests use synthetic data in isolated files/databases; no real user records or printer jobs are used. The live database is outside the portable and is not replaced by application updates. No historical output import occurs automatically.

Pending: physical hardware validation, RFID, bulk import, pagination and optional backup-restoration UI. No physical-print confirmation, permanent event deletion, event editing, synchronization or database encryption is implemented. See ROADMAP and VALIDATION.

License: MIT for TagPrinter source and project documentation, Copyright (c) 2026 Dario Meneses. The repository root LICENSE is copied into portable packages. Third-party licensing remains separate. Application version is unchanged.
