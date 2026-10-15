using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Starport 固定剪贴板：点卡片复制中文，点 EN 复制英文；窗口置顶，点击卡片不抢目标软件焦点

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

// 日期占位符：{今天}/{today}、{明天}/{tomorrow}
// 中文栏显示「10月15日」，英文栏显示「15 Oct」；凌晨分界时间前算作前一个营业日
static class DateTokens
{
    public static int CutoffHour;  // 0 = 按日历日期

    public static DateTime BusinessDate
    {
        get
        {
            DateTime now = DateTime.Now;
            return (now.Hour < CutoffHour ? now.AddDays(-1) : now).Date;
        }
    }

    static string Fmt(DateTime d, bool en)
    {
        return en ? d.ToString("d MMM", CultureInfo.InvariantCulture) : d.Month + "月" + d.Day + "日";
    }

    // 数量占位符：{数量}/{qty}，复制时弹窗询问
    public static bool HasQty(string text) { return text.Contains("{数量}") || text.Contains("{qty}"); }

    public static string FillQty(string text, string qty) { return text.Replace("{数量}", qty).Replace("{qty}", qty); }

    public static string Expand(string text, bool en)
    {
        if (text.IndexOf('{') < 0) return text;
        DateTime today = BusinessDate;
        string t = Fmt(today, en), m = Fmt(today.AddDays(1), en);
        return text.Replace("{今天}", t).Replace("{today}", t).Replace("{明天}", m).Replace("{tomorrow}", m);
    }
}

class Phrase
{
    public string Zh, En;
    public int Count;
    public Phrase(string zh, string en, int count) { Zh = zh; En = en; Count = count; }
}

// 一条词条卡片。
// 平时：主体 = 复制中文，EN = 复制英文，位置固定不动。
// 管理模式：不复制；主体或 ✎ = 编辑，✕ = 删除。
class Card : Control
{
    enum Zone { None, Body, En, Edit, Del }

    public readonly Phrase P;
    public readonly int Index;
    public readonly bool Manage;
    public event Action<Card, bool> CopyRequested;  // bool = 是否英文
    public event Action<Card> EditRequested, DeleteRequested;

    bool hover;
    Zone zone;

    public Card(Phrase p, int index, bool manage)
    {
        P = p;
        Index = index;
        Manage = manage;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Theme.Bg;
        Cursor = Cursors.Hand;
        Height = HeightFor(p);
    }

    public const int Gap = 4;  // 卡片之间的间距
    public static int HeightFor(Phrase p) { return p.En.Length > 0 ? 52 : 40; }

    int BoxH { get { return Height - Gap; } }

    // 卡片位置变化（列表滚动）时重绘，以便更新底部渐隐
    protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); Invalidate(); }

    // 右侧按钮区的位置只取决于卡片尺寸，与悬停无关，所以不会跳动
    Rectangle EnRect { get { return new Rectangle(Width - 42, (BoxH - 22) / 2, 32, 22); } }
    Rectangle DelRect { get { return new Rectangle(Width - 38, (BoxH - 28) / 2, 28, 28); } }
    Rectangle EditRect { get { return new Rectangle(Width - 68, (BoxH - 28) / 2, 28, 28); } }

    Rectangle TextArea
    {
        get
        {
            int right = Manage ? EditRect.Left - 6 : (P.En.Length > 0 ? EnRect.Left - 8 : Width - 12);
            return new Rectangle(38, 0, Math.Max(10, right - 38), BoxH);
        }
    }

    // 文字是否被省略号截断（只有截断时才需要提示框）
    // 卡片上数量显示为「?」，表示点击时再填
    public string ZhText { get { return DateTokens.FillQty(DateTokens.Expand(P.Zh, false), "?"); } }
    public string EnText { get { return DateTokens.FillQty(DateTokens.Expand(P.En, true), "?"); } }

    public bool IsTruncated
    {
        get
        {
            int w = TextArea.Width;
            const TextFormatFlags f = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            return TextRenderer.MeasureText(ZhText, Theme.Zh, Size.Empty, f).Width > w ||
                   (P.En.Length > 0 && TextRenderer.MeasureText(EnText, Theme.En, Size.Empty, f).Width > w);
        }
    }

    Zone HitTest(Point pt)
    {
        if (!ClientRectangle.Contains(pt) || pt.Y >= BoxH) return Zone.None;
        if (Manage)
        {
            if (DelRect.Contains(pt)) return Zone.Del;
            if (EditRect.Contains(pt)) return Zone.Edit;
            return Zone.Body;
        }
        if (P.En.Length > 0 && EnRect.Contains(pt)) return Zone.En;
        return Zone.Body;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; zone = Zone.None; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Zone z = HitTest(e.Location);
        if (z != zone) { zone = z; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        switch (HitTest(e.Location))
        {
            case Zone.Body:
                if (Manage) { if (EditRequested != null) EditRequested(this); }
                else if (CopyRequested != null) CopyRequested(this, false);
                break;
            case Zone.En: if (CopyRequested != null) CopyRequested(this, true); break;
            case Zone.Edit: if (EditRequested != null) EditRequested(this); break;
            case Zone.Del: if (DeleteRequested != null) DeleteRequested(this); break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Bg);

        int h = BoxH;
        RectangleF box = new RectangleF(0.5f, 0.5f, Width - 1.5f, h - 1);
        using (GraphicsPath path = Theme.Round(box, 10))
        using (SolidBrush b = new SolidBrush(hover ? Theme.CardHover : Theme.Card))
        using (Pen pen = new Pen(hover ? Theme.Clay : Theme.Line))
        {
            if (Manage) pen.DashStyle = DashStyle.Dash;  // 虚线边框，一眼看出处于管理模式
            g.FillPath(b, path);
            g.DrawPath(pen, path);
        }

        // 序号对应键盘最上排 1 2 3 … 9 0；第 11 张起不显示序号
        Theme.Text(g, Index < 9 ? (Index + 1).ToString() : (Index == 9 ? "0" : ""), Theme.Num,
            new Rectangle(6, 0, 28, h), Theme.Clay, TextFormatFlags.HorizontalCenter);

        if (Manage)
        {
            Theme.Text(g, "✎", Theme.Sym, EditRect, zone == Zone.Edit || zone == Zone.Body ? Theme.Clay : Theme.Muted, TextFormatFlags.HorizontalCenter);
            Theme.Text(g, "✕", Theme.Sym, DelRect, zone == Zone.Del ? Color.Firebrick : Theme.Muted, TextFormatFlags.HorizontalCenter);
        }
        else if (P.En.Length > 0)
        {
            Rectangle r = EnRect;
            using (GraphicsPath chip = Theme.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 11))
            {
                if (zone == Zone.En)
                    using (SolidBrush cb = new SolidBrush(Theme.Clay)) g.FillPath(cb, chip);
                using (Pen cp = new Pen(Theme.Clay)) g.DrawPath(cp, chip);
            }
            Theme.Text(g, "EN", Theme.Chip, r, zone == Zone.En ? Color.White : Theme.Clay, TextFormatFlags.HorizontalCenter);
        }

        Rectangle t = TextArea;
        if (P.En.Length > 0)
        {
            Theme.Text(g, ZhText, Theme.Zh, new Rectangle(t.X, 4, t.Width, 23), Theme.Ink, TextFormatFlags.Left);
            Theme.Text(g, EnText, Theme.En, new Rectangle(t.X, 26, t.Width, 18), Theme.Muted, TextFormatFlags.Left);
        }
        else
        {
            Theme.Text(g, ZhText, Theme.Zh, t, Theme.Ink, TextFormatFlags.Left);
        }

        if (Parent != null)
        {
            int viewBottom = Parent.ClientSize.Height - Top;  // 列表可视区底边在卡片坐标中的位置
            if (viewBottom > 0 && viewBottom < Height)
            {
                Rectangle fade = new Rectangle(0, viewBottom - 28, Width, 28);
                using (LinearGradientBrush lb = new LinearGradientBrush(
                    new Rectangle(fade.X, fade.Y - 1, fade.Width, fade.Height + 2),
                    Color.FromArgb(0, Theme.Bg), Theme.Bg, LinearGradientMode.Vertical))
                    g.FillRectangle(lb, fade);
            }
        }
    }
}

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

