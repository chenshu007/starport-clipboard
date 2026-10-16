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
