namespace TagPrinter;

public sealed partial class ReportsView
{
    // SQL determines ordering and group filters before LIMIT/OFFSET; only one page is materialized for the grid.
    private int pageIndex;
    private long totalRows;
    private string sortKey="Id";
    private bool sortDescending=true,binding;
    private HistoryQuery? lastFilter;
    private string? groupType,groupBrand,groupModel;
    private readonly Button previousPage=new(){AutoSize=true},nextPage=new(){AutoSize=true};
    private readonly Label pageLabel=new(){AutoSize=true,Padding=new Padding(2,6,2,0)};
    private readonly ComboBox pageSize=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=65};
    /// <summary>Moves to a bounded page, used by isolated tests and explicit paging controls.</summary>
    internal void GoToPage(int index){pageIndex=Math.Max(0,index);RefreshData();}
}
