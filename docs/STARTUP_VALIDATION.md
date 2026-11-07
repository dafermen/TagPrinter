# Startup validation — 0.16

## How to start

Extract the complete ZIP into a fresh folder. Open **TagPrinter.exe at the root**, not app/TagPrinter.exe. Keep config.json at the root for editable presets. User settings/history remain at their existing LocalAppData locations.

The root executable is a small .NET Framework 4.x Windows launcher. Windows 10/11 include this OS component; it does not need or load the bundled .NET 8 files in app/. The actual WinForms application and self-contained .NET 8 runtime are in app/. No separate .NET 8 installation is required.

## Immutable files

Publishing creates integrity-manifest.xml from every immutable published payload file, including runtime, application assemblies, dependencies, native SQLite and runtime JSON files. It records relative path, byte length and SHA-256 and embeds an identical copy in the launcher.

Before loading the payload the launcher compares the external manifest to its embedded copy, checks every listed file exists/is readable, and verifies size/hash. A same-length altered byte is detected. Missing/changed manifests block launch. Cloud-backed files may hydrate when read; the launcher hashes their actual bytes. This can add time on slower disks or cloud/network storage.

The manifest is a corruption/incomplete-copy check, not a digital publisher signature. It cannot establish authenticity if someone replaces the whole package/launcher. The launcher cannot repair itself or diagnose its own OS-loader failure. System Windows components, printer drivers, hardware, unexpected extra files and later changes after hashing are outside this guarantee. Always obtain the ZIP from the trusted project release.

## Mutable data

Root config.json must exist and be readable JSON object data (maximum 1 MB in the launcher). It is deliberately excluded from hashes. The application then validates schema, presets and supported ranges. Valid edits do not become corruption warnings.

Before opening the main form, existing last-work JSON is parsed. Malformed saved work blocks startup and is logged instead of being silently replaced by defaults/autosave. A missing last-work file is normal. Preserve the damaged file before restoring or renaming it manually.

SQLite runs PRAGMA quick_check before schema initialization. Detected corruption blocks opening and is logged; the app does not recreate/delete the database. This is SQLite structural checking, not complete semantic validation of every snapshot or a replacement for backups. New empty databases initialize normally. Errors encountered later are also reported/logged where possible.

## Diagnostics and recovery

Failure messages list affected files (first twelve on screen; complete list in the log). Logs append UTC timestamps to %LOCALAPPDATA%/TagPrinter/logs/startup-YYYYMMDD.log. The launcher selects Spanish from a readable saved Language value, otherwise English. Logging failure does not hide the primary diagnostic. Log files may contain local paths/error details; review before sharing.

For damaged program files, download again if necessary and extract the entire ZIP into a new folder. Preserve custom config.json and user data. Do not mix DLLs from different releases. For bad config/settings/database, preserve originals and restore known valid copies; the application never deletes them automatically. Existing database backup instructions still apply, including WAL/SHM handling with all instances closed.

## Verification

Build-Launcher.ps1 creates the manifest and compiles Launcher.cs with the Windows Framework C# compiler. Publish.ps1 runs the root launcher's --self-test, which validates files, starts the child with isolated self-tests and propagates its exit code. Test-Integrity.ps1 then mutates only a fresh disposable package copy under artifacts: missing application/native/runtime files, missing/changed manifest, same-size corrupt runtime, malformed/missing config, allowed whitespace edit and restoration.

--verify-only checks package integrity and config JSON syntax without opening the application; it does not run full AppConfig schema/SQLite/last-work checks. --report writes an explicit test report. Verify-only does not write live startup logs or user data. Normal launch performs the application-side checks as well.

Schema 3 adds vocabulary and movement reasons. Opening version 1 makes a protected before-assets copy; opening version 2 makes a protected before-catalogs copy before transactional migration. If that backup/migration cannot complete, startup fails without silently deleting/replacing inventory. See ASSETS.md for rollback and old-backup restore behavior.
