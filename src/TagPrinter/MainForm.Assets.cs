namespace TagPrinter;

public partial class MainForm
{
    // The selected identity is transient editor provenance, also retained in last-work JSON across restart.
    private readonly TabPage assetsTab=new(){BackColor=Color.White};
    private AssetsView? assetsView;
    private string? selectedAssetUid;
    /// <summary>Builds the second module without replacing the existing label-event history.</summary>
    private void InitializeAssets()
    {
        assetsView=new AssetsView(history,LoadAssetLabel);assetsTab.Controls.Add(assetsView);workspaceTabs.TabPages.Insert(1,assetsTab);
        workspaceTabs.SelectedIndexChanged+=(_,_)=>{if(!ready)return;if(workspaceTabs.SelectedTab==assetsTab)Guard(()=>assetsView.RefreshData());if(workspaceTabs.SelectedTab==labelTab)Guard(()=>{RefreshAssetSuggestions();RefreshAssetMatch();});};
    }
    /// <summary>Loads only identity into the current template; blank serial assets use text-only output and never auto-print.</summary>
    internal void LoadAssetLabel(AssetRecord asset)
    {
        var current=CaptureWork();sourceHistoryId=null;
        Restore(current with {AssetUid=asset.Uid,AssetType=asset.AssetType,Brand=asset.Brand,Model=asset.Model,Serial=asset.Serial,PartNumber=asset.PartNumber,MacAddress=asset.MacAddress,FreeTextMode=false,ShowCode=string.IsNullOrWhiteSpace(asset.Serial)?false:current.ShowCode});
        selectedAssetUid=asset.Uid;assetSection.Expanded=true;workspaceTabs.SelectedTab=labelTab;SaveConfig();status.Text=Ui.T("Activo cargado: ","Asset loaded: ")+asset.Code+Ui.T(". Revisa la etiqueta antes de imprimir.",". Review the label before printing.");
    }
    /// <summary>Selects and refreshes master records without loading or printing a label.</summary>
    internal bool AssetListHasUsableSpace=>assetsView!.ListHasUsableSpace;
    /// <summary>Selects master records and refreshes their bounded SQL page.</summary>
    internal void ShowAssets(AssetFilter? filter=null){workspaceTabs.SelectedTab=assetsTab;if(filter is not null)assetsView!.ShowFilter(filter);else assetsView!.RefreshData();}
    /// <summary>Checks translated command geometry under the current window size.</summary>
    internal bool AssetCommandsFit=>assetsView!.CommandsFit;
}
