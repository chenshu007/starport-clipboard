using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// 用量统计（戏仿 Claude 的 Usage 页面）：今天 / 近 7 天 / 累计、节省的打字时间、近 7 天柱状图、最常用 3 条
class UsageDialog : Form
{
    public const int CharsPerMinute = 40;  // 估算打字速度：每分钟 40 字

    readonly int today, week, total;
    readonly long savedChars;
    readonly DateTime since;
    readonly DateTime[] dayDates;
    readonly int[] dayCounts;
    readonly List<KeyValuePair<string, int>> top;
    readonly MainForm main;

    public UsageDialog(MainForm main, int today, int week, int total, long savedChars, DateTime since,
                       DateTime[] dayDates, int[] dayCounts, List<KeyValuePair<string, int>> top)
    {
        this.main = main;
        this.today = today; this.week = week; this.total = total;
        this.savedChars = savedChars; this.since = since;
        this.dayDates = dayDates; this.dayCounts = dayCounts; this.top = top;

        Text = "用量 Usage";
        Font = Theme.Small;
        BackColor = Theme.Bg;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(360, 470);

        Button reset = DialogButtons.Secondary("重置");
        reset.SetBounds(160, 424, 88, 30);
        reset.Click += delegate
        {
            if (MessageBox.Show(this, "清空全部统计，并把每条词条的点击次数归零？\n（启动时的排序也会重新开始）", "重置统计",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            this.main.ResetStats();
            Close();
        };
        Button close = DialogButtons.Primary("关闭");
        close.SetBounds(256, 424, 88, 30);
        close.DialogResult = DialogResult.Cancel;
        Controls.Add(reset);
        Controls.Add(close);
        CancelButton = close;
    }

    public static string FormatSaved(long chars)
    {
        long minutes = chars / CharsPerMinute;
        if (minutes < 1) return "不到 1 分钟";
        if (minutes < 60) return minutes + " 分钟";
        return (minutes / 60) + " 小时 " + (minutes % 60) + " 分钟";
    }

    void Tile(Graphics g, int x, string label, int value)
    {
        Rectangle r = new Rectangle(x, 54, 100, 66);
        using (GraphicsPath p = Theme.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 10))
        using (SolidBrush b = new SolidBrush(Theme.Card))
        using (Pen pen = new Pen(Theme.Line))
        {
            g.FillPath(b, p);
            g.DrawPath(pen, p);
        }
        Theme.Text(g, label, Theme.Small, new Rectangle(r.X + 12, r.Y + 6, r.Width - 16, 20), Theme.Muted, TextFormatFlags.Left);
        using (Font f = new Font("Georgia", 17f))
            Theme.Text(g, value.ToString("N0"), f, new Rectangle(r.X + 10, r.Y + 26, r.Width - 14, 32), Theme.Ink, TextFormatFlags.Left);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Bg);

        Theme.Text(g, "用量", Theme.Title, new Rectangle(16, 12, 200, 30), Theme.Ink, TextFormatFlags.Left);
        Theme.Text(g, "Usage", Theme.En, new Rectangle(76, 16, 120, 24), Theme.Muted, TextFormatFlags.Left);

        Tile(g, 16, "今天", today);
        Tile(g, 130, "近 7 天", week);
        Tile(g, 244, "累计", total);

        // 节省的打字时间
        Theme.Text(g, "为你节省了约 " + FormatSaved(savedChars) + "打字", Theme.Zh,
            new Rectangle(16, 132, 330, 26), Theme.Clay, TextFormatFlags.Left);
        Theme.Text(g, "共复制 " + savedChars.ToString("N0") + " 字 · 按每分钟 " + CharsPerMinute + " 字估算", Theme.Small,
            new Rectangle(16, 158, 330, 20), Theme.Muted, TextFormatFlags.Left);

        // 近 7 天柱状图
        Theme.Text(g, "近 7 天", Theme.Small, new Rectangle(16, 188, 200, 20), Theme.Ink, TextFormatFlags.Left);
        int max = 1;
        foreach (int c in dayCounts) max = Math.Max(max, c);
        string[] wk = { "日", "一", "二", "三", "四", "五", "六" };
        int barW = 30, gap = (328 - barW * 7) / 6, baseY = 290, maxH = 70;
        for (int i = 0; i < 7; i++)
        {
            int x = 16 + i * (barW + gap);
            int h = dayCounts[i] == 0 ? 2 : Math.Max(4, dayCounts[i] * maxH / max);
            bool isToday = i == 6;
            using (GraphicsPath p = Theme.Round(new RectangleF(x, baseY - h, barW, h), Math.Min(4, h / 2f)))
            using (SolidBrush b = new SolidBrush(isToday ? Theme.Clay : Color.FromArgb(232, 196, 180)))
                g.FillPath(b, p);
            if (dayCounts[i] > 0)
                Theme.Text(g, dayCounts[i].ToString(), Theme.Small, new Rectangle(x - 10, baseY - h - 18, barW + 20, 16),
                    Theme.Muted, TextFormatFlags.HorizontalCenter);
            Theme.Text(g, isToday ? "今天" : wk[(int)dayDates[i].DayOfWeek], Theme.Small,
                new Rectangle(x - 10, baseY + 4, barW + 20, 18), isToday ? Theme.Clay : Theme.Muted, TextFormatFlags.HorizontalCenter);
        }

        // 最常用 3 条
        Theme.Text(g, "最常用", Theme.Small, new Rectangle(16, 322, 200, 20), Theme.Ink, TextFormatFlags.Left);
        int topMax = top.Count > 0 ? Math.Max(1, top[0].Value) : 1;
        for (int i = 0; i < top.Count; i++)
        {
            int y = 346 + i * 24;
            Theme.Text(g, (i + 1) + ".  " + top[i].Key, Theme.Small, new Rectangle(16, y, 190, 20), Theme.Ink, TextFormatFlags.Left);
            int bw = Math.Max(3, top[i].Value * 80 / topMax);
            using (GraphicsPath p = Theme.Round(new RectangleF(212, y + 6, bw, 8), 4))
            using (SolidBrush b = new SolidBrush(Theme.Clay))
                g.FillPath(b, p);
            Theme.Text(g, top[i].Value.ToString("N0") + " 次", Theme.Small, new Rectangle(296, y, 50, 20), Theme.Muted, TextFormatFlags.Right);
        }
        if (top.Count == 0)
            Theme.Text(g, "还没有复制记录", Theme.Small, new Rectangle(16, 346, 300, 20), Theme.Muted, TextFormatFlags.Left);

        Theme.Text(g, "统计始于 " + since.Year + "年" + since.Month + "月" + since.Day + "日", Theme.Small,
            new Rectangle(16, 428, 150, 22), Theme.Muted, TextFormatFlags.Left);
    }
}
