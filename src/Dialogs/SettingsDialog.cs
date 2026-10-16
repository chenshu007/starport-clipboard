using System;
using System.Drawing;
using System.Windows.Forms;

// 设置弹窗：用户模式开关 + 后缀
class SettingsDialog : Form
{
    readonly CheckBox chk = new CheckBox();
    readonly TextBox tSuffix = new TextBox();
    readonly Label preview = new Label();
    readonly NumericUpDown numCutoff = new NumericUpDown();
    readonly Label lPwd = new Label();
    readonly Button bPwdSet = DialogButtons.Secondary("");
    readonly Button bPwdOff = DialogButtons.Secondary("关闭密码…");
    readonly MainForm main;

    public bool UserMode { get { return chk.Checked; } }
    public string Suffix { get { return tSuffix.Text.Replace("\r", "").Replace("\n", "").Trim(); } }

    public int CutoffHour { get { return (int)numCutoff.Value; } }

    public SettingsDialog(MainForm main, bool userMode, string suffix, int cutoffHour)
    {
        this.main = main;
        Text = "设置";
        Font = Theme.Small;
        BackColor = Theme.Bg;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(340, 422);

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

        Button ok = DialogButtons.Primary("保存");
        ok.SetBounds(214, 372, 106, 32);
        ok.DialogResult = DialogResult.OK;
        Button cancel = DialogButtons.Secondary("取消");
        cancel.SetBounds(100, 372, 106, 32);
        cancel.DialogResult = DialogResult.Cancel;

        chk.CheckedChanged += delegate { UpdatePreview(); };
        tSuffix.TextChanged += delegate { UpdatePreview(); };
        UpdatePreview();

        // 底部：版本号 + 官网链接（点击用浏览器打开）
        string ver = Program.ReadVersion();
        string prefix = (ver.Length > 0 ? "v" + ver + " · " : "");
        LinkLabel about = new LinkLabel();
        about.Text = prefix + Program.Website;
        about.LinkArea = new LinkArea(prefix.Length, Program.Website.Length);
        about.LinkBehavior = LinkBehavior.HoverUnderline;
        about.ForeColor = Theme.Muted;
        about.LinkColor = Theme.Clay;
        about.ActiveLinkColor = Theme.ClayDark;
        about.SetBounds(20, 344, 300, 20);
        about.LinkClicked += delegate
        {
            try { System.Diagnostics.Process.Start("https://" + Program.Website); }
            catch { MessageBox.Show(this, "无法打开浏览器", "Starport", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };

        // 启动密码：词条加密保存，打开程序要输入密码（立即生效，不等点「保存」）
        lPwd.SetBounds(20, 246, 100, 22);
        bPwdSet.SetBounds(122, 240, 96, 30);
        bPwdSet.Click += delegate { this.main.SetPassword(this); UpdatePasswordRow(); };
        bPwdOff.SetBounds(224, 240, 96, 30);
        bPwdOff.Click += delegate { this.main.RemovePassword(this); UpdatePasswordRow(); };
        Label lPwdHint = new Label();
        lPwdHint.Text = "词条与备份加密存放 · 忘记密码无法找回";
        lPwdHint.ForeColor = Theme.Muted;
        lPwdHint.SetBounds(20, 274, 300, 20);
        UpdatePasswordRow();

        // 导出/导入词条：方便同事之间共用一套常用语、换电脑
        Button exp = DialogButtons.Secondary("导出词条…");
        exp.SetBounds(20, 300, 96, 30);
        exp.Click += delegate { main.ExportPhrases(this); };
        Button imp = DialogButtons.Secondary("导入词条…");
        imp.SetBounds(122, 300, 96, 30);
        imp.Click += delegate { main.ImportPhrases(this); };

        Button usage = DialogButtons.Secondary("用量统计…");
        usage.SetBounds(224, 300, 96, 30);
        usage.Click += delegate { main.ShowUsage(this); };

        Controls.AddRange(new Control[] { chk, l, tSuffix, preview, numCutoff, lc, lcEnd, lc2, lPwd, bPwdSet, bPwdOff, lPwdHint, exp, imp, usage, about, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    void UpdatePasswordRow()
    {
        bool on = main.PasswordEnabled;
        lPwd.Text = on ? "启动密码：已开启" : "启动密码：未开启";
        bPwdSet.Text = on ? "修改密码…" : "设置密码…";
        bPwdOff.Visible = on;
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
