using System;
using System.Drawing;
using System.Windows.Forms;

// 输入一个密码：启动时解锁词条文件，或修改/关闭启动密码前核对当前密码。
// check 不为 null 时在窗口里核对，输错就提示并留在窗口里
class PasswordDialog : Form
{
    readonly TextBox tPwd = new TextBox();
    readonly Label error = new Label();
    readonly Func<string, bool> check;

    public string Password { get { return tPwd.Text; } }

    public PasswordDialog(string title, string prompt, string errorText, Func<string, bool> check, bool standalone)
    {
        this.check = check;
        Text = title;
        Font = Theme.Small;
        BackColor = Theme.Bg;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = standalone ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = standalone;  // 启动时还没有主窗口，要能在任务栏找到它
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(320, 176);

        Label lp = new Label();
        lp.Text = prompt;
        lp.SetBounds(20, 14, 280, 40);
        tPwd.SetBounds(20, 58, 280, 26);
        tPwd.Font = Theme.Zh;
        tPwd.UseSystemPasswordChar = true;
        error.Text = errorText ?? "";
        error.ForeColor = Color.Firebrick;
        error.SetBounds(20, 90, 280, 36);

        Button ok = DialogButtons.Primary("确定");
        ok.SetBounds(194, 130, 106, 30);
        ok.Click += delegate
        {
            if (Password.Length == 0) { tPwd.Focus(); return; }
            if (this.check != null && !this.check(Password))
            {
                error.Text = "密码未通过对齐评估，请重试。";
                tPwd.SelectAll();
                tPwd.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        };
        Button cancel = DialogButtons.Secondary(standalone ? "退出" : "取消");
        cancel.SetBounds(80, 130, 106, 30);
        cancel.DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[] { lp, tPwd, error, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        tPwd.Focus();
    }

    // 启动时问密码（第 attempt 次）；返回 null 表示不输了，程序退出
    public static string AskToUnlock(int attempt)
    {
        string err = attempt < 2 ? null
                   : attempt < 4 ? "密码未通过对齐评估，请重试。"
                   : "多次未通过评估。如果确定没有输错，可能是词条文件和备份都已损坏。";
        using (PasswordDialog d = new PasswordDialog("Starport Haiku · Constitutional Lock",
            "词条处于宪法级保护之下。\n请输入启动密码，完成身份对齐：", err, null, true))
            return d.ShowDialog() == DialogResult.OK ? d.Password : null;
    }
}
