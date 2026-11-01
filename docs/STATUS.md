# Estado

Versión 0.11.0. Proyecto: C:\Projects\TagPrinter. Repositorio: https://github.com/dafermen/TagPrinter.

Completado: seis campos con visibilidad individual, Brand en lugar de Title, migración de trabajos anteriores, configuración JSON de tamaños/defaults, imágenes y documentación bilingüe, impresión/exportación y portable autocontenido.

Los valores de ejemplo de las capturas son sintéticos. El código utiliza siempre el serial. P/N y MAC vacíos no ocupan filas. Configuración general y último trabajo están separados.

Pendiente: validar hardware real y definir/implementar grabación/verificación RFID. No hay importación de inventario ni historial completo.

Tipo de activo / Asset type aparece antes de Marca y tiene checkbox y autoguardado. Ejemplo: Monitor / Dell / H22203H / ABC123 / ZYX123 / 00:1A:2B:3C:4D:5E. El código representa ABC123. En trabajos existentes el nuevo campo empieza vacío para no inventar el tipo; completa su valor cuando corresponda.

El selector de idioma está en **05 Configuración / Settings**, contraída por defecto junto con 02, 03 y 04. Abre su encabezado para cambiarlo. La zona principal comienza con los datos del activo.
