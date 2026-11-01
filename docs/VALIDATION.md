# Validación v0.4

Ejecutar scripts/Test.ps1. Resultado: src/TagPrinter/bin/Release/net8.0-windows/verification/results.txt.

Cobertura:

- 32 etiquetas completas (dos tamaños, dos DPI, cuatro distribuciones, dos tipos de código), generadas y decodificadas.
- 12 combinaciones adicionales de margen/DPI/tipo.
- Fuente manual/automática, independencia del código, guías excluidas, márgenes blancos, desplazamiento exacto y rechazo de recortes.
- Persistencia/reapertura/migración de datos e idioma y ajustes; nombres de archivo.
- Cuatro PNG de código independiente (QR/Code 128 a 203/300 DPI), guardados, reabiertos y decodificados: contenido, formato, resolución, QR cuadrado, fondo opaco blanco/negro y zonas blancas.
- Rechazo de serial vacío y ASCII inválido para Code 128. Generación independiente del diseño de etiqueta.
- Capturas en inglés/español. Configuración de prueba separada de los datos del usuario.

Compilación Release sin errores ni advertencias y pruebas satisfactorias. Publish.ps1 ejecuta también --self-test con el EXE portable antes de crear el ZIP.

No se imprimieron etiquetas ni se grabaron chips. Leer PNG no garantiza lectura de la impresión. No se ha probado en una máquina Windows limpia sin SDK.

## v0.5

Comprobación de inicio contraído, expansión/contracción de ambas secciones y conservación del trabajo. Reapertura con secciones contraídas. Se mantienen las pruebas de render, exportación y persistencia.

## v0.6

Cambio de etiquetas y eliminación del botón manual. Se conserva el temporizador de autoguardado y el guardado al cerrar/exportar/imprimir. Ejecutar los scripts de prueba y publicación para validar render, exportación y recuperación.

## v0.7

Las comprobaciones de estado inicial, expansión, contracción, conservación del trabajo y reapertura ahora incluyen la sección 04 Printing / Impresión.

## v0.8

32 combinaciones de visibilidad (ocho máscaras de campos, dos tipos de código, dos distribuciones) decodificadas con el mismo serial. Comparación de imágenes al cambiar valores de campos ocultos. Comprobaciones de selección inicial, persistencia/reapertura, migración y serial obligatorio aunque su texto esté oculto. Se mantienen las comprobaciones anteriores.

## v0.9

Ocho combinaciones de P/N/MAC y QR/Code 128 decodificadas. Pruebas de persistencia de campos/flags, migración Title/ShowTitle, defaults desde JSON, rechazo de JSON inválido/configuración fuera de rango, selección estable tras reordenar tamaños y recuperación como personalizado al retirar un preset. Capturas sintéticas asset-fields-en.png y asset-fields-es.png. La prueba no envía etiquetas a impresoras ni toca el último trabajo del usuario.

## v0.10

Ocho combinaciones de Tipo visible/oculto, 203/300 DPI y QR/Code 128; los seis campos del ejemplo se renderizan y se recupera siempre ABC123 del código. Comparación de imágenes con Tipo oculto, persistencia del valor/checkbox, carga de trabajos anteriores sin inventar un tipo y capturas en ambos idiomas.

## v0.11

La sección 05 está incluida en las comprobaciones de inicio/reapertura contraídos, expansión/contracción y conservación del trabajo. Se conserva la prueba de recuperación del idioma y las capturas en inglés y español.

## v0.12

Comprobación de que Nuevo activo limpia solo los seis valores y los guarda, conserva el resto del WorkState y mantiene copias/método en el resumen. Verificación de URL fija sin abrir el navegador en las pruebas. Se mantienen las pruebas de render/exportación/persistencia.

## v0.13

Pruebas reales con SQLite nativo: eventos duplicados, persistencia, snapshots, vínculos origen, búsquedas parametrizadas por campo, comodines literales, inyección como texto, acciones, límites UTC inclusivo/exclusivo, CSV reabierto con TextFieldParser (comillas/punto y coma/saltos de línea/JSON), backup reabierto y rechazo de destino activo.

Integración: exportaciones PNG/código/ZPL crean eventos; rutas inválidas, serial vacío, cambios de formulario y Nuevo activo no crean éxitos. Reportes ordena y exporta en ese orden, recupera dimensiones históricas pese a cambios en config.json, no imprime al cargar y no usa silenciosamente una impresora ausente. Cambiar idioma conserva filtros desactivados. 01 se expande/contrae sin alterar datos; tooltip About/Acerca de. Capturas de Labels/Reports con tres eventos sintéticos. No se abrió navegador ni se envió impresión física en las pruebas.