// 新增/编辑弹窗：普通可激活窗口，保证打字和中文输入法正常
class EditDialog : Form
{
    readonly TextBox tZh = new TextBox();
    readonly TextBox tEn = new TextBox();

    public string Zh { get { return Clean(tZh.Text); } }
    public string En { get { return Clean(tEn.Text); } }

    static string Clean(string s) { return s.Replace('|', '｜').Trim(); }

    public EditDialog(string title, string zh, string en)
    {
        Text = title;
        Font = Theme.Small;
        BackColor = Theme.Bg;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(340, 200);

        Label l1 = new Label();
        l1.Text = "中文（点卡片复制）";
        l1.ForeColor = Theme.Muted;
        l1.SetBounds(20, 14, 300, 20);
        tZh.SetBounds(20, 36, 300, 26);
        tZh.Font = Theme.Zh;
        tZh.Text = zh;

        Label l2 = new Label();
        l2.Text = "English（点 EN 复制，可留空）";
        l2.ForeColor = Theme.Muted;
        l2.SetBounds(20, 74, 300, 20);
        tEn.SetBounds(20, 96, 300, 26);
        tEn.Font = Theme.Zh;
        tEn.Text = en;

        Button ok = FlatButton("保存", Theme.Clay, Color.White);
        ok.SetBounds(214, 148, 106, 32);
        ok.Click += delegate
        {
            if (Zh.Length == 0) { tZh.Focus(); return; }
            DialogResult = DialogResult.OK;
        };
        Button cancel = FlatButton("取消", Color.White, Theme.Ink);
        cancel.SetBounds(100, 148, 106, 32);
        cancel.DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[] { l1, tZh, l2, tEn, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    static Button FlatButton(string text, Color back, Color fore)
    {
        Button b = new Button();
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Theme.Line;
        b.BackColor = back;
        b.ForeColor = fore;
        b.Cursor = Cursors.Hand;
        return b;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        tZh.Focus();
        tZh.SelectAll();
    }
}

// 设置弹窗：用户模式开关 + 后缀
// 数量弹窗：未改动时直接按 1–9 立即确认；10 以上先按退格再输入，回车确认
class QtyDialog : Form
{
    readonly TextBox tQty = new TextBox();
    bool touched;

    public int Qty { get { int n; return int.TryParse(tQty.Text.Trim(), out n) ? n : 0; } }

    public QtyDialog(string preview, int lastQty)
    {
        Text = "份数";
        Font = Theme.Small;
        BackColor = Theme.Bg;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(300, 150);

        Label lp = new Label();
        lp.Text = preview;
        lp.AutoEllipsis = true;
        lp.SetBounds(16, 12, 270, 22);
        Label lq = new Label();
        lq.Text = "份数";
        lq.SetBounds(16, 48, 40, 22);
        tQty.SetBounds(60, 44, 80, 28);
        tQty.Font = new Font("Georgia", 13f);
        tQty.TextAlign = HorizontalAlignment.Center;
        tQty.Text = lastQty.ToString();
        Label lh = new Label();
        lh.Text = "直接按 1–9 立即复制 · 回车确认 · Esc 取消\n10 份以上：先按退格再输入";
        lh.ForeColor = Theme.Muted;
        lh.SetBounds(16, 84, 280, 40);

        tQty.KeyPress += delegate(object o, KeyPressEventArgs e)
        {
            if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) { e.Handled = true; return; }
            if (!touched && e.KeyChar >= '1' && e.KeyChar <= '9')
            {
                tQty.Text = e.KeyChar.ToString();
                e.Handled = true;
                DialogResult = DialogResult.OK;
                return;
            }
            touched = true;
        };
        KeyDown += delegate(object o, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && Qty > 0) { e.SuppressKeyPress = true; DialogResult = DialogResult.OK; }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; DialogResult = DialogResult.Cancel; }
        };

