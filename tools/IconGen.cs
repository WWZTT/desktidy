// 生成 assets/app.ico：多尺寸（16/24/32/48/64/128/256）32 位图标。
// 仅构建期使用，不参与主程序。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class IconGen
{
    private static readonly int[] Sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };

    private static int Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "app.ico";
        var images = new List<byte[]>();
        foreach (int size in Sizes)
        {
            using (Bitmap bmp = Render(size))
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                images.Add(ms.ToArray());
            }
        }

        string dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((ushort)0);            // reserved
            w.Write((ushort)1);            // type = icon
            w.Write((ushort)images.Count); // count
            int offset = 6 + 16 * images.Count;
            for (int i = 0; i < images.Count; i++)
            {
                int size = Sizes[i];
                w.Write((byte)(size >= 256 ? 0 : size)); // width
                w.Write((byte)(size >= 256 ? 0 : size)); // height
                w.Write((byte)0);   // palette
                w.Write((byte)0);   // reserved
                w.Write((ushort)1); // color planes
                w.Write((ushort)32);// bpp
                w.Write(images[i].Length);
                w.Write(offset);
                offset += images[i].Length;
            }
            foreach (byte[] img in images) w.Write(img);
        }

        Console.WriteLine("icon written: " + Path.GetFullPath(outPath) + " (" + new FileInfo(outPath).Length + " bytes)");
        return 0;
    }

    private static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            float pad = size * 0.03f;
            var bg = new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad);
            using (GraphicsPath path = Rounded(bg, size * 0.22f))
            using (var brush = new LinearGradientBrush(bg, Color.FromArgb(58, 122, 232), Color.FromArgb(26, 74, 168), 90f))
                g.FillPath(brush, path);

            // 文件夹：主体 + 顶部标签
            float fx = size * 0.16f, fy = size * 0.26f, fw = size * 0.68f, fh = size * 0.46f;
            var body = new RectangleF(fx, fy + size * 0.05f, fw, fh);
            using (GraphicsPath path = Rounded(body, size * 0.06f))
            using (var brush = new LinearGradientBrush(body, Color.FromArgb(255, 214, 122), Color.FromArgb(238, 172, 44), 90f))
                g.FillPath(brush, path);

            float tw = fw * 0.46f;
            var tab = new RectangleF(fx + size * 0.02f, fy - size * 0.01f, tw, size * 0.12f);
            using (GraphicsPath path = Rounded(tab, size * 0.045f))
            using (var brush = new SolidBrush(Color.FromArgb(255, 231, 176)))
                g.FillPath(brush, path);

            // 归档条（表示“文件被收进去”）
            float bw = fw * 0.62f, bh = Math.Max(1.6f, size * 0.045f);
            using (var brush = new SolidBrush(Color.FromArgb(120, 90, 40)))
            {
                g.FillRectangle(brush, fx + fw * 0.16f, fy + fh * 0.42f, bw, bh);
                g.FillRectangle(brush, fx + fw * 0.16f, fy + fh * 0.62f, bw * 0.66f, bh);
            }

            // 右下角勾选徽章
            float d = size * 0.38f;
            var badge = new RectangleF(size - d - size * 0.09f, size - d - size * 0.09f, d, d);
            if (size >= 24)
            {
                using (var brush = new SolidBrush(Color.FromArgb(46, 190, 120)))
                    g.FillEllipse(brush, badge);
                using (var pen = new Pen(Color.White, Math.Max(1.4f, size * 0.055f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    g.DrawLines(pen, new PointF[]
                    {
                        new PointF(badge.X + d * 0.26f, badge.Y + d * 0.52f),
                        new PointF(badge.X + d * 0.44f, badge.Y + d * 0.70f),
                        new PointF(badge.X + d * 0.76f, badge.Y + d * 0.30f)
                    });
                }
            }
        }
        return bmp;
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
