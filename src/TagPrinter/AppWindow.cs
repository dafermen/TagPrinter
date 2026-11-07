namespace TagPrinter;

/// <summary>Normal application window; verification windows stay off-screen and never activate the user's desktop.</summary>
public class AppWindow : Form
{
    // Use the explicit executable flag, not a data path: normal application windows must remain interactive.
    private static readonly bool verification=Environment.GetCommandLineArgs().Contains("--self-test");
    /// <summary>Hides only verification windows from the taskbar; normal windows retain WinForms behavior.</summary>
    public AppWindow(){if(verification)ShowInTaskbar=false;}
    /// <summary>Showing an isolated test window must not take keyboard focus from the user's application.</summary>
    protected override bool ShowWithoutActivation=>verification || base.ShowWithoutActivation;
    /// <summary>Prevents activation even if a verification control requests focus or receives a stray desktop click.</summary>
    protected override CreateParams CreateParams
    {
        get{var parameters=base.CreateParams;if(verification)parameters.ExStyle|=0x08000000;return parameters;}
    }
    /// <summary>Keeps synthetic controls outside every visible monitor while allowing real layout and bitmap rendering.</summary>
    /// <param name="e">Load event forwarded to the base window.</param>
    protected override void OnLoad(EventArgs e)
    {
        if(verification)
        {
            StartPosition=FormStartPosition.Manual;
            var desktop=SystemInformation.VirtualScreen;
            Location=new Point(desktop.Left-Width-1000,desktop.Top-Height-1000);
        }
        base.OnLoad(e);
    }
}
