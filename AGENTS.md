# TagPrinter

- Solución TagPrinter.sln; aplicación src/TagPrinter/TagPrinter.csproj.
- Mantener inglés/español, inglés inicial y recuperación del idioma guardado.
- Conservar compatibilidad con %LOCALAPPDATA%/InventoryLabels/settings.json.
- Pruebas con configuración aislada, sin enviar trabajos a impresoras.
- Actualizar docs/STATUS.md, CHANGELOG.md y VALIDATION.md con los cambios.
- Verificar con scripts/Test.ps1; publicar con scripts/Publish.ps1.
- No versionar bin/obj/artifacts. Distribuir toda la carpeta portable.
- No confundir éxito del spooler con impresión física o grabación RFID.
