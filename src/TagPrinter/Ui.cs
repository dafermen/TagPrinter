namespace TagPrinter;
public static class Ui
{
    public static string Language {get;set;}="en";
    public static string T(string es,string en)=>Language=="es"?es:en;
    static readonly (string Es,string En)[] Catalog=[
        ("Ajustar automáticamente","Fit automatically"),
        ("+ mueve a la derecha / abajo","+ moves right / down"),
        ("Mostrar guía (no se imprime)","Show margin guide (not printed)"),
        ("03  Texto y márgenes","03  Text and margins"),
        ("Margen interior · mm","Inner margin · mm"),
        ("Desplazamiento de impresión…","Print offsets…"),
        ("04  Impresión","04  Printing"),
        ("Idioma","Language"),
        ("Código","Code"),
        ("Las casillas incluyen u ocultan cada elemento en la etiqueta.","Checkboxes include or hide each item on the label."),
        ("Nuevo activo","New asset"),
        ("Proyecto","Project"),
        ("05  Configuración","05  Settings"),
        ("No. de parte","Part number"),
        ("Dirección MAC","MAC address"),
        ("Las casillas controlan el texto impreso. El código siempre usa el serial.","Checkboxes control printed text. The code always uses the serial."),
        ("01  Datos del activo","01  Asset details"),
        ("Marca","Brand"),
        ("Tipo de activo","Asset type"),
        ("Modelo","Model"),
        ("Serial (contenido del código)","Serial (code content)"),
        ("02  Etiqueta y plantilla","02  Label and template"),
        ("Personalizado","Custom"),
        ("Tamaño · ancho × alto","Size · width × height"),
        ("Ancho mm","Width mm"),
        ("Alto mm","Height mm"),
        ("Código abajo · centrado","Code below · centered"),
        ("Código abajo · izquierda","Code below · left"),
        ("Código abajo · derecha","Code below · right"),
        ("Código a la derecha del texto","Code to the right of text"),
        ("Distribución de la plantilla","Template layout"),
        ("Código de barras · Code 128","Barcode · Code 128"),
        ("Tipo de código","Code type"),
        ("03  Impresión","03  Printing"),
        ("Impresora instalada en Windows","Windows printer"),
        ("Actualizar impresoras","Refresh printers"),
        ("Controlador de Windows (general)","Windows driver (general)"),
        ("ZPL directo (solo equipos compatibles)","Direct ZPL (compatible printers only)"),
        ("Método de impresión","Print method"),
        ("Resolución de la impresora · DPI","Printer resolution · DPI"),
        ("Copias","Copies"),
        ("Preferencias del controlador…","Driver preferences…"),
        ("Vista de la plantilla","Template preview"),
        ("La vista previa se actualiza al cambiar datos, tamaño o distribución.\nEl código contiene el serial completo.","Preview updates when you change the data, size or layout.\nThe code contains the full serial number."),
        ("RFID: esta versión imprime el contenido visible.\nLa grabación y verificación del chip todavía no están habilitadas.","RFID: this version prints the visible content.\nChip encoding and verification are not enabled yet."),
        ("Exportar etiqueta","Export Tag"),
        ("Exportar código","Export Code"),
        ("Guardar ZPL…","Save ZPL…"),
        ("Imprimir etiquetas","Print labels"),
        ("Etiquetas","TagPrinter"),
        ("No se pudieron consultar las impresoras: ","Could not load printers: "),
        ("Selecciona una impresora instalada en Windows.","Select a Windows printer."),
        ("La impresora seleccionada no está disponible.","The selected printer is unavailable."),
        ("Etiqueta de inventario","Inventory label"),
        ("Etiqueta personalizada","Custom label"),
        ("La aplicación usa el tamaño seleccionado en Etiqueta y plantilla. Ajusta también el tamaño del material y la calibración en el controlador de Windows.","The app uses the size selected in Label and template. Also configure media size and calibration in the Windows driver."),
        ("Imagen PNG|*.png","PNG image|*.png"),
    ];
    static string TranslateText(string value)
    {
        foreach(var entry in Catalog)if(value==entry.Es||value==entry.En)return T(entry.Es,entry.En);
        return value;
    }
    public static void Translate(Control root)
    {
        if(root is ComboBox combo)
        {
            int index=combo.SelectedIndex;
            var translated=combo.Items.Cast<object>().Select(item=>TranslateText(item.ToString()??"")).ToArray();
            if(translated.Where((text,i)=>text!=(combo.Items[i]?.ToString()??"")).Any())
            {
                combo.BeginUpdate();
                try {combo.SelectedIndex=-1;for(int i=0;i<translated.Length;i++)combo.Items[i]=translated[i];combo.SelectedIndex=index;}
                finally {combo.EndUpdate();}
            }
        }
        else if(root is not TextBox && root is not NumericUpDown && root is not DateTimePicker)
        {
            string translated=TranslateText(root.Text);
            if(translated!=root.Text)root.Text=translated;
        }
        foreach(Control child in root.Controls)Translate(child);
    }
}
