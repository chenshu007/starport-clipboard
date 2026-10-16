using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

// 主窗口的图片区界面；图片本身存在 ImageStash 里
partial class MainForm
{
    // 图片区（点「扩展上下文窗口」展开）：粘贴截图、拖入图片、缩略图、放大查看、删除、清空
    void BuildImageArea()
    {
        imagePanel.Dock = DockStyle.Bottom;
        imagePanel.Height = ImageAreaH;
        imagePanel.Visible = false;
        imagePanel.AllowDrop = true;
        imagePanel.DragEnter += OnImageDragEnter;
        imagePanel.DragDrop += OnImageDragDrop;

        imageHeader.Dock = DockStyle.Top;
        imageHeader.Height = 36;
        imageHeader.AllowDrop = true;
        imageHeader.DragEnter += OnImageDragEnter;
        imageHeader.DragDrop += OnImageDragDrop;
        imageHeader.Paint += delegate(object o, PaintEventArgs e)
        {
            Theme.Text(e.Graphics, "可拖入图片 · 最多 6 张", Theme.Small,
                new Rectangle(104, 4, 196, 28), Theme.Muted, TextFormatFlags.Left);
        };
        btnPasteImg.Outline = true;
        btnPasteImg.SetBounds(14, 5, 82, 26);
        btnPasteImg.Click += delegate { PasteImage(); };
        btnClearImg.Outline = true;
        btnClearImg.SetBounds(FormW - 70, 5, 56, 26);
        btnClearImg.Click += delegate { ClearImages(); };
        imageHeader.Controls.Add(btnPasteImg);
        imageHeader.Controls.Add(btnClearImg);

        imageGrid.Dock = DockStyle.Fill;
        imageGrid.AutoScroll = true;
        imageGrid.AllowDrop = true;
        imageGrid.DragEnter += OnImageDragEnter;
        imageGrid.DragDrop += OnImageDragDrop;
        imageGrid.Paint += delegate(object o, PaintEventArgs e)
        {
            if (stash.Count == 0)
            {
                Theme.Text(e.Graphics, "暂无参考图片 · 点「粘贴截图」或拖入图片文件", Theme.Small,
                    new Rectangle(14, 0, imageGrid.Width - 28, imageGrid.Height),
                    Theme.Muted, TextFormatFlags.HorizontalCenter);
            }
        };

        imagePanel.Controls.Add(imageGrid);
        imagePanel.Controls.Add(imageHeader);
    }

    void ToggleContext()
    {
        contextExpanded = !contextExpanded;
        imagePanel.Visible = contextExpanded;
        btnContext.Text = contextExpanded ? "收起上下文" : "扩展上下文窗口";
        btnContext.Invalidate();
        if (contextExpanded) RebuildImages();
        Rebuild();
    }

    void PasteImage()
    {
        try
        {
            if (!Clipboard.ContainsImage())
            {
                Flash("剪贴板里没有图片", true);
                return;
            }
            Image clip = Clipboard.GetImage();
            if (clip == null)
            {
                Flash("剪贴板里没有图片", true);
                return;
            }
            bool evicted;
            stash.Add(clip, out evicted);
            RebuildImages();
            Flash(evicted ? "已满 6 张，已移除最早的一张" : "已粘贴截图", false);
        }
        catch
        {
            Flash("读取剪贴板图片失败", true);
        }
    }

    void OnImageDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap))
            e.Effect = DragDropEffects.Copy;
        else
            e.Effect = DragDropEffects.None;
    }

    void OnImageDragDrop(object sender, DragEventArgs e)
    {
        int added = 0;
        bool ignored = false;
        bool anyEvicted = false;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null)
            {
                foreach (string f in files)
                {
                    if (!ImageStash.IsAllowedFile(f)) { ignored = true; continue; }
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(f);
                        using (MemoryStream ms = new MemoryStream(bytes))
                        {
                            Image raw = Image.FromStream(ms);
                            bool ev;
                            if (stash.Add(raw, out ev))
                            {
                                added++;
                                if (ev) anyEvicted = true;
                            }
                        }
                    }
                    catch { ignored = true; }
                }
            }
        }
        else if (e.Data.GetDataPresent(DataFormats.Bitmap))
        {
            try
            {
                Image bmp = e.Data.GetData(DataFormats.Bitmap) as Image;
                if (bmp != null)
                {
                    Image clone = new Bitmap(bmp);
                    bool ev;
                    if (stash.Add(clone, out ev))
                    {
                        added++;
                        if (ev) anyEvicted = true;
                    }
                }
            }
            catch { ignored = true; }
        }

        if (added > 0)
        {
            RebuildImages();
            if (anyEvicted) Flash("已满 6 张，已移除最早的一张", false);
            else if (ignored) Flash("已添加图片（非支持格式已忽略）", false);
            else Flash("已添加图片", false);
        }
        else if (ignored)
        {
            Flash("仅支持拖入 png/jpg/jpeg/bmp/gif 图片", true);
        }
    }

    void RebuildImages()
    {
        imageGrid.SuspendLayout();
        imageGrid.AutoScrollPosition = Point.Empty;
        foreach (Control c in imageGrid.Controls) c.Dispose();
        imageGrid.Controls.Clear();

        for (int i = 0; i < stash.Count; i++)
        {
            ImageCard ic = new ImageCard(stash[i]);
            int col = i % 3, row = i / 3;
            ic.Location = new Point(14 + col * (104 + 10), 6 + row * (78 + 8));
            ic.ViewRequested += delegate(ImageCard k) { BeginInvoke((MethodInvoker)delegate { ViewImage(k.Img); }); };
            ic.DeleteRequested += delegate(ImageCard k) { BeginInvoke((MethodInvoker)delegate { RemoveImage(k.Img); }); };
            ic.AllowDrop = true;
            ic.DragEnter += OnImageDragEnter;
            ic.DragDrop += OnImageDragDrop;
            imageGrid.Controls.Add(ic);
        }

        btnClearImg.Visible = stash.Count > 0;
        imageGrid.ResumeLayout();
        imageGrid.Invalidate();
    }

    void ViewImage(Image img)
    {
        if (img == null) return;
        IntPtr target = IntPtr.Zero;
        try { target = GetForegroundWindow(); } catch { }
        using (ImageViewer v = new ImageViewer(img))
        {
            v.ShowDialog(this);
            if (target != IntPtr.Zero && target != Handle)
                try { SetForegroundWindow(target); } catch { }
        }
    }

    void RemoveImage(Image img)
    {
        if (stash.Remove(img))
        {
            RebuildImages();
            Flash("已移除图片", false);
        }
    }

    void ClearImages()
    {
        if (stash.Count == 0) return;
        IntPtr target = IntPtr.Zero;
        try { target = GetForegroundWindow(); } catch { }
        DialogResult r = MessageBox.Show(this, "清空全部 " + stash.Count + " 张参考图片？", "确认清空",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (target != IntPtr.Zero && target != Handle)
            try { SetForegroundWindow(target); } catch { }
        if (r != DialogResult.Yes) return;

        foreach (Control c in imageGrid.Controls) c.Dispose();
        imageGrid.Controls.Clear();
        stash.Clear();
        RebuildImages();
        Flash("已清空图片", false);
    }
}
