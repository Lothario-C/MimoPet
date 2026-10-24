// Wallpaper.cs —— 图片读取、缩放、JPEG 编码
//
// 用 GDI+ 完成，不依赖 Python + Pillow。
// 原图常是 PNG 或超大尺寸，直接塞进包里会让 asar 膨胀几十 MB，
// 所以统一缩到 MaxWidth 以内再编码成 JPEG。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace MiMoSkin
{
    public static class Wallpaper
    {
        public static readonly string[] Extensions = new string[]
        {
            ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff"
        };

        public static bool IsImage(string path)
        {
            string e = Path.GetExtension(path).ToLowerInvariant();
            for (int i = 0; i < Extensions.Length; i++) if (Extensions[i] == e) return true;
            return false;
        }

        public sealed class Result
        {
            public byte[] Jpeg;
            public int Width;
            public int Height;
            public string Error;
        }

        // 读图 → 缩放到 maxWidth 以内 → 编码 JPEG。
        // 透明图会铺黑底，避免转 JPEG 后透明区域变成白斑。
        public static Result Process(string srcPath, int maxWidth)
        {
            Result r = new Result();
            try
            {
                using (Image src = LoadDetached(srcPath))
                {
                    int w = src.Width, h = src.Height;
                    if (w <= 0 || h <= 0) { r.Error = "图片尺寸异常"; return r; }

                    double ratio = 1.0;
                    if (maxWidth > 0 && w > maxWidth) ratio = (double)maxWidth / w;

                    int tw = Math.Max(1, (int)Math.Round(w * ratio));
                    int th = Math.Max(1, (int)Math.Round(h * ratio));

                    using (Bitmap bmp = new Bitmap(tw, th, PixelFormat.Format24bppRgb))
                    {
                        bmp.SetResolution(96, 96);
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.CompositingQuality = CompositingQuality.HighQuality;
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.SmoothingMode = SmoothingMode.HighQuality;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                            // 透明区域铺黑底（深色主题下更自然）
                            g.Clear(Color.Black);
                            g.DrawImage(src, new Rectangle(0, 0, tw, th),
                                new Rectangle(0, 0, w, h), GraphicsUnit.Pixel);
                        }

                        using (MemoryStream ms = new MemoryStream())
                        {
                            // 质量 88：肉眼看不出损失，体积约为原 PNG 的 1/10
                            ImageCodecInfo jpg = FindJpegCodec();
                            if (jpg != null)
                            {
                                EncoderParameters ep = new EncoderParameters(1);
                                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                                bmp.Save(ms, jpg, ep);
                                ep.Dispose();
                            }
                            else bmp.Save(ms, ImageFormat.Jpeg);

                            r.Jpeg = ms.ToArray();
                        }

                        r.Width = tw;
                        r.Height = th;
                    }
                }
            }
            catch (Exception e)
            {
                r.Error = e.Message;
            }
            return r;
        }

        // 用 FileStream 读完再交给 Image，避免 Image 持有文件句柄导致后续无法替换/删除
        static Image LoadDetached(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            MemoryStream ms = new MemoryStream(bytes);
            Image img = Image.FromStream(ms, true, true);
            // 立刻复制成新位图，脱离 ms
            Bitmap copy = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(copy))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(img, 0, 0, img.Width, img.Height);
            }
            img.Dispose();
            ms.Dispose();
            return copy;
        }

        static ImageCodecInfo FindJpegCodec()
        {
            ImageCodecInfo[] all = ImageCodecInfo.GetImageEncoders();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].FormatID == ImageFormat.Jpeg.Guid) return all[i];
            }
            return null;
        }

        /* --------------------------- 文件夹扫描 --------------------------- */

        public static System.Collections.Generic.List<string> ScanFolder(string dir, int maxDepth)
        {
            System.Collections.Generic.List<string> outList = new System.Collections.Generic.List<string>();
            Walk(dir, 0, maxDepth, outList);
            return outList;
        }

        static void Walk(string dir, int depth, int maxDepth, System.Collections.Generic.List<string> outList)
        {
            if (depth > maxDepth) return;
            string[] files, dirs;
            try
            {
                files = Directory.GetFiles(dir);
                dirs = Directory.GetDirectories(dir);
            }
            catch { return; }

            for (int i = 0; i < files.Length; i++)
            {
                if (!IsImage(files[i])) continue;
                try
                {
                    FileInfo fi = new FileInfo(files[i]);
                    if (fi.Length > 0) outList.Add(files[i]);
                }
                catch { }
            }
            for (int i = 0; i < dirs.Length; i++) Walk(dirs[i], depth + 1, maxDepth, outList);
        }
    }
}
