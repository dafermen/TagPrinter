namespace TagPrinter;

public partial class MainForm
{
    private readonly TabPage servicesTab=new(){BackColor=Color.White};
    private ServicesView? servicesView;
    /// <summary>Adds local maintenance/warranty tracking without changing printer or label generation behavior.</summary>
    private void InitializeServices()
    {
        servicesView=new ServicesView(history,uid=>ShowAssets(new(AssetUid:uid)));servicesTab.Controls.Add(servicesView);workspaceTabs.TabPages.Insert(2,servicesTab);workspaceTabs.SelectedIndexChanged+=(_,_)=>{if(ready && workspaceTabs.SelectedTab==servicesTab)Guard(()=>servicesView.RefreshData());};
    }
    /// <summary>Opens global reminders or the two service lists for one stable asset identity.</summary>
    internal void ShowServices(string? assetUid=null){workspaceTabs.SelectedTab=servicesTab;servicesView!.ShowAsset(assetUid);}
    /// <summary>Accesses actual controls in isolated integration tests, never the live user database.</summary>
    internal ServicesView Services=>servicesView!;
}
