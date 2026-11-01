# Arquitectura

- MainForm: datos, controles, impresión Windows y diálogos de exportación.
- LabelRenderer: etiqueta completa, textos, fuente, distribución, márgenes, guías y ZPL.
- CodeRenderer: símbolo independiente, escala entera y zonas blancas explícitas. No utiliza datos de la plantilla.
- RawPrinter: envío RAW por spooler Windows.
- WorkStore: JSON atómico del último trabajo.
- Ui: catálogo inglés/español.
- SelfTest: comprobaciones, lectura de PNG y capturas con estado aislado.

Vista previa: LabelRenderer con guías opcionales. PNG completo, Windows y ZPL: mismo render sin guías. Solo código: CodeRenderer, independiente de si la plantilla cabe.

ZXing.Net 0.16.11 genera/decodifica. Code 128 valida ASCII; QR permite UTF-8. Resoluciones: 203/300 DPI.

Persistencia: %LOCALAPPDATA%\InventoryLabels\settings.json conserva compatibilidad con TagPrint. El producto, EXE, namespace, proyecto y solución ahora se llaman TagPrinter.

Perfil Portable: win-x64 autocontenido, sin trimming ni single-file. Distribuir toda la carpeta. El controlador de impresora se instala aparte.

Límites: no hay codificación RFID, base de datos de inventario ni historial de todos los trabajos. El spooler no confirma salida física. Las muestras requieren prueba real.

## v0.9

AppConfig valida config.json y proporciona catálogo de LabelSize con Id estable y ConfigDefaults. MainForm aplica defaults antes de recuperar WorkStore. LabelData/WorkState incluyen Brand, PartNumber, MacAddress y sus flags. WorkStore migra Title/ShowTitle; conserva la ruta histórica. Los campos opcionales vacíos se omiten del render. config.json se copia a build/publicación mediante MSBuild. El icono también se incorpora como recurso para que las capturas usen el icono del producto.
