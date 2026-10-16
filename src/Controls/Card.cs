using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// 一条词条卡片。
// 平时：主体 = 复制中文，EN = 复制英文，位置固定不动。
// 管理模式：不复制；主体或 ✎ = 编辑，✕ = 删除。
class Card : Control
{
    enum Zone { None, Body, En, Pin, Edit, Del }

    public readonly Phrase P;
    public readonly int Index;
    public readonly bool Manage;
    public event Action<Card, bool> CopyRequested;  // bool = 是否英文
    public event Action<Card> EditRequested, DeleteRequested, PinRequested;

    bool hover;
    Zone zone;

    public Card(Phrase p, int index, bool manage)
    {
        P = p;
        Index = index;
        Manage = manage;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Theme.Bg;
        Cursor = Cursors.Hand;
        Height = HeightFor(p);
    }

    public const int Gap = 4;  // 卡片之间的间距
    const int HeightTwoLines = 52, HeightOneLine = 40;  // 有英文时显示两行（含间距）
    public static int HeightFor(Phrase p) { return p.En.Length > 0 ? HeightTwoLines : HeightOneLine; }

    // 右侧按钮：平时是 EN，管理模式是 📌 ✎ ✕（从右往左），离卡片右边 Edge
    const int Edge = 10;
    const int ChipW = 32, ChipH = 22;          // EN
    const int IconSize = 28, IconStep = 30;    // 📌 ✎ ✕
    const int CountW = 48;                     // 管理模式的「n 次」
    const int TextLeft = 38;                   // 文字左边（左侧是序号）
    const int Radius = 10;
    const int FadeH = 28;                      // 列表可视区底边的渐隐高度

    int BoxH { get { return Height - Gap; } }

