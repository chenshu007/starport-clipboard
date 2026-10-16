using System;
using System.Drawing;
using System.Windows.Forms;

// 对话框底部的扁平按钮：浅色描边；主按钮（保存/关闭）橙底白字，其余白底、字色随对话框
static class DialogButtons
{
    static Button Flat(string text, Color back)
    {
        Button b = new Button();
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Theme.Line;
        b.BackColor = back;
        return b;
    }

    public static Button Primary(string text)
    {
        Button b = Flat(text, Theme.Clay);
        b.ForeColor = Color.White;
        return b;
    }

    public static Button Secondary(string text)
    {
        return Flat(text, Color.White);
    }
}
