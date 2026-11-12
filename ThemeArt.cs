using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace CodexAccountDock
{
    internal sealed class ThemeLook
    {
        internal Color PanelTop,PanelBottom,ButtonTop,ButtonBottom,Accent,Accent2,Rim,BarFrom,BarTo,SuccessTop,SuccessBottom;
    }
    internal static class ThemeVisual
    {
        private static Color Rgb(int value){return Color.FromArgb((value>>16)&255,(value>>8)&255,value&255);}
        private static ThemeLook Make(int top,int bottom,int buttonTop,int buttonBottom,int accent,int accent2,int rim,int barFrom,int barTo,int successTop,int successBottom)
        {
            return new ThemeLook {PanelTop=Rgb(top),PanelBottom=Rgb(bottom),ButtonTop=Rgb(buttonTop),ButtonBottom=Rgb(buttonBottom),Accent=Rgb(accent),Accent2=Rgb(accent2),Rim=Rgb(rim),BarFrom=Rgb(barFrom),BarTo=Rgb(barTo),SuccessTop=Rgb(successTop),SuccessBottom=Rgb(successBottom)};
        }
        private static ThemeLook custom;
        private static int customKey;
        private static ThemeLook Custom
        {
            get
            {
                int key=Palette.GlassTop.ToArgb()^Palette.GlassBottom.ToArgb()^Palette.ButtonTop.ToArgb()^Palette.ButtonBottom.ToArgb()^Palette.Accents[0].ToArgb()^Palette.Accents[1].ToArgb();
                if(custom==null || key!=customKey)
                {
                    customKey=key;
                    custom=new ThemeLook {PanelTop=Palette.GlassTop,PanelBottom=Palette.GlassBottom,ButtonTop=Palette.ButtonTop,ButtonBottom=Palette.ButtonBottom,
                        Accent=Palette.Accents[0],Accent2=Palette.Accents[1],Rim=Blend(Palette.Accents[0],Color.White,.7f),
                        BarFrom=Palette.Accents[0],BarTo=Palette.Accents[1],SuccessTop=Blend(Palette.GlassTop,Color.SeaGreen,.35f),SuccessBottom=Blend(Palette.GlassBottom,Color.DarkGreen,.22f)};
                }
                return custom;
            }
        }
        private static readonly ThemeLook Heritage=Make(0x286e61,0x103e39,0x2d7d68,0x0b4d43,0x70e2bc,0xffe4a4,0xfff0b9,0xbffff0,0xffe88d,0x249455,0x1a6544);
        private static readonly ThemeLook Spring=Make(0x267d72,0x0b5147,0x2c9782,0x095447,0x79e4cb,0xffd8e7,0xddfff2,0xb0fff0,0xffd4e8,0x27a663,0x116d48);
        private static readonly ThemeLook Summer=Make(0x187da1,0x06486a,0x1d9fba,0x07506c,0x77edff,0xb6fdf1,0xc5f8ff,0xacfaff,0xffd4ef,0x179c8c,0x087170);
        private static readonly ThemeLook Autumn=Make(0x96672e,0x55371d,0xba873c,0x704420,0xffcd75,0xfff2b6,0xfff0ba,0xfff5b6,0xff9d39,0x839a39,0x52692d);
        private static readonly ThemeLook Winter=Make(0x296ab4,0x124579,0x308dc9,0x0a537f,0x92ddff,0xd0c7ff,0xdaf7ff,0xbefaff,0xd8c8ff,0x269bb0,0x126d85);
        // Keep the shipped lavender reference stable; imported media still derives its own palette.
        private static readonly ThemeLook Reference=Make(0x746a9a,0x524e76,0xa991d0,0x584f83,0xd4bdff,0xc2efff,0xffffff,0xb1ebff,0xffb8de,0x2c9d76,0x2d675f);
        internal static Color HeaderInk {get{return IsReferenceCustom?Color.FromArgb(65,44,109):Color.White;}}
        internal static bool IsReferenceCustom {get{return ThemeSelection.Current=="custom" && !File.Exists(DynamicBackground.PosterPath);}}
        internal static ThemeLook Current {get{return For(ThemeSelection.Current);}}
        internal static ThemeLook For(string id)
        {
            switch(id){case "heritage":return Heritage;case "spring":return Spring;case "summer":return Summer;case "autumn":return Autumn;case "winter":return Winter;default:return File.Exists(DynamicBackground.PosterPath)?Custom:Reference;}
        }
        internal static Color Blend(Color a,Color b,float amount)
        {return Color.FromArgb((int)(a.R+(b.R-a.R)*amount),(int)(a.G+(b.G-a.G)*amount),(int)(a.B+(b.B-a.B)*amount));}
    }
    internal static class ThemeArt
    {
        public static string[] Ids = { "heritage", "spring", "summer", "autumn", "winter" };

        private sealed class ThemeSpec
        {
            public Color Top;
            public Color Middle;
            public Color Bottom;
            public Color Glow;
            public Color Accent;
            public Color Accent2;
            public Color Shadow;
            public Color Mist;
        }

        public static string Name(string id)
        {
            switch ((id ?? string.Empty).ToLowerInvariant())
            {
                case "heritage": return "山河·锦绣";
                case "spring": return "春·花信";
                case "summer": return "夏·听海";
                case "autumn": return "秋·拾光";
                case "winter": return "冬·晴雪";
                default: return "山河赤金";
            }
        }

        public static string Subtitle(string id)
        {
            switch ((id ?? string.Empty).ToLowerInvariant())
            {
                case "heritage": return "山河入画，心有所向";
                case "spring": return "风有约，花不误";
                case "summer": return "把日子交给海风";
                case "autumn": return "一叶知秋，满目温柔";
                case "winter": return "山川落雪，心有暖阳";
                default: return "星河映九州 · 赤心照山河";
            }
        }

        public static void ApplyPalette(string id)
        {
            ThemeLook look=ThemeVisual.For((id??"").ToLowerInvariant());
            SetPalette(ThemeVisual.Blend(look.Accent,Color.White,.82f),ThemeVisual.Blend(look.Accent,Color.White,.94f),
                look.PanelTop,look.PanelBottom,look.ButtonTop,look.ButtonBottom,
                new[]{look.Accent,look.Accent2,look.Rim,look.BarFrom,look.BarTo,ThemeVisual.Blend(look.Accent,Color.White,.5f)});
        }

        private static void SetPalette(Color surface, Color muted, Color glassTop, Color glassBottom,
            Color buttonTop, Color buttonBottom, Color[] accents)
        {
            Palette.Surface = surface;
            Palette.Muted = muted;
            Palette.GlassTop = glassTop;
            Palette.GlassBottom = glassBottom;
            Palette.ButtonTop = buttonTop;
            Palette.ButtonBottom = buttonBottom;
            for (int i = 0; i < Palette.Accents.Length && i < accents.Length; i++) Palette.Accents[i] = accents[i];
        }

        public static Bitmap Render(string id, Size size)
        {
            using(var stream=typeof(ThemeArt).Assembly.GetManifestResourceStream("CodexAccountDock."+id+".png"))
            {
                if(stream!=null) using(Image photo=Image.FromStream(stream)) return new Bitmap(photo);
            }
            int width = Math.Max(1, size.Width);
            int height = Math.Max(1, size.Height);
            string key = (id ?? "heritage").ToLowerInvariant();
            ThemeSpec spec = Spec(key);
            Bitmap bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                DrawAtmosphere(g, width, height, spec);
                if (key == "spring") DrawSpring(g, width, height, spec);
                else if (key == "summer") DrawSummer(g, width, height, spec);
                else if (key == "autumn") DrawAutumn(g, width, height, spec);
                else if (key == "winter") DrawWinter(g, width, height, spec);
                else DrawHeritage(g, width, height, spec);
                DrawQuietCenter(g, width, height, spec);
                DrawFilmDetails(g, width, height, spec);
            }
            return bitmap;
        }

        private static ThemeSpec Spec(string id)
        {
            ThemeSpec s = new ThemeSpec();
            switch (id)
            {
                case "spring":
                    s.Top = Color.FromArgb(38, 78, 70); s.Middle = Color.FromArgb(72, 107, 87); s.Bottom = Color.FromArgb(24, 49, 49);
                    s.Glow = Color.FromArgb(238, 187, 208); s.Accent = Color.FromArgb(244, 202, 220); s.Accent2 = Color.FromArgb(152, 225, 188);
                    s.Shadow = Color.FromArgb(17, 35, 37); s.Mist = Color.FromArgb(212, 228, 215); break;
                case "summer":
                    s.Top = Color.FromArgb(14, 66, 89); s.Middle = Color.FromArgb(15, 104, 105); s.Bottom = Color.FromArgb(7, 39, 60);
                    s.Glow = Color.FromArgb(93, 222, 212); s.Accent = Color.FromArgb(120, 238, 204); s.Accent2 = Color.FromArgb(240, 161, 188);
                    s.Shadow = Color.FromArgb(5, 30, 47); s.Mist = Color.FromArgb(159, 225, 218); break;
                case "autumn":
                    s.Top = Color.FromArgb(80, 44, 35); s.Middle = Color.FromArgb(139, 72, 41); s.Bottom = Color.FromArgb(44, 30, 31);
                    s.Glow = Color.FromArgb(255, 187, 79); s.Accent = Color.FromArgb(247, 158, 62); s.Accent2 = Color.FromArgb(204, 79, 51);
                    s.Shadow = Color.FromArgb(45, 26, 28); s.Mist = Color.FromArgb(244, 202, 130); break;
                case "winter":
                    s.Top = Color.FromArgb(27, 54, 89); s.Middle = Color.FromArgb(53, 82, 119); s.Bottom = Color.FromArgb(17, 31, 57);
                    s.Glow = Color.FromArgb(137, 201, 241); s.Accent = Color.FromArgb(188, 227, 252); s.Accent2 = Color.FromArgb(166, 173, 239);
                    s.Shadow = Color.FromArgb(14, 28, 51); s.Mist = Color.FromArgb(210, 231, 246); break;
                default:
                    s.Top = Color.FromArgb(92, 20, 29); s.Middle = Color.FromArgb(135, 34, 36); s.Bottom = Color.FromArgb(46, 17, 27);
                    s.Glow = Color.FromArgb(246, 185, 69); s.Accent = Color.FromArgb(251, 205, 104); s.Accent2 = Color.FromArgb(202, 73, 54);
                    s.Shadow = Color.FromArgb(42, 14, 25); s.Mist = Color.FromArgb(239, 210, 150); break;
            }
            return s;
        }

        private static void DrawAtmosphere(Graphics g, int w, int h, ThemeSpec s)
        {
            using (LinearGradientBrush background = new LinearGradientBrush(new Rectangle(0, 0, w, h), s.Top, s.Bottom, LinearGradientMode.Vertical))
            {
                ColorBlend blend = new ColorBlend();
                blend.Colors = new Color[] { s.Top, s.Middle, s.Bottom };
                blend.Positions = new float[] { 0f, .46f, 1f };
                background.InterpolationColors = blend;
                g.FillRectangle(background, 0, 0, w, h);
            }
            Glow(g, new RectangleF(-w * .30f, -h * .13f, w * .95f, w * .95f), Color.FromArgb(105, s.Glow), Color.Transparent);
            Glow(g, new RectangleF(w * .56f, h * .57f, w * .66f, w * .66f), Color.FromArgb(60, s.Accent2), Color.Transparent);
            Glow(g, new RectangleF(w * .22f, h * .22f, w * .60f, h * .55f), Color.FromArgb(30, s.Mist), Color.Transparent);
        }

        private static void DrawQuietCenter(Graphics g, int w, int h, ThemeSpec s)
        {
            RectangleF center = new RectangleF(w * .10f, h * .12f, w * .80f, h * .82f);
            using (GraphicsPath path = EllipsePath(center))
            using (PathGradientBrush shade = new PathGradientBrush(path))
            {
                shade.CenterColor = Color.FromArgb(70, s.Shadow);
                shade.SurroundColors = new Color[] { Color.FromArgb(5, s.Shadow) };
                shade.FocusScales = new PointF(.43f, .72f);
                g.FillPath(shade, path);
            }
        }

        private static void DrawHeritage(Graphics g, int w, int h, ThemeSpec s)
        {
            DrawSun(g, w * .75f, h * .12f, w * .18f, s.Glow, 115);
            DrawMountainRange(g, w, h, .36f, .15f, Color.FromArgb(84, 92, 25, 36), Color.FromArgb(25, 225, 164, 70), 0);
            DrawMountainRange(g, w, h, .57f, .19f, Color.FromArgb(125, 53, 20, 31), Color.FromArgb(46, 244, 193, 90), 1);
            DrawMountainRange(g, w, h, .80f, .22f, Color.FromArgb(190, 39, 17, 28), Color.FromArgb(70, 245, 204, 118), 2);
            DrawCloudRibbon(g, w, h, .19f, Color.FromArgb(70, s.Mist), true);
            DrawCloudRibbon(g, w, h, .67f, Color.FromArgb(48, s.Glow), false);
            DrawSeal(g, w * .83f, h * .82f, Math.Max(24f, w * .055f), Color.FromArgb(150, 218, 69, 50), Color.FromArgb(195, 250, 214, 142));
            DrawGoldArc(g, new RectangleF(-w * .18f, h * .03f, w * .62f, h * .26f), s.Accent, 62);
            DrawGoldArc(g, new RectangleF(w * .66f, h * .47f, w * .55f, h * .31f), s.Accent, 45);
        }

        private static void DrawSpring(Graphics g, int w, int h, ThemeSpec s)
        {
            DrawMountainRange(g, w, h, .45f, .16f, Color.FromArgb(70, 51, 84, 72), Color.FromArgb(22, s.Mist), 0);
            DrawMountainRange(g, w, h, .78f, .20f, Color.FromArgb(140, 26, 58, 53), Color.FromArgb(34, s.Accent2), 2);
            DrawCloudRibbon(g, w, h, .27f, Color.FromArgb(44, 232, 242, 236), true);
            DrawBranch(g, new PointF(-w * .04f, h * .18f), new PointF(w * .34f, h * .02f), Color.FromArgb(145, 54, 50, 50), w * .009f);
            DrawBranch(g, new PointF(w * 1.03f, h * .70f), new PointF(w * .77f, h * .88f), Color.FromArgb(135, 46, 51, 48), w * .008f);
            DrawMagnoliaCluster(g, w * .08f, h * .13f, w * .095f, s.Accent, s.Accent2, -15f);
            DrawMagnoliaCluster(g, w * .27f, h * .06f, w * .073f, s.Accent, s.Accent2, 18f);
            DrawMagnoliaCluster(g, w * .91f, h * .72f, w * .086f, s.Accent, s.Accent2, 24f);
            DrawMagnoliaCluster(g, w * .78f, h * .86f, w * .066f, s.Accent, s.Accent2, -22f);
            for (int i = 0; i < 13; i++)
            {
                float x = w * (.05f + ((i * 37) % 92) / 100f);
                float y = h * (.08f + ((i * 53) % 78) / 100f);
                DrawDew(g, x, y, Math.Max(2.5f, w * (.004f + (i % 3) * .0015f)), s.Mist);
            }
        }

        private static void DrawSummer(Graphics g, int w, int h, ThemeSpec s)
        {
            DrawWaveField(g, w, h, h * .62f, Color.FromArgb(55, s.Accent), 10);
            DrawWaveField(g, w, h, h * .78f, Color.FromArgb(44, 152, 220, 229), 7);
            DrawMountainRange(g, w, h, .45f, .11f, Color.FromArgb(60, 16, 72, 78), Color.FromArgb(23, s.Mist), 1);
            DrawLotusLeaf(g, w * .05f, h * .72f, w * .25f, s.Accent, -17f);
            DrawLotusLeaf(g, w * .88f, h * .54f, w * .20f, s.Accent, 20f);
            DrawLotusLeaf(g, w * .95f, h * .87f, w * .30f, s.Accent, -12f);
            DrawLotus(g, w * .12f, h * .65f, w * .12f, s.Accent2, Color.FromArgb(255, 240, 196), -8f);
            DrawLotus(g, w * .90f, h * .45f, w * .09f, s.Accent2, Color.FromArgb(255, 232, 182), 12f);
            DrawMoon(g, w * .73f, h * .13f, w * .10f, Color.FromArgb(180, 181, 248, 232));
        }

        private static void DrawAutumn(Graphics g, int w, int h, ThemeSpec s)
        {
            DrawSun(g, w * .25f, h * .16f, w * .22f, s.Glow, 100);
            DrawMountainRange(g, w, h, .42f, .13f, Color.FromArgb(67, 102, 55, 37), Color.FromArgb(22, s.Glow), 0);
            DrawMountainRange(g, w, h, .72f, .21f, Color.FromArgb(150, 67, 38, 34), Color.FromArgb(38, s.Accent), 2);
            DrawBranch(g, new PointF(w * 1.05f, -h * .02f), new PointF(w * .70f, h * .30f), Color.FromArgb(170, 58, 32, 29), w * .010f);
            DrawBranch(g, new PointF(-w * .05f, h * .75f), new PointF(w * .26f, h * .91f), Color.FromArgb(145, 57, 34, 30), w * .008f);
            for (int i = 0; i < 14; i++)
            {
                float x = i < 8 ? w * (.76f + ((i * 17) % 24) / 100f) : w * (.01f + ((i * 19) % 25) / 100f);
                float y = i < 8 ? h * (.03f + ((i * 23) % 30) / 100f) : h * (.69f + ((i * 29) % 24) / 100f);
                DrawGinkgo(g, x, y, w * (.035f + (i % 3) * .010f), i % 2 == 0 ? s.Glow : s.Accent, i * 29f);
            }
            for (int i = 0; i < 8; i++)
                DrawFallingLeaf(g, w * (.19f + ((i * 31) % 66) / 100f), h * (.23f + ((i * 47) % 58) / 100f), w * .018f, i % 2 == 0 ? s.Accent : s.Accent2, i * 41f);
        }

        private static void DrawWinter(Graphics g, int w, int h, ThemeSpec s)
        {
            DrawMoon(g, w * .73f, h * .13f, w * .14f, Color.FromArgb(195, 212, 237, 255));
            DrawMountainRange(g, w, h, .43f, .18f, Color.FromArgb(78, 55, 83, 118), Color.FromArgb(32, s.Mist), 0);
            DrawMountainRange(g, w, h, .68f, .25f, Color.FromArgb(148, 29, 53, 84), Color.FromArgb(45, 191, 225, 249), 2);
            DrawMountainRange(g, w, h, .88f, .18f, Color.FromArgb(205, 19, 34, 58), Color.FromArgb(35, s.Accent2), 1);
            DrawPine(g, -w * .02f, h * .12f, w * .35f, h * .40f, Color.FromArgb(175, 19, 48, 65), Color.FromArgb(155, 193, 226, 243), false);
            DrawPine(g, w * .81f, h * .58f, w * .24f, h * .34f, Color.FromArgb(180, 17, 45, 62), Color.FromArgb(145, 204, 235, 249), true);
            for (int i = 0; i < 34; i++)
            {
                float x = w * (((i * 43 + 11) % 101) / 100f);
                float y = h * (((i * 67 + 7) % 97) / 100f);
                float r = Math.Max(1.2f, w * (.0023f + (i % 4) * .0008f));
                DrawSnow(g, x, y, r, Color.FromArgb(105 + (i % 4) * 18, 226, 241, 255));
            }
        }

        private static void DrawFilmDetails(Graphics g, int w, int h, ThemeSpec s)
        {
            using (Pen topLine = new Pen(Color.FromArgb(72, s.Mist), Math.Max(1f, w / 900f)))
            {
                g.DrawLine(topLine, w * .08f, h * .045f, w * .36f, h * .045f);
                g.DrawLine(topLine, w * .64f, h * .955f, w * .92f, h * .955f);
            }
            for (int i = 0; i < 18; i++)
            {
                float x = w * (((i * 47 + 13) % 97) / 100f);
                float y = h * (((i * 73 + 5) % 91) / 100f);
                float r = Math.Max(1f, w * (i % 5 == 0 ? .0030f : .0015f));
                using (SolidBrush b = new SolidBrush(Color.FromArgb(i % 5 == 0 ? 92 : 48, s.Mist))) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
            }
        }

        private static void DrawMountainRange(Graphics g, int w, int h, float baseY, float amplitude, Color fill, Color ridge, int phase)
        {
            float yBase = h * baseY;
            float a = h * amplitude;
            PointF[] peaks = new PointF[]
            {
                new PointF(-w * .08f, yBase + a * .45f), new PointF(w * .08f, yBase - a * (.35f + phase * .04f)),
                new PointF(w * .19f, yBase + a * .08f), new PointF(w * .34f, yBase - a * (.78f - phase * .07f)),
                new PointF(w * .47f, yBase - a * .05f), new PointF(w * .60f, yBase - a * (.55f + phase * .03f)),
                new PointF(w * .72f, yBase + a * .12f), new PointF(w * .88f, yBase - a * (.70f - phase * .05f)),
                new PointF(w * 1.08f, yBase + a * .32f)
            };
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddLines(peaks);
                path.AddLine(peaks[peaks.Length - 1], new PointF(w * 1.08f, h * 1.02f));
                path.AddLine(w * 1.08f, h * 1.02f, -w * .08f, h * 1.02f);
                path.CloseFigure();
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, (int)(yBase - a), w, Math.Max(1, (int)(h - yBase + a))), Color.FromArgb(Math.Max(0, fill.A - 32), fill), fill, LinearGradientMode.Vertical))
                    g.FillPath(b, path);
                using (Pen p = new Pen(ridge, Math.Max(1.2f, w * .0021f))) g.DrawLines(p, peaks);
            }
            using (Pen contour = new Pen(Color.FromArgb(Math.Max(12, ridge.A / 2), ridge), Math.Max(.8f, w * .0012f)))
            {
                contour.DashStyle = DashStyle.Solid;
                for (int k = 1; k <= 2; k++)
                {
                    PointF[] shifted = new PointF[peaks.Length];
                    for (int i = 0; i < peaks.Length; i++) shifted[i] = new PointF(peaks[i].X, peaks[i].Y + a * (.10f + k * .09f));
                    g.DrawLines(contour, shifted);
                }
            }
        }

        private static void DrawCloudRibbon(Graphics g, int w, int h, float yFactor, Color color, bool fromLeft)
        {
            float y = h * yFactor;
            using (GraphicsPath path = new GraphicsPath())
            {
                if (fromLeft)
                {
                    path.StartFigure(); path.AddBezier(-w * .12f, y, w * .04f, y - h * .03f, w * .18f, y + h * .025f, w * .39f, y);
                    path.AddBezier(w * .39f, y, w * .49f, y - h * .018f, w * .55f, y + h * .015f, w * .64f, y - h * .008f);
                }
                else
                {
                    path.StartFigure(); path.AddBezier(w * 1.12f, y, w * .92f, y - h * .025f, w * .79f, y + h * .025f, w * .58f, y);
                    path.AddBezier(w * .58f, y, w * .49f, y - h * .014f, w * .42f, y + h * .018f, w * .34f, y);
                }
                using (Pen p = new Pen(color, Math.Max(2f, w * .006f))) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawPath(p, path); }
            }
        }

        private static void DrawWaveField(Graphics g, int w, int h, float startY, Color color, int count)
        {
            using (Pen p = new Pen(color, Math.Max(1f, w * .0018f)))
            {
                for (int i = 0; i < count; i++)
                {
                    float y = startY + i * h * .026f;
                    using (GraphicsPath path = new GraphicsPath())
                    {
                        path.AddBezier(-w * .05f, y, w * .18f, y - h * .018f, w * .32f, y + h * .020f, w * .52f, y);
                        path.AddBezier(w * .52f, y, w * .70f, y - h * .018f, w * .88f, y + h * .018f, w * 1.05f, y);
                        g.DrawPath(p, path);
                    }
                }
            }
        }

        private static void DrawBranch(Graphics g, PointF start, PointF end, Color color, float width)
        {
            using (Pen p = new Pen(color, Math.Max(2f, width)))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                PointF c1 = new PointF(start.X + (end.X - start.X) * .28f, start.Y + (end.Y - start.Y) * .05f);
                PointF c2 = new PointF(start.X + (end.X - start.X) * .72f, start.Y + (end.Y - start.Y) * .88f);
                g.DrawBezier(p, start, c1, c2, end);
            }
        }

        private static void DrawMagnoliaCluster(Graphics g, float x, float y, float radius, Color petal, Color leaf, float angle)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(x, y); g.RotateTransform(angle);
            for (int i = 0; i < 6; i++)
            {
                GraphicsState petalState = g.Save();
                g.RotateTransform(i * 60f);
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddBezier(0, 0, -radius * .28f, -radius * .22f, -radius * .18f, -radius * .82f, 0, -radius);
                    path.AddBezier(0, -radius, radius * .23f, -radius * .78f, radius * .31f, -radius * .22f, 0, 0);
                    using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(-radius * .32f, -radius, radius * .64f, radius), Color.FromArgb(225, 255, 245, 248), Color.FromArgb(165, petal), LinearGradientMode.Vertical)) g.FillPath(b, path);
                    using (Pen p = new Pen(Color.FromArgb(95, 255, 255, 255), Math.Max(.7f, radius * .015f))) g.DrawPath(p, path);
                }
                g.Restore(petalState);
            }
            using (SolidBrush center = new SolidBrush(Color.FromArgb(230, 255, 220, 139))) g.FillEllipse(center, -radius * .10f, -radius * .10f, radius * .20f, radius * .20f);
            using (SolidBrush leafBrush = new SolidBrush(Color.FromArgb(130, leaf))) g.FillEllipse(leafBrush, radius * .10f, radius * .20f, radius * .72f, radius * .27f);
            g.Restore(state);
        }

        private static void DrawDew(Graphics g, float x, float y, float radius, Color color)
        {
            using (GraphicsPath path = EllipsePath(new RectangleF(x - radius, y - radius, radius * 2, radius * 2)))
            using (PathGradientBrush b = new PathGradientBrush(path))
            {
                b.CenterPoint = new PointF(x - radius * .35f, y - radius * .35f);
                b.CenterColor = Color.FromArgb(210, 255, 255, 255);
                b.SurroundColors = new Color[] { Color.FromArgb(28, color) };
                g.FillPath(b, path);
            }
        }

        private static void DrawLotusLeaf(Graphics g, float x, float y, float radius, Color color, float angle)
        {
            GraphicsState state = g.Save(); g.TranslateTransform(x, y); g.RotateTransform(angle);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddBezier(-radius, 0, -radius * .75f, -radius * .67f, radius * .68f, -radius * .67f, radius, 0);
                path.AddBezier(radius, 0, radius * .65f, radius * .53f, -radius * .67f, radius * .53f, -radius, 0);
                path.AddLine(-radius, 0, 0, 0); path.CloseFigure();
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(-radius, -radius * .7f, radius * 2, radius * 1.3f), Color.FromArgb(135, color), Color.FromArgb(48, color), LinearGradientMode.Vertical)) g.FillPath(b, path);
                using (Pen p = new Pen(Color.FromArgb(105, 218, 255, 233), Math.Max(.8f, radius * .012f)))
                {
                    g.DrawPath(p, path);
                    for (int i = -2; i <= 2; i++) g.DrawLine(p, 0, 0, radius * .78f, i * radius * .16f);
                }
            }
            g.Restore(state);
        }

        private static void DrawLotus(Graphics g, float x, float y, float radius, Color petal, Color center, float angle)
        {
            GraphicsState state = g.Save(); g.TranslateTransform(x, y); g.RotateTransform(angle);
            for (int i = 0; i < 9; i++)
            {
                float a = -82f + i * 20.5f;
                GraphicsState ps = g.Save(); g.RotateTransform(a);
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddBezier(0, 0, radius * .20f, -radius * .25f, radius * .16f, -radius * .75f, 0, -radius);
                    path.AddBezier(0, -radius, -radius * .18f, -radius * .73f, -radius * .23f, -radius * .25f, 0, 0);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(125 + (i % 3) * 28, petal))) g.FillPath(b, path);
                }
                g.Restore(ps);
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(230, center))) g.FillEllipse(b, -radius * .12f, -radius * .12f, radius * .24f, radius * .24f);
            g.Restore(state);
        }

        private static void DrawGinkgo(Graphics g, float x, float y, float radius, Color color, float angle)
        {
            GraphicsState state = g.Save(); g.TranslateTransform(x, y); g.RotateTransform(angle);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddBezier(0, 0, -radius * .98f, -radius * .48f, -radius * .72f, -radius, 0, -radius * .67f);
                path.AddBezier(0, -radius * .67f, radius * .71f, -radius, radius * .98f, -radius * .48f, 0, 0);
                path.CloseFigure();
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(-radius, -radius, radius * 2, radius), Color.FromArgb(235, color), Color.FromArgb(118, color), LinearGradientMode.Vertical)) g.FillPath(b, path);
                using (Pen p = new Pen(Color.FromArgb(115, 255, 232, 162), Math.Max(.7f, radius * .025f)))
                {
                    g.DrawLine(p, 0, 0, 0, radius * .58f);
                    for (int i = -2; i <= 2; i++) g.DrawLine(p, 0, -radius * .04f, i * radius * .34f, -radius * .72f);
                }
            }
            g.Restore(state);
        }

        private static void DrawFallingLeaf(Graphics g, float x, float y, float radius, Color color, float angle)
        {
            GraphicsState state = g.Save(); g.TranslateTransform(x, y); g.RotateTransform(angle);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddBezier(0, -radius, radius * .92f, -radius * .48f, radius * .72f, radius * .67f, 0, radius);
                path.AddBezier(0, radius, -radius * .69f, radius * .54f, -radius * .87f, -radius * .48f, 0, -radius);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(170, color))) g.FillPath(b, path);
                using (Pen p = new Pen(Color.FromArgb(120, 255, 221, 143), Math.Max(.6f, radius * .04f))) g.DrawLine(p, 0, -radius * .7f, 0, radius * .75f);
            }
            g.Restore(state);
        }

        private static void DrawPine(Graphics g, float x, float y, float width, float height, Color branch, Color snow, bool mirror)
        {
            GraphicsState state = g.Save();
            if (mirror) { g.TranslateTransform(x + width, y); g.ScaleTransform(-1, 1); } else g.TranslateTransform(x, y);
            using (Pen trunk = new Pen(branch, Math.Max(2f, width * .026f)))
            {
                trunk.StartCap = LineCap.Round; trunk.EndCap = LineCap.Round;
                g.DrawBezier(trunk, 0, 0, width * .28f, height * .18f, width * .45f, height * .72f, width, height);
                for (int i = 1; i <= 7; i++)
                {
                    float t = i / 8f; float bx = width * (.12f + .77f * t); float by = height * (.08f + .78f * t);
                    float len = width * (.23f - i * .012f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        PointF end = new PointF(bx + len * side, by + height * .08f);
                        using (Pen twig = new Pen(Color.FromArgb(145, branch), Math.Max(1f, width * .010f))) g.DrawLine(twig, bx, by, end.X, end.Y);
                        for (int n = 0; n < 5; n++)
                        {
                            float nt = (n + 1) / 6f; float nx = bx + (end.X - bx) * nt; float ny = by + (end.Y - by) * nt;
                            using (Pen needle = new Pen(Color.FromArgb(115, branch), Math.Max(.7f, width * .004f)))
                                g.DrawLine(needle, nx, ny, nx + width * .045f * side, ny - height * .035f);
                        }
                        using (Pen frost = new Pen(Color.FromArgb(90, snow), Math.Max(1f, width * .009f))) g.DrawLine(frost, bx, by - 1, end.X, end.Y - 1);
                    }
                }
            }
            g.Restore(state);
        }

        private static void DrawSun(Graphics g, float x, float y, float diameter, Color color, int alpha)
        {
            RectangleF glowRect = new RectangleF(x - diameter, y - diameter, diameter * 2, diameter * 2);
            Glow(g, glowRect, Color.FromArgb(Math.Min(255, alpha), color), Color.Transparent);
            float core = diameter * .34f;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(Math.Min(235, alpha + 55), color))) g.FillEllipse(b, x - core, y - core, core * 2, core * 2);
        }

        private static void DrawMoon(Graphics g, float x, float y, float diameter, Color color)
        {
            Glow(g, new RectangleF(x - diameter, y - diameter, diameter * 2, diameter * 2), Color.FromArgb(90, color), Color.Transparent);
            float r = diameter * .42f;
            using (SolidBrush b = new SolidBrush(color)) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
        }

        private static void DrawGoldArc(Graphics g, RectangleF rect, Color color, int alpha)
        {
            using (Pen p = new Pen(Color.FromArgb(alpha, color), Math.Max(1.2f, rect.Width * .006f)))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawArc(p, rect, 205, 92);
                RectangleF inner = new RectangleF(rect.X + rect.Width * .07f, rect.Y + rect.Height * .10f, rect.Width * .86f, rect.Height * .78f);
                g.DrawArc(p, inner, 207, 86);
            }
        }

        private static void DrawSeal(Graphics g, float x, float y, float size, Color fill, Color line)
        {
            RectangleF rect = new RectangleF(x - size / 2, y - size / 2, size, size);
            using (SolidBrush b = new SolidBrush(fill)) g.FillRectangle(b, rect);
            using (Pen p = new Pen(line, Math.Max(1f, size * .045f)))
            {
                g.DrawRectangle(p, rect.X, rect.Y, rect.Width, rect.Height);
                g.DrawLine(p, rect.X + size * .22f, rect.Y + size * .30f, rect.Right - size * .20f, rect.Y + size * .30f);
                g.DrawLine(p, rect.X + size * .22f, rect.Y + size * .52f, rect.Right - size * .20f, rect.Y + size * .52f);
                g.DrawLine(p, rect.X + size * .34f, rect.Y + size * .20f, rect.X + size * .34f, rect.Bottom - size * .20f);
                g.DrawLine(p, rect.X + size * .66f, rect.Y + size * .20f, rect.X + size * .66f, rect.Bottom - size * .20f);
            }
        }

        private static void DrawSnow(Graphics g, float x, float y, float radius, Color color)
        {
            using (Pen p = new Pen(color, Math.Max(.65f, radius * .22f)))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                for (int i = 0; i < 3; i++)
                {
                    double angle = i * Math.PI / 3.0;
                    float dx = (float)Math.Cos(angle) * radius;
                    float dy = (float)Math.Sin(angle) * radius;
                    g.DrawLine(p, x - dx, y - dy, x + dx, y + dy);
                }
            }
        }

        private static GraphicsPath EllipsePath(RectangleF rect)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddEllipse(rect);
            return path;
        }

        private static void Glow(Graphics g, RectangleF rect, Color center, Color edge)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            using (GraphicsPath path = EllipsePath(rect))
            using (PathGradientBrush glow = new PathGradientBrush(path))
            {
                glow.CenterColor = center;
                glow.SurroundColors = new Color[] { edge };
                glow.FocusScales = new PointF(.18f, .18f);
                g.FillPath(glow, path);
            }
        }
    }
}