        Controls.AddRange(new Control[] { lp, lq, tQty, lh });

        // 出现在鼠标附近，并保证不超出屏幕
        Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int x = Math.Min(Cursor.Position.X - 40, wa.Right - Width);
        int y = Math.Min(Cursor.Position.Y + 16, wa.Bottom - Height);
        Location = new Point(Math.Max(wa.Left, x), Math.Max(wa.Top, y));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        tQty.Focus();
        tQty.SelectAll();
    }
}

class SettingsDialog : Form
{
    readonly CheckBox chk = new CheckBox();
    readonly TextBox tSuffix = new TextBox();
    readonly Label preview = new Label();
    readonly NumericUpDown numCutoff = new NumericUpDown();

    public bool UserMode { get { return chk.Checked; } }
    public string Suffix { get { return tSuffix.Text.Replace("\r", "").Replace("\n", "").Trim(); } }

    public int CutoffHour { get { return (int)numCutoff.Value; } }

    public SettingsDialog(bool userMode, string suffix, int cutoffHour)
    {
        Text = "设置";
        Font = Theme.Small;
        BackColor = Theme.Bg;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(340, 320);

        chk.Text = "用户模式：复制时自动在末尾加上后缀";
        chk.SetBounds(20, 16, 300, 24);
        chk.Checked = userMode;

        Label l = new Label();
        l.Text = "后缀（例如：-Mike）";
        l.ForeColor = Theme.Muted;
        l.SetBounds(20, 50, 300, 20);
        tSuffix.SetBounds(20, 72, 300, 26);
        tSuffix.Font = Theme.Zh;
        tSuffix.Text = suffix;

        preview.SetBounds(20, 108, 300, 40);
        preview.ForeColor = Theme.Clay;

        Label lc = new Label();
        lc.Text = "营业日分界：凌晨";
        lc.SetBounds(20, 166, 104, 22);
        numCutoff.SetBounds(124, 163, 48, 26);
        Label lcEnd = new Label();
        lcEnd.Text = "点前，{今天} 仍算前一天";
        lcEnd.SetBounds(176, 166, 160, 22);
        numCutoff.Minimum = 0;
        numCutoff.Maximum = 6;
        numCutoff.Value = Math.Max(0, Math.Min(6, cutoffHour));
        Label lc2 = new Label();
        lc2.Text = "0 = 按日历日期。词条里可写 {今天} {明天} 自动填日期";
        lc2.ForeColor = Theme.Muted;
        lc2.SetBounds(20, 196, 320, 40);

        Button ok = new Button();
        ok.Text = "保存";
        ok.FlatStyle = FlatStyle.Flat;
        ok.FlatAppearance.BorderColor = Theme.Line;
        ok.BackColor = Theme.Clay;
        ok.ForeColor = Color.White;
        ok.SetBounds(214, 270, 106, 32);
        ok.DialogResult = DialogResult.OK;
        Button cancel = new Button();
        cancel.Text = "取消";
        cancel.FlatStyle = FlatStyle.Flat;
        cancel.FlatAppearance.BorderColor = Theme.Line;
        cancel.BackColor = Color.White;
        cancel.SetBounds(100, 270, 106, 32);
        cancel.DialogResult = DialogResult.Cancel;

        chk.CheckedChanged += delegate { UpdatePreview(); };
        tSuffix.TextChanged += delegate { UpdatePreview(); };
        UpdatePreview();

        // 底部：版本号 + 官网链接（点击用浏览器打开）
        string ver = MainForm.ReadVersion();
        string prefix = (ver.Length > 0 ? "v" + ver + " · " : "");
        LinkLabel about = new LinkLabel();
        about.Text = prefix + MainForm.Website;
        about.LinkArea = new LinkArea(prefix.Length, MainForm.Website.Length);
        about.LinkBehavior = LinkBehavior.HoverUnderline;
        about.ForeColor = Theme.Muted;
        about.LinkColor = Theme.Clay;
        about.ActiveLinkColor = Theme.ClayDark;
        about.SetBounds(20, 240, 300, 20);
        about.LinkClicked += delegate
        {
            try { System.Diagnostics.Process.Start("https://" + MainForm.Website); }
            catch { MessageBox.Show(this, "无法打开浏览器", "Starport", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };

        Controls.AddRange(new Control[] { chk, l, tSuffix, preview, numCutoff, lc, lcEnd, lc2, about, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    void UpdatePreview()
    {
        tSuffix.Enabled = chk.Checked;
        preview.Text = "复制效果：" + MainForm.WithSuffix("港币1000押金", chk.Checked, Suffix) +
                       "\n界面上不显示后缀";
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        tSuffix.Focus();
    }
}

class ImageCard : Control
{
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
        Size = new Size(104, 78);
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

// 放大查看图片窗口：点击或按 Esc 关闭，绝不写入剪贴板
class ImageViewer : Form
{
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
        int maxW = (int)(wa.Width * 0.8), maxH = (int)(wa.Height * 0.8);
        int pad = 24;
        float scale = Math.Min(1f, Math.Min((float)(maxW - pad) / Math.Max(1, img.Width), (float)(maxH - pad) / Math.Max(1, img.Height)));
        int cw = Math.Max(200, (int)Math.Round(img.Width * scale) + pad);
        int ch = Math.Max(150, (int)Math.Round(img.Height * scale) + pad);
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

        float availW = Math.Max(1, ClientSize.Width - 24);
        float availH = Math.Max(1, ClientSize.Height - 24);
        float scale = Math.Min(1f, Math.Min(availW / img.Width, availH / img.Height));
        float dw = img.Width * scale, dh = img.Height * scale;
        float dx = (ClientSize.Width - dw) / 2f, dy = (ClientSize.Height - dh) / 2f;
        g.DrawImage(img, dx, dy, dw, dh);
        using (Pen p = new Pen(Theme.Line)) g.DrawRectangle(p, dx - 1, dy - 1, dw + 1, dh + 1);
    }
}

class MainForm : Form
{
    const int WM_MOUSEACTIVATE = 0x21;
    const int MA_NOACTIVATE = 3;
    const int HTCLIENT = 1;
    const int FormW = 380, HeaderH = 80, FooterH = 52, ContextBarH = 34, ImageAreaH = 216;
    const int MaxVisibleCards = 10, MaxPhrases = 20, MaxImages = 6, MaxImageSide = 2560;

    static readonly string[] Defaults =
    {
        "港币1000押金|HKD 1,000 deposit",
        "VAPB，预授权1000|VAPB, pre-auth 1,000",
        "请收取押金|Please collect the deposit",
        "加急，客人赶时间|Urgent – guest is in a hurry",
        "遗落物品，客人上去拿|Guest will go up to collect left items",
        "客人遗落物品不要了|Guest does not want the left items",
        "借用酒店的充电器在前台|Hotel charger on loan – return to front desk",
        "无视请勿打扰（DND），进行查房|Override DND – proceed with room check",
        "提前退房 {今天}|Early out {today}",
        "加购{数量}份自助早餐 {明天}|{qty} EBF {tomorrow}",
    };

    // 默认词条版本：升级后给老用户补上新增的默认词条（只补一次）
    const int DefaultsVersion = 3;
    static readonly string[] DefaultsV2 =
    {
        "提前退房 {今天}|Early out {today}",
        "加购{数量}份自助早餐 {明天}|{qty} EBF {tomorrow}",
    };
    int defaultsVersion = 1;
    int lastQty = 2;

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    DateTime shownDate;

    readonly string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "phrases.txt");
    readonly List<Phrase> phrases = new List<Phrase>();
    readonly List<Image> images = new List<Image>();
    readonly Panel header = new Panel();
    readonly Panel list = new Panel();
    readonly Panel contextBar = new Panel();
    readonly Panel imagePanel = new Panel();
    readonly Panel imageHeader = new Panel();
    readonly Panel imageGrid = new Panel();
    readonly Panel footer = new Panel();
    readonly Label status = new Label();
    readonly PillButton btnPin = new PillButton("📌");
    readonly PillButton btnSettings = new PillButton("\u2699");
    readonly PillButton btnContext = new PillButton("扩展上下文窗口");
    readonly PillButton btnPasteImg = new PillButton("粘贴截图");
    readonly PillButton btnClearImg = new PillButton("清空");
    readonly PillButton btnAdd = new PillButton("＋ 新增");
    readonly PillButton btnManage = new PillButton("管理");
    bool topMost = true;
    bool contextExpanded;
    bool userMode;       // 用户模式：复制时自动加后缀，界面不显示
    string userSuffix = "";
    bool manage;  // 管理模式：卡片只编辑/删除，不复制
    const string ManageHint = "管理中 · 点卡片编辑";
    readonly System.Windows.Forms.Timer tip = new System.Windows.Forms.Timer();
    readonly ToolTip toolTip = new ToolTip();
    Image logo;

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }

    public MainForm()
    {
        string ver = ReadVersion();
        Text = "Starport 固定剪贴板" + (ver.Length > 0 ? " v" + ver : "");
        Font = Theme.Small;
        BackColor = Theme.Bg;
        TopMost = true;
        ShowInTaskbar = true;  // 任务栏显示图标，被遮挡时可点任务栏找回
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        try
        {
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png");
            if (s != null)
            {
                logo = Image.FromStream(s);
                using (Bitmap bmp = new Bitmap(logo, 32, 32)) Icon = Icon.FromHandle(bmp.GetHicon());
            }
        }
        catch { }

        toolTip.ShowAlways = true;  // 窗口不激活时也显示完整文字
        toolTip.InitialDelay = 700;

        header.Dock = DockStyle.Top;
        header.Height = HeaderH;
        header.Paint += PaintHeader;
        btnPin.Font = Theme.Sym;
        btnPin.SetBounds(FormW - 84, 12, 32, 32);
        btnPin.Click += delegate { ToggleTopMost(); };
        btnSettings.Outline = true;
        btnSettings.Font = Theme.Sym;
        btnSettings.SetBounds(FormW - 46, 12, 32, 32);
        btnSettings.Click += delegate { OpenSettings(); };
        toolTip.SetToolTip(btnSettings, "设置");
        header.Controls.Add(btnPin);
        header.Controls.Add(btnSettings);

        list.Dock = DockStyle.Fill;
        list.AutoScroll = true;

        contextBar.Dock = DockStyle.Bottom;
        contextBar.Height = ContextBarH;
        contextBar.Paint += delegate(object o, PaintEventArgs e)
        {
            using (Pen p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 14, 0, contextBar.Width - 14, 0);
        };
        btnContext.Outline = true;
        btnContext.SetBounds(14, 4, FormW - 28, 26);
        btnContext.Click += delegate { ToggleContext(); };
        toolTip.SetToolTip(btnContext, "Expand context window");
        contextBar.Controls.Add(btnContext);

        imagePanel.Dock = DockStyle.Bottom;
        imagePanel.Height = ImageAreaH;
        imagePanel.Visible = false;
        imagePanel.AllowDrop = true;
        imagePanel.DragEnter += OnImageDragEnter;
        imagePanel.DragDrop += OnImageDragDrop;

        imageHeader.Dock = DockStyle.Top;
        imageHeader.Height = 36;
        imageHeader.AllowDrop = true;
        imageHeader.DragEnter += OnImageDragEnter;
        imageHeader.DragDrop += OnImageDragDrop;
        imageHeader.Paint += delegate(object o, PaintEventArgs e)
        {
            Theme.Text(e.Graphics, "可拖入图片 · 最多 6 张", Theme.Small,
                new Rectangle(104, 4, 196, 28), Theme.Muted, TextFormatFlags.Left);
        };
        btnPasteImg.Outline = true;
        btnPasteImg.SetBounds(14, 5, 82, 26);
        btnPasteImg.Click += delegate { PasteImage(); };
        btnClearImg.Outline = true;
        btnClearImg.SetBounds(FormW - 70, 5, 56, 26);
        btnClearImg.Click += delegate { ClearImages(); };
        imageHeader.Controls.Add(btnPasteImg);
        imageHeader.Controls.Add(btnClearImg);

        imageGrid.Dock = DockStyle.Fill;
        imageGrid.AutoScroll = true;
        imageGrid.AllowDrop = true;
        imageGrid.DragEnter += OnImageDragEnter;
        imageGrid.DragDrop += OnImageDragDrop;
        imageGrid.Paint += delegate(object o, PaintEventArgs e)
        {
            if (images.Count == 0)
            {
                Theme.Text(e.Graphics, "暂无参考图片 · 点「粘贴截图」或拖入图片文件", Theme.Small,
                    new Rectangle(14, 0, imageGrid.Width - 28, imageGrid.Height),
                    Theme.Muted, TextFormatFlags.HorizontalCenter);
            }
        };

        imagePanel.Controls.Add(imageGrid);
        imagePanel.Controls.Add(imageHeader);

        footer.Dock = DockStyle.Bottom;
        footer.Height = FooterH;
        footer.Paint += delegate(object o, PaintEventArgs e)
        {
            using (Pen p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
        };
        status.SetBounds(14, 15, FormW - 196, 22);
        status.ForeColor = Theme.Clay;
        status.AutoEllipsis = true;
        btnAdd.SetBounds(FormW - 106, 11, 92, 30);
        btnAdd.Click += delegate { AddPhrase(); };
        btnManage.Outline = true;
        btnManage.SetBounds(FormW - 178, 11, 64, 30);
        btnManage.Click += delegate { SetManage(!manage); };
        footer.Controls.Add(status);
        footer.Controls.Add(btnManage);
        footer.Controls.Add(btnAdd);

        // Controls 顺序决定 Dock 顺序：后加入的先停靠在边缘，list 最后占满剩余空间
        Controls.Add(list);
        Controls.Add(header);
        Controls.Add(contextBar);
        Controls.Add(imagePanel);
        Controls.Add(footer);

        shownDate = DateTokens.BusinessDate;
        System.Windows.Forms.Timer dayTimer = new System.Windows.Forms.Timer();
        dayTimer.Interval = 30000;
        dayTimer.Tick += delegate
        {
            if (DateTokens.BusinessDate == shownDate) return;
            shownDate = DateTokens.BusinessDate;
            foreach (Control c in list.Controls) c.Invalidate();
        };
        dayTimer.Start();

        tip.Interval = 1600;
        tip.Tick += delegate { tip.Stop(); ShowIdleStatus(); };

        KeyDown += OnKey;
        LoadPhrases();
        UpdatePinButton();
        ClientSize = new Size(FormW, 300);
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - Width - 20, wa.Top + 60);
        Rebuild();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        foreach (Control c in imageGrid.Controls) c.Dispose();
        imageGrid.Controls.Clear();
        foreach (Image img in images) img.Dispose();
        images.Clear();
        if (logo != null) { logo.Dispose(); logo = null; }
        base.OnFormClosed(e);
    }

    // 版本号来自编译时打包进 exe 的 VERSION 文件（见 build.bat 的 /resource:VERSION,version.txt）
    public const string Website = "starport1116.com";

    public static string ReadVersion()
    {
        try
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("version.txt"))
            {
                if (s == null) return "";
                using (StreamReader r = new StreamReader(s)) return r.ReadToEnd().Trim();
            }
        }
        catch { return ""; }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    // 点击窗口内容时不激活本窗口，目标软件保持焦点；拖动标题栏照常
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE && (m.LParam.ToInt64() & 0xFFFF) == HTCLIENT)
        {
            m.Result = (IntPtr)MA_NOACTIVATE;
            return;
        }
        base.WndProc(ref m);
    }

