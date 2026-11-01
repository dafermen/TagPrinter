# Historial SQLite y Reportes — v0.13

## Qué se registra

Cada operación completada genera un evento independiente, aunque el serial se repita:

| Acción estable | Significado |
|---|---|
| ExportTag | Se guardó el PNG de la etiqueta completa |
| ExportCode | Se guardó el PNG del código |
| ExportZpl | Se guardó el archivo ZPL |
| PrintSubmitted | Terminó la llamada de envío al sistema de impresión |

Se conserva fecha/hora UTC, los seis campos, todos los valores/checkboxes del diseño, resolución, dimensiones, fuente, márgenes, offsets, impresora, copias, ruta del archivo exportado y registro origen cuando se recuperó de Reportes.

Una fila representa una operación; la columna Copias indica la cantidad solicitada. **PrintSubmitted no confirma salida física ni RFID**. No se graba una fila por pulsación, cambio de vista previa, Nuevo activo o guardado de preferencias. Una exportación cancelada o fallida no crea un evento exitoso.

Si el archivo/impresión terminó pero la escritura del historial falla, se muestra un aviso independiente. No se reenvía automáticamente el trabajo. No existe una transacción conjunta entre Windows/archivos externos y SQLite.

## Ubicación y compatibilidad

La base real está en `%LOCALAPPDATA%\TagPrinter\TagPrinter.db`. No está dentro del portable y no se reemplaza al actualizar. Los archivos `-wal` y `-shm` pueden existir mientras SQLite está abierto; no deben manipularse mientras la aplicación esté en uso.

El historial empieza desde v0.13, sin importar automáticamente archivos de versiones anteriores. Los datos sintéticos de las capturas/pruebas nunca se insertan en la base del usuario. `config.json` sigue separado del historial y del último trabajo.

## Buscar, filtrar y ordenar

Abre Reportes / Reports. Escribe parte del serial o de otro dato, elige el campo y pulsa Buscar / actualizar o Enter. «Todos los campos» busca en los seis datos del activo. Los caracteres `%` y `_` se buscan literalmente. La comparación de mayúsculas de SQLite es insensible para ASCII.

La acción se puede filtrar con su selector. Las fechas están desactivadas por defecto: marca Desde y/o Hasta para usarlas. Se interpretan como fechas locales, incluyendo todo el día Hasta. «Limpiar filtros» vuelve a mostrar todo. Pulsa los encabezados para ordenar por fecha, serial, copias u otra columna. Desplázate horizontalmente para ver impresora y columnas adicionales.

## Recuperar para reimprimir

Pulsa el serial o selecciona una fila y usa Cargar etiqueta. Se cambia a Etiquetas y se recuperan datos y diseño, sin imprimir automáticamente ni crear otro evento.

Las dimensiones se recuperan como Personalizado para respetar el diseño histórico incluso si el catálogo fue modificado. Se conserva el idioma actual de la interfaz. Si la impresora guardada no está instalada, queda sin seleccionar: elige una disponible en 04 antes de imprimir. Revisa siempre la vista previa.

Cuando vuelvas a exportar o enviar a impresión, se crea un nuevo evento con el registro origen. Nuevo activo borra esa referencia y limpia solo los datos del activo.

## Exportar CSV

- Exportar vista CSV: filas actualmente mostradas, respetando su orden.
- Exportar todo CSV: todos los eventos, sin filtros.

Separador `;`, UTF-8 con BOM y todas las celdas entre comillas. Las comillas, punto y coma y saltos de línea se conservan correctamente. Fechas ISO-8601 en UTC, números con formato invariante. Los encabezados/acciones estables están en inglés para facilitar importación en bases de datos.

Incluye StateJson con la instantánea completa del diseño. Al importar en Excel u otra herramienta, declara serial/MAC/No. de parte como texto para conservar su representación. Exportar un reporte no crea eventos de etiquetas.

## Copia de seguridad

«Copia de seguridad» genera una copia SQLite consistente con su API de backup, incluso con la aplicación abierta. No permite elegir la base activa como destino. Conserva las copias fuera de la carpeta del portable.

No hay botón de restauración en esta versión. Para restaurar manualmente, cierra todas las instancias, conserva una copia de la carpeta de datos actual y reemplaza la base por una copia válida; no mezcles archivos WAL/SHM de bases distintas.

## Modelo y límites

SQLite con esquema user_version=1, índices por serial y fecha, inserciones parametrizadas y sin edición/borrado de eventos desde la interfaz. La tabla carga los resultados de la búsqueda; un historial muy grande podría requerir paginación en una versión posterior. Los filtros por texto parcial pueden recorrer filas. La base local no está cifrada ni sincronizada con otros equipos.
