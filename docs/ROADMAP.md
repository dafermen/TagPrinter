# Próximos pasos

Completado en v0.13: SQLite, Reportes, CSV, recuperación para reimpresión, 01 plegable y enlace GitHub como icono.

Pendiente:

1. Validar impresión y escaneo con hardware real.
2. Definir identificadores/memoria RFID e implementar codificación y verificación.
3. Evaluar importación de inventario y paginación para historiales grandes.
4. Evaluar restauración de backups desde la interfaz y sincronización solo si se concreta la necesidad.

## Idea pendiente: etiqueta sin QR ni código de barras

Añadir un checkbox para mostrar u ocultar el código seleccionado (QR o Code 128), marcado por defecto. Al desmarcarlo, permitir etiquetas de solo texto, sin modificar los checkboxes de los campos del activo.

Considerar en su implementación futura:

- Vista previa, Export Tag, impresión y ZPL coherentes con la selección.
- Aprovechar el espacio liberado para los textos visibles.
- Guardar la selección con el último trabajo y la instantánea del historial para reimpresión.
- Definir si el serial puede ser opcional cuando no se genera código, sin perder compatibilidad con Reportes.

Estado: solo propuesta registrada. No implementar hasta que el usuario lo indique.
