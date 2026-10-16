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

partial class MainForm : Form
{
    // 布局（像素）：窗口宽度固定，高度按卡片数自动调整
    const int FormW = 380, HeaderH = 80, FooterH = 52, ContextBarH = 34, ImageAreaH = 216;
    const int Pad = 14;                       // 左右边距
    const int HeaderBtn = 32, HeaderBtnY = 12;  // 右上角 📌 ⚙ 按钮
    const int MaxVisibleCards = 10;           // 列表区最多同时显示的卡片数（对应数字键 1–0）
    const int ListTop = 10, ListBottom = 4;   // 卡片列表上下留白
    const int PeekH = 18;                     // 超过 10 张时露出下一张的高度
    const int MinListH = 64;
    const double MaxScreenShare = 0.85;       // 整个窗口最多占屏幕工作区高度的比例
    const int InitialClientH = 300;           // 算出实际高度之前的临时高度
    const int DefaultRight = 20, DefaultTop = 60;  // 默认位置：工作区右上角往里
    // 底部从右往左：＋ 新增、管理、撤销（删除后临时出现）；左边是提示文字
    const int FooterBtnY = 11, FooterBtnH = 30;
    const int AddW = 92, ManageW = 64, UndoW = 56;
    const int AddX = FormW - Pad - AddW, ManageX = AddX - 8 - ManageW, UndoX = ManageX - 6 - UndoW;
    const int StatusW = ManageX - 4 - Pad, StatusWithUndoW = UndoX - 4 - Pad;
    // 图片区：缩略图每行 3 张
    const int ImageHeaderH = 36, ImageCols = 3, ImageGapX = 10, ImageGapY = 8, ImageGridTop = 6;

    // 时间（毫秒）
    const int FlashMs = 1600;     // 底部提示显示多久
    const int UndoMs = 6000;      // 删除后「撤销」按钮保留多久
    const int DayCheckMs = 30000; // 多久检查一次营业日是否变了（卡片上的日期要跟着变）
    const int TooltipDelayMs = 700;

    Phrase undoPhrase;     // 刚删除、还能撤销的词条
    int undoIndex;

    DateTime shownDate;

    readonly PhraseStore store;  // phrases.txt：词条、设置、用量统计
    readonly Settings settings;
    readonly UsageStats stats;
    readonly ImageStash stash = new ImageStash();  // 图片暂存区
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

    int showMsg;  // 再次打开 exe 时，新进程发来的「显示窗口」消息号

    public MainForm(PhraseStore store)
    {
        this.store = store;
        store.Error += delegate(string msg) { Flash(msg, true); };
        settings = store.Settings;
        stats = store.Stats;
        string ver = Program.ReadVersion();
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
        toolTip.InitialDelay = TooltipDelayMs;

        header.Dock = DockStyle.Top;
        header.Height = HeaderH;
        header.Paint += PaintHeader;
        header.MouseUp += OnHeaderClick;
        btnPin.Font = Theme.Sym;
        btnPin.SetBounds(FormW - Pad - 2 * HeaderBtn - 6, HeaderBtnY, HeaderBtn, HeaderBtn);
        btnPin.Click += delegate { ToggleTopMost(); };
        btnSettings.Outline = true;
        btnSettings.Font = Theme.Sym;
        btnSettings.SetBounds(FormW - Pad - HeaderBtn, HeaderBtnY, HeaderBtn, HeaderBtn);
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
            using (Pen p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, Pad, 0, contextBar.Width - Pad, 0);
        };
        btnContext.Outline = true;
        btnContext.SetBounds(Pad, 4, FormW - 2 * Pad, 26);
        btnContext.Click += delegate { ToggleContext(); };
        toolTip.SetToolTip(btnContext, "Expand context window");
        contextBar.Controls.Add(btnContext);

        BuildImageArea();

        footer.Dock = DockStyle.Bottom;
        footer.Height = FooterH;
        footer.Paint += delegate(object o, PaintEventArgs e)
        {
            using (Pen p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
        };
        status.SetBounds(Pad, 15, StatusW, 22);
        btnUndo.Outline = true;
        btnUndo.SetBounds(UndoX, FooterBtnY, UndoW, FooterBtnH);
        btnUndo.Visible = false;
        btnUndo.Click += delegate { UndoDelete(); };
        footer.Controls.Add(btnUndo);
        status.ForeColor = Theme.Clay;
        status.AutoEllipsis = true;
        btnAdd.SetBounds(AddX, FooterBtnY, AddW, FooterBtnH);
        btnAdd.Click += delegate { AddPhrase(); };
        btnManage.Outline = true;
        btnManage.SetBounds(ManageX, FooterBtnY, ManageW, FooterBtnH);
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
        dayTimer.Interval = DayCheckMs;
        dayTimer.Tick += delegate
        {
            if (DateTokens.BusinessDate == shownDate) return;
            shownDate = DateTokens.BusinessDate;
            foreach (Control c in list.Controls) c.Invalidate();
        };
        dayTimer.Start();

        tip.Interval = FlashMs;
        tip.Tick += delegate { tip.Stop(); ShowIdleStatus(); };

        KeyDown += OnKey;
        store.Load();
        UpdatePinButton();
        ClientSize = new Size(FormW, InitialClientH);
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - Width - DefaultRight, wa.Top + DefaultTop);
        if (settings.HasWinPos && IsOnScreen(settings.WinPos)) Location = settings.WinPos;  // 恢复上次的位置
        Rebuild();

