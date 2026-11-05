namespace TagPrinter;

public partial class MainForm
{
    /// <summary>Updates the nonprinting diagram using physical stock dimensions, independently of content rotation.</summary>
    private void UpdatePaperPreview()
    {
        preview.ShowPrinter=showFeedGuide.Checked;
        preview.StockWidth=width.Value;preview.StockHeight=height.Value;
        preview.Invalidate();
    }
}
