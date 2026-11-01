# Estado

Versión 0.14.0 — C:\Projects\TagPrinter — https://github.com/dafermen/TagPrinter.

Completado: sección 01 plegable/expandida; historial SQLite de operaciones con fecha y diseño; Reportes con búsqueda, acciones/fechas, ordenación, CSV filtrado/completo y backup; recuperación por serial sin impresión automática; icono GitHub inferior izquierdo con tooltip bilingüe. Se conservan las funciones previas de etiquetas, configuración y último trabajo.

Las pruebas/capturas usan datos sintéticos aislados. La base real empieza vacía y fuera del portable. No se importan automáticamente etiquetas antiguas.

Pendiente: prueba de impresión física del usuario, RFID, importación de inventario y eventual paginación de historiales grandes. No hay confirmación física de impresión ni borrado/edición del historial.

## v0.14: filas compactas y código opcional

Cada campo tiene su nombre, control y checkbox en la misma fila. Los checkboxes solo incluyen/ocultan el elemento en la etiqueta; no borran datos ni deshabilitan edición. Sus tooltips y nombres accesibles explican la acción.

En 01, la fila Código permite elegir QR/Code 128 y activar/desactivar su inclusión. Sin código, los textos usan toda el área y el serial puede estar vacío. Se bloquean las etiquetas completamente vacías. Vista previa, Export Tag, ZPL e impresión comparten ese diseño. Export Code es independiente: siempre genera un símbolo y necesita serial.

La selección se guarda y se recupera del historial. En Reportes las etiquetas sin serial muestran (Sin serial) / (No serial) como enlace; se recuperan por su ID, sin inventar un serial. Los datos anteriores siguen mostrando su código por defecto.
