using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

// 图片暂存区：参考截图只存在内存里供对照查看，不进剪贴板、不写硬盘
// 最多 6 张，再放入时移除最早的一张；放入时统一转成位图，长边超过 2560 的按比例缩小
class ImageStash
{
    public const int MaxImages = 6, MaxImageSide = 2560;

    readonly List<Image> images = new List<Image>();

    public int Count { get { return images.Count; } }
    public Image this[int index] { get { return images[index]; } }

    static Image Normalize(Image src)
    {
        int w = src.Width, h = src.Height;
        if (w > MaxImageSide || h > MaxImageSide)
        {
            float scale = (float)MaxImageSide / Math.Max(w, h);
            w = Math.Max(1, (int)Math.Round(w * scale));
            h = Math.Max(1, (int)Math.Round(h * scale));
        }
        Bitmap bmp = new Bitmap(w, h);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, new Rectangle(0, 0, w, h));
        }
        return bmp;
    }

    // 放入一张图片，raw 由这里释放；已满时移除最早的一张，evicted = true
    public bool Add(Image raw, out bool evicted)
    {
        evicted = false;
        if (raw == null) return false;
        Image norm;
        try { norm = Normalize(raw); }
        finally { raw.Dispose(); }

        if (images.Count >= MaxImages)
        {
            Image old = images[0];
            images.RemoveAt(0);
            old.Dispose();
            evicted = true;
        }
        images.Add(norm);
        return true;
    }

    // 移除并释放一张；不在暂存区里时返回 false
    public bool Remove(Image img)
    {
        if (!images.Remove(img)) return false;
        img.Dispose();
        return true;
    }

    // 移除并释放全部
    public void Clear()
    {
        foreach (Image img in images) img.Dispose();
        images.Clear();
    }

    // 可以拖入的图片文件：png/jpg/jpeg/bmp/gif
    public static bool IsAllowedFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string ext = Path.GetExtension(path);
        if (ext == null) return false;
        ext = ext.ToLowerInvariant();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif";
    }
}
