namespace TagPrinter;

public partial class MainForm
{
    private readonly TabPage countsTab=new(){BackColor=Color.White};
    private PhysicalCountsView? countsView;
    private bool countCloseBlocked;
    /// <summary>Adds physical reconciliation without changing labels, RFID, asset custody or authentication.</summary>
    private void InitializeCounts()
    {
        countsView=new PhysicalCountsView(history,uid=>ShowAssets(new(AssetUid:uid)));countsTab.Controls.Add(countsView);workspaceTabs.TabPages.Insert(3,countsTab);workspaceTabs.SelectedIndexChanged+=(_,_)=>{if(ready && workspaceTabs.SelectedTab==countsTab)Guard(()=>countsView.RefreshData());};
        countsView.PendingWorkChanged+=busy=>{if(!busy && countCloseBlocked){countCloseBlocked=false;status.Text=Ui.T("Ya no hay lecturas pendientes en cola. Puedes cerrar la aplicación.","No queued readings remain. You can close the application.");}};
        // Normal closure cannot silently abandon a healthy drain or a failed, explicitly retained queue.
        FormClosing+=(_,e)=>{if(countsView.HasPendingWork){e.Cancel=true;countCloseBlocked=true;workspaceTabs.SelectedTab=countsTab;status.Text=Ui.T("Espera a que se guarden las lecturas. Si la cola falló, reintenta o descarta explícitamente la cola antes de cerrar.","Wait for readings to save. If the queue failed, retry or explicitly discard it before closing.");}};
    }
    /// <summary>Opens physical inventory without creating a session or submitting printer jobs.</summary>
    internal void ShowCounts(){workspaceTabs.SelectedTab=countsTab;countsView!.RefreshData();}
    /// <summary>Provides actual controls to isolated integration tests.</summary>
    internal PhysicalCountsView Counts=>countsView!;
}