    // 卡片位置变化（列表滚动）时重绘，以便更新底部渐隐
    protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); Invalidate(); }

    // 右侧按钮区的位置只取决于卡片尺寸，与悬停无关，所以不会跳动
    Rectangle EnRect { get { return new Rectangle(Width - (Edge + ChipW), (BoxH - ChipH) / 2, ChipW, ChipH); } }
    Rectangle DelRect { get { return new Rectangle(Width - (Edge + IconSize), (BoxH - IconSize) / 2, IconSize, IconSize); } }
    Rectangle EditRect { get { return new Rectangle(Width - (Edge + IconSize + IconStep), (BoxH - IconSize) / 2, IconSize, IconSize); } }
    Rectangle PinRect { get { return new Rectangle(Width - (Edge + IconSize + 2 * IconStep), (BoxH - IconSize) / 2, IconSize, IconSize); } }
    Rectangle CountRect { get { return new Rectangle(PinRect.Left - (CountW + 2), 0, CountW, BoxH); } }  // 管理模式：点击次数

    Rectangle TextArea
    {
        get
        {
            int right = Manage ? CountRect.Left - 4 : (P.En.Length > 0 ? EnRect.Left - 8 : Width - 12);
            return new Rectangle(TextLeft, 0, Math.Max(10, right - TextLeft), BoxH);
        }
    }

    // 文字是否被省略号截断（只有截断时才需要提示框）
    // 卡片上数量显示为「?」，表示点击时再填
    public string ZhText { get { return DateTokens.FillQty(DateTokens.Expand(P.Zh, false), "?"); } }
    public string EnText { get { return DateTokens.FillQty(DateTokens.Expand(P.En, true), "?"); } }

    public bool IsTruncated
    {
        get
        {
            int w = TextArea.Width;
            const TextFormatFlags f = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            return TextRenderer.MeasureText(ZhText, Theme.Zh, Size.Empty, f).Width > w ||
                   (P.En.Length > 0 && TextRenderer.MeasureText(EnText, Theme.En, Size.Empty, f).Width > w);
        }
    }

    Zone HitTest(Point pt)
    {
        if (!ClientRectangle.Contains(pt) || pt.Y >= BoxH) return Zone.None;
        if (Manage)
        {
            if (DelRect.Contains(pt)) return Zone.Del;
            if (EditRect.Contains(pt)) return Zone.Edit;
            if (PinRect.Contains(pt)) return Zone.Pin;
            return Zone.Body;
        }
        if (P.En.Length > 0 && EnRect.Contains(pt)) return Zone.En;
        return Zone.Body;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; zone = Zone.None; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Zone z = HitTest(e.Location);
        if (z != zone) { zone = z; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        switch (HitTest(e.Location))
        {
            case Zone.Body:
                if (Manage) { if (EditRequested != null) EditRequested(this); }
                else if (CopyRequested != null) CopyRequested(this, false);
                break;
            case Zone.En: if (CopyRequested != null) CopyRequested(this, true); break;
            case Zone.Edit: if (EditRequested != null) EditRequested(this); break;
            case Zone.Del: if (DeleteRequested != null) DeleteRequested(this); break;
            case Zone.Pin: if (PinRequested != null) PinRequested(this); break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Bg);

        int h = BoxH;
        RectangleF box = new RectangleF(0.5f, 0.5f, Width - 1.5f, h - 1);
        using (GraphicsPath path = Theme.Round(box, Radius))
        using (SolidBrush b = new SolidBrush(hover ? Theme.CardHover : Theme.Card))
        using (Pen pen = new Pen(hover ? Theme.Clay : Theme.Line))
        {
            if (Manage) pen.DashStyle = DashStyle.Dash;  // 虚线边框，一眼看出处于管理模式
            g.FillPath(b, path);
            g.DrawPath(pen, path);
        }

        // 钉住的卡片：左侧一道橙色细条
        if (P.Pinned)
            using (GraphicsPath bar = Theme.Round(new RectangleF(3, 10, 3, Math.Max(4, h - 20)), 1.5f))
            using (SolidBrush bb = new SolidBrush(Theme.Clay))
                g.FillPath(bb, bar);

        // 序号对应键盘最上排 1 2 3 … 9 0；第 11 张起不显示序号
        Theme.Text(g, Index < 9 ? (Index + 1).ToString() : (Index == 9 ? "0" : ""), Theme.Num,
            new Rectangle(6, 0, 28, h), Theme.Clay, TextFormatFlags.HorizontalCenter);

        if (Manage)
        {
            Theme.Text(g, P.Count + " 次", Theme.Small, CountRect, Theme.Muted, TextFormatFlags.Right);
            Theme.Text(g, "\U0001F4CC", Theme.Sym, PinRect,
                P.Pinned || zone == Zone.Pin ? Theme.Clay : Theme.Line, TextFormatFlags.HorizontalCenter);
            Theme.Text(g, "✎", Theme.Sym, EditRect, zone == Zone.Edit || zone == Zone.Body ? Theme.Clay : Theme.Muted, TextFormatFlags.HorizontalCenter);
            Theme.Text(g, "✕", Theme.Sym, DelRect, zone == Zone.Del ? Color.Firebrick : Theme.Muted, TextFormatFlags.HorizontalCenter);
        }
        else if (P.En.Length > 0)
        {
            Rectangle r = EnRect;
            using (GraphicsPath chip = Theme.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 11))
            {
                if (zone == Zone.En)
                    using (SolidBrush cb = new SolidBrush(Theme.Clay)) g.FillPath(cb, chip);
                using (Pen cp = new Pen(Theme.Clay)) g.DrawPath(cp, chip);
            }
            Theme.Text(g, "EN", Theme.Chip, r, zone == Zone.En ? Color.White : Theme.Clay, TextFormatFlags.HorizontalCenter);
        }

        Rectangle t = TextArea;
        if (P.En.Length > 0)
        {
            Theme.Text(g, ZhText, Theme.Zh, new Rectangle(t.X, 4, t.Width, 23), Theme.Ink, TextFormatFlags.Left);
            Theme.Text(g, EnText, Theme.En, new Rectangle(t.X, 26, t.Width, 18), Theme.Muted, TextFormatFlags.Left);
        }
        else
        {
            Theme.Text(g, ZhText, Theme.Zh, t, Theme.Ink, TextFormatFlags.Left);
        }

        if (Parent != null)
        {
            int viewBottom = Parent.ClientSize.Height - Top;  // 列表可视区底边在卡片坐标中的位置
            if (viewBottom > 0 && viewBottom < Height)
            {
                Rectangle fade = new Rectangle(0, viewBottom - FadeH, Width, FadeH);
                using (LinearGradientBrush lb = new LinearGradientBrush(
                    new Rectangle(fade.X, fade.Y - 1, fade.Width, fade.Height + 2),
                    Color.FromArgb(0, Theme.Bg), Theme.Bg, LinearGradientMode.Vertical))
                    g.FillRectangle(lb, fade);
            }
        }
    }
}
