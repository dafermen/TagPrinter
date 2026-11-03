# User guide — TagPrinter 0.16

## Start and update

Extract the entire Windows x64 ZIP and open TagPrinter.exe. The portable includes .NET and SQLite; install the Windows printer driver separately. Keep the full folder together. Data is stored under your Windows user profile, outside the portable, so replacing the application folder does not replace history.

English is the initial language. Change it in the collapsed 05 Settings section; the application remembers the selection. Asset text is never translated.

## Create a label

01 Asset details starts expanded. Enter asset type, brand, model, serial, part number and MAC address. The checkbox to the right of each input includes/hides its text without erasing the value. Asset type is the heading; brand becomes the heading if type is absent.

The Code row chooses QR or Code 128 and whether the complete label includes a symbol. Both encode the serial. Code 128 accepts printable ASCII; use QR for other characters. Hiding serial text does not remove the serial from the symbol.

Turn off Code for text-only output. Text then uses the entire content area and serial becomes optional. At least one nonempty visible text field is required. New asset clears the six text fields and loaded-record reference, retaining design/printer preferences.

## Media and orientation — section 02

Choose a catalog preset or Custom dimensions in millimeters. Horizontal/vertical swaps width and height and selects Custom. Supported custom dimensions in the editor are 20–200 mm for each side; catalog validation has its own bounds documented in CONFIGURATION.md. A printer must physically support the chosen media.

Choose one of four layouts: code below centered, below left, below right or to the right of text. When the code is hidden, this selection does not constrain text space.

Content rotation is 0, 90, 180 or 270 degrees clockwise. It rotates text and symbol together inside the selected physical media. It does not rotate text independently. The preview shows the resulting output.

## Fonts and margins — section 03

Select an installed Windows font, body base size from 4 to 72 points, general Bold and independent Bold heading. The heading is 1.3 times the body size. Font files are not embedded; missing saved fonts fall back to Arial in the editor.

Fit automatically shrinks text from 100 down to 50 percent when necessary. Plus/minus changes scale in ten percentage-point increments and switches to manual mode. An oversized design shows an error instead of silently clipping. For an exact requested size, disable automatic fitting and check the preview.

Inner margin is 1–5 mm. The optional guide is preview-only. Print offsets shift content by up to 5 mm horizontally/vertically before rotation; shifts outside the physical label are rejected.

![Font controls and rotated portrait label](images/format-en.png)

## Print — section 04

Select an installed printer, resolution (203/300 DPI), copies and method. Windows driver works through PrintDocument; direct ZPL is for printers that understand ZPL. Selecting a printer model alone does not establish ZPL compatibility.

The visible summary repeats destination, copies and method. Check it before Print labels. Also configure material, calibration and scaling in the driver. Print a sample and scan it before a batch. A successful submission does not confirm physical output or RFID encoding.

## Export

| Button | Result | Default filename |
|---|---|---|
| Export Tag | Full-label PNG without guides | serial.png, or label.png if serial is empty |
| Export Code | Standalone QR/barcode with white quiet zones | serial_QR.png or serial_barcode.png |
| Save ZPL | Full-label raster commands and quantity | serial.zpl, or label.zpl |

Export Code works independently of the Code checkbox and full-label layout; it always needs a valid serial. Names are sanitized for Windows. Canceling a dialog does not create a successful event.

## Automatic saving

Edits are saved after a short delay and when closing. Reopening restores the last values/preferences. This is independent of output history: autosave does not add report rows. If a save fails, the status area reports it.

## Reports and recycle bin

Reports lists completed exports/submissions. Search any asset field, optionally restrict action/dates and click headers to sort. Serial links (including No serial) load the original design without printing. Review it before sending again. Missing historical printers are left unselected.

Select one or multiple rows, click Delete and confirm to recycle them. Check Recycle bin and use Restore to recover selected events. Original PNG/ZPL files and printer queues are unaffected. Export view CSV preserves visible sorting; Export all CSV exports all rows in the current active/trash scope. Back up database copies both scopes.

![Reports and recycle-bin controls](images/reports-en.png)

## Dashboard

Search and optional dates restrict active events. The grouping table shows asset type + brand + model and quantity of unique normalized serials. Repeated actions for the same serial count once using its latest matching snapshot. Blank serials are reported separately as actions. Group spelling/case differences can create separate groups.

Summary indicators separate actions, PNG tag exports, submitted jobs and requested copies. They are not physical inventory/print verification. Double-click a group to open its full active matching history in Reports; Dashboard search/date restrictions are not carried over.

![Dashboard](images/dashboard-en.png)

## Further reading

- [Configuration and data locations](CONFIGURATION.md)
- [History, reporting and backups](HISTORY.md)
- [Troubleshooting](TROUBLESHOOTING.md)
- [Student source walkthrough](STUDENT_GUIDE.md)
- [Project](https://github.com/dafermen/TagPrinter) — also opened by the lower-left GitHub icon with About tooltip.

## Startup checks (0.16)

Open the root TagPrinter.exe to validate the immutable app/ payload before loading it. Editable config.json stays at the portable root; last work and SQLite are checked separately without deleting damaged data. See [startup validation](STARTUP_VALIDATION.md) for manifests, logs, recovery and limits.
