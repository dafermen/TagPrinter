using System.Drawing.Drawing2D;
namespace TagPrinter;

public partial class MainForm
{
    /// <summary>Draws a user-selected physical feed reference in the preview rails, never in the label bitmap.</summary>
    /// <param name="sender">Preview frame that owns the nonprinting rails.</param>
    /// <param name="e">Paint surface; its Graphics remains owned by Windows Forms.</param>
    private void PaintFeedGuide(object? sender,PaintEventArgs e)
    {
        if(!showFeedGuide.Checked)return;
        var bounds=previewFrame.ClientRectangle;if(bounds.Width<80 || bounds.Height<80)return;
        using var pen=new Pen(Color.FromArgb(35,85,145),2){EndCap=LineCap.Flat};
        // Direction belongs to physical paper, independent of content rotation or swapped media dimensions.
        foreach(float x in new[]{18f,bounds.Width-18f})
        {
            float y=bounds.Height/2f;
            PointF a,b;
            switch(feedDirection.SelectedIndex)
            {
                case 1:a=new(x,y+28);b=new(x,y-28);break;
                case 2:a=new(x+11,y);b=new(x-11,y);break;
                case 3:a=new(x-11,y);b=new(x+11,y);break;
                default:a=new(x,y-28);b=new(x,y+28);break;
            }
            e.Graphics.DrawLine(pen,a,b);
            // Explicit device-pixel heads stay visible at every label zoom level.
            float length=(float)Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Y-a.Y,2));
            float ux=(b.X-a.X)/length,uy=(b.Y-a.Y)/length;
            using var head=new SolidBrush(pen.Color);
            e.Graphics.FillPolygon(head,[b,new PointF(b.X-9*ux+5*uy,b.Y-9*uy-5*ux),new PointF(b.X-9*ux-5*uy,b.Y-9*uy+5*ux)]);
        }
        using var format=new StringFormat{Alignment=StringAlignment.Center};
        using var brush=new SolidBrush(pen.Color);
        e.Graphics.DrawString(Ui.T("ARRIBA","TOP"),Font,brush,new RectangleF(36,2,bounds.Width-72,22),format);
        e.Graphics.DrawString(Ui.T("ABAJO","BOTTOM"),Font,brush,new RectangleF(36,bounds.Height-22,bounds.Width-72,22),format);
    }
}
