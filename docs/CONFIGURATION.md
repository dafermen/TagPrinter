# config.json

## Dos archivos, dos funciones

| Archivo | Ubicación | Uso |
|---|---|---|
| config.json | Junto a TagPrinter.exe; fuente: src/TagPrinter/config.json | Catálogo de tamaños y valores iniciales; editable manualmente |
| TagPrinter.db | %LOCALAPPDATA%\TagPrinter | Historial SQLite y diseños generados |
| settings.json | %LOCALAPPDATA%\InventoryLabels | Último trabajo, valores de los campos y preferencias; guardado automático |

Cierra la aplicación, edita config.json y vuelve a abrirla. No pongas contraseñas ni datos de inventario en el archivo del proyecto. El archivo publicado en GitHub contiene solo ejemplos generales.

## Ejemplo mínimo completo

```json
{
  "SchemaVersion": 1,
  "LabelSizes": [
    { "Id": "100x50", "Name": "100 × 50 mm", "WidthMm": 100, "HeightMm": 50 },
    { "Id": "75x35", "Name": "75 × 35 mm", "WidthMm": 75, "HeightMm": 35 }
  ],
  "Defaults": {
    "Language": "en",
    "LabelPresetId": "100x50",
    "Dpi": 203,
    "Layout": 3,
    "Qr": false,
    "ShowCode": true,
    "AutoFont": true,
    "FontPercent": 100,
    "MarginMm": 2
  }
}
```

El archivo distribuido incluye seis ejemplos: 100×50, 50×25, 60×40, 80×50, 4×2 y 4×6 pulgadas. «Personalizado» se añade automáticamente; no es necesario incluirlo.

## Valores admitidos

- SchemaVersion: 1.
- LabelSizes: 1–100 entradas, Id único y Name no vacío. Id `custom` reservado.
- WidthMm: 25–150 mm; HeightMm: 20–200 mm; hasta un decimal.
- Defaults.Language: `en` o `es`.
- LabelPresetId: Id existente en el catálogo.
- Dpi: 203 o 300.
- Layout: 0 código abajo centrado, 1 abajo izquierda, 2 abajo derecha, 3 a la derecha del texto.
- Qr: true para QR, false para Code 128.
- ShowCode: true (predeterminado) incluye el código en etiquetas nuevas; false permite etiquetas de solo texto. El último trabajo guardado tiene prioridad.
- AutoFont: true para ajuste automático. FontPercent: 50–200, utilizado cuando AutoFont es false.
- MarginMm: 1–5 mm, hasta un decimal.

Se aceptan nombres de propiedades sin distinguir mayúsculas. JSON no admite comentarios. Si falta el archivo se usan valores integrados; si existe y está mal formado o contiene valores inválidos, se muestra un mensaje y no se sobrescribe el archivo.

## Prioridad y cambios de catálogo

Los Defaults se aplican al iniciar sin trabajo guardado. Si hay un trabajo, se recuperan sus preferencias, incluido idioma, fuente y tamaño; editar Defaults no elimina ese trabajo.

Los tamaños se guardan por Id. Puedes reordenarlos sin cambiar la selección anterior. Si eliminas el tamaño seleccionado, sus últimas dimensiones se recuperan como Personalizado. Si modificas las medidas de un Id existente, se usan las medidas actualizadas del catálogo. Conserva Id estables y cambia LabelPresetId si eliminas el predeterminado.

El catálogo siempre se toma del config.json de esa instalación. No requiere conexión a internet ni modifica los tamaños del controlador Windows.

La ubicación del historial está fuera del portable y es independiente de config.json. Reportes incluye copia de seguridad y CSV. Consulta HISTORY.md.
