using System.Drawing.Printing;
using System.Text.Json;
namespace TagPrinter;

public class MainForm : Form
{
    readonly TextBox assetType=new(){Text="Monitor"};
    readonly CheckBox showAssetType=new(){Checked=true,AutoSize=true};
    readonly TextBox brand=new(){Text="Dell"}, model=new(){Text="22\" - H22203H"}, serial=new(){Text="ABC123456"};
    readonly TextBox partNumber=new(), macAddress=new();
    readonly CheckBox showPartNumber=new(){Checked=true,AutoSize=true}, showMacAddress=new(){Checked=true,AutoSize=true};
    readonly CheckBox showBrand=new(){Checked=true,AutoSize=true}, showModel=new(){Checked=true,AutoSize=true}, showSerialText=new(){Checked=true,AutoSize=true};
    readonly ComboBox printers=Combo(), sizes=Combo(), layout=Combo(), code=Combo(), mode=Combo(), dpi=Combo();
    readonly NumericUpDown width=Number(25,150,100), height=Number(20,200,50), copies=Number(1,999,1);
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
    int fontPercent=100, effectiveFontPercent=100;
    internal readonly CollapsibleSection templateSection, textSection, printingSection;
    bool ready, translating;
    static ComboBox Combo()=>new(){DropDownStyle=ComboBoxStyle.DropDownList};
    static NumericUpDown Number(decimal min,decimal max,decimal value)=>new(){Minimum=min,Maximum=max,Value=value,DecimalPlaces=0};
    public MainForm(string? configPath=null, AppConfig? configuration=null)
    {
        config=configPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"InventoryLabels","settings.json");
        appConfig=configuration ?? AppConfig.Load(Path.Combine(AppContext.BaseDirectory,"config.json"));
        Ui.Language=appConfig.Defaults.Language;
        Text="TagPrinter | v0.10";
        using var iconStream=typeof(MainForm).Assembly.GetManifestResourceStream("TagPrinter.TagPrinter.ico");
        if(iconStream is not null)Icon=new Icon(iconStream); Width=1160; Height=850; MinimumSize=new Size(980,720);
        Font=new Font("Segoe UI",10); BackColor=Color.White; StartPosition=FormStartPosition.CenterScreen;
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(20)};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,380)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var fields=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,AutoScroll=true,Padding=new Padding(0,0,20,0)};
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var target=fields;
        void Heading(string text) { target.Controls.Add(new Label {Text=text,AutoSize=true,Font=new Font(Font,FontStyle.Bold),Margin=new Padding(0,14,0,6)}); }
        void Field(string label,Control control) { target.Controls.Add(new Label{Text=label,AutoSize=true,Margin=new Padding(0,5,0,3)}); control.Dock=DockStyle.Top; target.Controls.Add(control); }
        language.Items.AddRange(["English","Español"]);language.SelectedIndex=0;Field(Ui.T("Idioma","Language"),language);
        Heading(Ui.T("01  Datos del activo","01  Asset details"));
        void TextField(CheckBox check,string caption,TextBox input)
        {
            check.Text=caption;check.Margin=new Padding(0,5,0,3);target.Controls.Add(check);
            input.Dock=DockStyle.Top;target.Controls.Add(input);
        }
        TextField(showAssetType,Ui.T("Tipo de activo","Asset type"),assetType);
        assetType.MaxLength=80;
        TextField(showBrand,Ui.T("Marca","Brand"),brand);
        TextField(showModel,Ui.T("Modelo","Model"),model);
        TextField(showSerialText,"Serial",serial);
        TextField(showPartNumber,Ui.T("No. de parte","Part number"),partNumber);
        TextField(showMacAddress,Ui.T("Dirección MAC","MAC address"),macAddress);
        partNumber.MaxLength=100;macAddress.MaxLength=100;
        target.Controls.Add(new Label{Text=Ui.T("Las casillas controlan el texto impreso. El código siempre usa el serial.","Checkboxes control printed text. The code always uses the serial."),AutoSize=true,MaximumSize=new Size(330,0),ForeColor=Color.DimGray,Margin=new Padding(0,5,0,0)});
        brand.MaxLength=80; model.MaxLength=100; serial.MaxLength=120;
        templateSection=new CollapsibleSection(Ui.T("02  Etiqueta y plantilla","02  Label and template"));
        fields.Controls.Add(templateSection);target=templateSection.Body;
        foreach(var preset in appConfig.LabelSizes)sizes.Items.Add(preset.Name);
        sizes.Items.Add(Ui.T("Personalizado","Custom"));
        Field(Ui.T("Tamaño · ancho × alto","Size · width × height"),sizes);
        width.DecimalPlaces=1; height.DecimalPlaces=1;
        var dims=new FlowLayoutPanel{AutoSize=true}; dims.Controls.AddRange([new Label{Text=Ui.T("Ancho mm","Width mm"),AutoSize=true},width,new Label{Text=Ui.T("Alto mm","Height mm"),AutoSize=true},height]); width.Width=65;height.Width=65;
        target.Controls.Add(dims);
        layout.Items.AddRange([Ui.T("Código abajo · centrado","Code below · centered"),Ui.T("Código abajo · izquierda","Code below · left"),Ui.T("Código abajo · derecha","Code below · right"),Ui.T("Código a la derecha del texto","Code to the right of text")]); Field(Ui.T("Distribución de la plantilla","Template layout"),layout);
        code.Items.AddRange([Ui.T("Código de barras · Code 128","Barcode · Code 128"),"QR"]); Field(Ui.T("Tipo de código","Code type"),code);
        textSection=new CollapsibleSection(Ui.T("03  Texto y márgenes","03  Text and margins"));
        fields.Controls.Add(textSection);target=textSection.Body;
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
        var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(16,0,0,0)};
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,48));right.RowStyles.Add(new RowStyle(SizeType.Absolute,46));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.Absolute,74));right.RowStyles.Add(new RowStyle(SizeType.Absolute,100));
        right.Controls.Add(new Label{Text=Ui.T("Vista de la plantilla","Template preview"),Font=new Font("Segoe UI",20,FontStyle.Bold),AutoSize=true});
        right.Controls.Add(new Label{Text=Ui.T("La vista previa se actualiza al cambiar datos, tamaño o distribución.\nEl código contiene el serial completo.","Preview updates when you change the data, size or layout.\nThe code contains the full serial number."),AutoSize=true});var previewHost=new Panel{Dock=DockStyle.Fill};previewHost.Controls.Add(preview);previewHost.Controls.Add(previewError);right.Controls.Add(previewHost);
        right.Controls.Add(new Label{Text=Ui.T("RFID: esta versión imprime el contenido visible.\nLa grabación y verificación del chip todavía no están habilitadas.","RFID: this version prints the visible content.\nChip encoding and verification are not enabled yet."),Dock=DockStyle.Fill,Padding=new Padding(8),ForeColor=Color.FromArgb(120,70,10)});
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill}; var export=new Button{Text=Ui.T("Exportar etiqueta","Export Tag"),AutoSize=true};var zpl=new Button{Text=Ui.T("Guardar ZPL…","Save ZPL…"),AutoSize=true};
        var codeExport=new Button{Text=Ui.T("Exportar código","Export Code"),AutoSize=true};
        codeExport.Click+=(_,_)=>Guard(ExportCode);
        export.Click+=(_,_)=>Guard(()=>Export(false));zpl.Click+=(_,_)=>Guard(()=>Export(true));print.Click+=(_,_)=>Guard(Print);buttons.Controls.AddRange([export,codeExport,zpl,print]);right.Controls.Add(buttons);
        root.Controls.Add(fields,0,0);root.Controls.Add(right,1,0);Controls.Add(root);Controls.Add(status);
        sizes.SelectedIndex=appConfig.LabelSizes.FindIndex(p=>p.Id==appConfig.Defaults.LabelPresetId);
        layout.SelectedIndex=appConfig.Defaults.Layout;code.SelectedIndex=appConfig.Defaults.Qr?1:0;mode.SelectedIndex=0;dpi.SelectedIndex=appConfig.Defaults.Dpi==300?1:0;
        margin.Value=appConfig.Defaults.MarginMm;autoFont.Checked=appConfig.Defaults.AutoFont;fontPercent=appConfig.Defaults.FontPercent;
        LoadPrinters();LoadConfig();
        foreach(var check in new[]{showAssetType,showBrand,showModel,showSerialText,showPartNumber,showMacAddress})check.CheckedChanged+=(_,_)=>UpdatePreview();
        foreach(var t in new[]{assetType,brand,model,serial,partNumber,macAddress})t.TextChanged+=(_,_)=>UpdatePreview();
        foreach(var c in new[]{layout,code,mode,dpi})c.SelectedIndexChanged+=(_,_)=>UpdatePreview();
        sizes.SelectedIndexChanged+=(_,_)=>ApplySize(); width.ValueChanged+=(_,_)=>UpdatePreview();height.ValueChanged+=(_,_)=>UpdatePreview();
        language.SelectedIndex=Ui.Language=="es"?1:0;
        language.SelectedIndexChanged+=(_,_)=>{if(translating)return;translating=true;bool active=ready;ready=false;try{Ui.Language=language.SelectedIndex==1?"es":"en";Ui.Translate(this);}finally{ready=active;translating=false;}UpdatePreview();if(ready)SaveConfig();};
        autoFont.CheckedChanged+=(_,_)=>UpdatePreview();showGuides.CheckedChanged+=(_,_)=>UpdatePreview();
        foreach(var number in new[]{margin,offsetX,offsetY})number.ValueChanged+=(_,_)=>UpdatePreview();
        copies.ValueChanged+=(_,_)=>ScheduleSave();printers.SelectedIndexChanged+=(_,_)=>ScheduleSave();
        autosave.Tick+=(_,_)=>{autosave.Stop();SaveConfig();};
        FormClosing+=(_,_)=>SaveConfig();Ui.Translate(this);ready=true;ApplySize();
    }
    void Guard(Action action) {try {action();}catch(Exception ex){MessageBox.Show(this,ex.Message,Ui.T("Etiquetas","TagPrinter"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
    LabelData Data()=>new(brand.Text.Trim(),model.Text.Trim(),serial.Text.Trim(),width.Value,height.Value,int.Parse(dpi.Text),layout.SelectedIndex,code.SelectedIndex==1,autoFont.Checked,fontPercent,margin.Value,offsetX.Value,offsetY.Value,showBrand.Checked,showModel.Checked,showSerialText.Checked,partNumber.Text.Trim(),macAddress.Text.Trim(),showPartNumber.Checked,showMacAddress.Checked,assetType.Text.Trim(),showAssetType.Checked);
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
        ScheduleSave();
        try
        {
            var next=LabelRenderer.Render(Data(),showGuides.Checked,out effectiveFontPercent);var old=preview.Image;preview.Image=next;old?.Dispose();
            fontValue.Text=autoFont.Checked?$"Auto ({effectiveFontPercent} %)":$"{fontPercent} %";
            previewError.Visible=false;
            status.Text=$"{width.Value} × {height.Value} mm · {dpi.Text} DPI · {next.Width} × {next.Height} px.\n"+Ui.T("Escanea una prueba impresa. En etiquetas pequeñas las barras pueden medir un solo punto.","Scan a printed sample. Small labels may use one-dot barcode bars.");print.Enabled=true;
        }
        catch(Exception ex) {fontValue.Text=autoFont.Checked?"Auto":$"{fontPercent} %";var old=preview.Image;preview.Image=null;old?.Dispose();status.Text=ex.Message;previewError.Text=ex.Message;previewError.Visible=true;previewError.BringToFront();print.Enabled=false;}
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
        using var image=LabelRenderer.Render(Data());using var doc=Document();
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
        SaveConfig();status.Text=string.Format(Ui.T("Enviado a {0}: {1} etiqueta(s). No confirma impresión física ni RFID.","Sent to {0}: {1} label(s). This does not confirm physical printing or RFID."),printers.Text,copies.Value);
    }
    void Export(bool zpl)
    {
        using var image=LabelRenderer.Render(Data());using var dialog=new SaveFileDialog{Filter=zpl?"ZPL|*.zpl":Ui.T("Imagen PNG|*.png","PNG image|*.png"),FileName=SafeFileName(serial.Text)+(zpl?".zpl":".png")};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        if(zpl)File.WriteAllText(dialog.FileName,LabelRenderer.ToZpl(image,(int)copies.Value),System.Text.Encoding.ASCII);else image.Save(dialog.FileName,System.Drawing.Imaging.ImageFormat.Png);
        SaveConfig();
    }
    internal static string CodeFileName(string serial,bool qr)=>SafeFileName(serial)+(qr?"_QR.png":"_barcode.png");
    void ExportCode()
    {
        bool qr=code.SelectedIndex==1;
        using var image=CodeRenderer.Render(serial.Text.Trim(),qr,int.Parse(dpi.Text));
        using var dialog=new SaveFileDialog
        {
            Title=Ui.T("Exportar código","Export Code"),
            Filter=Ui.T("Imagen PNG|*.png","PNG image|*.png"),
            FileName=CodeFileName(serial.Text,qr),DefaultExt="png",AddExtension=true
        };
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        image.Save(dialog.FileName,System.Drawing.Imaging.ImageFormat.Png);
        SaveConfig();
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
    internal WorkState CaptureWork()=>new(){Printer=printers.Text,Size=sizes.SelectedIndex,LabelPresetId=sizes.SelectedIndex<appConfig.LabelSizes.Count?appConfig.LabelSizes[sizes.SelectedIndex].Id:"custom",Width=width.Value,Height=height.Value,Layout=layout.SelectedIndex,Code=code.SelectedIndex,Mode=mode.SelectedIndex,Dpi=dpi.SelectedIndex,AssetType=assetType.Text,ShowAssetType=showAssetType.Checked,Brand=brand.Text,PartNumber=partNumber.Text,MacAddress=macAddress.Text,ShowPartNumber=showPartNumber.Checked,ShowMacAddress=showMacAddress.Checked,Model=model.Text,Serial=serial.Text,Language=Ui.Language,ShowBrand=showBrand.Checked,ShowModel=showModel.Checked,ShowSerialText=showSerialText.Checked,ShowGuides=showGuides.Checked,AutoFont=autoFont.Checked,FontPercent=fontPercent,MarginMm=margin.Value,OffsetXmm=offsetX.Value,OffsetYmm=offsetY.Value,Copies=(int)copies.Value};
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
        showGuides.Checked=s.ShowGuides;autoFont.Checked=s.AutoFont;fontPercent=Math.Clamp(s.FontPercent,50,200);margin.Value=Math.Clamp(s.MarginMm,1,5);offsetX.Value=Math.Clamp(s.OffsetXmm,-5,5);offsetY.Value=Math.Clamp(s.OffsetYmm,-5,5);
        brand.Text=s.Brand;model.Text=s.Model;serial.Text=s.Serial;copies.Value=Math.Clamp(s.Copies,1,999);Ui.Language=s.Language=="es"?"es":"en";
        language.SelectedIndex=Ui.Language=="es"?1:0;
        ready=wasReady;if(ready){Ui.Translate(this);ApplySize();}
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
    protected override void Dispose(bool disposing) {if(disposing){autosave.Dispose();preview.Image?.Dispose();}base.Dispose(disposing);}
}
