namespace TagPrinter;

/// <summary>A keyboard-accessible section that always starts collapsed.</summary>
public sealed class CollapsibleSection : TableLayoutPanel
{
    readonly Button header;
    bool expanded;
    public TableLayoutPanel Body {get;}
    public bool Expanded
    {
        get=>expanded;
        set
        {
            expanded=value;Body.Visible=value;
            header.AccessibleDescription=Ui.T(value?"Expandida":"Contraída",value?"Expanded":"Collapsed");
            header.Invalidate();PerformLayout();
        }
    }
    public CollapsibleSection(string title)
    {
        ColumnCount=1;RowCount=2;AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;
        Dock=DockStyle.Top;Margin=new Padding(0,12,0,0);Padding=Padding.Empty;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header=new Button{Text=title,Dock=DockStyle.Top,Height=38,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(22,0,0,0),Margin=Padding.Empty,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(237,242,248),Font=new Font("Segoe UI",10,FontStyle.Bold)};
        header.FlatAppearance.BorderSize=0;
        header.Click+=(_,_)=>Expanded=!Expanded;
        header.Paint+=(_,e)=>
        {
            int cy=header.Height/2;
            Point[] points=expanded?[new(8,cy-3),new(18,cy-3),new(13,cy+3)]:[new(10,cy-5),new(10,cy+5),new(16,cy)];
            e.Graphics.FillPolygon(Brushes.SteelBlue,points);
        };
        Body=new TableLayoutPanel{ColumnCount=1,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,Margin=Padding.Empty,Padding=new Padding(8,6,0,8)};
        Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        Controls.Add(header,0,0);Controls.Add(Body,0,1);Expanded=false;
    }
    internal void Toggle()=>header.PerformClick();
}
