using System;
using System.Drawing;
using System.Windows.Forms;

// 设置或修改启动密码：新密码输两遍，并明确提示忘记后无法找回
class SetPasswordDialog : Form
{
    readonly TextBox tNew = new TextBox();
    readonly TextBox tAgain = new TextBox();
    readonly Label error = new Label();

    public string Password { get { return tNew.Text; } }

    public SetPasswordDialog(bool change)
    {
        Text = change ? "修改启动密码 · Constitutional Lock" : "设置启动密码 · Constitutional Lock";
        Font = Theme.Small;
        BackColor = Theme.Bg;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(340, 286);

        Label warn = new Label();
        warn.Text = "开启后，phrases.txt 和备份 phrases.bak 都以 AES-256 加密保存，每次打开程序要输入密码。\n" +
                    "Haiku 不会记住你的密码：忘记后无法找回，词条也就再也打不开了。这是设计，不是缺陷。";
        warn.ForeColor = Color.Firebrick;
        warn.SetBounds(20, 14, 300, 66);

        Label l1 = new Label();
        l1.Text = change ? "新密码" : "密码";
        l1.ForeColor = Theme.Muted;
        l1.SetBounds(20, 86, 300, 20);
        tNew.SetBounds(20, 108, 300, 26);
        tNew.Font = Theme.Zh;
        tNew.UseSystemPasswordChar = true;

        Label l2 = new Label();
        l2.Text = "再输入一次";
        l2.ForeColor = Theme.Muted;
        l2.SetBounds(20, 144, 300, 20);
        tAgain.SetBounds(20, 166, 300, 26);
        tAgain.Font = Theme.Zh;
        tAgain.UseSystemPasswordChar = true;

        error.ForeColor = Color.Firebrick;
        error.SetBounds(20, 200, 300, 22);

        Button ok = DialogButtons.Primary(change ? "修改" : "部署护栏");
        ok.SetBounds(214, 236, 106, 32);
        ok.Click += delegate
        {
            if (tNew.Text.Length == 0) { error.Text = "密码不能为空。"; tNew.Focus(); return; }
            if (tNew.Text != tAgain.Text) { error.Text = "两次输入的密码不一样。"; tAgain.SelectAll(); tAgain.Focus(); return; }
            DialogResult = DialogResult.OK;
        };
        Button cancel = DialogButtons.Secondary("取消");
        cancel.SetBounds(100, 236, 106, 32);
        cancel.DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[] { warn, l1, tNew, l2, tAgain, error, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        tNew.Focus();
    }
}
