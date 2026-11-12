using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace CodexAccountDock.Assets
{
    internal static class IconBuilder
    {
        private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

        [STAThread]
        private static int Main(string[] args)
        {
            string projectRoot = args.Length > 0
                ? Path.GetFullPath(args[0])
                : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
            string assets = Path.Combine(projectRoot, "assets");
            Directory.CreateDirectory(assets);

            var pngEntries = new List<byte[]>();
            foreach (int size in Sizes)
            {
                using (Bitmap bitmap = Render(size))
                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    pngEntries.Add(stream.ToArray());
                    if (size == 256)
                    {
                        bitmap.Save(Path.Combine(assets, "app-preview.png"), ImageFormat.Png);
                    }
                }
            }

            WriteIco(Path.Combine(assets, "app.ico"), Sizes, pngEntries);
            Console.WriteLine("Generated " + Path.Combine(assets, "app.ico"));
            Console.WriteLine("Generated " + Path.Combine(assets, "app-preview.png"));
            return 0;
        }

        private static Bitmap Render(int outputSize)
        {
            const int designSize = 1024;
            using (var large = new Bitmap(designSize, designSize, PixelFormat.Format32bppArgb))
            {
                large.SetResolution(96, 96);
                using (Graphics g = Graphics.FromImage(large))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);
                    DrawMark(g, outputSize <= 32);
                }

                var output = new Bitmap(outputSize, outputSize, PixelFormat.Format32bppArgb);
                output.SetResolution(96, 96);
                using (Graphics g = Graphics.FromImage(output))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.InterpolationMode = outputSize <= 32
                        ? InterpolationMode.HighQualityBilinear
                        : InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.DrawImage(large, new Rectangle(0, 0, outputSize, outputSize), 0, 0, designSize, designSize, GraphicsUnit.Pixel);
                }
                return output;
            }
        }

        private static void DrawMark(Graphics g, bool compact)
        {
            RectangleF tile = new RectangleF(62, 54, 900, 900);
            using (GraphicsPath shadow = RoundedRect(new RectangleF(72, 72, 880, 880), 238))
            using (var brush = new SolidBrush(Color.FromArgb(46, 58, 45, 115)))
            {
                g.FillPath(brush, shadow);
            }

            using (GraphicsPath tilePath = RoundedRect(tile, 242))
            {
                GraphicsState saved = g.Save();
                g.SetClip(tilePath);
                using (var gradient = new LinearGradientBrush(tile, Color.FromArgb(255, 238, 240, 255), Color.FromArgb(255, 109, 184, 255), 42f))
                {
                    var blend = new ColorBlend
                    {
                        Positions = new[] { 0f, 0.34f, 0.7f, 1f },
                        Colors = new[]
                        {
                            Color.FromArgb(255, 246, 244, 255),
                            Color.FromArgb(255, 202, 205, 255),
                            Color.FromArgb(255, 109, 190, 246),
                            Color.FromArgb(255, 80, 108, 223)
                        }
                    };
                    gradient.InterpolationColors = blend;
                    g.FillRectangle(gradient, tile);
                }

                using (var glow = new PathGradientBrush(EllipsePath(new RectangleF(-70, -110, 820, 690))))
                {
                    glow.CenterColor = Color.FromArgb(190, 255, 255, 255);
                    glow.SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) };
                    g.FillRectangle(glow, tile);
                }

                using (var lowerGlow = new PathGradientBrush(EllipsePath(new RectangleF(430, 450, 730, 700))))
                {
                    lowerGlow.CenterColor = Color.FromArgb(110, 94, 255, 221);
                    lowerGlow.SurroundColors = new[] { Color.FromArgb(0, 94, 255, 221) };
                    g.FillRectangle(lowerGlow, tile);
                }
                g.Restore(saved);

                using (var border = new Pen(Color.FromArgb(220, 255, 255, 255), 18f))
                {
                    g.DrawPath(border, tilePath);
                }
            }

            DrawGlassCard(g, new RectangleF(226, 216, 432, 432), 112, -7f,
                Color.FromArgb(126, 255, 255, 255), Color.FromArgb(96, 116, 128, 245));
            DrawGlassCard(g, new RectangleF(390, 378, 430, 430), 112, 5f,
                Color.FromArgb(156, 255, 255, 255), Color.FromArgb(92, 112, 235, 226));

            using (var connector = new Pen(Color.FromArgb(205, 255, 255, 255), 42f))
            {
                connector.StartCap = LineCap.Round;
                connector.EndCap = LineCap.Round;
                g.DrawBezier(connector, new PointF(355, 500), new PointF(440, 390), new PointF(590, 647), new PointF(690, 536));
            }

            DrawOrb(g, new PointF(350, 503), 80, Color.FromArgb(255, 56, 109, 244));
            DrawOrb(g, new PointF(517, 512), 66, Color.FromArgb(255, 139, 102, 246));
            DrawOrb(g, new PointF(691, 535), 80, Color.FromArgb(255, 31, 206, 190));

            using (var highlight = new Pen(Color.FromArgb(128, 255, 255, 255), 18f))
            {
                highlight.StartCap = LineCap.Round;
                highlight.EndCap = LineCap.Round;
                g.DrawArc(highlight, new RectangleF(142, 118, 600, 420), 198, 83);
            }

            if (compact)
            {
                using (GraphicsPath rear = RoundedRect(new RectangleF(242, 228, 420, 420), 112))
                using (GraphicsPath front = RoundedRect(new RectangleF(374, 358, 430, 430), 112))
                using (var edge = new Pen(Color.FromArgb(238, 255, 255, 255), 34f))
                using (var connector = new Pen(Color.FromArgb(230, 255, 255, 255), 58f))
                {
                    g.DrawPath(edge, rear);
                    g.DrawPath(edge, front);
                    connector.StartCap = LineCap.Round;
                    connector.EndCap = LineCap.Round;
                    g.DrawLine(connector, 344, 503, 694, 535);
                }
                DrawOrb(g, new PointF(344, 503), 145, Color.FromArgb(255, 35, 91, 241));
                DrawOrb(g, new PointF(518, 519), 132, Color.FromArgb(255, 132, 83, 246));
                DrawOrb(g, new PointF(694, 535), 145, Color.FromArgb(255, 12, 194, 175));
            }
        }

        private static void DrawGlassCard(Graphics g, RectangleF rect, float radius, float angle, Color top, Color bottom)
        {
            GraphicsState saved = g.Save();
            g.TranslateTransform(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
            g.RotateTransform(angle);
            RectangleF local = new RectangleF(-rect.Width / 2f, -rect.Height / 2f, rect.Width, rect.Height);
            using (GraphicsPath path = RoundedRect(local, radius))
            {
                using (var brush = new LinearGradientBrush(local, top, bottom, 55f))
                {
                    g.FillPath(brush, path);
                }
                using (var border = new Pen(Color.FromArgb(225, 255, 255, 255), 14f))
                {
                    g.DrawPath(border, path);
                }
                using (var shine = new Pen(Color.FromArgb(115, 255, 255, 255), 18f))
                {
                    g.DrawArc(shine, local.X + 35, local.Y + 35, local.Width - 70, local.Height - 70, 205, 100);
                }
            }
            g.Restore(saved);
        }

        private static void DrawOrb(Graphics g, PointF center, float diameter, Color color)
        {
            RectangleF shadow = new RectangleF(center.X - diameter / 2f + 8, center.Y - diameter / 2f + 12, diameter, diameter);
            using (var shadowBrush = new SolidBrush(Color.FromArgb(48, 30, 38, 90)))
            {
                g.FillEllipse(shadowBrush, shadow);
            }
            RectangleF orb = new RectangleF(center.X - diameter / 2f, center.Y - diameter / 2f, diameter, diameter);
            using (var brush = new SolidBrush(color))
            using (var border = new Pen(Color.FromArgb(235, 255, 255, 255), 12f))
            {
                g.FillEllipse(brush, orb);
                g.DrawEllipse(border, orb);
            }
            using (var gloss = new SolidBrush(Color.FromArgb(185, 255, 255, 255)))
            {
                g.FillEllipse(gloss, orb.X + diameter * .19f, orb.Y + diameter * .15f, diameter * .24f, diameter * .18f);
            }
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            float diameter = Math.Min(Math.Min(radius * 2f, rect.Width), rect.Height);
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath EllipsePath(RectangleF rect)
        {
            var path = new GraphicsPath();
            path.AddEllipse(rect);
            path.CloseFigure();
            return path;
        }

        private static void WriteIco(string path, int[] sizes, IList<byte[]> images)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)images.Count);
                int offset = 6 + images.Count * 16;
                for (int i = 0; i < images.Count; i++)
                {
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write(images[i].Length);
                    writer.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] image in images)
                {
                    writer.Write(image);
                }
            }
        }
    }
}