        showMsg = NativeMethods.RegisterMessage(Program.InstanceKey);
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
        DisposeChildren(imageGrid);
        stash.Clear();
        if (logo != null) { logo.Dispose(); logo = null; }
        base.OnFormClosed(e);
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
        if (m.Msg == NativeMethods.WM_MOUSEACTIVATE && (m.LParam.ToInt64() & 0xFFFF) == NativeMethods.HTCLIENT)
        {
            m.Result = (IntPtr)NativeMethods.MA_NOACTIVATE;
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
            System.Diagnostics.Process.Start("https://" + Program.Website);
            Flash("正在打开 " + Program.Website, false);
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
        totalCardH = ListTop;
        int visibleH = ListTop;
        for (int i = 0; i < store.Phrases.Count; i++)
        {
            int ch = Card.HeightFor(store.Phrases[i]);
            totalCardH += ch;
            if (i < MaxVisibleCards) visibleH += ch;
        }
        totalCardH += ListBottom;
        visibleH += store.Phrases.Count > MaxVisibleCards ? PeekH : ListBottom;  // 还有更多时露出下一张的一截

        // 整个窗口最多占屏幕工作区 85%；展开图片区时列表让出高度，改为滚动
        Rectangle wa = Screen.FromControl(this).WorkingArea;
        int maxClient = (int)(wa.Height * MaxScreenShare) - SystemInformation.CaptionHeight;
        int maxList = maxClient - HeaderH - ContextBarH - FooterH - (contextExpanded ? ImageAreaH : 0);
        return Math.Max(MinListH, Math.Min(visibleH, maxList));
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

    // 释放并移除全部子控件（卡片、缩略图每次都重新创建）
    static void DisposeChildren(Control parent)
    {
        foreach (Control c in parent.Controls) c.Dispose();
        parent.Controls.Clear();
    }

    void Rebuild()
    {
        list.SuspendLayout();
        list.AutoScrollPosition = Point.Empty;
        DisposeChildren(list);

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
        int cardW = FormW - 2 * Pad - (total > listH ? SystemInformation.VerticalScrollBarWidth : 0);

        int y = ListTop;
        foreach (Card c in cards)
        {
            c.SetBounds(Pad, y, cardW, c.Height);
            // 只有文字被截断的卡片才显示完整内容提示
            toolTip.SetToolTip(c, c.IsTruncated ? (c.P.En.Length > 0 ? c.ZhText + "\n" + c.EnText : c.ZhText) : null);
            y += c.Height;
            list.Controls.Add(c);
        }
        list.ResumeLayout();
        UpdateWindowSize();
    }

    // 弹窗，关闭后把焦点还给原来的软件：点卡片和按钮时本窗口不会被激活，弹窗前的前台仍是目标软件
    // （拿不到或还不回焦点都不影响后续操作）
    DialogResult ShowKeepingFocus(Func<DialogResult> show)
    {
        IntPtr target = NativeMethods.GetForeground();
        DialogResult r = show();
        if (target != IntPtr.Zero && target != Handle)
            NativeMethods.SetForeground(target);
        return r;
    }

    void CopyText(Phrase p, bool en)
    {
        string s = DateTokens.Expand(en ? p.En : p.Zh, en);
        if (DateTokens.HasQty(s))
        {
            int qty;
            using (QtyDialog d = new QtyDialog(DateTokens.FillQty(s, "?"), settings.LastQty < 1 ? 2 : settings.LastQty))
            {
                DialogResult r = ShowKeepingFocus(delegate { return d.ShowDialog(this); });
                qty = d.Qty;
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
        status.Width = StatusWithUndoW;  // 给「撤销」按钮让位
        btnUndo.Visible = true;
        tip.Stop();
        tip.Interval = UndoMs;
        tip.Start();
    }

    void HideUndo()
    {
        undoPhrase = null;
        btnUndo.Visible = false;
        status.Width = StatusW;
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

    // ---------- 启动密码 ----------
    public bool PasswordEnabled { get { return store.PasswordEnabled; } }

    // 开启或修改启动密码（修改前先核对当前密码）
    public void SetPassword(IWin32Window owner)
    {
        bool change = store.PasswordEnabled;
        if (change && !ConfirmPassword(owner, "修改启动密码", "请输入当前密码：")) return;
        using (SetPasswordDialog d = new SetPasswordDialog(change))
        {
            if (d.ShowDialog(owner) != DialogResult.OK) return;
            Cursor old = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            bool ok = store.SetPassword(d.Password);
            Cursor.Current = old;
            if (ok)
                MessageBox.Show(owner, change ? "启动密码已修改，旧密码已退役。" : "护栏已部署：词条和备份都已加密。\n下次打开程序时需要输入密码完成对齐。",
                    "启动密码", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(owner, "设置失败（目录只读？），词条文件保持原样。", "启动密码", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // 关闭启动密码：先核对当前密码，词条和备份恢复为明文
    public void RemovePassword(IWin32Window owner)
    {
        if (!ConfirmPassword(owner, "关闭启动密码", "下调安全等级需要人工批准：关闭后词条和备份恢复为明文保存。\n请输入当前密码：")) return;
        if (store.RemovePassword())
            MessageBox.Show(owner, "启动密码已关闭，词条回到明文保存。\n安全等级已下调，请谨慎对待共享文件夹。", "启动密码", MessageBoxButtons.OK, MessageBoxIcon.Information);
        else
            MessageBox.Show(owner, "关闭失败（目录只读？），词条文件保持原样。", "启动密码", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    bool ConfirmPassword(IWin32Window owner, string title, string prompt)
    {
        using (PasswordDialog d = new PasswordDialog(title, prompt, null, store.CheckPassword, false))
            return d.ShowDialog(owner) == DialogResult.OK;
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
        tip.Interval = FlashMs;
        tip.Start();
    }
}
