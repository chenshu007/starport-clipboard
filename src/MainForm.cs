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

// Starport Haiku（固定剪贴板）：点卡片复制中文，点 EN 复制英文；窗口置顶，点击卡片不抢目标软件焦点

class MainForm : Form
{
    const int WM_MOUSEACTIVATE = 0x21;
    const int MA_NOACTIVATE = 3;
    const int HTCLIENT = 1;
    const int FormW = 380, HeaderH = 80, FooterH = 52, ContextBarH = 34, ImageAreaH = 216;
    const int MaxVisibleCards = 10, MaxImages = 6, MaxImageSide = 2560;

    Phrase undoPhrase;     // 刚删除、还能撤销的词条
    int undoIndex;

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    DateTime shownDate;

    readonly PhraseStore store;  // phrases.txt：词条、设置、用量统计
    readonly Settings settings;
    readonly UsageStats stats;
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
    bool contextExpanded;
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
    static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--self-test") >= 0) return SelfTest.Run();
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
            return 0;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        GC.KeepAlive(mutex);
        return 0;
    }

    public MainForm()
    {
        store = new PhraseStore(AppDomain.CurrentDomain.BaseDirectory);  // 词条文件和 exe 放在同一文件夹
        store.Error += delegate(string msg) { Flash(msg, true); };
        settings = store.Settings;
        stats = store.Stats;
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
        store.Load();
        UpdatePinButton();
        ClientSize = new Size(FormW, 300);
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - Width - 20, wa.Top + 60);
        if (settings.HasWinPos && IsOnScreen(settings.WinPos)) Location = settings.WinPos;  // 恢复上次的位置
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
        if (settings.HasWinPos && settings.WinPos == Location) return;
        settings.WinPos = Location;
        settings.HasWinPos = true;
        store.Save();
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
        TopMost = settings.TopMost;
        btnPin.Outline = !settings.TopMost;
        btnPin.Invalidate();
        toolTip.SetToolTip(btnPin, settings.TopMost ? "置顶：开" : "置顶：关");
    }

    void ToggleTopMost()
    {
        settings.TopMost = !settings.TopMost;
        UpdatePinButton();
        store.Save();
        Flash(settings.TopMost ? "已置顶" : "已取消置顶", false);
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

    public void ShowUsage(IWin32Window owner)
    {
        DateTime today = DateTokens.BusinessDate;
        DateTime[] dates = new DateTime[7];
        int[] counts = new int[7];
        int week = 0;
        for (int i = 0; i < 7; i++)
        {
            dates[i] = today.AddDays(i - 6);
            int n = stats.CountOn(dates[i]);
            counts[i] = n;
            week += n;
        }
        List<KeyValuePair<string, int>> top = UsageStats.TopPhrases(store.Phrases, 3);
        using (UsageDialog d = new UsageDialog(this, counts[6], week, stats.Total, stats.Chars, stats.Since, dates, counts, top))
            d.ShowDialog(owner);
    }

    public void ResetStats()
    {
        stats.Reset();
        foreach (Phrase p in store.Phrases) p.Count = 0;
        store.Save();
        Rebuild();
        Flash("统计已重置", false);
    }

    int CalcListHeight(out int totalCardH)
    {
        totalCardH = 10;
        int visibleH = 10;
        for (int i = 0; i < store.Phrases.Count; i++)
        {
            int ch = Card.HeightFor(store.Phrases[i]);
            totalCardH += ch;
            if (i < MaxVisibleCards) visibleH += ch;
        }
        totalCardH += 4;
        visibleH += store.Phrases.Count > MaxVisibleCards ? 18 : 4;  // 还有更多时露出下一张的一截

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
        for (int i = 0; i < store.Phrases.Count; i++)
        {
            Card c = new Card(store.Phrases[i], i, manage);
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
            using (QtyDialog d = new QtyDialog(DateTokens.FillQty(s, "?"), settings.LastQty < 1 ? 2 : settings.LastQty))
            {
                DialogResult r = d.ShowDialog(this);
                qty = d.Qty;
                if (target != IntPtr.Zero && target != Handle)
                    try { SetForegroundWindow(target); } catch { }
                if (r != DialogResult.OK || qty < 1) return;
            }
            settings.LastQty = qty;
            s = DateTokens.FillQty(s, qty.ToString());
        }
        for (int n = 0; n < 5; n++)
        {
            try
            {
                string text = WithSuffix(s, settings.UserMode, settings.UserSuffix);
                Clipboard.SetText(text);
                p.Count++;
                stats.RecordCopy(text.Length);
                store.Save();
                Flash("✓ 已复制  " + s, false);
                return;
            }
            catch (ExternalException) { Thread.Sleep(60); }  // 剪贴板被占用，重试
            catch (Exception) { break; }
        }
        Flash("复制失败", true);
    }

    void AddPhrase()
    {
        if (store.Phrases.Count >= PhraseStore.MaxPhrases)
        {
            Flash("已达 20 条上限，请先在管理中删除不常用的", true);
            return;
        }
        using (EditDialog d = new EditDialog("新增词条", "", ""))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (store.Exists(d.Zh, null)) { Flash("已存在相同词条", true); return; }
            store.Phrases.Add(new Phrase(d.Zh, d.En, 0));
            store.Save();
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
            if (store.Exists(d.Zh, p)) { Flash("已存在相同词条", true); return; }
            p.Zh = d.Zh;
            p.En = d.En;
            store.Save();
            Rebuild();
            Flash("已保存", false);
        }
    }

    // 直接删除，底部显示「撤销」，6 秒内可恢复
    void RemovePhrase(Phrase p)
    {
        int idx = store.Phrases.IndexOf(p);
        if (idx < 0) return;
        store.Phrases.RemoveAt(idx);
        store.Save();
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
        if (store.Phrases.Count >= PhraseStore.MaxPhrases) { Flash("已达 20 条上限，无法恢复", true); return; }
        store.Phrases.Insert(Math.Min(undoIndex, store.Phrases.Count), undoPhrase);
        store.Save();
        Rebuild();
        Flash("已恢复", false);
    }

    void TogglePin(Phrase p)
    {
        p.Pinned = !p.Pinned;
        store.Sort(false);
        store.Save();
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
                store.Export(d.FileName);
                MessageBox.Show(owner, "已导出 " + store.Phrases.Count + " 条词条。", "导出词条",
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
            int added, dup, over;
            try
            {
                added = store.Import(File.ReadAllLines(d.FileName, Encoding.UTF8), out dup, out over);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "导入失败：" + ex.Message, "导入词条", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (added > 0) { store.Save(); Rebuild(); }
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
        if (!manage && n >= 0 && n < store.Phrases.Count) { CopyText(store.Phrases[n], false); e.Handled = true; }
    }

    // 用户模式下在末尾加后缀（用空格隔开）；状态栏和卡片仍只显示原文
    public static string WithSuffix(string text, bool on, string suffix)
    {
        return on && suffix.Length > 0 ? text + " " + suffix : text;
    }

    void OpenSettings()
    {
        using (SettingsDialog d = new SettingsDialog(this, settings.UserMode, settings.UserSuffix, DateTokens.CutoffHour))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            settings.UserMode = d.UserMode;
            settings.UserSuffix = d.Suffix;
            DateTokens.CutoffHour = d.CutoffHour;
            Rebuild();  // 分界时间变了，卡片上的日期可能要更新
            store.Save();
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
