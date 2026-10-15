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

// Starport Haiku（固定剪贴板）：点卡片复制中文，点 EN 复制英文；窗口置顶，点击卡片不抢目标软件焦点

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
    public bool Pinned;  // 钉住：固定在最上面，不参与按次数排序
    public Phrase(string zh, string en, int count) { Zh = zh; En = en; Count = count; }
}

// 一条词条卡片。
// 平时：主体 = 复制中文，EN = 复制英文，位置固定不动。
// 管理模式：不复制；主体或 ✎ = 编辑，✕ = 删除。
class Card : Control
{
    enum Zone { None, Body, En, Pin, Edit, Del }

    public readonly Phrase P;
    public readonly int Index;
    public readonly bool Manage;
    public event Action<Card, bool> CopyRequested;  // bool = 是否英文
    public event Action<Card> EditRequested, DeleteRequested, PinRequested;

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
    Rectangle PinRect { get { return new Rectangle(Width - 98, (BoxH - 28) / 2, 28, 28); } }
    Rectangle CountRect { get { return new Rectangle(PinRect.Left - 50, 0, 48, BoxH); } }  // 管理模式：点击次数

    Rectangle TextArea
    {
        get
        {
            int right = Manage ? CountRect.Left - 4 : (P.En.Length > 0 ? EnRect.Left - 8 : Width - 12);
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
            if (PinRect.Contains(pt)) return Zone.Pin;
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
            case Zone.Pin: if (PinRequested != null) PinRequested(this); break;
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

        // 钉住的卡片：左侧一道橙色细条
        if (P.Pinned)
            using (GraphicsPath bar = Theme.Round(new RectangleF(3, 10, 3, Math.Max(4, h - 20)), 1.5f))
            using (SolidBrush bb = new SolidBrush(Theme.Clay))
                g.FillPath(bb, bar);

        // 序号对应键盘最上排 1 2 3 … 9 0；第 11 张起不显示序号
        Theme.Text(g, Index < 9 ? (Index + 1).ToString() : (Index == 9 ? "0" : ""), Theme.Num,
            new Rectangle(6, 0, 28, h), Theme.Clay, TextFormatFlags.HorizontalCenter);

        if (Manage)
        {
            Theme.Text(g, P.Count + " 次", Theme.Small, CountRect, Theme.Muted, TextFormatFlags.Right);
            Theme.Text(g, "\U0001F4CC", Theme.Sym, PinRect,
                P.Pinned || zone == Zone.Pin ? Theme.Clay : Theme.Line, TextFormatFlags.HorizontalCenter);
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

    public SettingsDialog(MainForm main, bool userMode, string suffix, int cutoffHour)
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
        ClientSize = new Size(340, 362);

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
        ok.SetBounds(214, 312, 106, 32);
        ok.DialogResult = DialogResult.OK;
        Button cancel = new Button();
        cancel.Text = "取消";
        cancel.FlatStyle = FlatStyle.Flat;
        cancel.FlatAppearance.BorderColor = Theme.Line;
        cancel.BackColor = Color.White;
        cancel.SetBounds(100, 312, 106, 32);
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
        about.SetBounds(20, 284, 300, 20);
        about.LinkClicked += delegate
        {
            try { System.Diagnostics.Process.Start("https://" + MainForm.Website); }
            catch { MessageBox.Show(this, "无法打开浏览器", "Starport", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };

        // 导出/导入词条：方便同事之间共用一套常用语、换电脑
        Button exp = new Button();
        exp.Text = "导出词条…";
        exp.FlatStyle = FlatStyle.Flat;
        exp.FlatAppearance.BorderColor = Theme.Line;
        exp.BackColor = Color.White;
        exp.SetBounds(20, 240, 96, 30);
        exp.Click += delegate { main.ExportPhrases(this); };
        Button imp = new Button();
        imp.Text = "导入词条…";
        imp.FlatStyle = FlatStyle.Flat;
        imp.FlatAppearance.BorderColor = Theme.Line;
        imp.BackColor = Color.White;
        imp.SetBounds(122, 240, 96, 30);
        imp.Click += delegate { main.ImportPhrases(this); };

        Button usage = new Button();
        usage.Text = "用量统计…";
        usage.FlatStyle = FlatStyle.Flat;
        usage.FlatAppearance.BorderColor = Theme.Line;
        usage.BackColor = Color.White;
        usage.SetBounds(224, 240, 96, 30);
        usage.Click += delegate { main.ShowUsage(this); };

        Controls.AddRange(new Control[] { chk, l, tSuffix, preview, numCutoff, lc, lcEnd, lc2, exp, imp, usage, about, ok, cancel });
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

// 用量统计（戏仿 Claude 的 Usage 页面）：今天 / 近 7 天 / 累计、节省的打字时间、近 7 天柱状图、最常用 3 条
class UsageDialog : Form
{
    public const int CharsPerMinute = 40;  // 估算打字速度：每分钟 40 字

    readonly int today, week, total;
    readonly long savedChars;
    readonly DateTime since;
    readonly DateTime[] dayDates;
    readonly int[] dayCounts;
    readonly List<KeyValuePair<string, int>> top;
    readonly MainForm main;

    public UsageDialog(MainForm main, int today, int week, int total, long savedChars, DateTime since,
                       DateTime[] dayDates, int[] dayCounts, List<KeyValuePair<string, int>> top)
    {
        this.main = main;
        this.today = today; this.week = week; this.total = total;
        this.savedChars = savedChars; this.since = since;
        this.dayDates = dayDates; this.dayCounts = dayCounts; this.top = top;

        Text = "用量 Usage";
        Font = Theme.Small;
        BackColor = Theme.Bg;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(360, 470);

        Button reset = new Button();
        reset.Text = "重置";
        reset.FlatStyle = FlatStyle.Flat;
        reset.FlatAppearance.BorderColor = Theme.Line;
        reset.BackColor = Color.White;
        reset.SetBounds(160, 424, 88, 30);
        reset.Click += delegate
        {
            if (MessageBox.Show(this, "清空全部统计，并把每条词条的点击次数归零？\n（启动时的排序也会重新开始）", "重置统计",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            this.main.ResetStats();
            Close();
        };
        Button close = new Button();
        close.Text = "关闭";
        close.FlatStyle = FlatStyle.Flat;
        close.FlatAppearance.BorderColor = Theme.Line;
        close.BackColor = Theme.Clay;
        close.ForeColor = Color.White;
        close.SetBounds(256, 424, 88, 30);
        close.DialogResult = DialogResult.Cancel;
        Controls.Add(reset);
        Controls.Add(close);
        CancelButton = close;
    }

    public static string FormatSaved(long chars)
    {
        long minutes = chars / CharsPerMinute;
        if (minutes < 1) return "不到 1 分钟";
        if (minutes < 60) return minutes + " 分钟";
        return (minutes / 60) + " 小时 " + (minutes % 60) + " 分钟";
    }

    void Tile(Graphics g, int x, string label, int value)
    {
        Rectangle r = new Rectangle(x, 54, 100, 66);
        using (GraphicsPath p = Theme.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 10))
        using (SolidBrush b = new SolidBrush(Theme.Card))
        using (Pen pen = new Pen(Theme.Line))
        {
            g.FillPath(b, p);
            g.DrawPath(pen, p);
        }
        Theme.Text(g, label, Theme.Small, new Rectangle(r.X + 12, r.Y + 6, r.Width - 16, 20), Theme.Muted, TextFormatFlags.Left);
        using (Font f = new Font("Georgia", 17f))
            Theme.Text(g, value.ToString("N0"), f, new Rectangle(r.X + 10, r.Y + 26, r.Width - 14, 32), Theme.Ink, TextFormatFlags.Left);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Bg);

        Theme.Text(g, "用量", Theme.Title, new Rectangle(16, 12, 200, 30), Theme.Ink, TextFormatFlags.Left);
        Theme.Text(g, "Usage", Theme.En, new Rectangle(76, 16, 120, 24), Theme.Muted, TextFormatFlags.Left);

        Tile(g, 16, "今天", today);
        Tile(g, 130, "近 7 天", week);
        Tile(g, 244, "累计", total);

        // 节省的打字时间
        Theme.Text(g, "为你节省了约 " + FormatSaved(savedChars) + "打字", Theme.Zh,
            new Rectangle(16, 132, 330, 26), Theme.Clay, TextFormatFlags.Left);
        Theme.Text(g, "共复制 " + savedChars.ToString("N0") + " 字 · 按每分钟 " + CharsPerMinute + " 字估算", Theme.Small,
            new Rectangle(16, 158, 330, 20), Theme.Muted, TextFormatFlags.Left);

        // 近 7 天柱状图
        Theme.Text(g, "近 7 天", Theme.Small, new Rectangle(16, 188, 200, 20), Theme.Ink, TextFormatFlags.Left);
        int max = 1;
        foreach (int c in dayCounts) max = Math.Max(max, c);
        string[] wk = { "日", "一", "二", "三", "四", "五", "六" };
        int barW = 30, gap = (328 - barW * 7) / 6, baseY = 290, maxH = 70;
        for (int i = 0; i < 7; i++)
        {
            int x = 16 + i * (barW + gap);
            int h = dayCounts[i] == 0 ? 2 : Math.Max(4, dayCounts[i] * maxH / max);
            bool isToday = i == 6;
            using (GraphicsPath p = Theme.Round(new RectangleF(x, baseY - h, barW, h), Math.Min(4, h / 2f)))
            using (SolidBrush b = new SolidBrush(isToday ? Theme.Clay : Color.FromArgb(232, 196, 180)))
                g.FillPath(b, p);
            if (dayCounts[i] > 0)
                Theme.Text(g, dayCounts[i].ToString(), Theme.Small, new Rectangle(x - 10, baseY - h - 18, barW + 20, 16),
                    Theme.Muted, TextFormatFlags.HorizontalCenter);
            Theme.Text(g, isToday ? "今天" : wk[(int)dayDates[i].DayOfWeek], Theme.Small,
                new Rectangle(x - 10, baseY + 4, barW + 20, 18), isToday ? Theme.Clay : Theme.Muted, TextFormatFlags.HorizontalCenter);
        }

        // 最常用 3 条
        Theme.Text(g, "最常用", Theme.Small, new Rectangle(16, 322, 200, 20), Theme.Ink, TextFormatFlags.Left);
        int topMax = top.Count > 0 ? Math.Max(1, top[0].Value) : 1;
        for (int i = 0; i < top.Count; i++)
        {
            int y = 346 + i * 24;
            Theme.Text(g, (i + 1) + ".  " + top[i].Key, Theme.Small, new Rectangle(16, y, 190, 20), Theme.Ink, TextFormatFlags.Left);
            int bw = Math.Max(3, top[i].Value * 80 / topMax);
            using (GraphicsPath p = Theme.Round(new RectangleF(212, y + 6, bw, 8), 4))
            using (SolidBrush b = new SolidBrush(Theme.Clay))
                g.FillPath(b, p);
            Theme.Text(g, top[i].Value.ToString("N0") + " 次", Theme.Small, new Rectangle(296, y, 50, 20), Theme.Muted, TextFormatFlags.Right);
        }
        if (top.Count == 0)
            Theme.Text(g, "还没有复制记录", Theme.Small, new Rectangle(16, 346, 300, 20), Theme.Muted, TextFormatFlags.Left);

        Theme.Text(g, "统计始于 " + since.Year + "年" + since.Month + "月" + since.Day + "日", Theme.Small,
            new Rectangle(16, 428, 150, 22), Theme.Muted, TextFormatFlags.Left);
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

    // 用量统计：#stats_since=、#stats_total=、#stats_chars=、#stats_days=日期:次数,…（只保留近 7 天）
    DateTime statsSince = DateTime.MinValue;
    int statsTotal;
    long statsChars;
    readonly Dictionary<string, int> statsDays = new Dictionary<string, int>();
    Point winPos;          // 上次关闭/拖动后的窗口位置
    bool hasWinPos;
    Phrase undoPhrase;     // 刚删除、还能撤销的词条
    int undoIndex;

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
    readonly PillButton btnUndo = new PillButton("撤销");  // 删除后临时出现
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

    [DllImport("user32.dll")] static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);

    // 同一个文件夹只允许运行一个实例（两个实例同时写 phrases.txt 会互相覆盖）
    static string InstanceKey;
    int showMsg;

    static string MakeInstanceKey()
    {
        string path = AppDomain.CurrentDomain.BaseDirectory.ToLowerInvariant();
        uint h = 2166136261;  // FNV-1a，跨进程稳定
        foreach (char ch in path) { h ^= ch; h *= 16777619; }
        return "StarportFixedClipboard_" + h.ToString("X8");  // 沿用旧名，新旧版本放同一文件夹也不会同时运行
    }

    [STAThread]
    static void Main()
    {
        InstanceKey = MakeInstanceKey();
        Mutex mutex = null;
        bool first = true;
        try { mutex = new Mutex(true, "Local\\" + InstanceKey, out first); } catch { first = true; }
        if (!first)
        {
            // 已经在运行：通知原来的窗口显示到最前面，然后退出
            try
            {
                AllowSetForegroundWindow(-1);
                PostMessage((IntPtr)0xFFFF, RegisterWindowMessage(InstanceKey), IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        GC.KeepAlive(mutex);
    }

    public MainForm()
    {
        string ver = ReadVersion();
        Text = "Starport Haiku" + (ver.Length > 0 ? " v" + ver : "");
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
        header.MouseUp += OnHeaderClick;
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
        btnUndo.Outline = true;
        btnUndo.SetBounds(FormW - 240, 11, 56, 30);
        btnUndo.Visible = false;
        btnUndo.Click += delegate { UndoDelete(); };
        footer.Controls.Add(btnUndo);
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
        if (hasWinPos && IsOnScreen(winPos)) Location = winPos;  // 恢复上次的位置
        Rebuild();

        try { showMsg = RegisterWindowMessage(InstanceKey); } catch { showMsg = 0; }
        ResizeEnd += delegate { RememberPosition(); };  // 拖动窗口结束时记住位置
        FormClosing += delegate { RememberPosition(); };
    }

    // 标题栏左上角附近在某块屏幕的工作区内，才认为位置有效（防止拔掉副屏后窗口跑到看不见的地方）
    static bool IsOnScreen(Point p)
    {
        foreach (Screen sc in Screen.AllScreens)
            if (sc.WorkingArea.Contains(new Point(p.X + 60, p.Y + 10))) return true;
        return false;
    }

    void RememberPosition()
    {
        if (WindowState != FormWindowState.Normal) return;
        if (hasWinPos && winPos == Location) return;
        winPos = Location;
        hasWinPos = true;
        Save();
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
        if (showMsg != 0 && m.Msg == showMsg)
        {
            // 有人再次双击了 exe：把已经开着的窗口还原并放到最前面
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
            return;
        }
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
        Theme.Text(g, "Starport1116", Theme.Title, new Rectangle(x, 6, w, 28), Theme.Ink, TextFormatFlags.Left);
        Theme.Text(g, "前沿剪贴板研究实验室", Theme.Small, new Rectangle(x + 1, 33, w, 20), Theme.Muted, TextFormatFlags.Left);
        Theme.Text(g, "Putting safety at the frontier desk.", Theme.En, new Rectangle(x + 1, 52, w, 18), Theme.Clay, TextFormatFlags.Left);
        using (Pen p = new Pen(Theme.Line)) g.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
    }

    // 彩蛋：连续点击顶部 logo / 标题 5 次（每次间隔不超过 0.8 秒）打开官网
    int headerClicks;
    DateTime lastHeaderClick;

    void OnHeaderClick(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        DateTime now = DateTime.Now;
        headerClicks = (now - lastHeaderClick).TotalMilliseconds <= 800 ? headerClicks + 1 : 1;
        lastHeaderClick = now;
        if (headerClicks < 5) return;
        headerClicks = 0;
        try
        {
            System.Diagnostics.Process.Start("https://" + Website);
            Flash("正在打开 " + Website, false);
        }
        catch { Flash("无法打开浏览器", true); }
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

    // phrases.txt 每行：中文|English|点击次数|钉住（后三项可省略）
    void ParseLines(string[] lines)
    {
        foreach (string line in lines)
        {
            if (line.StartsWith("#topmost=")) { topMost = line.Substring(9).Trim() != "0"; continue; }
            if (line.StartsWith("#user_mode=")) { userMode = line.Substring(11).Trim() == "1"; continue; }
            if (line.StartsWith("#user_suffix=")) { userSuffix = line.Substring(13).Trim(); continue; }
            if (line.StartsWith("#last_qty=")) { int.TryParse(line.Substring(10).Trim(), out lastQty); continue; }
            if (line.StartsWith("#day_cutoff=")) { int.TryParse(line.Substring(12).Trim(), out DateTokens.CutoffHour); continue; }
            if (line.StartsWith("#defaults=")) { int.TryParse(line.Substring(10).Trim(), out defaultsVersion); continue; }
            if (line.StartsWith("#stats_since="))
            {
                DateTime d;
                if (DateTime.TryParseExact(line.Substring(13).Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                    statsSince = d;
                continue;
            }
            if (line.StartsWith("#stats_total=")) { int.TryParse(line.Substring(13).Trim(), out statsTotal); continue; }
            if (line.StartsWith("#stats_chars=")) { long.TryParse(line.Substring(13).Trim(), out statsChars); continue; }
            if (line.StartsWith("#stats_days="))
            {
                foreach (string kv in line.Substring(12).Split(','))
                {
                    string[] a = kv.Split(':');
                    int dayCount;
                    if (a.Length == 2 && a[0].Trim().Length == 10 && int.TryParse(a[1], out dayCount)) statsDays[a[0].Trim()] = dayCount;
                }
                continue;
            }
            if (line.StartsWith("#win_pos="))
            {
                string[] xy = line.Substring(9).Split(',');
                int x, y;
                if (xy.Length == 2 && int.TryParse(xy[0], out x) && int.TryParse(xy[1], out y))
                {
                    winPos = new Point(x, y);
                    hasWinPos = true;
                }
                continue;
            }
            if (IsSettingLine(line)) continue;  // 未知设置行（如新版本写入的）跳过；「#1203 ...」这类词条照常读取
            string[] parts = line.Split('|');
            string zh = parts[0].Trim();
            if (zh.Length == 0) continue;
            string en = parts.Length > 1 ? parts[1].Trim() : "";
            int n = 0;
            if (parts.Length > 2) int.TryParse(parts[2].Trim(), out n);
            Phrase ph = new Phrase(zh, en, Math.Max(0, n));
            ph.Pinned = parts.Length > 3 && parts[3].Trim() == "1";
            phrases.Add(ph);
        }
    }

    void LoadPhrases()
    {
        try
        {
            bool recovered = false;
            if (File.Exists(file))
            {
                ParseLines(File.ReadAllLines(file, Encoding.UTF8));
                // 文件是空的（多半是保存时断电），而备份还在：从备份恢复
                if (new FileInfo(file).Length == 0 && File.Exists(backupFile))
                {
                    ParseLines(File.ReadAllLines(backupFile, Encoding.UTF8));
                    recovered = phrases.Count > 0;
                }
            }
            else ParseLines(Defaults);
            if (recovered) { Save(); Flash("词条文件为空，已从备份恢复", true); }
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

        // 首次启用统计：用已有的点击次数估算累计次数和字数
        if (statsSince == DateTime.MinValue)
        {
            statsSince = DateTokens.BusinessDate;
            statsTotal = 0;
            statsChars = 0;
            foreach (Phrase p in phrases)
            {
                statsTotal += p.Count;
                statsChars += (long)p.Count * DateTokens.FillQty(DateTokens.Expand(p.Zh, false), "2").Length;
            }
            Save();
        }

        SortPhrases(true);
        BackupDaily();
    }

    static string DayKey(DateTime d) { return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }

    // 记一次复制：今天 +1、累计 +1、字数累加；只保留近 7 天的每日数据
    void RecordCopy(int chars)
    {
        DateTime today = DateTokens.BusinessDate;
        string key = DayKey(today);
        int n;
        statsDays.TryGetValue(key, out n);
        statsDays[key] = n + 1;
        statsTotal++;
        statsChars += chars;
        List<string> old = new List<string>();
        foreach (string k in statsDays.Keys)
        {
            DateTime d;
            if (!DateTime.TryParseExact(k, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ||
                d < today.AddDays(-6) || d > today)
                old.Add(k);
        }
        foreach (string k in old) statsDays.Remove(k);
    }

    public void ShowUsage(IWin32Window owner)
    {
        DateTime today = DateTokens.BusinessDate;
        DateTime[] dates = new DateTime[7];
        int[] counts = new int[7];
        int week = 0;
        for (int i = 0; i < 7; i++)
        {
            dates[i] = today.AddDays(i - 6);
            int n;
            statsDays.TryGetValue(DayKey(dates[i]), out n);
            counts[i] = n;
            week += n;
        }
        List<Phrase> sorted = new List<Phrase>(phrases);
        sorted.Sort(delegate(Phrase a, Phrase b) { return b.Count.CompareTo(a.Count); });
        List<KeyValuePair<string, int>> top = new List<KeyValuePair<string, int>>();
        for (int i = 0; i < sorted.Count && top.Count < 3; i++)
            if (sorted[i].Count > 0)
                top.Add(new KeyValuePair<string, int>(DateTokens.FillQty(DateTokens.Expand(sorted[i].Zh, false), "?"), sorted[i].Count));
        using (UsageDialog d = new UsageDialog(this, counts[6], week, statsTotal, statsChars, statsSince, dates, counts, top))
            d.ShowDialog(owner);
    }

    public void ResetStats()
    {
        statsSince = DateTokens.BusinessDate;
        statsTotal = 0;
        statsChars = 0;
        statsDays.Clear();
        foreach (Phrase p in phrases) p.Count = 0;
        Save();
        Rebuild();
        Flash("统计已重置", false);
    }

    // 钉住的排最前（保持原顺序）；其余按点击次数从高到低（稳定排序）。byCount=false 时只把钉住的提到前面
    void SortPhrases(bool byCount)
    {
        List<Phrase> pinned = new List<Phrase>(), rest = new List<Phrase>();
        foreach (Phrase p in phrases) (p.Pinned ? pinned : rest).Add(p);
        if (byCount)
        {
            for (int i = 1; i < rest.Count; i++)
            {
                Phrase cur = rest[i];
                int j = i - 1;
                while (j >= 0 && rest[j].Count < cur.Count) { rest[j + 1] = rest[j]; j--; }
                rest[j + 1] = cur;
            }
        }
        phrases.Clear();
        phrases.AddRange(pinned);
        phrases.AddRange(rest);
    }

    string backupFile { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "phrases.bak"); } }

    // 每天第一次启动时把 phrases.txt 备份成隐藏的 phrases.bak
    void BackupDaily()
    {
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length == 0) return;
            if (File.Exists(backupFile))
            {
                if (File.GetLastWriteTime(backupFile).Date >= DateTime.Today) return;
                File.SetAttributes(backupFile, FileAttributes.Normal);  // 隐藏文件不能被 File.Copy 覆盖
            }
            File.Copy(file, backupFile, true);
            File.SetLastWriteTime(backupFile, DateTime.Now);  // Copy 会沿用原文件时间，这里改成今天
            File.SetAttributes(backupFile, FileAttributes.Hidden);
        }
        catch { }
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
            if (hasWinPos) lines.Add("#win_pos=" + winPos.X + "," + winPos.Y);
            if (statsSince != DateTime.MinValue)
            {
                lines.Add("#stats_since=" + statsSince.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                lines.Add("#stats_total=" + statsTotal);
                lines.Add("#stats_chars=" + statsChars);
                List<string> kv = new List<string>();
                foreach (KeyValuePair<string, int> d in statsDays) kv.Add(d.Key + ":" + d.Value);
                lines.Add("#stats_days=" + string.Join(",", kv.ToArray()));
            }
            foreach (Phrase p in phrases)
                lines.Add(p.Zh + "|" + p.En + "|" + p.Count + (p.Pinned ? "|1" : ""));
            byte[] data = new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines.ToArray()) + "\r\n");

            // 先完整写入临时文件并刷到磁盘，再替换正式文件；中途断电也不会把 phrases.txt 写成半截
            string tmp = file + ".tmp";
            if (File.Exists(tmp)) { File.SetAttributes(tmp, FileAttributes.Normal); File.Delete(tmp); }
            using (FileStream fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }
            if (File.Exists(file))
            {
                File.SetAttributes(file, FileAttributes.Normal);  // 隐藏文件不能直接被替换
                try { File.Replace(tmp, file, null); }
                catch
                {
                    // 某些磁盘（如网络共享）不支持替换，退回为覆盖复制
                    File.Copy(tmp, file, true);
                    File.Delete(tmp);
                }
            }
            else File.Move(tmp, file);
        }
        catch { Flash("保存失败（目录只读？）", true); }
        finally { if (File.Exists(file)) HideFile(); }
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
            c.PinRequested += delegate(Card k) { BeginInvoke((MethodInvoker)delegate { TogglePin(k.P); }); };
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
                string text = WithSuffix(s, userMode, userSuffix);
                Clipboard.SetText(text);
                p.Count++;
                RecordCopy(text.Length);
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

    // 直接删除，底部显示「撤销」，6 秒内可恢复
    void RemovePhrase(Phrase p)
    {
        int idx = phrases.IndexOf(p);
        if (idx < 0) return;
        phrases.RemoveAt(idx);
        Save();
        Rebuild();

        string name = p.Zh.Length > 6 ? p.Zh.Substring(0, 6) + "…" : p.Zh;
        Flash("已删除「" + name + "」", false);
        undoPhrase = p;
        undoIndex = idx;
        status.Width = FormW - 258;  // 给「撤销」按钮让位
        btnUndo.Visible = true;
        tip.Stop();
        tip.Interval = 6000;
        tip.Start();
    }

    void HideUndo()
    {
        undoPhrase = null;
        btnUndo.Visible = false;
        status.Width = FormW - 196;
    }

    void UndoDelete()
    {
        if (undoPhrase == null) return;
        if (phrases.Count >= MaxPhrases) { Flash("已达 20 条上限，无法恢复", true); return; }
        phrases.Insert(Math.Min(undoIndex, phrases.Count), undoPhrase);
        Save();
        Rebuild();
        Flash("已恢复", false);
    }

    void TogglePin(Phrase p)
    {
        p.Pinned = !p.Pinned;
        SortPhrases(false);
        Save();
        Rebuild();
        Flash(p.Pinned ? "已钉住，固定在最上面" : "已取消钉住", false);
    }

    // ---------- 5. 导出 / 导入 ----------
    public void ExportPhrases(IWin32Window owner)
    {
        using (SaveFileDialog d = new SaveFileDialog())
        {
            d.Title = "导出词条";
            d.Filter = "文本文件 (*.txt)|*.txt";
            d.FileName = "starport-phrases.txt";
            if (d.ShowDialog(owner) != DialogResult.OK) return;
            try
            {
                List<string> lines = new List<string>();
                foreach (Phrase p in phrases) lines.Add(p.Zh + "|" + p.En);  // 只导出词条，不含个人点击次数
                File.WriteAllLines(d.FileName, lines.ToArray(), new UTF8Encoding(true));
                MessageBox.Show(owner, "已导出 " + phrases.Count + " 条词条。", "导出词条",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "导出失败：" + ex.Message, "导出词条", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    // 合并导入：已有的（中文相同）跳过，不删除现有词条，最多到 20 条
    public void ImportPhrases(IWin32Window owner)
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = "导入词条";
            d.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
            if (d.ShowDialog(owner) != DialogResult.OK) return;
            int added = 0, dup = 0, over = 0;
            try
            {
                foreach (string line in File.ReadAllLines(d.FileName, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0 || IsSettingLine(line)) continue;
                    string[] parts = line.Split('|');
                    string zh = parts[0].Trim();
                    if (zh.Length == 0) continue;
                    string en = parts.Length > 1 ? parts[1].Trim() : "";
                    if (Exists(zh, null)) { dup++; continue; }
                    if (phrases.Count >= MaxPhrases) { over++; continue; }
                    phrases.Add(new Phrase(zh, en, 0));
                    added++;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "导入失败：" + ex.Message, "导入词条", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (added > 0) { Save(); Rebuild(); }
            string msg = "已导入 " + added + " 条。";
            if (dup > 0) msg += "\n跳过重复 " + dup + " 条。";
            if (over > 0) msg += "\n超出 20 条上限，未导入 " + over + " 条。";
            MessageBox.Show(owner, msg, "导入词条", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
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
        using (SettingsDialog d = new SettingsDialog(this, userMode, userSuffix, DateTokens.CutoffHour))
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
        HideUndo();
        status.ForeColor = Theme.Muted;
        status.Text = manage ? ManageHint : "";
    }

    void Flash(string msg, bool err)
    {
        HideUndo();
        status.ForeColor = err ? Color.Firebrick : Theme.Clay;
        status.Text = msg;
        tip.Stop();
        tip.Interval = 1600;
        tip.Start();
    }
}

