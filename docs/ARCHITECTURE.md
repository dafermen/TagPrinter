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

## v0.15 — Fuente, orientación, papelera y Dashboard

- Fuente instalada en Windows, tamaño base 4–72 pt, negrilla general y encabezado en negrilla independiente. El encabezado conserva proporción 1.3; ajuste automático y escala porcentual siguen disponibles. El tamaño efectivo puede reducirse con ajuste automático. Si falta la fuente al mover el portable, se usa Arial al recuperar el trabajo.
- En 02: horizontal/vertical intercambia ancho y alto y pasa a tamaño personalizado. Rotación del contenido 0/90/180/270 gira texto y código juntos dentro del tamaño físico elegido; no gira solo el texto. Vista previa, PNG, ZPL e impresión usan el mismo resultado. Export Code permanece independiente.
- Reportes permite seleccionar varias filas, Eliminar y confirmar. Papelera muestra los registros retirados y permite restaurarlos. No se borran archivos PNG/ZPL ni trabajos en la impresora. Backup incluye la papelera; CSV exporta registros activos o de la papelera según la vista.
- Dashboard muestra agrupaciones tipo + marca + modelo, activos únicos por serial (sin distinguir mayúsculas y con espacios extremos eliminados), acciones, exportaciones PNG de etiquetas, trabajos y copias enviados. Fechas y búsqueda limitan los eventos analizados: se toma el registro más reciente de cada serial dentro de esos resultados. Sin serial se informa aparte como cantidad de acciones, no de activos. Los registros en papelera no cuentan. Doble clic abre los registros del grupo en Reportes; la vista de detalle muestra todo su historial activo.
- Trabajos enviados y copias no confirman impresión física. El Dashboard representa historial de etiquetas, no inventario verificado. Identificadores reutilizados pueden representar activos distintos; por ahora un serial identifica un activo.
- Estado de formato recuperable en último trabajo y snapshots históricos. Registros antiguos conservan Arial 10 pt, encabezado resaltado y rotación 0. SQLite añade tabla trash sin alterar los registros anteriores. Los historiales grandes todavía se cargan en memoria.
