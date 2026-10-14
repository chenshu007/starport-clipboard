using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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

class Phrase
{
    public string Zh, En;
    public int Count;
    public Phrase(string zh, string en, int count) { Zh = zh; En = en; Count = count; }
}

// 一条词条卡片：主体 = 复制中文，EN 标签 = 复制英文，悬停时显示 ✎ ✕
class Card : Control
{
    enum Zone { None, Body, En, Edit, Del }

    public readonly Phrase P;
    public readonly int Index;
    public event Action<Card, string> CopyRequested;
    public event Action<Card> EditRequested, DeleteRequested;

    bool hover;
    Zone zone;
    Rectangle rEn, rEdit, rDel;

    public Card(Phrase p, int index)
    {
        P = p;
        Index = index;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Theme.Bg;
        Cursor = Cursors.Hand;
        Height = p.En.Length > 0 ? 58 : 44;
    }

    Zone HitTest(Point pt)
    {
        if (!ClientRectangle.Contains(pt)) return Zone.None;
        if (hover && rDel.Contains(pt)) return Zone.Del;
        if (hover && rEdit.Contains(pt)) return Zone.Edit;
        if (P.En.Length > 0 && rEn.Contains(pt)) return Zone.En;
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
            case Zone.Body: if (CopyRequested != null) CopyRequested(this, P.Zh); break;
            case Zone.En: if (CopyRequested != null) CopyRequested(this, P.En); break;
            case Zone.Edit: if (EditRequested != null) EditRequested(this); break;
            case Zone.Del: if (DeleteRequested != null) DeleteRequested(this); break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Bg);

        int h = Height - 6;  // 卡片之间留 6px 间距
        RectangleF box = new RectangleF(0.5f, 0.5f, Width - 1.5f, h - 1);
        using (GraphicsPath path = Theme.Round(box, 10))
        using (SolidBrush b = new SolidBrush(hover ? Theme.CardHover : Theme.Card))
        using (Pen pen = new Pen(hover ? Theme.Clay : Theme.Line))
        {
            g.FillPath(b, path);
            g.DrawPath(pen, path);
        }

        Theme.Text(g, Index < 9 ? (Index + 1).ToString() : "·", Theme.Num,
            new Rectangle(6, 0, 28, h), Theme.Clay, TextFormatFlags.HorizontalCenter);

        int right = Width - 10;
        if (hover)
        {
            rDel = new Rectangle(right - 26, (h - 26) / 2, 26, 26);
            rEdit = new Rectangle(right - 54, (h - 26) / 2, 26, 26);
            Theme.Text(g, "✕", Theme.Sym, rDel, zone == Zone.Del ? Color.Firebrick : Theme.Muted, TextFormatFlags.HorizontalCenter);
            Theme.Text(g, "✎", Theme.Sym, rEdit, zone == Zone.Edit ? Theme.Clay : Theme.Muted, TextFormatFlags.HorizontalCenter);
            right -= 58;
        }
        else
        {
            rDel = rEdit = Rectangle.Empty;
        }

        if (P.En.Length > 0)
        {
            rEn = new Rectangle(right - 32, (h - 22) / 2, 32, 22);
            using (GraphicsPath chip = Theme.Round(new RectangleF(rEn.X + 0.5f, rEn.Y + 0.5f, rEn.Width - 1, rEn.Height - 1), 11))
            {
                if (zone == Zone.En)
                    using (SolidBrush cb = new SolidBrush(Theme.Clay)) g.FillPath(cb, chip);
                using (Pen cp = new Pen(Theme.Clay)) g.DrawPath(cp, chip);
            }
            Theme.Text(g, "EN", Theme.Chip, rEn, zone == Zone.En ? Color.White : Theme.Clay, TextFormatFlags.HorizontalCenter);
            right -= 40;
        }
        else
        {
            rEn = Rectangle.Empty;
        }

        int left = 38, w = Math.Max(10, right - left);
        if (P.En.Length > 0)
        {
            Theme.Text(g, P.Zh, Theme.Zh, new Rectangle(left, 6, w, 24), Theme.Ink, TextFormatFlags.Left);
            Theme.Text(g, P.En, Theme.En, new Rectangle(left, 29, w, 18), Theme.Muted, TextFormatFlags.Left);
        }
        else
        {
            Theme.Text(g, P.Zh, Theme.Zh, new Rectangle(left, 0, w, h), Theme.Ink, TextFormatFlags.Left);
        }
    }
}

// 圆角橙色按钮
class PillButton : Control
{
    bool hover;

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
        using (SolidBrush b = new SolidBrush(hover ? Theme.ClayDark : Theme.Clay))
            g.FillPath(b, p);
        Theme.Text(g, Text, Font, ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter);
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

class MainForm : Form
{
    const int WM_MOUSEACTIVATE = 0x21;
    const int MA_NOACTIVATE = 3;
    const int HTCLIENT = 1;
    const int FormW = 360, HeaderH = 80, FooterH = 52;

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
    };

