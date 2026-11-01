# TagPrinter

Windows desktop app for inventory labels and local reports / Aplicación de etiquetas de inventario e historial local.

**C# · WinForms · .NET 8 · SQLite · Windows x64 · English / Español**

![TagPrinter — labels](docs/images/tagprinter-en.png)

## Download / Descargar

[Portable releases](https://github.com/dafermen/TagPrinter/releases/latest)

Extract the entire ZIP and open **TagPrinter.exe**. .NET and SQLite are included. Install your printer's Windows driver separately.

Extrae todo el ZIP y abre **TagPrinter.exe**. Incluye .NET y SQLite; no requiere instalar un servidor de base de datos. El controlador de la impresora se instala por separado.

## Features / Funciones

- Compact rows for asset type, brand, model, serial, part number and MAC address, with visibility checkboxes on the right.
- Optional QR / Code 128: turn off Code for text-only labels. A serial is optional for those labels.
- QR / Code 128 containing the serial, label sizes, four layouts, font scaling, margins and print offsets.
- **01 Asset details** collapsible and expanded at startup; sections 02–05 collapsed.
- **New asset**, visible printer/copies summary, automatic save and restore of the last work.
- **Export Tag**, **Export Code**, ZPL and Windows/RAW-ZPL printing.
- **Reports** tab: search by serial or asset fields, optional local-date/action filters, sortable columns, recover a label by clicking its serial.
- **SQLite history** of successful exports and print submissions, with a full design snapshot. Repeated serials are separate events.
- CSV export of the current sorted/filtered view or all history, using `;` and UTF-8.
- Database backup. GitHub icon at the bottom left with **About / Acerca de** tooltip.

![TagPrinter — reports](docs/images/reports-en.png)

## Quick test / Prueba rápida

1. Complete a label and export it with Export Tag or Export Code.
2. Open Reports: a new event should appear.
3. Search the serial; click a column header to sort.
4. Click its serial: the Labels tab loads the saved design. Review it and click Print yourself.
5. Export view CSV or Back up database as needed.

El historial empieza con operaciones realizadas desde v0.13. No se reconstruyen etiquetas de versiones anteriores y no se incluyen datos de ejemplo en la base real.

## Data and configuration

| File | Location | Purpose |
|---|---|---|
| config.json | Beside the EXE | Label sizes and startup defaults |
| settings.json | %LOCALAPPDATA%\InventoryLabels | Last work; compatible with older versions |
| TagPrinter.db | %LOCALAPPDATA%\TagPrinter | Permanent event history and saved designs |

Updates to the portable do not replace the history. Config defaults are overridden by the last work. See [configuration](docs/CONFIGURATION.md) and [history/reports](docs/HISTORY.md).

## Develop / Desarrollar

Windows with .NET 8 SDK:

```powershell
dotnet restore TagPrinter.sln
dotnet build TagPrinter.sln -c Release
dotnet run --project src/TagPrinter
powershell -ExecutionPolicy Bypass -File scripts/Test.ps1
powershell -ExecutionPolicy Bypass -File scripts/Publish.ps1
```

The publish script verifies the self-contained EXE and creates `artifacts/TagPrinter-v0.14-PORTABLE-Windows-x64.zip`. Generated output and personal databases are excluded from Git.

## Documentation

[User guide](docs/USER_GUIDE.md) · [History and reports](docs/HISTORY.md) · [Configuration](docs/CONFIGURATION.md) · [Architecture](docs/ARCHITECTURE.md) · [Validation](docs/VALIDATION.md) · [Changelog](docs/CHANGELOG.md) · [Status](docs/STATUS.md) · [Roadmap](docs/ROADMAP.md)

## Limits

**RFID chip encoding and verification are not implemented.** Print submissions mean the app's print call completed; they do not confirm physical printing. Test a real sample before batches. A history load never prints automatically.

Los CSV conservan valores originales y fechas UTC; al importarlos, trata el serial y MAC como texto para mantener ceros iniciales. La tabla muestra fecha/hora local. No hay importación masiva de inventario ni borrado de registros en esta versión.

## Dependencies and artwork

- ZXing.Net 0.16.11 — [project](https://github.com/micjahn/ZXing.Net), Apache-2.0.
- Microsoft.Data.Sqlite 8.0.28 / SQLitePCLRaw — SQLite provider and native engine distributed through NuGet.
- .NET runtime — [license and notices](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT).
- GitHub mark from the [official brand assets](https://brand.github.com/foundations/logo), used only as a link to this project. GitHub marks are trademarks of GitHub, Inc.; no affiliation or endorsement is implied.

## v0.14: filas compactas y código opcional

Cada campo tiene su nombre, control y checkbox en la misma fila. Los checkboxes solo incluyen/ocultan el elemento en la etiqueta; no borran datos ni deshabilitan edición. Sus tooltips y nombres accesibles explican la acción.

En 01, la fila Código permite elegir QR/Code 128 y activar/desactivar su inclusión. Sin código, los textos usan toda el área y el serial puede estar vacío. Se bloquean las etiquetas completamente vacías. Vista previa, Export Tag, ZPL e impresión comparten ese diseño. Export Code es independiente: siempre genera un símbolo y necesita serial.

La selección se guarda y se recupera del historial. En Reportes las etiquetas sin serial muestran (Sin serial) / (No serial) como enlace; se recuperan por su ID, sin inventar un serial. Los datos anteriores siguen mostrando su código por defecto.
