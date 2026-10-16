using System;
using System.Drawing;
using System.Windows.Forms;

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
