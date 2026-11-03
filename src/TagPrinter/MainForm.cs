using System.Drawing.Printing;
using System.Text.Json;
namespace TagPrinter;

public partial class MainForm : Form
{
    internal const string ProjectUrl="https://github.com/dafermen/TagPrinter";
    readonly Label printSummary=new(){Dock=DockStyle.Fill,AutoEllipsis=true,Padding=new Padding(0,5,0,0),ForeColor=Color.FromArgb(35,65,100)};
    internal string PrintSummaryText=>printSummary.Text;
    readonly TextBox assetType=new(){Text="Monitor"};
    readonly CheckBox showCode=new(){Checked=true,AutoSize=true};
    readonly List<(CheckBox Check,Label Caption)> visibilityFields=new();
    readonly CheckBox showAssetType=new(){Checked=true,AutoSize=true};
    readonly TextBox brand=new(){Text="Dell"}, model=new(){Text="22\" - H22203H"}, serial=new(){Text="ABC123456"};
    readonly TextBox partNumber=new(), macAddress=new();
    readonly CheckBox showPartNumber=new(){Checked=true,AutoSize=true}, showMacAddress=new(){Checked=true,AutoSize=true};
    readonly CheckBox showBrand=new(){Checked=true,AutoSize=true}, showModel=new(){Checked=true,AutoSize=true}, showSerialText=new(){Checked=true,AutoSize=true};
    readonly ComboBox printers=Combo(), sizes=Combo(), layout=Combo(), code=Combo(), mode=Combo(), dpi=Combo();
    readonly NumericUpDown width=Number(20,200,100), height=Number(20,200,50), copies=Number(1,999,1);
    readonly Label previewDescription=new(){AutoSize=true};
    readonly PictureBox preview=new(){Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(226,232,240)};
    readonly Label status=new(){AutoSize=false,Dock=DockStyle.Bottom,Height=66,Padding=new Padding(12),ForeColor=Color.FromArgb(30,64,110)};
    readonly Button print=new(){Text=Ui.T("Imprimir etiquetas","Print labels"),AutoSize=true,BackColor=Color.FromArgb(30,80,150),ForeColor=Color.White,FlatStyle=FlatStyle.Flat};
    readonly string config;
    readonly AppConfig appConfig;
    readonly ComboBox language=Combo();
    readonly System.Windows.Forms.Timer autosave=new(){Interval=600};
    readonly Label previewError=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=Color.Firebrick,BackColor=Color.FromArgb(226,232,240),Visible=false};
    readonly CheckBox autoFont=new(){Checked=true,AutoSize=true,Text=Ui.T("Ajustar automáticamente","Fit automatically")};
    readonly CheckBox showGuides=new(){Checked=true,AutoSize=true,Text=Ui.T("Mostrar guía (no se imprime)","Show margin guide (not printed)")};
    readonly Label fontValue=new(){AutoSize=true,Padding=new Padding(6)};
    readonly NumericUpDown margin=Number(1,5,2), offsetX=Number(-5,5,0), offsetY=Number(-5,5,0);
    readonly ComboBox fontFamily=Combo(), rotation=Combo(), orientation=Combo();
    readonly NumericUpDown fontSize=Number(4,72,10);
    readonly CheckBox bold=new(){AutoSize=true,Text=Ui.T("Negrilla","Bold")}, headingBold=new(){AutoSize=true,Checked=true,Text=Ui.T("Resaltar encabezado","Bold heading")};
    int fontPercent=100, effectiveFontPercent=100;
    internal readonly CollapsibleSection assetSection, templateSection, textSection, printingSection, settingsSection;
    bool ready, translating;
    static ComboBox Combo()=>new(){DropDownStyle=ComboBoxStyle.DropDownList};
    static NumericUpDown Number(decimal min,decimal max,decimal value)=>new(){Minimum=min,Maximum=max,Value=value,DecimalPlaces=0};
    public MainForm(string? configPath=null, AppConfig? configuration=null,string? historyPath=null)
    {
        config=configPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"InventoryLabels","settings.json");
        history=new HistoryStore(historyPath ?? (configPath is not null?Path.ChangeExtension(configPath,".db"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TagPrinter","TagPrinter.db")));
        appConfig=configuration ?? AppConfig.Load(Path.Combine(AppContext.BaseDirectory,"config.json"));
        Ui.Language=appConfig.Defaults.Language;
        Text="TagPrinter | v0.15";
        using var iconStream=typeof(MainForm).Assembly.GetManifestResourceStream("TagPrinter.TagPrinter.ico");
        if(iconStream is not null)Icon=new Icon(iconStream); Width=1160; Height=850; MinimumSize=new Size(980,720);
        Font=new Font("Segoe UI",10); BackColor=Color.White; StartPosition=FormStartPosition.CenterScreen;
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(20,12,20,12)};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,380)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var fields=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,AutoScroll=true,Padding=new Padding(0,0,20,0)};
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var target=fields;
        void Field(string label,Control control) { target.Controls.Add(new Label{Text=label,AutoSize=true,Margin=new Padding(0,5,0,3)}); control.Dock=DockStyle.Top; target.Controls.Add(control); }
        language.Items.AddRange(["English","Español"]);language.SelectedIndex=0;
        assetSection=new CollapsibleSection(Ui.T("01  Datos del activo","01  Asset details")){Expanded=true};
        fields.Controls.Add(assetSection);target=assetSection.Body;
        var dataRows=new TableLayoutPanel{ColumnCount=3,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,Margin=Padding.Empty};
        dataRows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,115));dataRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));dataRows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,28));
        target.Controls.Add(dataRows);
        void TextField(CheckBox check,string caption,Control input)
        {
            int row=dataRows.RowCount++;dataRows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label=new Label{Text=caption,AutoSize=true,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(0,3,3,3)};
            check.Text="";check.AutoSize=true;check.Anchor=AnchorStyles.None;check.Margin=new Padding(6,4,2,4);
            input.Dock=DockStyle.Fill;input.Margin=new Padding(0,3,0,3);
            dataRows.Controls.Add(label,0,row);dataRows.Controls.Add(input,1,row);dataRows.Controls.Add(check,2,row);visibilityFields.Add((check,label));
        }
        TextField(showAssetType,Ui.T("Tipo de activo","Asset type"),assetType);
        assetType.MaxLength=80;
        TextField(showBrand,Ui.T("Marca","Brand"),brand);
        TextField(showModel,Ui.T("Modelo","Model"),model);
        TextField(showSerialText,"Serial",serial);
        TextField(showPartNumber,Ui.T("No. de parte","Part number"),partNumber);
        TextField(showMacAddress,Ui.T("Dirección MAC","MAC address"),macAddress);
        code.Items.AddRange([Ui.T("Código de barras · Code 128","Barcode · Code 128"),"QR"]);
        TextField(showCode,Ui.T("Código","Code"),code);
        partNumber.MaxLength=100;macAddress.MaxLength=100;
        target.Controls.Add(new Label{Text=Ui.T("Las casillas incluyen u ocultan cada elemento en la etiqueta.","Checkboxes include or hide each item on the label."),AutoSize=true,MaximumSize=new Size(330,0),ForeColor=Color.DimGray,Margin=new Padding(0,5,0,0)});
        brand.MaxLength=80; model.MaxLength=100; serial.MaxLength=120;
        templateSection=new CollapsibleSection(Ui.T("02  Etiqueta y plantilla","02  Label and template"));
        fields.Controls.Add(templateSection);target=templateSection.Body;
        foreach(var preset in appConfig.LabelSizes)sizes.Items.Add(preset.Name);
        sizes.Items.Add(Ui.T("Personalizado","Custom"));
        Field(Ui.T("Tamaño · ancho × alto","Size · width × height"),sizes);
        width.DecimalPlaces=1; height.DecimalPlaces=1;
        var dims=new FlowLayoutPanel{AutoSize=true}; dims.Controls.AddRange([new Label{Text=Ui.T("Ancho mm","Width mm"),AutoSize=true},width,new Label{Text=Ui.T("Alto mm","Height mm"),AutoSize=true},height]); width.Width=65;height.Width=65;
        target.Controls.Add(dims);
        orientation.Items.AddRange([Ui.T("Horizontal","Landscape"),Ui.T("Vertical","Portrait")]);orientation.SelectedIndex=0;
        Field(Ui.T("Orientación de etiqueta","Label orientation"),orientation);
        rotation.Items.AddRange(["0°","90°","180°","270°"]);rotation.SelectedIndex=0;
        Field(Ui.T("Rotación del contenido","Content rotation"),rotation);
        layout.Items.AddRange([Ui.T("Código abajo · centrado","Code below · centered"),Ui.T("Código abajo · izquierda","Code below · left"),Ui.T("Código abajo · derecha","Code below · right"),Ui.T("Código a la derecha del texto","Code to the right of text")]); Field(Ui.T("Distribución de la plantilla","Template layout"),layout);
        textSection=new CollapsibleSection(Ui.T("03  Texto y márgenes","03  Text and margins"));
        fields.Controls.Add(textSection);target=textSection.Body;
        foreach(var name in FontFamily.Families.Select(f=>f.Name).OrderBy(n=>n))fontFamily.Items.Add(name);
        fontFamily.SelectedItem="Arial";if(fontFamily.SelectedIndex<0)fontFamily.SelectedIndex=0;
        Field(Ui.T("Fuente","Font"),fontFamily);Field(Ui.T("Tamaño base · pt","Base size · pt"),fontSize);
        target.Controls.Add(bold);target.Controls.Add(headingBold);
        target.Controls.Add(autoFont);
        var fontRow=new FlowLayoutPanel{AutoSize=true};
        var smaller=new Button{Text="−",Width=42,Height=32};var bigger=new Button{Text="+",Width=42,Height=32};
        smaller.Click+=(_,_)=>ChangeFont(-10);bigger.Click+=(_,_)=>ChangeFont(10);
        fontRow.Controls.AddRange([smaller,fontValue,bigger]);target.Controls.Add(fontRow);
        margin.DecimalPlaces=1;margin.Increment=.5m;Field(Ui.T("Margen interior · mm","Inner margin · mm"),margin);
        target.Controls.Add(showGuides);
        var advanced=new Button{Text=Ui.T("Desplazamiento de impresión…","Print offsets…"),AutoSize=true};target.Controls.Add(advanced);
        var offsets=new FlowLayoutPanel{AutoSize=true,Visible=false};
        foreach(var number in new[]{offsetX,offsetY}){number.DecimalPlaces=1;number.Increment=.5m;number.Width=64;}
        offsets.Controls.AddRange([new Label{Text=Ui.T("Horizontal mm","Horizontal mm"),AutoSize=true},offsetX,new Label{Text=Ui.T("Vertical mm","Vertical mm"),AutoSize=true},offsetY]);
        offsets.Controls.Add(new Label{Text=Ui.T("+ mueve a la derecha / abajo","+ moves right / down"),AutoSize=true});
        target.Controls.Add(offsets);advanced.Click+=(_,_)=>offsets.Visible=!offsets.Visible;
        printingSection=new CollapsibleSection(Ui.T("04  Impresión","04  Printing"));
        fields.Controls.Add(printingSection);target=printingSection.Body;
        Field(Ui.T("Impresora instalada en Windows","Windows printer"),printers);
        var refresh=new Button{Text=Ui.T("Actualizar impresoras","Refresh printers"),AutoSize=true};refresh.Click+=(_,_)=>LoadPrinters();target.Controls.Add(refresh);
        mode.Items.AddRange([Ui.T("Controlador de Windows (general)","Windows driver (general)"),Ui.T("ZPL directo (solo equipos compatibles)","Direct ZPL (compatible printers only)")]); Field(Ui.T("Método de impresión","Print method"),mode);
        dpi.Items.AddRange(["203","300"]);Field(Ui.T("Resolución de la impresora · DPI","Printer resolution · DPI"),dpi); Field(Ui.T("Copias","Copies"),copies);
        var options=new Button{Text=Ui.T("Preferencias del controlador…","Driver preferences…"),AutoSize=true};options.Click+=(_,_)=>Guard(Preferences);target.Controls.Add(options);
        settingsSection=new CollapsibleSection(Ui.T("05  Configuración","05  Settings"));
        fields.Controls.Add(settingsSection);target=settingsSection.Body;
        Field(Ui.T("Idioma","Language"),language);
        var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(16,0,0,0)};
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,48));right.RowStyles.Add(new RowStyle(SizeType.Absolute,46));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.Absolute,74));right.RowStyles.Add(new RowStyle(SizeType.Absolute,128));
        right.Controls.Add(new Label{Text=Ui.T("Vista de la plantilla","Template preview"),Font=new Font("Segoe UI",20,FontStyle.Bold),AutoSize=true});
        right.Controls.Add(previewDescription);var previewHost=new Panel{Dock=DockStyle.Fill};previewHost.Controls.Add(preview);previewHost.Controls.Add(previewError);right.Controls.Add(previewHost);
        right.Controls.Add(new Label{Text=Ui.T("RFID: esta versión imprime el contenido visible.\nLa grabación y verificación del chip todavía no están habilitadas.","RFID: this version prints the visible content.\nChip encoding and verification are not enabled yet."),Dock=DockStyle.Fill,Padding=new Padding(8),ForeColor=Color.FromArgb(120,70,10)});
        var actionArea=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
        actionArea.RowStyles.Add(new RowStyle(SizeType.Absolute,48));actionArea.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        actionArea.Controls.Add(printSummary,0,0);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty};
        var newAsset=new Button{Text=Ui.T("Nuevo activo","New asset"),AutoSize=true};
        newAsset.Click+=(_,_)=>StartNewAsset();buttons.Controls.Add(newAsset); var export=new Button{Text=Ui.T("Exportar etiqueta","Export Tag"),AutoSize=true};var zpl=new Button{Text=Ui.T("Guardar ZPL…","Save ZPL…"),AutoSize=true};
        var codeExport=new Button{Text=Ui.T("Exportar código","Export Code"),AutoSize=true};
        codeExport.Click+=(_,_)=>Guard(ExportCode);
        export.Click+=(_,_)=>Guard(()=>Export(false));zpl.Click+=(_,_)=>Guard(()=>Export(true));print.Click+=(_,_)=>Guard(Print);buttons.Controls.AddRange([export,codeExport,zpl,print]);actionArea.Controls.Add(buttons,0,1);right.Controls.Add(actionArea);
        root.Controls.Add(fields,0,0);root.Controls.Add(right,1,0);InitializeWorkspace(root);
        sizes.SelectedIndex=appConfig.LabelSizes.FindIndex(p=>p.Id==appConfig.Defaults.LabelPresetId);
        layout.SelectedIndex=appConfig.Defaults.Layout;code.SelectedIndex=appConfig.Defaults.Qr?1:0;mode.SelectedIndex=0;dpi.SelectedIndex=appConfig.Defaults.Dpi==300?1:0;
        showCode.Checked=appConfig.Defaults.ShowCode;
        margin.Value=appConfig.Defaults.MarginMm;autoFont.Checked=appConfig.Defaults.AutoFont;fontPercent=appConfig.Defaults.FontPercent;
        LoadPrinters();LoadConfig();
        foreach(var check in new[]{showAssetType,showBrand,showModel,showSerialText,showPartNumber,showMacAddress,showCode})check.CheckedChanged+=(_,_)=>UpdatePreview();
        foreach(var t in new[]{assetType,brand,model,serial,partNumber,macAddress})t.TextChanged+=(_,_)=>UpdatePreview();
        foreach(var c in new[]{layout,code,mode,dpi})c.SelectedIndexChanged+=(_,_)=>UpdatePreview();
        sizes.SelectedIndexChanged+=(_,_)=>ApplySize(); width.ValueChanged+=(_,_)=>UpdatePreview();height.ValueChanged+=(_,_)=>UpdatePreview();
        language.SelectedIndex=Ui.Language=="es"?1:0;
        language.SelectedIndexChanged+=(_,_)=>{if(translating)return;translating=true;bool active=ready;ready=false;try{Ui.Language=language.SelectedIndex==1?"es":"en";Ui.Translate(this);UpdateChrome();}finally{ready=active;translating=false;}UpdatePreview();if(ready)SaveConfig();};
        fontFamily.SelectedIndexChanged+=(_,_)=>UpdatePreview();rotation.SelectedIndexChanged+=(_,_)=>UpdatePreview();
        fontSize.ValueChanged+=(_,_)=>UpdatePreview();bold.CheckedChanged+=(_,_)=>UpdatePreview();headingBold.CheckedChanged+=(_,_)=>UpdatePreview();
        orientation.SelectedIndexChanged+=(_,_)=>Guard(()=>{if(!ready)return;bool portrait=orientation.SelectedIndex==1;if((portrait && width.Value>height.Value)||(!portrait && width.Value<height.Value)){bool active=ready;ready=false;decimal w=width.Value,h=height.Value;if(h<width.Minimum || h>width.Maximum || w<height.Minimum || w>height.Maximum){ready=active;throw new ArgumentException(Ui.T("Dimensiones no compatibles con esa orientación.","Dimensions do not support that orientation."));}sizes.SelectedIndex=appConfig.LabelSizes.Count;width.Value=h;height.Value=w;ready=active;ApplySize();}});
        autoFont.CheckedChanged+=(_,_)=>UpdatePreview();showGuides.CheckedChanged+=(_,_)=>UpdatePreview();
        foreach(var number in new[]{margin,offsetX,offsetY})number.ValueChanged+=(_,_)=>UpdatePreview();
        copies.ValueChanged+=(_,_)=>{UpdatePrintSummary();ScheduleSave();};printers.SelectedIndexChanged+=(_,_)=>{UpdatePrintSummary();ScheduleSave();};
        autosave.Tick+=(_,_)=>{autosave.Stop();SaveConfig();};
        FormClosing+=(_,_)=>SaveConfig();Ui.Translate(this);UpdateChrome();ready=true;ApplySize();
    }
    internal void StartNewAsset()
    {
        sourceHistoryId=null;assetSection.Expanded=true;
        bool active=ready;ready=false;
        foreach(var input in new[]{assetType,brand,model,serial,partNumber,macAddress})input.Clear();
        ready=active;UpdatePreview();SaveConfig();assetType.Focus();
    }
    void UpdatePrintSummary()
    {
        string printerName=printers.SelectedIndex>=0?printers.Text:Ui.T("Sin seleccionar","Not selected");
        printSummary.Text=string.Format(Ui.T("Impresora: {0}\nCopias: {1} · {2}","Printer: {0}\nCopies: {1} · {2}"),printerName,copies.Value,mode.SelectedIndex==1?"ZPL":"Windows");
    }
    void Guard(Action action) {try {action();}catch(Exception ex){MessageBox.Show(this,ex.Message,Ui.T("Etiquetas","TagPrinter"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
    LabelData Data()=>new(brand.Text.Trim(),model.Text.Trim(),serial.Text.Trim(),width.Value,height.Value,int.Parse(dpi.Text),layout.SelectedIndex,code.SelectedIndex==1,autoFont.Checked,fontPercent,margin.Value,offsetX.Value,offsetY.Value,showBrand.Checked,showModel.Checked,showSerialText.Checked,partNumber.Text.Trim(),macAddress.Text.Trim(),showPartNumber.Checked,showMacAddress.Checked,assetType.Text.Trim(),showAssetType.Checked,showCode.Checked,fontFamily.Text,fontSize.Value,bold.Checked,headingBold.Checked,rotation.SelectedIndex*90);
    internal void ChangeFont(int step)
    {
        fontPercent=Math.Clamp((autoFont.Checked?effectiveFontPercent:fontPercent)+step,50,200);
        bool active=ready;ready=false;autoFont.Checked=false;ready=active;UpdatePreview();
    }
    void ApplySize()
    {
        bool custom=sizes.SelectedIndex==appConfig.LabelSizes.Count;width.Enabled=height.Enabled=custom;
        if(!custom && sizes.SelectedIndex>=0)
        {
            bool old=ready;ready=false;var preset=appConfig.LabelSizes[sizes.SelectedIndex];
            width.Value=preset.WidthMm;height.Value=preset.HeightMm;ready=old;
        }
        UpdatePreview();
    }
    void UpdatePreview()
    {
        if(!ready)return;
        orientation.SelectedIndex=height.Value>width.Value?1:0;
        ScheduleSave();UpdatePrintSummary();
        previewDescription.Text=showCode.Checked?Ui.T("La vista previa se actualiza al cambiar datos, tamaño o distribución.\nEl código contiene el serial completo.","Preview updates when you change the data, size or layout.\nThe code contains the full serial number."):Ui.T("Vista previa de la etiqueta. El código está desactivado.\nEl texto utiliza toda el área disponible.","Label preview. The code is disabled.\nText uses the full available area.");
        try
        {
            var next=LabelRenderer.Render(Data(),showGuides.Checked,out effectiveFontPercent);var old=preview.Image;preview.Image=next;old?.Dispose();
            fontValue.Text=autoFont.Checked?$"Auto ({effectiveFontPercent} %)":$"{fontPercent} %";
            previewError.Visible=false;
            status.Text=$"{width.Value} × {height.Value} mm · {dpi.Text} DPI · {next.Width} × {next.Height} px.\n"+(showCode.Checked?Ui.T("Escanea una prueba impresa. En etiquetas pequeñas las barras pueden medir un solo punto.","Scan a printed sample. Small labels may use one-dot barcode bars."):Ui.T("Etiqueta de solo texto.","Text-only label."));print.Enabled=true;
        }
        catch(Exception ex) {previewError.ForeColor=string.IsNullOrWhiteSpace(serial.Text)?Color.DimGray:Color.Firebrick;fontValue.Text=autoFont.Checked?"Auto":$"{fontPercent} %";var old=preview.Image;preview.Image=null;old?.Dispose();status.Text=ex.Message;previewError.Text=ex.Message;previewError.Visible=true;previewError.BringToFront();print.Enabled=false;}
    }
    void LoadPrinters()
    {
        string selected=printers.Text;printers.Items.Clear();
        try {foreach(string p in PrinterSettings.InstalledPrinters)printers.Items.Add(p);var def=new PrinterSettings().PrinterName;printers.SelectedItem=printers.Items.Contains(selected)?selected:def;if(printers.SelectedIndex<0 && printers.Items.Count>0)printers.SelectedIndex=0;}
        catch(Exception ex){status.Text=Ui.T("No se pudieron consultar las impresoras: ","Could not load printers: ")+ex.Message;}
    }
    PrintDocument Document()
    {
        if(printers.SelectedIndex<0)throw new InvalidOperationException(Ui.T("Selecciona una impresora instalada en Windows.","Select a Windows printer."));
        var doc=new PrintDocument();doc.PrinterSettings.PrinterName=printers.Text;
        if(!doc.PrinterSettings.IsValid){doc.Dispose();throw new InvalidOperationException(Ui.T("La impresora seleccionada no está disponible.","The selected printer is unavailable."));}
        doc.DocumentName=Ui.T("Etiqueta de inventario","Inventory label");
        doc.DefaultPageSettings.PaperSize=new PaperSize(Ui.T("Etiqueta personalizada","Custom label"),(int)Math.Round(width.Value/25.4m*100),(int)Math.Round(height.Value/25.4m*100));
        doc.DefaultPageSettings.Margins=new Margins(0,0,0,0);doc.DefaultPageSettings.Landscape=false;
        return doc;
    }
    void Preferences()
    {
        using var doc=Document();using var dialog=new PrintDialog{Document=doc,UseEXDialog=true};
        if(dialog.ShowDialog(this)==DialogResult.OK){printers.SelectedItem=doc.PrinterSettings.PrinterName;MessageBox.Show(this,Ui.T("La aplicación usa el tamaño seleccionado en Etiqueta y plantilla. Ajusta también el tamaño del material y la calibración en el controlador de Windows.","The app uses the size selected in Label and template. Also configure media size and calibration in the Windows driver."));}
    }
    void Print()
    {
        var snapshot=GenerationState();using var image=LabelRenderer.Render(Data());using var doc=Document();
        if(mode.SelectedIndex==1) RawPrinter.Send(printers.Text,LabelRenderer.ToZpl(image,(int)copies.Value));
        else
        {
            int remaining=(int)copies.Value;
            doc.PrintPage+=(_,e)=>
            {
                var g=e.Graphics!;g.TranslateTransform(-e.PageSettings.HardMarginX,-e.PageSettings.HardMarginY);
                g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(image,new RectangleF(0,0,(float)(width.Value/25.4m*100),(float)(height.Value/25.4m*100)));
                e.HasMorePages=--remaining>0;
            };
            doc.Print();
        }
        RecordGeneration("PrintSubmitted","",snapshot);
        SaveConfig();status.Text=string.Format(Ui.T("Enviado a {0}: {1} etiqueta(s). No confirma impresión física ni RFID.","Sent to {0}: {1} label(s). This does not confirm physical printing or RFID."),printers.Text,copies.Value);
    }
    void Export(bool zpl)
    {
        using var dialog=new SaveFileDialog{Filter=zpl?"ZPL|*.zpl":Ui.T("Imagen PNG|*.png","PNG image|*.png"),FileName=SafeFileName(serial.Text)+(zpl?".zpl":".png")};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        ExportTagFile(dialog.FileName,zpl);
    }
    internal static string CodeFileName(string serial,bool qr)=>SafeFileName(serial)+(qr?"_QR.png":"_barcode.png");
    void ExportCode()
    {
        bool qr=code.SelectedIndex==1;
        using var dialog=new SaveFileDialog
        {
            Title=Ui.T("Exportar código","Export Code"),
            Filter=Ui.T("Imagen PNG|*.png","PNG image|*.png"),
            FileName=CodeFileName(serial.Text,qr),DefaultExt="png",AddExtension=true
        };
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        ExportCodeFile(dialog.FileName);
        status.Text=Ui.T("Código exportado con zona blanca de lectura.","Code exported with a white quiet zone.");
    }
    internal static string SafeFileName(string value)
    {
        var invalid=Path.GetInvalidFileNameChars();
        string name=new string(value.Trim().Select(c=>invalid.Contains(c)?'_':c).ToArray()).TrimEnd('.', ' ');
        if(string.IsNullOrWhiteSpace(name)) name="label";
        string stem=name.Split('.')[0].ToUpperInvariant();
        if(new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(stem))name="_"+name;
        return name;
    }
    void ScheduleSave(){if(!ready)return;autosave.Stop();autosave.Start();}
    internal WorkState CaptureWork()=>new(){Printer=printers.Text,Size=sizes.SelectedIndex,LabelPresetId=sizes.SelectedIndex<appConfig.LabelSizes.Count?appConfig.LabelSizes[sizes.SelectedIndex].Id:"custom",Width=width.Value,Height=height.Value,Layout=layout.SelectedIndex,Code=code.SelectedIndex,Mode=mode.SelectedIndex,Dpi=dpi.SelectedIndex,AssetType=assetType.Text,ShowAssetType=showAssetType.Checked,Brand=brand.Text,PartNumber=partNumber.Text,MacAddress=macAddress.Text,ShowPartNumber=showPartNumber.Checked,ShowMacAddress=showMacAddress.Checked,Model=model.Text,Serial=serial.Text,Language=Ui.Language,ShowBrand=showBrand.Checked,ShowModel=showModel.Checked,ShowSerialText=showSerialText.Checked,ShowCode=showCode.Checked,ShowGuides=showGuides.Checked,AutoFont=autoFont.Checked,FontPercent=fontPercent,MarginMm=margin.Value,OffsetXmm=offsetX.Value,OffsetYmm=offsetY.Value,Copies=(int)copies.Value,FontFamily=fontFamily.Text,FontSizePt=fontSize.Value,Bold=bold.Checked,HeadingBold=headingBold.Checked,ContentRotation=rotation.SelectedIndex*90};
    internal void Restore(WorkState s)
    {
        bool wasReady=ready;ready=false;
        if(printers.Items.Contains(s.Printer))printers.SelectedItem=s.Printer;
        int presetIndex=appConfig.LabelSizes.FindIndex(p=>p.Id==s.LabelPresetId);
        if(presetIndex<0 && s.LabelPresetId!="custom")presetIndex=appConfig.LabelSizes.FindIndex(p=>p.WidthMm==s.Width && p.HeightMm==s.Height);
        sizes.SelectedIndex=presetIndex>=0?presetIndex:appConfig.LabelSizes.Count;width.Value=Math.Clamp(s.Width,width.Minimum,width.Maximum);height.Value=Math.Clamp(s.Height,height.Minimum,height.Maximum);
        layout.SelectedIndex=Math.Clamp(s.Layout,0,3);code.SelectedIndex=Math.Clamp(s.Code,0,1);mode.SelectedIndex=Math.Clamp(s.Mode,0,1);dpi.SelectedIndex=Math.Clamp(s.Dpi,0,1);
        assetType.Text=s.AssetType;showAssetType.Checked=s.ShowAssetType;
        partNumber.Text=s.PartNumber;macAddress.Text=s.MacAddress;showPartNumber.Checked=s.ShowPartNumber;showMacAddress.Checked=s.ShowMacAddress;
        showBrand.Checked=s.ShowBrand;showModel.Checked=s.ShowModel;showSerialText.Checked=s.ShowSerialText;
        showCode.Checked=s.ShowCode;
        fontFamily.SelectedItem=fontFamily.Items.Contains(s.FontFamily)?s.FontFamily:"Arial";
        fontSize.Value=Math.Clamp(s.FontSizePt,4,72);bold.Checked=s.Bold;headingBold.Checked=s.HeadingBold;rotation.SelectedIndex=Math.Clamp(s.ContentRotation/90,0,3);
        showGuides.Checked=s.ShowGuides;autoFont.Checked=s.AutoFont;fontPercent=Math.Clamp(s.FontPercent,50,200);margin.Value=Math.Clamp(s.MarginMm,1,5);offsetX.Value=Math.Clamp(s.OffsetXmm,-5,5);offsetY.Value=Math.Clamp(s.OffsetYmm,-5,5);
        brand.Text=s.Brand;model.Text=s.Model;serial.Text=s.Serial;copies.Value=Math.Clamp(s.Copies,1,999);Ui.Language=s.Language=="es"?"es":"en";
        language.SelectedIndex=Ui.Language=="es"?1:0;
        ready=wasReady;if(ready){Ui.Translate(this);UpdateChrome();ApplySize();}
    }
    internal void SaveConfig()
    {
        try {WorkStore.Save(config,CaptureWork());}
        catch(Exception ex){status.Text=Ui.T("No se pudo guardar: ","Could not save: ")+ex.Message;}
    }
    void LoadConfig()
    {
        try {if(File.Exists(config))Restore(WorkStore.Load(config));}
        catch{status.Text=Ui.T("No se pudo recuperar el trabajo anterior.","Could not restore previous work.");}
    }
    protected override void Dispose(bool disposing) {if(disposing){autosave.Dispose();aboutTip.Dispose();aboutButton.Image?.Dispose();preview.Image?.Dispose();}base.Dispose(disposing);}
}
