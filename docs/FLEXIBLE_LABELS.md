# Flexible text and paper-feed reference — 0.19

## Wrapped asset headings

Asset type wraps onto additional lines at the available text width. Its measured height is reserved before placing subsequent fields; the ending is not silently clipped. Automatic fitting can reduce the font to 50 percent. If complete content still cannot fit, the preview explains the overflow and disables printing. Enlarge the media, reduce the font or shorten the content. No finite label can hold unlimited text.

## Free text only

In 01 Asset details, enable **Free text only** and write up to 2000 characters, including explicit line breaks. Only this text is rendered: no asset fields, serial or symbol is required. Fonts, bold, centering, margins and content rotation still apply. Blank content is rejected. Switching modes keeps the asset editors' values in current work/autosave/configuration; **New asset** clears both sets of text.

Free text produces no new inventory database events, so it does not appear in Reports or Dashboard. Last-work autosave and full configuration transfer still preserve the draft. Old snapshots explicitly marked FreeTextMode are hidden without deletion. Standalone Export Code remains independent and uses the asset serial; it requires a valid serial even in free-text mode.

## Space returned by smaller codes

The existing percentage still chooses whole-pixel modules relative to a fitted baseline. After choosing the actual symbol dimensions (including its quiet zones), the renderer gives the remaining width or height to text, with an additional gap. Reducing a right-side symbol releases width; reducing a bottom symbol releases height. Enlarging it consumes that space. Quiet zones are never removed and the bitmap is not stretched. A percentage change may produce the same width until the next integral module step, particularly for Code 128.

This intentionally changes text placement even at 100 percent if the old reservation exceeded the actual code size. Existing history/configuration retains its settings, but re-rendering uses the new layout. Standalone code output is unchanged.

## Printer and paper-exit preview

Version 0.20 replaces the user-selected arrows with a fixed printer, downward feed path and first-edge marker. Content rotates inside unchanged physical stock. See [Printer and paper-exit preview](PAPER_EXIT_PREVIEW.md).

FreeTextMode/FreeText and preview visibility survive autosave/restart, full configuration transfer and older output-history snapshot compatibility. The old FeedDirection value is retained for compatibility but no longer affects the diagram.

## Implementation and verification

LabelRenderer measures wrapped field heights and uses actual symbol dimensions to allocate remaining space. PaperFeedPreview draws the printer scene around the existing image, outside exported/printed pixels. WorkState carries new fields with compatible defaults; configuration transfer bounds free text to 2000 characters and the preserved legacy direction to 0–3.

LayoutImprovementsTests checks real pixels, the final character of a wrapped heading, text inside the reclaimed QR region, free-text field isolation, blank rejection, inventory exclusion/transfer/restart and byte-identical PNG output with the printer scene on/off. Existing decode and rotation tests remain in the full suite. Physical printer/scanner confirmation is deferred until hardware is available.
