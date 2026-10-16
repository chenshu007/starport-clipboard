using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// 放大查看图片窗口：点击或按 Esc 关闭，绝不写入剪贴板
class ImageViewer : Form
{
    const double MaxScreenShare = 0.8;  // 窗口最多占屏幕工作区的比例
    const int Pad = 24;                 // 图片四周留白（两边合计）
    const int MinW = 200, MinH = 150;

    readonly Image img;

    public ImageViewer(Image image)
    {
        img = image;
        Text = "查看参考图（点击或按 Esc 关闭）";
        Font = Theme.Small;
        BackColor = Theme.Bg;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        AutoScaleMode = AutoScaleMode.None;

        Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int maxW = (int)(wa.Width * MaxScreenShare), maxH = (int)(wa.Height * MaxScreenShare);
        float scale = Math.Min(1f, Math.Min((float)(maxW - Pad) / Math.Max(1, img.Width), (float)(maxH - Pad) / Math.Max(1, img.Height)));
        int cw = Math.Max(MinW, (int)Math.Round(img.Width * scale) + Pad);
        int ch = Math.Max(MinH, (int)Math.Round(img.Height * scale) + Pad);
        ClientSize = new Size(Math.Min(maxW, cw), Math.Min(maxH, ch));
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);

        MouseUp += delegate { Close(); };
        KeyDown += delegate(object o, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Theme.Bg);
        if (img == null || img.Width <= 0 || img.Height <= 0) return;

        float availW = Math.Max(1, ClientSize.Width - Pad);
        float availH = Math.Max(1, ClientSize.Height - Pad);
        float scale = Math.Min(1f, Math.Min(availW / img.Width, availH / img.Height));
        float dw = img.Width * scale, dh = img.Height * scale;
        float dx = (ClientSize.Width - dw) / 2f, dy = (ClientSize.Height - dh) / 2f;
        g.DrawImage(img, dx, dy, dw, dh);
        using (Pen p = new Pen(Theme.Line)) g.DrawRectangle(p, dx - 1, dy - 1, dw + 1, dh + 1);
    }
}
