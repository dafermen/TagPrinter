# Configuration and data contracts — 0.25

## Three separate stores

| File | Location | Responsibility |
|---|---|---|
| config.json | Beside EXE; source src/TagPrinter/config.json | Installation media catalog and startup defaults. |
| settings.json | %LOCALAPPDATA%/InventoryLabels | Last edited values/preferences; historical path retained for compatibility. |
| TagPrinter.db | %LOCALAPPDATA%/TagPrinter | Master assets/audit/reasons/catalogs, completed events, design snapshots and recycle bin. |

Close the app before manually editing configuration. Installation defaults apply first; last work overrides them. Changing defaults does not reset existing work. The catalog is always loaded from the current installation and never configures driver media automatically.

## Example config.json

```json
{
  "SchemaVersion": 1,
  "LabelSizes": [
    { "Id": "100x50", "Name": "100 × 50 mm", "WidthMm": 100, "HeightMm": 50 },
    { "Id": "75x35", "Name": "75 × 35 mm", "WidthMm": 75, "HeightMm": 35 }
  ],
  "Defaults": {
    "Language": "en",
    "LabelPresetId": "100x50",
    "Dpi": 203,
    "Layout": 3,
    "Qr": false,
    "ShowCode": true,
    "AutoFont": true,
    "FontPercent": 100,
    "MarginMm": 2
  }
}
```

The distributed catalog includes 100×50, 50×25, 60×40, 80×50 mm, 4×2 and 4×6 inch examples. Custom is added by the UI and must not be a catalog ID.

## Validated installation values

- SchemaVersion: 1; property names are case-insensitive. JSON comments are not supported.
- LabelSizes: 1–100 presets with unique nonempty IDs and names. ID custom is reserved.
- Catalog dimensions: WidthMm 25–150, HeightMm 20–200 mm; at most one decimal place. These catalog bounds differ from custom editor dimensions (20–200 mm each), which permit orientation swapping.
- Language: en/es; LabelPresetId must identify a catalog preset.
- Dpi: actual resolution 203/300. Layout: 0 bottom-center, 1 bottom-left, 2 bottom-right, 3 beside text.
- Qr: true selects QR; false selects Code 128. ShowCode: whether a fresh label includes the symbol.
- AutoFont: enables fitting. FontPercent: 50–200 manual percentage. MarginMm: 1–5, one decimal place.

AppConfig does not expose font family, base point size, bold, rotation or history paths as installation Defaults. Set those in the UI; they are saved in WorkState. Unknown JSON properties are not configuration options and should not be relied on.

If config.json is missing, built-in values are used. Existing malformed/null/invalid files produce an error and are not overwritten.

## Saved-work meanings

WorkState stores six text fields and their inclusion flags, ShowCode, language, printer, copies, actual Width/Height, stable LabelPresetId and selector indexes. Code is 0 Code128/1 QR; Mode is 0 Windows/1 RAW ZPL; Dpi is an index (0=203, 1=300), unlike config Defaults.Dpi.

FontFamily is an installed name; FontSizePt is 4–72 body points; heading size is 1.3 times the body. Bold affects all text and HeadingBold independently affects the heading. ContentRotation is 0/90/180/270 degrees clockwise. Offsets are -5 to +5 mm before rotation; ShowGuides affects preview only. Supported copies are 1–999.

Missing new fields retain property initializers. Legacy Title/ShowTitle map to Brand/ShowBrand only when new keys are absent. Missing AssetType remains empty. Last-work writes use a sibling temporary file and replacement; this is not a multi-instance concurrency guarantee.

## Catalog changes and history

Stable IDs preserve selection across reordering. If a saved ID is removed, legacy dimension matching may recover an equivalent preset; otherwise the size becomes Custom. Editing an existing preset's dimensions updates ordinary last-work selection when that preset is applied. Keep IDs stable and update LabelPresetId when removing its default.

History loading deliberately forces Custom dimensions, preserving the original recorded design regardless of catalog changes. It also keeps current language and clears unavailable printers. See [history](HISTORY.md) for database scope and backups.

## Startup checks (0.16)

Open the root TagPrinter.exe to validate the immutable app/ payload before loading it. Editable config.json stays at the portable root; last work and SQLite are checked separately without deleting damaged data. See [startup validation](STARTUP_VALIDATION.md) for manifests, logs, recovery and limits.

## Full settings transfer and CodePercent

05 Settings exports/imports a complete versioned ConfigurationBundle (catalog/defaults plus all current WorkState values). Import replaces rather than merges, with confirmation, automatic backup and I/O rollback. See [configuration transfer](CONFIGURATION_TRANSFER.md) for scope and limits. Defaults.CodePercent and WorkState.CodePercent are integers 25–200, default 100, preserving older designs. Root config remains editable and excluded from payload hashes.

## Centering, recycle-bin purge and printer status — 0.18

03 Text and margins adds Center text for the whole text block. Reports > Recycle bin adds irreversible Empty recycle bin with all-filter scope confirmation. The visible printer summary now includes an automatically refreshed Windows status indicator. CenterText persists in work/history/full transfer; live status does not. See [centering, recycle-bin purge and Windows status](TEXT_TRASH_PRINTER_STATUS.md).

Work snapshots also persist FreeTextMode, FreeText (up to 2000 characters), ShowFeedGuide and legacy FeedDirection (0–3, retained but ignored). These are included in full configuration transfer; the fixed printer diagram affects only the preview. See FLEXIBLE_LABELS.md.

Since 0.20, ShowFeedGuide controls the fixed printer/paper-exit diagram. FeedDirection is a retained legacy field with no effect; no feed-direction selector is shown.

## Inventory tools — 0.22

AppConfig.Profiles stores up to 100 unique named design-only WorkState snapshots; Apply preserves current identity/language/printer/copies. AppConfig.AutomaticBackups defaults to true and controls the daily policy. Both are included in full configuration transfer and persisted to editable root config.json by the UI; they are not stored/restored with the inventory SQLite file. See INVENTORY_TOOLS.md.

WorkState.AssetUid optionally retains the selected local master identity. Profiles clear it on capture and preserve the current selection on application. It is not a master-data backup; configuration transfer alone does not create asset assignments or synchronize a database.

Inventory vocabulary is stored in SQLite asset_catalog and restored with database snapshots. It is separate from ConfigurationBundle.Catalog (installation media/defaults/profiles). TagPrinter.db.backup-status.json beside the database records last success/path/attempt/failure independently of inventory restoration. See ASSET_OPERATIONS.md.
