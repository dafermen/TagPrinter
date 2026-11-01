# Guía de uso — TagPrinter 0.12

## Abrir

Extrae todo el ZIP portable y abre TagPrinter.exe. Conserva sus archivos y carpetas, incluido config.json. Incluye .NET. Para imprimir se requiere el controlador Windows de la impresora.

## Datos del activo

Tipo de activo / Asset type, Marca / Brand, Modelo / Model, Serial, No. de parte / Part number y Dirección MAC / MAC address. Las seis casillas comienzan marcadas para trabajos nuevos. Desmarcar oculta solo el texto y conserva el valor editable.

El serial es obligatorio: alimenta el QR o Code 128 incluso si ocultas su texto. No. de parte y MAC son opcionales; si están vacíos no consumen espacio en la etiqueta. MAC se imprime tal como se escribe; no se normaliza ni se valida como dirección de red en esta versión.

Los textos visibles se redistribuyen. Si desmarcas todos, queda únicamente el código. Seis campos pueden no caber en etiquetas pequeñas; usa un tamaño mayor o desmarca los que no necesitas. No se recortan textos silenciosamente.

## Secciones 02, 03, 04 y 05

Comienzan contraídas. Pulsa el encabezado o usa Enter/Espacio con el foco en él para expandir. Contraerlas no cambia sus valores.

- 02: tamaño de etiqueta (ancho × alto), medidas personalizadas, distribución y QR/Code 128. Los tamaños vienen de config.json.
- 03: ajuste automático de texto o botones −/+ (manual, 50–200 %), margen 1–5 mm (inicial 2 mm), guía azul de vista previa y desplazamientos.
- 04: impresora instalada, método Windows/ZPL, 203/300 DPI y copias.
- 05 Configuración / Settings: idioma English / Español. La selección se aplica al instante y se conserva al reabrir.

La fuente se ajusta conjuntamente sin modificar el código. La guía no se imprime ni se exporta. Desplazamientos positivos mueven a la derecha/abajo; se bloquea la salida del área disponible.

## Exportación

**Export Tag / Exportar etiqueta** guarda la etiqueta completa como serial.png.

**Export Code / Exportar código** guarda únicamente el símbolo: serial_QR.png o serial_barcode.png. Usa fondo blanco opaco, zona blanca de lectura, escala entera y el DPI seleccionado; no depende de la fuente, márgenes ni distribución de la etiqueta.

**Save ZPL / Guardar ZPL** exporta la etiqueta completa como comandos gráficos para un dispositivo compatible. Los nombres de archivo sustituyen caracteres que Windows no permite.

## Impresión

Selecciona la impresora real instalada en Windows. El método Windows usa su controlador. Direct ZPL requiere que la unidad acepte ZPL. Configura material/calibración y verifica que el tamaño coincida. Imprime una muestra y escanea el código antes de un lote. Enviar a la cola no confirma impresión física. No se escribe RFID.

## Guardado automático

Los cambios se guardan tras 600 ms, al cerrar, exportar o imprimir. La aplicación recupera solo el último trabajo, no un historial. Se conserva la ruta histórica %LOCALAPPDATA%\InventoryLabels\settings.json para compatibilidad.

Los antiguos Title / ShowTitle migran a Brand / ShowBrand sin borrar valores. Revisa la marca después de actualizar, ya que el contenido antiguo podría ser un tipo de equipo como «Monitor».

## Configuración general

[config.json: tamaños y valores iniciales](CONFIGURATION.md). Edita el archivo junto al EXE y reinicia. La aplicación no lo reescribe al guardar el trabajo.

Tipo de activo / Asset type aparece antes de Marca y tiene checkbox y autoguardado. Ejemplo: Monitor / Dell / H22203H / ABC123 / ZYX123 / 00:1A:2B:3C:4D:5E. El código representa ABC123. En trabajos existentes el nuevo campo empieza vacío para no inventar el tipo; completa su valor cuando corresponda.

El selector de idioma está en **05 Configuración / Settings**, contraída por defecto junto con 02, 03 y 04. Abre su encabezado para cambiarlo. La zona principal comienza con los datos del activo.

## v0.12: trabajo diario

**New asset / Nuevo activo** vacía los seis datos del activo y guarda el trabajo vacío, conservando impresora, copias, plantilla, idioma, tamaños, fuente, márgenes y checkboxes. No mantiene una copia del activo anterior ni un historial; exporta su etiqueta antes si necesitas conservarla.

El resumen junto a los botones muestra siempre impresora, copias y método Windows/ZPL, aunque 04 esté contraída. Se actualiza al cambiar opciones o idioma.

En **05 Configuración / Settings**, el enlace `github.com/dafermen/TagPrinter` abre el repositorio en el navegador predeterminado solo al pulsarlo.
