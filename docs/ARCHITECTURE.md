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

## v0.13: historial y reportes

HistoryStore usa Microsoft.Data.Sqlite 8.0.28. Esquema events con metadatos indexados y WorkState completo en JSON, UTC, referencia al evento recuperado y versiones PRAGMA. Consultas parametrizadas, WAL y backup nativo.

ReportsView enlaza un DataTable ordenable a DataGridView con seriales como enlaces. Convierte filtros de fechas locales a límites UTC. CSV independiente con escaping de comillas y separador punto y coma. MainForm.History integra tabs, icono About y hooks tras operaciones exitosas. La recuperación fuerza dimensiones históricas personalizadas y conserva el idioma actual.

Las pruebas reciben configPath/historyPath aislados. En uso normal: settings.json histórico en InventoryLabels y TagPrinter.db en LocalAppData/TagPrinter. No se hace una transacción atómica conjunta con spooler o archivos: un fallo de historial tras una operación externa se comunica sin reenviar trabajos.

## v0.14

ShowCode se añade como propiedad opcional true a LabelData/WorkState/ConfigDefaults. El render evita el encoder cuando está desactivado y utiliza toda el área para texto. HistoryStore permite serial vacío solo en eventos de etiqueta sin código, y Reports usa el ID para recuperar incluso cuando el texto de enlace es (No serial). No cambia el esquema SQLite. La cuadrícula de datos usa filas AutoSize con nombres accesibles y ToolTip por checkbox.
