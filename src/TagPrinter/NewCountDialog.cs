namespace TagPrinter;

/// <summary>Creates a location-scoped snapshot explicitly; large snapshot insertion runs outside the UI thread.</summary>
internal sealed class NewCountDialog : AppWindow
{
    private readonly CountStore store;
    private readonly TextBox name=new(){Name="CountName",MaxLength=160,Dock=DockStyle.Top};
    private readonly ComboBox location=new(){Name="CountLocation",Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDown,MaxLength=160,Tag="UserData"};
    private readonly TextBox notes=new(){Name="CountNotes",MaxLength=2000,Multiline=true,Height=85,Dock=DockStyle.Top,ScrollBars=ScrollBars.Vertical};
    private readonly Label error=new(){AutoSize=true,MaximumSize=new Size(560,0),ForeColor=Color.Firebrick};
    private readonly Button create=new(){Name="CreateCount",AutoSize=true},cancel=new(){AutoSize=true,DialogResult=DialogResult.Cancel};
    private bool busy;
    internal CountSession? CreatedSession {get;private set;}
    /// <summary>Offers at most 200 location prefixes; typing a new exact location is also allowed.</summary>
    internal NewCountDialog(HistoryStore history)
    {
        store=new(history);Text=Ui.T("Nueva toma física","New physical count");Width=650;Height=445;MinimumSize=new Size(560,420);StartPosition=FormStartPosition.CenterParent;name.Text=Ui.T("Toma física ","Physical count ")+DateTime.Today.ToString("yyyy-MM-dd");var catalog=new AssetCatalog(history);
        void Locations(string prefix){string text=location.Text;location.Items.Clear();location.Items.AddRange(catalog.Suggest("Location",prefix).Cast<object>().ToArray());location.Text=text;}Locations("");bool typed=false;location.TextUpdate+=(_,_)=>typed=true;location.DropDown+=(_,_)=>Locations(typed?location.Text:"");location.DropDownClosed+=(_,_)=>typed=false;
        var fields=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,Padding=new Padding(16)};fields.ColumnStyles.Add(new(SizeType.Percent,100));void Add(string caption,Control input){fields.Controls.Add(new Label{Text=caption,AutoSize=true,Padding=new Padding(0,5,0,3)});fields.Controls.Add(input);}
        Add(Ui.T("Nombre *","Name *"),name);Add(Ui.T("Ubicación que vas a recorrer *","Location you will physically check *"),location);Add(Ui.T("Notas","Notes"),notes);fields.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(570,0),ForeColor=Color.DimGray,Text=Ui.T("Se guardará la lista de activos esperados en esta ubicación, excluyendo dados de baja. La lectura no cambiará su ubicación administrativa.","The expected assets at this location will be frozen, excluding retired assets. Readings do not change their administrative location.")});fields.Controls.Add(error);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=52,Padding=new Padding(16,4,0,0)};create.Text=Ui.T("Iniciar toma","Start count");cancel.Text=Ui.T("Cancelar","Cancel");footer.Controls.AddRange([create,cancel]);Controls.Add(fields);Controls.Add(footer);CancelButton=cancel;create.Click+=async(_,_)=>await CreateAsync();FormClosing+=(_,e)=>{if(busy)e.Cancel=true;};FluentIcons.ApplyTree(this);
    }
    /// <summary>Captures all input before the worker starts; errors retain values and no partial expected scope is committed.</summary>
    internal async Task<bool> CreateAsync()
    {
        if(busy)return false;string caption=name.Text,place=location.Text,description=notes.Text;busy=true;create.Enabled=cancel.Enabled=name.Enabled=location.Enabled=notes.Enabled=false;error.Text=Ui.T("Guardando la lista esperada…","Saving expected scope…");
        try{CreatedSession=await Task.Run(()=>store.Create(caption,place,description));DialogResult=DialogResult.OK;return true;}catch(Exception failure){error.Text=failure.Message;return false;}finally{busy=false;create.Enabled=cancel.Enabled=name.Enabled=location.Enabled=notes.Enabled=true;if(CreatedSession is not null)Close();}
    }
}
