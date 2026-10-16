using System;
using System.Drawing;
using System.Windows.Forms;

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

        Button ok = DialogButtons.Primary("保存");
        ok.Cursor = Cursors.Hand;
        ok.SetBounds(214, 148, 106, 32);
        ok.Click += delegate
        {
            if (Zh.Length == 0) { tZh.Focus(); return; }
            DialogResult = DialogResult.OK;
        };
        Button cancel = DialogButtons.Secondary("取消");
        cancel.ForeColor = Theme.Ink;
        cancel.Cursor = Cursors.Hand;
        cancel.SetBounds(100, 148, 106, 32);
        cancel.DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[] { l1, tZh, l2, tEn, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        tZh.Focus();
        tZh.SelectAll();
    }
}
