# Estado

Versión 0.15.0 — C:\Projects\TagPrinter — https://github.com/dafermen/TagPrinter.

Completado: sección 01 plegable/expandida; historial SQLite de operaciones con fecha y diseño; Reportes con búsqueda, acciones/fechas, ordenación, CSV filtrado/completo y backup; recuperación por serial sin impresión automática; icono GitHub inferior izquierdo con tooltip bilingüe. Se conservan las funciones previas de etiquetas, configuración y último trabajo.

Las pruebas/capturas usan datos sintéticos aislados. La base real empieza vacía y fuera del portable. No se importan automáticamente etiquetas antiguas.

Pendiente: prueba de impresión física del usuario, RFID, importación de inventario y eventual paginación de historiales grandes. No hay confirmación física de impresión ni edición de los registros.

## v0.14: filas compactas y código opcional

Cada campo tiene su nombre, control y checkbox en la misma fila. Los checkboxes solo incluyen/ocultan el elemento en la etiqueta; no borran datos ni deshabilitan edición. Sus tooltips y nombres accesibles explican la acción.

En 01, la fila Código permite elegir QR/Code 128 y activar/desactivar su inclusión. Sin código, los textos usan toda el área y el serial puede estar vacío. Se bloquean las etiquetas completamente vacías. Vista previa, Export Tag, ZPL e impresión comparten ese diseño. Export Code es independiente: siempre genera un símbolo y necesita serial.

La selección se guarda y se recupera del historial. En Reportes las etiquetas sin serial muestran (Sin serial) / (No serial) como enlace; se recuperan por su ID, sin inventar un serial. Los datos anteriores siguen mostrando su código por defecto.

## v0.15 — Fuente, orientación, papelera y Dashboard

- Fuente instalada en Windows, tamaño base 4–72 pt, negrilla general y encabezado en negrilla independiente. El encabezado conserva proporción 1.3; ajuste automático y escala porcentual siguen disponibles. El tamaño efectivo puede reducirse con ajuste automático. Si falta la fuente al mover el portable, se usa Arial al recuperar el trabajo.
- En 02: horizontal/vertical intercambia ancho y alto y pasa a tamaño personalizado. Rotación del contenido 0/90/180/270 gira texto y código juntos dentro del tamaño físico elegido; no gira solo el texto. Vista previa, PNG, ZPL e impresión usan el mismo resultado. Export Code permanece independiente.
- Reportes permite seleccionar varias filas, Eliminar y confirmar. Papelera muestra los registros retirados y permite restaurarlos. No se borran archivos PNG/ZPL ni trabajos en la impresora. Backup incluye la papelera; CSV exporta registros activos o de la papelera según la vista.
- Dashboard muestra agrupaciones tipo + marca + modelo, activos únicos por serial (sin distinguir mayúsculas y con espacios extremos eliminados), acciones, exportaciones PNG de etiquetas, trabajos y copias enviados. Fechas y búsqueda limitan los eventos analizados: se toma el registro más reciente de cada serial dentro de esos resultados. Sin serial se informa aparte como cantidad de acciones, no de activos. Los registros en papelera no cuentan. Doble clic abre los registros del grupo en Reportes; la vista de detalle muestra todo su historial activo.
- Trabajos enviados y copias no confirman impresión física. El Dashboard representa historial de etiquetas, no inventario verificado. Identificadores reutilizados pueden representar activos distintos; por ahora un serial identifica un activo.
- Estado de formato recuperable en último trabajo y snapshots históricos. Registros antiguos conservan Arial 10 pt, encabezado resaltado y rotación 0. SQLite añade tabla trash sin alterar los registros anteriores. Los historiales grandes todavía se cargan en memoria.
