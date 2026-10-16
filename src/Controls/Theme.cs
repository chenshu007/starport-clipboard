using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

static class Theme
{
    public static readonly Color Bg = Color.FromArgb(245, 244, 238);
    public static readonly Color Card = Color.White;
    public static readonly Color CardHover = Color.FromArgb(250, 239, 232);
    public static readonly Color Ink = Color.FromArgb(25, 25, 25);
    public static readonly Color Muted = Color.FromArgb(125, 120, 110);
    public static readonly Color Clay = Color.FromArgb(217, 119, 87);
    public static readonly Color ClayDark = Color.FromArgb(193, 95, 64);
    public static readonly Color Line = Color.FromArgb(229, 225, 214);

    public static readonly Font Zh = new Font("Microsoft YaHei UI", 10.5f);
    public static readonly Font En = new Font("Georgia", 9f, FontStyle.Italic);
    public static readonly Font Num = new Font("Georgia", 12f);
    public static readonly Font Title = new Font("Georgia", 15f);
    public static readonly Font Small = new Font("Microsoft YaHei UI", 9f);
    public static readonly Font Chip = new Font("Georgia", 8f, FontStyle.Bold);
    public static readonly Font Sym = new Font("Segoe UI Symbol", 10f);

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        float d = rad * 2;
        GraphicsPath p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags extra)
    {
        TextRenderer.DrawText(g, s, f, r, c,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis |
            TextFormatFlags.VerticalCenter | extra);
    }
}
