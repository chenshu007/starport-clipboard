using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// 圆角按钮：实心橙色，或 Outline = 橙色描边
class PillButton : Control
{
    bool hover;
    public bool Outline;

    public PillButton(string text)
    {
        Text = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Cursor = Cursors.Hand;
        Font = Theme.Small;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
        using (GraphicsPath p = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), (Height - 1) / 2f))
        {
            if (Outline)
            {
                if (hover) using (SolidBrush b = new SolidBrush(Theme.CardHover)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Clay)) g.DrawPath(pen, p);
            }
            else
            {
                using (SolidBrush b = new SolidBrush(hover ? Theme.ClayDark : Theme.Clay)) g.FillPath(b, p);
            }
        }
        Theme.Text(g, Text, Font, ClientRectangle, Outline ? Theme.Clay : Color.White, TextFormatFlags.HorizontalCenter);
    }
}