    readonly string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "phrases.txt");
    readonly List<Phrase> phrases = new List<Phrase>();
    readonly Panel header = new Panel();
    readonly Panel list = new Panel();
    readonly Panel footer = new Panel();
    readonly Label status = new Label();
    readonly PillButton btnAdd = new PillButton("＋ 新增");
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
        Text = "Starport 固定剪贴板";
        Font = Theme.Small;
        BackColor = Theme.Bg;
        TopMost = true;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        MaximizeBox = false;
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

        header.Dock = DockStyle.Top;
        header.Height = HeaderH;
        header.Paint += PaintHeader;

        list.Dock = DockStyle.Fill;
        list.AutoScroll = true;

        footer.Dock = DockStyle.Bottom;
        footer.Height = FooterH;
        footer.Paint += delegate(object o, PaintEventArgs e)
        {
            using (Pen p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
        };
        status.SetBounds(14, 15, FormW - 130, 22);
        status.ForeColor = Theme.Clay;
        status.AutoEllipsis = true;
        btnAdd.SetBounds(FormW - 106, 11, 92, 30);
        btnAdd.Click += delegate { AddPhrase(); };
        footer.Controls.Add(status);
        footer.Controls.Add(btnAdd);

        Controls.Add(list);
        Controls.Add(header);
        Controls.Add(footer);

        tip.Interval = 1600;
        tip.Tick += delegate { tip.Stop(); status.Text = ""; };

        KeyDown += OnKey;
        LoadPhrases();
        ClientSize = new Size(FormW, 300);
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - Width - 20, wa.Top + 60);
        Rebuild();
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

    // phrases.txt 每行：中文|English|点击次数（后两项可省略）
    void LoadPhrases()
    {
        try
        {
            string[] lines = File.Exists(file) ? File.ReadAllLines(file, Encoding.UTF8) : Defaults;
            foreach (string line in lines)
            {
                string[] parts = line.Split('|');
                string zh = parts[0].Trim();
                if (zh.Length == 0) continue;
                string en = parts.Length > 1 ? parts[1].Trim() : "";
                int n = 0;
                if (parts.Length > 2) int.TryParse(parts[2].Trim(), out n);
                phrases.Add(new Phrase(zh, en, Math.Max(0, n)));
            }
            if (!File.Exists(file)) Save();
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

    void Save()
    {
        try
        {
            string[] lines = new string[phrases.Count];
            for (int i = 0; i < phrases.Count; i++)
                lines[i] = phrases[i].Zh + "|" + phrases[i].En + "|" + phrases[i].Count;
            File.WriteAllLines(file, lines, new UTF8Encoding(false));
        }
        catch { Flash("保存失败（目录只读？）", true); }
    }

    void Rebuild()
    {
        list.SuspendLayout();
        list.AutoScrollPosition = Point.Empty;
        foreach (Control c in list.Controls) c.Dispose();
        list.Controls.Clear();

        List<Card> cards = new List<Card>();
        int total = 10;
        for (int i = 0; i < phrases.Count; i++)
        {
            Card c = new Card(phrases[i], i);
            c.CopyRequested += delegate(Card k, string s) { CopyText(k.P, s); };
            // 编辑/删除会重建卡片，延后执行，避免在卡片自身的鼠标事件里销毁它
            c.EditRequested += delegate(Card k) { BeginInvoke((MethodInvoker)delegate { EditPhrase(k.P); }); };
            c.DeleteRequested += delegate(Card k) { BeginInvoke((MethodInvoker)delegate { RemovePhrase(k.P); }); };
            toolTip.SetToolTip(c, phrases[i].En.Length > 0 ? phrases[i].Zh + "\n" + phrases[i].En : phrases[i].Zh);
            cards.Add(c);
            total += c.Height;
        }
        total += 4;

        int maxList = (int)(Screen.PrimaryScreen.WorkingArea.Height * 0.75) - HeaderH - FooterH;
        int listH = Math.Max(64, Math.Min(total, maxList));
        int cardW = FormW - 28 - (total > listH ? SystemInformation.VerticalScrollBarWidth : 0);

        int y = 10;
        foreach (Card c in cards)
        {
            c.SetBounds(14, y, cardW, c.Height);
            y += c.Height;
            list.Controls.Add(c);
        }
        list.ResumeLayout();
        ClientSize = new Size(FormW, HeaderH + listH + FooterH);
    }

    void CopyText(Phrase p, string s)
    {
        for (int n = 0; n < 5; n++)
        {
            try
            {
                Clipboard.SetText(s);
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

    // 数字键 1–9 复制对应中文（窗口激活时有效，例如刚拖动过标题栏）
    void OnKey(object sender, KeyEventArgs e)
    {
        int n = -1;
        if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9) n = e.KeyCode - Keys.D1;
        else if (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9) n = e.KeyCode - Keys.NumPad1;
        if (n >= 0 && n < phrases.Count) { CopyText(phrases[n], phrases[n].Zh); e.Handled = true; }
    }

    void Flash(string msg, bool err)
    {
        status.ForeColor = err ? Color.Firebrick : Theme.Clay;
        status.Text = msg;
        tip.Stop();
        tip.Start();
    }
}