    void PaintHeader(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        int x = 16;
        if (logo != null) { g.DrawImage(logo, new Rectangle(14, 16, 46, 46)); x = 68; }
        int w = header.Width - x - 10;
        Theme.Text(g, "Starport", Theme.Title, new Rectangle(x, 6, w, 28), Theme.Ink, TextFormatFlags.Left);
        Theme.Text(g, "前沿剪贴板研究实验室", Theme.Small, new Rectangle(x + 1, 33, w, 20), Theme.Muted, TextFormatFlags.Left);
        Theme.Text(g, "Putting safety at the frontier desk.", Theme.En, new Rectangle(x + 1, 52, w, 18), Theme.Clay, TextFormatFlags.Left);
        using (Pen p = new Pen(Theme.Line)) g.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
    }

    void UpdatePinButton()
    {
        TopMost = topMost;
        btnPin.Outline = !topMost;
        btnPin.Invalidate();
        toolTip.SetToolTip(btnPin, topMost ? "置顶：开" : "置顶：关");
    }

    void ToggleTopMost()
    {
        topMost = !topMost;
        UpdatePinButton();
        Save();
        Flash(topMost ? "已置顶" : "已取消置顶", false);
    }

    void ToggleContext()
    {
        contextExpanded = !contextExpanded;
        imagePanel.Visible = contextExpanded;
        btnContext.Text = contextExpanded ? "收起上下文" : "扩展上下文窗口";
        btnContext.Invalidate();
        if (contextExpanded) RebuildImages();
        Rebuild();
    }

