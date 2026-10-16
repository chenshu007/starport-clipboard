using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

class ImageCard : Control
{
    public const int W = 104, H = 78;  // 缩略图卡片尺寸

    public readonly Image Img;
    public event Action<ImageCard> ViewRequested, DeleteRequested;

    bool hover, delHover;

    public ImageCard(Image img)
    {
        Img = img;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Theme.Bg;
        Cursor = Cursors.Hand;
        Size = new Size(W, H);
    }

    Rectangle DelRect { get { return new Rectangle(Width - 24, 4, 20, 20); } }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; delHover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool dh = DelRect.Contains(e.Location);
        if (dh != delHover) { delHover = dh; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        if (!ClientRectangle.Contains(e.Location)) return;
        if (DelRect.Contains(e.Location))
        {
            if (DeleteRequested != null) DeleteRequested(this);
        }
        else
        {
            if (ViewRequested != null) ViewRequested(this);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Theme.Bg);

        RectangleF box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (GraphicsPath path = Theme.Round(box, 8))
        {
            using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillPath(b, path);

            Region oldClip = g.Clip;
            g.SetClip(path, CombineMode.Intersect);
            if (Img != null && Img.Width > 0 && Img.Height > 0)
            {
                float pad = 4f;
                float availW = Width - pad * 2, availH = Height - pad * 2;
                float scale = Math.Min(availW / Img.Width, availH / Img.Height);
                float dw = Img.Width * scale, dh = Img.Height * scale;
                float dx = (Width - dw) / 2f, dy = (Height - dh) / 2f;
                g.DrawImage(Img, dx, dy, dw, dh);
            }
            g.Clip = oldClip;

            using (Pen pen = new Pen(hover ? Theme.Clay : Theme.Line)) g.DrawPath(pen, path);
        }

        if (hover)
        {
            Rectangle r = DelRect;
            using (GraphicsPath dp = Theme.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f), 9.5f))
            using (SolidBrush db = new SolidBrush(delHover ? Color.Firebrick : Color.FromArgb(220, 255, 255, 255)))
            using (Pen dpen = new Pen(delHover ? Color.Firebrick : Theme.Line))
            {
                g.FillPath(db, dp);
                g.DrawPath(dpen, dp);
            }
            Theme.Text(g, "✕", Theme.Small, r, delHover ? Color.White : Theme.Ink, TextFormatFlags.HorizontalCenter);
        }
    }
}