    static Image NormalizeImage(Image src)
    {
        int w = src.Width, h = src.Height;
        if (w > MaxImageSide || h > MaxImageSide)
        {
            float scale = (float)MaxImageSide / Math.Max(w, h);
            w = Math.Max(1, (int)Math.Round(w * scale));
            h = Math.Max(1, (int)Math.Round(h * scale));
        }
        Bitmap bmp = new Bitmap(w, h);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, new Rectangle(0, 0, w, h));
        }
        return bmp;
    }

    bool AddStagedImage(Image raw, out bool evicted)
    {
        evicted = false;
        if (raw == null) return false;
        Image norm;
        try { norm = NormalizeImage(raw); }
        finally { raw.Dispose(); }

        if (images.Count >= MaxImages)
        {
            Image old = images[0];
            images.RemoveAt(0);
            old.Dispose();
            evicted = true;
        }
        images.Add(norm);
        return true;
    }

    void PasteImage()
    {
        try
        {
            if (!Clipboard.ContainsImage())
            {
                Flash("剪贴板里没有图片", true);
                return;
            }
            Image clip = Clipboard.GetImage();
            if (clip == null)
            {
                Flash("剪贴板里没有图片", true);
                return;
            }
            bool evicted;
            AddStagedImage(clip, out evicted);
            RebuildImages();
            Flash(evicted ? "已满 6 张，已移除最早的一张" : "已粘贴截图", false);
        }
        catch
        {
            Flash("读取剪贴板图片失败", true);
        }
    }

    static bool IsAllowedImageExt(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string ext = Path.GetExtension(path);
        if (ext == null) return false;
        ext = ext.ToLowerInvariant();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif";
    }

    void OnImageDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap))
            e.Effect = DragDropEffects.Copy;
        else
            e.Effect = DragDropEffects.None;
    }

    void OnImageDragDrop(object sender, DragEventArgs e)
    {
        int added = 0;
        bool ignored = false;
        bool anyEvicted = false;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null)
            {
                foreach (string f in files)
                {
                    if (!IsAllowedImageExt(f)) { ignored = true; continue; }
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(f);
                        using (MemoryStream ms = new MemoryStream(bytes))
                        {
                            Image raw = Image.FromStream(ms);
                            bool ev;
                            if (AddStagedImage(raw, out ev))
                            {
                                added++;
                                if (ev) anyEvicted = true;
                            }
                        }
                    }
                    catch { ignored = true; }
                }
            }
        }
        else if (e.Data.GetDataPresent(DataFormats.Bitmap))
        {
            try
            {
                Image bmp = e.Data.GetData(DataFormats.Bitmap) as Image;
                if (bmp != null)
                {
                    Image clone = new Bitmap(bmp);
                    bool ev;
                    if (AddStagedImage(clone, out ev))
                    {
                        added++;
                        if (ev) anyEvicted = true;
                    }
                }
            }
            catch { ignored = true; }
        }

        if (added > 0)
        {
            RebuildImages();
            if (anyEvicted) Flash("已满 6 张，已移除最早的一张", false);
            else if (ignored) Flash("已添加图片（非支持格式已忽略）", false);
            else Flash("已添加图片", false);
        }
        else if (ignored)
        {
            Flash("仅支持拖入 png/jpg/jpeg/bmp/gif 图片", true);
        }
    }

    void RebuildImages()
    {
        imageGrid.SuspendLayout();
        imageGrid.AutoScrollPosition = Point.Empty;
        foreach (Control c in imageGrid.Controls) c.Dispose();
        imageGrid.Controls.Clear();

        for (int i = 0; i < images.Count; i++)
        {
            ImageCard ic = new ImageCard(images[i]);
            int col = i % 3, row = i / 3;
            ic.Location = new Point(14 + col * (104 + 10), 6 + row * (78 + 8));
            ic.ViewRequested += delegate(ImageCard k) { BeginInvoke((MethodInvoker)delegate { ViewImage(k.Img); }); };
            ic.DeleteRequested += delegate(ImageCard k) { BeginInvoke((MethodInvoker)delegate { RemoveImage(k.Img); }); };
            ic.AllowDrop = true;
            ic.DragEnter += OnImageDragEnter;
            ic.DragDrop += OnImageDragDrop;
            imageGrid.Controls.Add(ic);
        }

        btnClearImg.Visible = images.Count > 0;
        imageGrid.ResumeLayout();
        imageGrid.Invalidate();
    }

    void ViewImage(Image img)
    {
        if (img == null) return;
        IntPtr target = IntPtr.Zero;
        try { target = GetForegroundWindow(); } catch { }
        using (ImageViewer v = new ImageViewer(img))
        {
            v.ShowDialog(this);
            if (target != IntPtr.Zero && target != Handle)
                try { SetForegroundWindow(target); } catch { }
        }
    }

    void RemoveImage(Image img)
    {
        if (images.Remove(img))
        {
            img.Dispose();
            RebuildImages();
            Flash("已移除图片", false);
        }
    }

    void ClearImages()
    {
        if (images.Count == 0) return;
        IntPtr target = IntPtr.Zero;
        try { target = GetForegroundWindow(); } catch { }
        DialogResult r = MessageBox.Show(this, "清空全部 " + images.Count + " 张参考图片？", "确认清空",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (target != IntPtr.Zero && target != Handle)
            try { SetForegroundWindow(target); } catch { }
        if (r != DialogResult.Yes) return;

        foreach (Control c in imageGrid.Controls) c.Dispose();
        imageGrid.Controls.Clear();
        foreach (Image img in images) img.Dispose();
        images.Clear();
        RebuildImages();
        Flash("已清空图片", false);
    }

    // 设置行格式：# + 小写英文/下划线 + =，例如 #topmost=1
    static bool IsSettingLine(string line)
    {
        int eq = line.IndexOf('=');
        if (!line.StartsWith("#") || eq < 2) return false;
        for (int i = 1; i < eq; i++)
        {
            char ch = line[i];
            if (!((ch >= 'a' && ch <= 'z') || ch == '_')) return false;
        }
        return true;
    }

    // phrases.txt 每行：中文|English|点击次数（后两项可省略）
    void LoadPhrases()
    {
        try
        {
            string[] lines = File.Exists(file) ? File.ReadAllLines(file, Encoding.UTF8) : Defaults;
            foreach (string line in lines)
            {
                // 设置行：#topmost=1、#user_mode=1、#user_suffix=后缀
                if (line.StartsWith("#topmost=")) { topMost = line.Substring(9).Trim() != "0"; continue; }
                if (line.StartsWith("#user_mode=")) { userMode = line.Substring(11).Trim() == "1"; continue; }
                if (line.StartsWith("#user_suffix=")) { userSuffix = line.Substring(13).Trim(); continue; }
                if (line.StartsWith("#last_qty=")) { int.TryParse(line.Substring(10).Trim(), out lastQty); continue; }
                if (line.StartsWith("#day_cutoff=")) { int.TryParse(line.Substring(12).Trim(), out DateTokens.CutoffHour); continue; }
                if (line.StartsWith("#defaults=")) { int.TryParse(line.Substring(10).Trim(), out defaultsVersion); continue; }
                if (IsSettingLine(line)) continue;  // 未知设置行（如新版本写入的）跳过；「#1203 ...」这类词条照常读取
                string[] parts = line.Split('|');
                string zh = parts[0].Trim();
                if (zh.Length == 0) continue;
                string en = parts.Length > 1 ? parts[1].Trim() : "";
                int n = 0;
                if (parts.Length > 2) int.TryParse(parts[2].Trim(), out n);
                phrases.Add(new Phrase(zh, en, Math.Max(0, n)));
            }
            if (!File.Exists(file)) { defaultsVersion = DefaultsVersion; Save(); }
            else
            {
                if (defaultsVersion < 2)
                {
                    foreach (string d in DefaultsV2)
                    {
                        string[] parts = d.Split('|');
                        if (!Exists(parts[0], null)) phrases.Add(new Phrase(parts[0], parts[1], 0));
                    }
                }
                if (defaultsVersion < 3)
                {
                    // v1.4 的固定 2 份 EBF 改为复制时输入份数（保留点击次数）
                    foreach (Phrase p in phrases)
                        if (p.Zh == "加购2份自助早餐 {明天}" && p.En == "2 EBF {tomorrow}")
                        {
                            p.Zh = "加购{数量}份自助早餐 {明天}";
                            p.En = "{qty} EBF {tomorrow}";
                        }
                }
                if (defaultsVersion < DefaultsVersion)
                {
                    defaultsVersion = DefaultsVersion;
                    Save();
                }
                HideFile();  // 旧版本留下的可见 phrases.txt 也设为隐藏
            }
        }
        catch { Flash("读取 phrases.txt 失败", true); }

        // 启动时按点击次数从高到低排（稳定排序，次数相同保持原顺序）
        for (int i = 1; i < phrases.Count; i++)
        {
            Phrase cur = phrases[i];
            int j = i - 1;
            while (j >= 0 && phrases[j].Count < cur.Count) { phrases[j + 1] = phrases[j]; j--; }
            phrases[j + 1] = cur;
        }
    }

    // phrases.txt 设为隐藏，文件夹里看起来只有一个 exe
    void HideFile()
    {
        try { File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.Hidden); }
        catch { }
    }

    void Save()
    {
        try
        {
            List<string> lines = new List<string>();
            lines.Add("#topmost=" + (topMost ? "1" : "0"));
            lines.Add("#user_mode=" + (userMode ? "1" : "0"));
            lines.Add("#user_suffix=" + userSuffix);
            lines.Add("#day_cutoff=" + DateTokens.CutoffHour);
            lines.Add("#last_qty=" + lastQty);
            lines.Add("#defaults=" + defaultsVersion);
            foreach (Phrase p in phrases)
                lines.Add(p.Zh + "|" + p.En + "|" + p.Count);
            // 隐藏文件不能用 File.WriteAllLines 覆盖（会拒绝访问），改为打开后截断再写
            byte[] data = new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines.ToArray()) + "\r\n");
            using (FileStream fs = new FileStream(file, FileMode.OpenOrCreate, FileAccess.Write))
            {
                fs.SetLength(0);
                fs.Write(data, 0, data.Length);
            }
            HideFile();
        }
        catch { Flash("保存失败（目录只读？）", true); }
    }

    int CalcListHeight(out int totalCardH)
    {
        totalCardH = 10;
        int visibleH = 10;
        for (int i = 0; i < phrases.Count; i++)
        {
            int ch = Card.HeightFor(phrases[i]);
            totalCardH += ch;
            if (i < MaxVisibleCards) visibleH += ch;
        }
        totalCardH += 4;
        visibleH += phrases.Count > MaxVisibleCards ? 18 : 4;  // 还有更多时露出下一张的一截

        // 整个窗口最多占屏幕工作区 85%；展开图片区时列表让出高度，改为滚动
        Rectangle wa = Screen.FromControl(this).WorkingArea;
        int maxClient = (int)(wa.Height * 0.85) - SystemInformation.CaptionHeight;
        int maxList = maxClient - HeaderH - ContextBarH - FooterH - (contextExpanded ? ImageAreaH : 0);
        return Math.Max(64, Math.Min(visibleH, maxList));
    }

    void UpdateWindowSize()
    {
        int total;
        int listH = CalcListHeight(out total);
        int extraH = contextExpanded ? ImageAreaH : 0;
        ClientSize = new Size(FormW, HeaderH + listH + ContextBarH + extraH + FooterH);

        // 窗口底部超出屏幕时往上挪
        Rectangle wa = Screen.FromControl(this).WorkingArea;
        if (Bottom > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - Height);
    }

    void Rebuild()
    {
        list.SuspendLayout();
        list.AutoScrollPosition = Point.Empty;
        foreach (Control c in list.Controls) c.Dispose();
        list.Controls.Clear();

        List<Card> cards = new List<Card>();
        for (int i = 0; i < phrases.Count; i++)
        {
            Card c = new Card(phrases[i], i, manage);
            c.CopyRequested += delegate(Card k, bool en) { CopyText(k.P, en); };
            // 编辑/删除会重建卡片，延后执行，避免在卡片自身的鼠标事件里销毁它
            c.EditRequested += delegate(Card k) { BeginInvoke((MethodInvoker)delegate { EditPhrase(k.P); }); };
            c.DeleteRequested += delegate(Card k) { BeginInvoke((MethodInvoker)delegate { RemovePhrase(k.P); }); };
            cards.Add(c);
        }

        int total;
        int listH = CalcListHeight(out total);
        int cardW = FormW - 28 - (total > listH ? SystemInformation.VerticalScrollBarWidth : 0);

        int y = 10;
        foreach (Card c in cards)
        {
            c.SetBounds(14, y, cardW, c.Height);
            // 只有文字被截断的卡片才显示完整内容提示
            toolTip.SetToolTip(c, c.IsTruncated ? (c.P.En.Length > 0 ? c.ZhText + "\n" + c.EnText : c.ZhText) : null);
            y += c.Height;
            list.Controls.Add(c);
        }
        list.ResumeLayout();
        UpdateWindowSize();
    }

    void CopyText(Phrase p, bool en)
    {
        string s = DateTokens.Expand(en ? p.En : p.Zh, en);
        if (DateTokens.HasQty(s))
        {
            // 点卡片时本窗口没被激活，前台仍是目标软件；弹窗关闭后把焦点还给它
            IntPtr target = IntPtr.Zero;
            try { target = GetForegroundWindow(); } catch { }  // 还焦点失败不影响复制
            int qty;
            using (QtyDialog d = new QtyDialog(DateTokens.FillQty(s, "?"), lastQty < 1 ? 2 : lastQty))
            {
                DialogResult r = d.ShowDialog(this);
                qty = d.Qty;
                if (target != IntPtr.Zero && target != Handle)
                    try { SetForegroundWindow(target); } catch { }
                if (r != DialogResult.OK || qty < 1) return;
            }
            lastQty = qty;
            s = DateTokens.FillQty(s, qty.ToString());
        }
        for (int n = 0; n < 5; n++)
        {
            try
            {
                Clipboard.SetText(WithSuffix(s, userMode, userSuffix));
                p.Count++;
                Save();
                Flash("✓ 已复制  " + s, false);
                return;
            }
            catch (ExternalException) { Thread.Sleep(60); }  // 剪贴板被占用，重试
            catch (Exception) { break; }
        }
        Flash("复制失败", true);
    }

    bool Exists(string zh, Phrase except)
    {
        foreach (Phrase p in phrases)
            if (p != except && p.Zh == zh) return true;
        return false;
    }

    void AddPhrase()
    {
        if (phrases.Count >= MaxPhrases)
        {
            Flash("已达 20 条上限，请先在管理中删除不常用的", true);
            return;
        }
        using (EditDialog d = new EditDialog("新增词条", "", ""))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (Exists(d.Zh, null)) { Flash("已存在相同词条", true); return; }
            phrases.Add(new Phrase(d.Zh, d.En, 0));
            Save();
            Rebuild();
            if (list.Controls.Count > 0) list.ScrollControlIntoView(list.Controls[list.Controls.Count - 1]);
            Flash("已添加", false);
        }
    }

    void EditPhrase(Phrase p)
    {
        using (EditDialog d = new EditDialog("编辑词条", p.Zh, p.En))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (Exists(d.Zh, p)) { Flash("已存在相同词条", true); return; }
            p.Zh = d.Zh;
            p.En = d.En;
            Save();
            Rebuild();
            Flash("已保存", false);
        }
    }

    void RemovePhrase(Phrase p)
    {
        if (MessageBox.Show(this, "删除「" + p.Zh + "」？", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        phrases.Remove(p);
        Save();
        Rebuild();
    }

    // 数字键 1–9、0 复制对应中文（窗口激活时有效，例如刚拖动过标题栏）
    void OnKey(object sender, KeyEventArgs e)
    {
        int n = -1;
        if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9) n = e.KeyCode - Keys.D1;
        else if (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9) n = e.KeyCode - Keys.NumPad1;
        else if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) n = 9;
        if (!manage && n >= 0 && n < phrases.Count) { CopyText(phrases[n], false); e.Handled = true; }
    }

    // 用户模式下在末尾加后缀（用空格隔开）；状态栏和卡片仍只显示原文
    public static string WithSuffix(string text, bool on, string suffix)
    {
        return on && suffix.Length > 0 ? text + " " + suffix : text;
    }

    void OpenSettings()
    {
        using (SettingsDialog d = new SettingsDialog(userMode, userSuffix, DateTokens.CutoffHour))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            userMode = d.UserMode;
            userSuffix = d.Suffix;
            DateTokens.CutoffHour = d.CutoffHour;
            Rebuild();  // 分界时间变了，卡片上的日期可能要更新
            Save();
            Flash("设置已保存", false);
        }
    }

    void SetManage(bool on)
    {
        manage = on;
        btnManage.Text = on ? "完成" : "管理";
        btnManage.Outline = !on;
        btnManage.Invalidate();
        Rebuild();
        tip.Stop();
        ShowIdleStatus();
    }

    void ShowIdleStatus()
    {
        status.ForeColor = Theme.Muted;
        status.Text = manage ? ManageHint : "";
    }

    void Flash(string msg, bool err)
    {
        status.ForeColor = err ? Color.Firebrick : Theme.Clay;
        status.Text = msg;
        tip.Stop();
        tip.Start();
    }
}

