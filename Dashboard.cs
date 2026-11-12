using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexAccountDock
{
    internal static class ExpiryDateTimeValue
    {
        private static readonly string[] Formats = { "yyyy-MM-dd HH:mm", "yyyy-MM-dd" };

        internal static bool TryParse(string text, out DateTime value, out bool hasTime)
        {
            value = default(DateTime); hasTime = false;
            string candidate = (text ?? string.Empty).Trim();
            if (candidate.Length == 0) return false;
            if (DateTime.TryParseExact(candidate, Formats[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            { hasTime = true; return true; }
            return DateTime.TryParseExact(candidate, Formats[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        internal static string Serialize(DateTime value) { return value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }

        internal static bool SelfTest(out string details)
        {
            DateTime oldValue, newValue, leapValue; bool oldHasTime, newHasTime, leapHasTime;
            bool ok = TryParse("2027-03-23", out oldValue, out oldHasTime)
                && oldValue == new DateTime(2027, 3, 23) && !oldHasTime
                && TryParse("2027-03-23 18:45", out newValue, out newHasTime)
                && newValue == new DateTime(2027, 3, 23, 18, 45, 0) && newHasTime
                && Serialize(newValue) == "2027-03-23 18:45"
                && TryParse(" 2028-02-29 00:01 ", out leapValue, out leapHasTime)
                && leapValue == new DateTime(2028, 2, 29, 0, 1, 0) && leapHasTime
                && !TryParse("2027/03/23", out oldValue, out oldHasTime)
                && !TryParse("2027-02-29", out oldValue, out oldHasTime)
                && !TryParse("2027-03-23 24:00", out oldValue, out oldHasTime)
                && !TryParse(string.Empty, out oldValue, out oldHasTime);
            details = ok ? "旧日期、新日期时间、闰日、空白修剪及无效日期时间断言通过" : "到期日期时间解析测试失败";
            return ok;
        }
    }

    internal static class Palette
    {
        public static readonly Color Background = Color.FromArgb(197, 202, 218);
        public static Color Surface = Color.FromArgb(218, 225, 241);
        public static readonly Color Border = Color.FromArgb(78, 87, 109, 148);
        public static readonly Color Ink = Color.White;
        public static Color Muted = Color.FromArgb(228, 235, 247);
        public static Color GlassTop = Color.FromArgb(70, 93, 128);
        public static Color GlassBottom = Color.FromArgb(38, 43, 72);
        public static Color ButtonTop = Color.FromArgb(76, 83, 105);
        public static Color ButtonBottom = Color.FromArgb(35, 42, 64);
        public static readonly Color[] Accents = { Color.FromArgb(164, 237, 255), Color.FromArgb(220, 194, 255), Color.FromArgb(179, 249, 225), Color.FromArgb(255, 211, 232), Color.FromArgb(248, 229, 182), Color.FromArgb(187, 211, 255) };
        public static bool Solid = false;
        private static Color Hsv(double hue, double saturation, double value)
        {
            hue = (hue % 360 + 360) % 360 / 60;
            double c = value * saturation, x = c * (1-Math.Abs(hue % 2-1)), m = value-c;
            double r=0, g=0, b=0;
            if(hue<1){r=c;g=x;}else if(hue<2){r=x;g=c;}else if(hue<3){g=c;b=x;}else if(hue<4){g=x;b=c;}else if(hue<5){r=x;b=c;}else{r=c;b=x;}
            return Color.FromArgb((int)Math.Round((r+m)*255), (int)Math.Round((g+m)*255), (int)Math.Round((b+m)*255));
        }
        public static void FromBackground(Image image)
        {
            double[] weights = new double[24];
            double saturationSum=0;
            using(Bitmap sample = new Bitmap(image, new Size(48,48)))
            for(int y=0;y<48;y++) for(int x=0;x<48;x++)
            {
                Color pixel=sample.GetPixel(x,y);
                if(pixel.A<128) continue;
                double saturation=pixel.GetSaturation(), brightness=pixel.GetBrightness();
                if(brightness<.08 || brightness>.95) continue;
                weights[Math.Min(23,(int)(pixel.GetHue()/15))] += saturation*saturation;
                saturationSum += saturation;
            }
            int strongest=0;
            for(int i=1;i<weights.Length;i++) if(weights[i]>weights[strongest]) strongest=i;
            double hue=saturationSum<15 ? 220 : strongest*15+7.5;
            GlassTop=Hsv(hue,.28,.43); GlassBottom=Hsv(hue,.34,.24);
            ButtonTop=Hsv(hue,.24,.43); ButtonBottom=Hsv(hue,.32,.25);
            Surface=Hsv(hue,.12,.93); Muted=Hsv(hue,.07,.97);
            for(int i=0;i<Accents.Length;i++) Accents[i]=Hsv(hue+i*23,.25,.99);
        }
        public static void UpdateControls(Control parent)
        {
            Label label=parent as Label;
            if(label!=null && label.Tag is string)
                label.ForeColor=(string)label.Tag=="accent" ? Accents[0] : (string)label.Tag=="accent2" ? Accents[1] : (string)label.Tag=="muted" ? Muted : Ink;
            if(parent is TextBoxBase || parent is ComboBox)
            {parent.BackColor=Surface;parent.ForeColor=ThemeVisual.Current.PanelBottom;}
            foreach(Control child in parent.Controls) UpdateControls(child);
            parent.Invalidate();
        }
        private static readonly string DisplayFamily=ChooseDisplayFamily();
        private static string ChooseDisplayFamily()
        {
            using(var fonts=new System.Drawing.Text.InstalledFontCollection())
            {
                string[] available=fonts.Families.Select(delegate(FontFamily f){return f.Name;}).ToArray();
                foreach(string family in new[]{"思源宋体 CN","Source Han Serif SC","华文中宋","STZhongsong","宋体","SimSun"})
                    if(available.Contains(family)) return family;
            }
            return "Microsoft YaHei UI";
        }
        public static Font DisplayFont(float size) { return new Font(AppTypography.ChineseFamily,size,FontStyle.Bold); }
        public static Font Font(float size, bool bold) { return new Font(AppTypography.ChineseFamily, size, bold ? FontStyle.Bold : FontStyle.Bold); }
        private static Font TextFont(string text, float size, bool bold)
        {
            bool chinese=text.Any(delegate(char c) { return c >= 0x3400 && c <= 0x9FFF; });
            return new Font(chinese ? AppTypography.ChineseFamily : AppTypography.LatinFamily, size, bold ? FontStyle.Bold : FontStyle.Bold);
        }
        public static Color Quota(double value) { return value < 0 ? Muted : Color.White; }
        public static void Icon(Graphics g, string kind, Rectangle box)
        {
            GraphicsState state=g.Save(); g.TranslateTransform(box.X,box.Y);g.ScaleTransform(box.Width/24f,box.Height/24f);
            using(Pen p=new Pen(Color.White,1.8f)) using(SolidBrush b=new SolidBrush(Color.White))
            {
                p.StartCap=p.EndCap=LineCap.Round;
                if(kind=="person") {g.FillEllipse(b,8,2,8,8);g.FillPie(b,4,12,16,15,180,180);}
                else if(kind=="clock") {g.FillEllipse(b,2,2,20,20);using(Pen hand=new Pen(GlassBottom,2)){g.DrawLine(hand,12,6,12,12);g.DrawLine(hand,12,12,16,15);}}
                else if(kind=="database") {g.FillEllipse(b,3,2,18,7);g.DrawArc(p,3,7,18,7,0,180);g.DrawArc(p,3,13,18,7,0,180);g.DrawLine(p,3,6,3,16);g.DrawLine(p,21,6,21,16);}
                else if(kind=="refresh") {g.DrawArc(p,3,3,18,18,25,135);g.DrawArc(p,3,3,18,18,205,135);g.DrawLines(p,new[]{new Point(17,2),new Point(21,7),new Point(15,7)});g.DrawLines(p,new[]{new Point(7,22),new Point(3,17),new Point(9,17)});}
                else if(kind=="news") {g.FillRectangle(b,3,14,4,8);g.FillRectangle(b,10,8,4,14);g.FillRectangle(b,17,2,4,20);}
                else if(kind=="shop") {g.DrawLines(p,new[]{new Point(4,8),new Point(3,22),new Point(21,22),new Point(20,8),new Point(4,8)});g.DrawArc(p,8,2,8,12,180,180);}
                else if(kind=="bell") {g.DrawArc(p,6,4,12,14,180,180);g.DrawLines(p,new[]{new Point(6,11),new Point(5,18),new Point(19,18),new Point(18,11)});g.DrawArc(p,10,18,4,4,0,180);}
                else if(kind=="calendar")
                {
                    g.DrawRectangle(p,3,5,18,17);g.DrawLine(p,3,10,21,10);g.DrawLine(p,8,2,8,7);g.DrawLine(p,16,2,16,7);
                    for(int row=0;row<2;row++)for(int col=0;col<3;col++)g.FillRectangle(b,6+col*5,13+row*4,2,2);
                }
                else {g.DrawRectangle(p,4,4,16,17);g.DrawLine(p,7,9,17,9);g.DrawLine(p,7,13,15,13);g.DrawLine(p,7,17,12,17);}
            }
            g.Restore(state);
        }
        public static string Percent(double value) { return value < 0 ? "—" : Math.Round(value).ToString("0", CultureInfo.InvariantCulture) + "%"; }
        public static bool IsFree(AccountMeta account) { return account != null && string.Equals(account.Plan,"free",StringComparison.OrdinalIgnoreCase); }
        public static bool IsPro(AccountMeta account) { return account != null && string.Equals((account.Plan??"").Trim(),"pro",StringComparison.OrdinalIgnoreCase); }
        public static bool HasPrimary(AccountMeta account) { return account != null && !IsFree(account) && !IsPro(account) && account.PrimaryRemaining >= 0; }
        public static bool HasSecondary(AccountMeta account) { return account != null && !IsFree(account) && account.SecondaryRemaining >= 0; }
        public static bool HasMonthly(AccountMeta account) { return IsFree(account) && account.HasMonthlyLimit && account.MonthlyRemaining >= 0; }
        public static bool HasAnyQuota(AccountMeta account) { return HasPrimary(account) || HasSecondary(account) || HasMonthly(account); }
        public static bool AllPresentQuotaAvailable(AccountMeta account)
        {
            if (!HasAnyQuota(account) || !string.IsNullOrEmpty(account.LastError)) return false;
            return (!HasPrimary(account) || account.PrimaryRemaining > 0)
                && (!HasSecondary(account) || account.SecondaryRemaining > 0)
                && (!HasMonthly(account) || account.MonthlyRemaining > 0);
        }
        public static double MainRemaining(AccountMeta account) { return IsFree(account) ? HasMonthly(account) ? account.MonthlyRemaining : -1 : IsPro(account) ? HasSecondary(account) ? account.SecondaryRemaining : -1 : HasPrimary(account) ? account.PrimaryRemaining : HasSecondary(account) ? account.SecondaryRemaining : -1; }
        public static string MainQuotaLabel(AccountMeta account) { return IsFree(account) ? "月额度剩余" : IsPro(account) ? "周额度剩余" : HasPrimary(account) ? "5 小时额度" : HasSecondary(account) ? "周额度剩余" : "额度剩余"; }
        public static bool WeeklyReset(AccountMeta account,bool detailed) { return !IsFree(account) && (IsPro(account) || detailed || (!HasPrimary(account) && HasSecondary(account))); }
        public static long VisibleResetAt(AccountMeta account,bool detailed) { return account==null?0:IsFree(account)?account.MonthlyResetAt:WeeklyReset(account,detailed)?account.SecondaryResetAt:account.PrimaryResetAt; }
        public static string VisibleResetLabel(AccountMeta account,bool detailed) { return IsFree(account)?"月":WeeklyReset(account,detailed)?"周":"短"; }
        public static string CompactCountdown(long unix)
        {
            string value=Countdown(unix);
            return value.Replace("分钟后重置","分").Replace("小时后重置","小时").Replace("天 ","天").Replace("小时 ","小时").Replace("后重置","");
        }
        public static string Countdown(long unix)
        {
            if (unix <= 0) return "时间未知";
            TimeSpan left = DateTimeOffset.FromUnixTimeSeconds(unix) - DateTimeOffset.Now;
            if (left.TotalSeconds <= 0) return "已到重置时间，待刷新";
            return left.TotalDays >= 1 ? ((int)left.TotalDays) + "天 " + left.Hours + "小时后重置" : ((int)left.TotalHours) + "小时 " + left.Minutes + "分钟后重置";
        }
        public static string Updated(AccountMeta account)
        {
            DateTime stamp;
            return DateTime.TryParse(account.LastQuotaAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out stamp) ? stamp.ToLocalTime().ToString("MM-dd HH:mm") : "尚未查询";
        }
        public static string Expiry(AccountMeta account)
        {
            string online = SubscriptionDisplay.Compact(account);
            if (!string.IsNullOrEmpty(online)) return online + "（官方上次读取）";
            DateTime date; bool hasTime;
            if (!ExpiryDateTimeValue.TryParse(account.SubscriptionExpiresOn, out date, out hasTime)) return "订阅到期时间未知（不代表未订阅）";
            TimeSpan left = date - DateTime.Now;
            string relative;
            if (hasTime && Math.Abs(left.TotalDays) < 1)
            {
                int minutes = Math.Max(0, (int)Math.Ceiling(Math.Abs(left.TotalMinutes)));
                relative = left.TotalMinutes < 0 ? "已过所填到期时间 " + minutes + " 分钟" : "距到期 " + (minutes / 60) + " 小时 " + (minutes % 60) + " 分钟";
            }
            else
            {
                int days = (date.Date - DateTime.Today).Days;
                relative = days < 0 ? "已过所填到期日 " + (-days) + " 天" : days == 0 ? "今天到期" : "距到期 " + days + " 天";
            }
            return relative + " · " + date.ToString(hasTime ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd") + "（手填）";
        }
        public static Label Label(string text, float size, Color color, bool bold)
        {
            return new Label { Text = text, Tag = color == Accents[0] ? "accent" : color == Muted ? "muted" : "ink", Font = TextFont(text, size, bold), ForeColor = color, BackColor = Color.Transparent, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        }
        public static void Text(Graphics g, string text, float size, Color color, Rectangle box, bool bold, TextFormatFlags align)
        {
            StringAlignment alignment=(align&TextFormatFlags.Right)!=0?StringAlignment.Far:(align&TextFormatFlags.HorizontalCenter)!=0?StringAlignment.Center:StringAlignment.Near;
            AppTypography.Draw(g,text,Math.Max(9f,size)*g.DpiY/72f,bold?FontStyle.Bold:FontStyle.Bold,box,color,alignment,true);
        }
        public static void Round(Graphics g, Rectangle box, Color color, int radius)
        {
            if (box.Width < 1 || box.Height < 1) return;
            using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(box, radius))
            using (SolidBrush brush = new SolidBrush(color)) g.FillPath(brush, path);
        }
        public static void Bar(Graphics g, Rectangle box, double remaining, Color accent)
        {
            Round(g, box, Color.FromArgb(90, GlassBottom), 4);
            if (remaining > 0)
            {
                Rectangle fill = new Rectangle(box.X, box.Y, Math.Max(4, (int)(box.Width * Math.Min(100, remaining) / 100)), box.Height);
                using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(fill, 4))
                using (LinearGradientBrush shine = new LinearGradientBrush(fill, ThemeSelection.Current=="custom"?Color.FromArgb(181,239,255):ThemeVisual.Current.BarFrom, ThemeSelection.Current=="custom"?Color.FromArgb(255,187,225):ThemeVisual.Current.BarTo, 0f)) g.FillPath(shine, path);
                using (Pen highlight = new Pen(Color.FromArgb(150, 255, 255, 255))) g.DrawLine(highlight, fill.Left+2, fill.Top+1, fill.Right-2, fill.Top+1);
            }
        }

        public static void Glass(Graphics g, Rectangle box, bool selected, Control owner)
        {
            if (box.Width < 2 || box.Height < 2) return;
            const int radius = 25;
            using (GraphicsPath shadow = GraphicsExtensions.CreateRoundedPath(new Rectangle(box.X + 1, box.Y + 4, box.Width - 2, box.Height), radius))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(selected ? 52 : 38, 24, 31, 52))) g.FillPath(brush, shadow);
            if (!Solid) GlassBackdrop.DrawFrosted(g, owner, box, radius);
            bool themed=ThemeSelection.Current!="custom";
            using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(box, radius))
            using (LinearGradientBrush brush = new LinearGradientBrush(box, Color.FromArgb(Solid ? 230 : AppearanceOptions.GlassAlpha(themed?154:selected ? 106 : 96), GlassTop), Color.FromArgb(Solid ? 240 : AppearanceOptions.GlassAlpha(themed?180:selected ? 140 : 128), GlassBottom), 90f))
            {
                g.FillPath(brush, path);
                GraphicsState state = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                using (GraphicsPath sheen = new GraphicsPath())
                {
                    sheen.AddEllipse(box.X - box.Width / 3, box.Y - box.Height / 2, box.Width * 4 / 3, box.Height);
                    using (PathGradientBrush light = new PathGradientBrush(sheen))
                    {
                        light.CenterColor = Color.FromArgb(58, Accents[0]);
                        light.SurroundColors = new[] { Color.FromArgb(0, 240, 245, 255) };
                        g.FillPath(light, sheen);
                    }
                }
                g.Restore(state);
                using (LinearGradientBrush rim = new LinearGradientBrush(box, Color.FromArgb(selected ? 220 : 158, 255, 255, 255), Color.FromArgb(selected ? 188 : 106, Accents[0]), 60f))
                {
                    rim.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(selected ? 224 : 164, Color.White), Color.FromArgb(selected ? 190 : 116, Accents[0]), Color.FromArgb(selected ? 156 : 84, Accents[1]), Color.FromArgb(selected ? 204 : 128, Accents[2]) }, Positions = new[] { 0f, .34f, .68f, 1f } };
                    using (Pen light = new Pen(Color.FromArgb(selected ? 235 : 200,Color.White), 1.3f)) g.DrawPath(light, path);
                }
            }
        }
    }

    internal sealed class AccountPopup : Form
    {
        private readonly DockContext context;
        private readonly AccountStore store;
        private readonly FlowLayoutPanel list;
        private readonly SummaryView overview;
        private readonly DetailView detail;
        private readonly Label status;
        private readonly Label count;
        private readonly ModuleButton refresh;
        private readonly Panel body;
        private readonly Label themeCaption;
        private Action updateHeader;
        private readonly Label refreshSummary;
        private readonly DisplayModeSwitch displayMode;
        private ReferenceSurface referenceSurface;
        private AccountToolsView toolsView;
        private ContextMenuStrip referenceAppearance;
        private readonly Dictionary<string,string> refreshStates=new Dictionary<string,string>();
        private bool refreshFinished, refreshSaved=true;
        private bool detailedMode=true;
        private string selected = "";
        private IntPtr registeredHandle;
        private bool registered;
        private bool busy;
        private const int HotkeyId = 0xCA11;
        public bool AllowExit;

        public AccountPopup(DockContext context, AccountStore store)
        {
            this.context = context;
            this.store = store;
            detailedMode=LoadDisplayMode();
            GlassBackdrop.PrepareTheme();
            DynamicBackground.Attach(this);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = DashboardNative.WindowTitle;
            AccessibleName = "Codex Account Dock 账户仪表盘";
            BackColor = Palette.Background;
            ForeColor = Palette.Ink;
            Font = Palette.Font(9, false);
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(5);
            MinimumSize = new Size(540, 620);
            ClientSize = new Size(620, 880);
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            DoubleBuffered = true;
            Opacity = 1;

            body = new GlassPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 0, 22, 0), BackColor = Palette.Background };
            Controls.Add(body);
            Panel footer = new GlassPanel { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(22, 0, 22, 0), BackColor = Palette.Background };
            status = Palette.Label("本地加密保存  ·  手动刷新额度", 8, Palette.Muted, false);
            status.Dock = DockStyle.Fill;
            footer.Controls.Add(status);
            Controls.Add(footer);

            Panel header = new GlassPanel { Dock = DockStyle.Top, Width = ClientSize.Width, Height = 138, BackColor = Palette.Background };
            ThemeSelection.AttachMotion(header);
            header.Paint += delegate(object sender, PaintEventArgs e)
            {
                if(header.Width<1 || header.Height<1) return;
                using(LinearGradientBrush shade=new LinearGradientBrush(header.ClientRectangle,Color.FromArgb(ThemeSelection.Current=="custom"?0:50,20,30,46),Color.Transparent,90f))
                    e.Graphics.FillRectangle(shade,header.ClientRectangle);
            };
            Label brand = new SpacedHeaderLabel {Text="ACCOUNT DOCK",BackColor=Color.Transparent,ForeColor=Palette.Accents[0]};
            brand.Font=new Font("Times New Roman",10,FontStyle.Bold);
            brand.SetBounds(30, 22, 265, 22);
            header.Controls.Add(brand);
            Label title = Palette.Label(ThemeSelection.Current=="custom"?"账户总览":ThemeSelection.Name, 30, Palette.Ink, true);
            title.SetBounds(26, 44, 320, 68);
            title.AutoEllipsis=false;
            title.Font=new Font("思源宋体 CN Heavy",35,FontStyle.Regular);
            if(ThemeSelection.Current=="custom") title.ForeColor=Color.FromArgb(64,46,107);
            header.Controls.Add(title);
            themeCaption=Palette.Label(ThemeSelection.Current=="custom"?"自定义背景 · 随心而换":ThemeSelection.Subtitle,13,Palette.Muted,false);
            themeCaption.SetBounds(30,112,530,26); themeCaption.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
            if(ThemeSelection.Current=="custom") {themeCaption.ForeColor=Color.FromArgb(64,46,107);brand.ForeColor=themeCaption.ForeColor;}
            header.Controls.Add(themeCaption);
            HeaderButton pin = new HeaderButton("置顶");
            pin.SetBounds(428, 26, 58, 32);
            pin.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            pin.Click += delegate { TopMost = !TopMost; pin.ForeColor = TopMost ? Palette.Accents[0] : Palette.Muted; pin.Invalidate(); };
            header.Controls.Add(pin);
            HeaderButton glass = new HeaderButton("外观⌄");
            glass.SetBounds(362, 26, 58, 32);
            glass.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            ContextMenuStrip appearance = new RoundedAppearanceMenu();
            appearance.Renderer=new BorderlessMenuRenderer();
            appearance.ForeColor=Color.White;
            appearance.ShowImageMargin=false;
            referenceAppearance=appearance;
            appearance.Font=Palette.Font(10,false);
            ToolStripMenuItem motion=new ToolStripMenuItem("四季微动") { Checked=ThemeSelection.Motion, CheckOnClick=true };
            motion.CheckedChanged+=delegate { ThemeSelection.Motion=motion.Checked; header.Invalidate(true); };
            appearance.Items.Add(motion);
            appearance.Items.Add("主题典藏 · 山河与四季…",null,delegate { string id=ThemeSelection.Choose(this); if(id!=null) ApplyTheme(id); });
            appearance.Items.Add(new ToolStripSeparator());
            appearance.Items.Add("更换背景 · 图片/高清MP4…", null, delegate { ChangeBackground(false); });
            ToolStripMenuItem videoMotion=new ToolStripMenuItem("暂停动态背景");
            appearance.Opening+=delegate {videoMotion.Enabled=DynamicBackground.HasVideo && ThemeSelection.Current=="custom";videoMotion.Text=DynamicBackground.Paused?"播放动态背景":"暂停动态背景";};
            videoMotion.Click+=delegate {try{DynamicBackground.TogglePause();}catch(Exception ex){MessageBox.Show(this,"无法保存播放状态："+ex.Message,"动态背景");}};
            appearance.Items.Add(videoMotion);
            ToolStripMenuItem videoError=new ToolStripMenuItem {Enabled=false};
            appearance.Items.Add(videoError);
            appearance.Opening+=delegate{videoError.Visible=!string.IsNullOrEmpty(DynamicBackground.LastError);videoError.Text=DynamicBackground.LastError??"";};
            appearance.Items.Add("恢复默认背景", null, delegate { ChangeBackground(true); });
            appearance.Items.Add(new ToolStripSeparator());
            appearance.Items.Add("明暗与透明度…",null,delegate
            {
                AppearanceOptions.Show(this,delegate
                {
                    Palette.UpdateControls(this);
                    SidePanels.RefreshAppearance();
                    Invalidate(true);
                });
            });
            glass.Click += delegate { RoundedAppearanceMenu.ShowInside(appearance,this,glass.PointToScreen(new Point(0,glass.Height))); };
            Disposed += delegate { appearance.Dispose(); };
            header.Controls.Add(glass);
            HeaderButton minimize = new HeaderButton("−");
            minimize.SetBounds(494, 26, 38, 32);
            minimize.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            minimize.Click += delegate { Hide(); };
            header.Controls.Add(minimize);
            HeaderButton close = new HeaderButton("×");
            close.SetBounds(540, 26, 38, 32);
            close.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            close.Click += delegate { Hide(); };
            header.Controls.Add(close);
            header.Layout+=delegate
            {
                int right=header.ClientSize.Width-20;
                close.SetBounds(right-44,18,44,35);
                minimize.SetBounds(right-94,18,40,35);
                pin.SetBounds(right-159,18,57,35);
                glass.SetBounds(right-235,18,68,35);
            };
            updateHeader=delegate
            {
                bool custom=ThemeSelection.Current=="custom";
                title.Text=custom?"账户总览":ThemeSelection.Name;
                themeCaption.Text=custom?"自定义背景 · 随心而换":ThemeSelection.Subtitle;
                title.ForeColor=brand.ForeColor=themeCaption.ForeColor=custom?Color.FromArgb(63,45,104):Color.White;
            };
            updateHeader();
            foreach (Control drag in new Control[] { header, brand, title, themeCaption })
            {
                drag.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Native.BeginWindowDrag(Handle); };
                drag.DoubleClick += delegate { WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; };
            }
            Controls.Add(header);

            detail = new DetailView { Dock = DockStyle.Bottom, Height = 222 };
            body.Controls.Add(detail);
            list = new GlassFlowPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Palette.Background, Padding = new Padding(0, 0, 0, 8) };
            body.Controls.Add(list);
            list.BringToFront();

            Panel section = new GlassPanel { Dock = DockStyle.Top, Height = 38, BackColor = Palette.Background };
            count = Palette.Label("已登记账户", 15, Palette.Ink, true);
            count.Dock = DockStyle.Left;
            count.Width = 220;
            section.Controls.Add(count);
            displayMode=new DisplayModeSwitch {Detailed=detailedMode};
            displayMode.SetBounds(section.ClientSize.Width-156,3,156,30);
            displayMode.Anchor=AnchorStyles.Top|AnchorStyles.Right;
            displayMode.SelectionChanged+=delegate
            {
                detailedMode=displayMode.Detailed;
                SaveDisplayMode();
                Rebuild();
            };
            section.Controls.Add(displayMode);
            body.Controls.Add(section);

            Panel toolbar = new GlassPanel { Dock = DockStyle.Top, Height = 142, BackColor = Palette.Background };
            refreshSummary=new RefreshBanner {Text="点击刷新额度，逐个显示更新结果",Font=Palette.Font(10.5f,true),ForeColor=Color.White,TextAlign=ContentAlignment.MiddleLeft};
            refreshSummary.SetBounds(1,104,toolbar.ClientSize.Width-2,33);
            refreshSummary.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top;
            refreshSummary.Padding=new Padding(12,0,0,0);
            toolbar.Controls.Add(refreshSummary);
            ModuleButton add = new ModuleButton("添加账户","person");
            add.Location = new Point(0, 6);
            add.Click += delegate { context.BeginNewLogin(); };
            toolbar.Controls.Add(add);
            ModuleButton import = new ModuleButton("AI 小铺","shop");
            import.Location = new Point(124, 6);
            import.Click += delegate { AiShop.Open(this); };
            toolbar.Controls.Add(import);
            refresh = new ModuleButton("刷新额度","refresh");
            refresh.Location = new Point(236, 6);
            refresh.Click += async delegate { await context.RefreshAllAsync(true); };
            toolbar.Controls.Add(refresh);
            ModuleButton daily = new ModuleButton("AI 日报","news");
            daily.Click += delegate { DailyNews.Open(this); };
            toolbar.Controls.Add(daily);
            ModuleButton monitor = new ModuleButton("重置 / IP 查询","bell");
            monitor.Click += delegate { ResetPanel.Open(this,store); };
            toolbar.Controls.Add(monitor);
            ModuleButton stagger = new ModuleButton("错峰计划","calendar");
            stagger.Click += delegate { StaggerPlan.Open(this,store); };
            toolbar.Controls.Add(stagger);
            toolbar.Resize += delegate
            {
                ModuleButton[] first = { add, import, refresh };
                ModuleButton[] second = { daily, monitor, stagger };
                int gap=8, width=Math.Max(1,(toolbar.ClientSize.Width-gap*2)/3);
                for(int i=0;i<3;i++)
                {
                    first[i].SetBounds(i*(width+gap),3,width,43);
                    second[i].SetBounds(i*(width+gap),53,width,43);
                }
            };
            body.Controls.Add(toolbar);
            overview = new SummaryView { Dock = DockStyle.Top, Height = 105 };
            body.Controls.Add(overview);

            list.Resize += delegate { ResizeCards(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!AllowExit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { if (toolsView != null) CloseTools(); else Hide(); e.Handled = true; } };
            foreach(Control legacy in Controls)legacy.Visible=false;
            Padding=Padding.Empty;
            AutoScaleMode=AutoScaleMode.None;
            referenceSurface=new ReferenceSurface(this,store,context) {Dock=DockStyle.Fill};
            Controls.Add(referenceSurface);
            referenceSurface.BringToFront();
            Rectangle available=Screen.FromPoint(Cursor.Position).WorkingArea;
            Size initialSize=DpiSupport.FitWindow(available,DpiSupport.DpiForPoint(Cursor.Position));
            MinimumSize=new Size(Math.Min(280,initialSize.Width),Math.Min(420,initialSize.Height));
            ClientSize=initialSize;
            referenceSurface.FitContent();
            IntPtr initial = Handle;
        }

        private static RoundButton Button(string text, Color background, Color foreground, int width)
        {
            RoundButton b = RoundButton.Create(text, background, foreground, 14);
            b.Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold);
            b.Size = new Size(width, 36);
            return b;
        }

        private bool LoadDisplayMode()
        {
            if(store.IsPreview) return Environment.GetCommandLineArgs().Contains("--detailed-preview");
            try
            {
                string path=Path.Combine(PortablePaths.Data,"display-mode.txt");
                return !File.Exists(path) || string.Equals(File.ReadAllText(path).Trim(),"detailed",StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void SaveDisplayMode()
        {
            if(store.IsPreview) return;
            try
            {
                Directory.CreateDirectory(PortablePaths.Data);
                File.WriteAllText(Path.Combine(PortablePaths.Data,"display-mode.txt"),detailedMode?"detailed":"compact");
            }
            catch(Exception ex) { status.Text="显示模式未能保存："+ex.Message; }
        }

        private bool changingBackground;
        private async void ChangeBackground(bool restore)
        {
            if(changingBackground)return;
            try
            {
                string selected=null;
                if(!restore)using(OpenFileDialog dialog=new OpenFileDialog())
                {
                    dialog.Title="选择高清背景（MP4视频或图片）";
                    dialog.Filter="背景文件|*.mp4;*.jpg;*.jpeg;*.png;*.bmp;*.gif|高清MP4视频（H.264）|*.mp4|静态图片|*.jpg;*.jpeg;*.png;*.bmp;*.gif";
                    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                    selected=dialog.FileName;
                }
                changingBackground=true;UseWaitCursor=true;
                if(restore)await DynamicBackground.RestoreAsync();
                else
                {
                    await DynamicBackground.ImportAsync(selected);
                }
                if(IsDisposed)return;
                ApplyTheme("custom");
            }
            catch(Exception ex){if(!IsDisposed)MessageBox.Show(this,"无法更换背景，原背景保持不变："+ex.Message+"\n建议使用300MB以内、H.264编码的MP4视频。","背景设置",MessageBoxButtons.OK,MessageBoxIcon.Information);}
            finally{changingBackground=false;if(!IsDisposed)UseWaitCursor=false;}
        }

        private void ApplyTheme(string id)
        {
            try
            {
                ThemeSelection.Select(id,!store.IsPreview);
                themeCaption.Text=ThemeSelection.Name+"  /  "+ThemeSelection.Subtitle;
                Palette.UpdateControls(this);
                if(updateHeader!=null) updateHeader();
                DailyNews.RefreshAppearance();
                SidePanels.RefreshAppearance();
                Rebuild(); Invalidate(true);
            }
            catch(Exception ex) { MessageBox.Show(this,"主题未能保存："+ex.Message,"主题"); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DashboardNative.DarkCaption(Handle);
            Native.EnableRoundedCorners(Handle);
            UpdateGlassOutline();
            registeredHandle = Handle;
            // Preview instances never steal the user's shortcut.
            if (store != null && !store.IsPreview) registered = Native.RegisterHotKey(Handle, HotkeyId, 0x4003, (int)Keys.D);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (registered) Native.UnregisterHotKey(registeredHandle, HotkeyId);
            registered = false;
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x02E0 && referenceSurface!=null)
            {
                int dpi=(int)(m.WParam.ToInt64()&0xffff);
                Rectangle proposed=Bounds;
                if(m.LParam!=IntPtr.Zero)
                {
                    int left=Marshal.ReadInt32(m.LParam,0),top=Marshal.ReadInt32(m.LParam,4);
                    int right=Marshal.ReadInt32(m.LParam,8),bottom=Marshal.ReadInt32(m.LParam,12);
                    if(right>left && bottom>top)proposed=Rectangle.FromLTRB(left,top,right,bottom);
                }
                FitReferenceDisplay(Screen.FromRectangle(proposed).WorkingArea,dpi,proposed.Location);
                m.Result=IntPtr.Zero;
                return;
            }
            if ((m.Msg == 0x0312 && m.WParam.ToInt32() == HotkeyId) || m.Msg == DashboardNative.OpenMessage)
            {
                context.TogglePopup(Cursor.Position);
                return;
            }
            base.WndProc(ref m);
            if(m.Msg==0x007E && referenceSurface!=null && IsHandleCreated && !IsDisposed)
                BeginInvoke((Action)delegate {if(!IsDisposed)FitReferenceDisplay(Screen.FromControl(this).WorkingArea,DpiSupport.DpiForWindow(Handle),Location);});
            if (m.Msg == 0x0084 && WindowState == FormWindowState.Normal)
            {
                long packed = m.LParam.ToInt64();
                Point point = PointToClient(new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF)));
                bool left = point.X < 6, right = point.X >= ClientSize.Width - 6;
                bool top = point.Y < 6, bottom = point.Y >= ClientSize.Height - 6;
                if (top && left) m.Result = new IntPtr(13);
                else if (top && right) m.Result = new IntPtr(14);
                else if (bottom && left) m.Result = new IntPtr(16);
                else if (bottom && right) m.Result = new IntPtr(17);
                else if (left) m.Result = new IntPtr(10);
                else if (right) m.Result = new IntPtr(11);
                else if (top) m.Result = new IntPtr(12);
                else if (bottom) m.Result = new IntPtr(15);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // ReferenceSurface paints the one visible rim above its artboard.
            // A second form-level stroke was hidden in the straight sections
            // but leaked through at the rounded corners as a dashed double edge.
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateGlassOutline();
        }

        private void UpdateGlassOutline()
        {
            if (ClientSize.Width < 60 || ClientSize.Height < 60) return;
            // Rebuild only on size changes, never during window movement.
            using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(ClientRectangle, ReferenceGlass.WindowRadius(ClientSize)))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { GlassBackdrop.Draw(e.Graphics, this); }

        internal static Size ReferenceWindowSize(Rectangle area)
        {
            double fit=Math.Max(1d/1536,Math.Min(.56d,Math.Min((area.Width-24)/1024d,(area.Height-24)/1536d)));
            return new Size(Math.Max(1,(int)Math.Round(1024*fit)),Math.Max(1,(int)Math.Round(1536*fit)));
        }

        private void FitReferenceDisplay(Rectangle area,int dpi,Point preferred)
        {
            Size size=DpiSupport.FitWindow(area,dpi);
            MinimumSize=new Size(Math.Min(280,size.Width),Math.Min(420,size.Height));
            MaximizedBounds=area;
            Bounds=new Rectangle(new Point(Math.Max(area.Left,Math.Min(preferred.X,area.Right-size.Width)),Math.Max(area.Top,Math.Min(preferred.Y,area.Bottom-size.Height))),size);
            UpdateGlassOutline();
            if(referenceSurface!=null)referenceSurface.RefreshView();
        }

        public void ShowNear(Point anchor)
        {
            Rectangle area = Screen.FromPoint(anchor).WorkingArea;
            MaximizedBounds = area;
            if (!Visible)
            {
                Size size=DpiSupport.FitWindow(area,DpiSupport.DpiForPoint(anchor));
                MinimumSize = new Size(Math.Min(280, size.Width), Math.Min(420, size.Height));
                Size = size;
                if(referenceSurface!=null)referenceSurface.FitContent();
                Location = new Point(area.Right - Width - 6, Math.Max(area.Top + 6, area.Top + (area.Height - Height) / 2));
            }
            WindowState = FormWindowState.Normal;
            Show();
            BringToFront();
            Activate();
            // Foreground once, without permanently covering other applications.
            DashboardNative.Activate(Handle);
        }

        public void SetBusy(bool value)
        {
            busy = value;
            refresh.Enabled = !value;
            UpdateFooter();
        }

        public void BeginQuotaRefresh(AccountMeta[] accounts)
        {
            refreshStates.Clear(); refreshFinished=false; refreshSaved=true;
            foreach(AccountMeta account in accounts) refreshStates[account.Id]="waiting";
            UpdateQuotaFeedback();
        }

        public void SetQuotaRefreshState(string id,string state)
        {
            refreshStates[id]=state;
            UpdateQuotaFeedback();
        }

        public void FinishQuotaRefresh(bool saved)
        {
            refreshFinished=true; refreshSaved=saved;
            UpdateQuotaFeedback();
        }

        private void UpdateQuotaFeedback()
        {
            int ok=refreshStates.Values.Count(delegate(string state){return state=="success";});
            int failed=refreshStates.Values.Count(delegate(string state){return state=="failed";});
            int pending=refreshStates.Values.Count(delegate(string state){return state=="partial";});
            int total=refreshStates.Count;
            refreshSummary.Text=total==0 ? "没有已登记账号，请先添加或登记当前账号" :
                (refreshFinished ? "本次完成" : "正在更新 "+(ok+pending+failed)+"/"+total)+"  ·  成功 "+ok+(pending>0?"  ·  待刷新 "+pending:"")+"  ·  失败 "+failed;
            if(refreshFinished && !refreshSaved) refreshSummary.Text+="  ·  保存失败";
            foreach(AccountRow row in list.Controls.OfType<AccountRow>())
            {
                string state;
                row.SetRefreshState(refreshStates.TryGetValue(row.AccountId,out state)?state:"");
            }
            overview.UpdateAccounts(store.Settings.Accounts);
            detail.Invalidate();
            if(referenceSurface!=null)referenceSurface.RefreshView();
        }

        private void UpdateFooter()
        {
            status.Text = busy ? "正在逐个查询账户，请稍候…" : registered ? "Ctrl + Alt + D 打开  ·  关闭窗口后驻留托盘" : store.IsPreview ? "预览数据 · 不读取真实账户" : "快捷键被占用，可从托盘或桌面图标打开";
            if(referenceSurface!=null)referenceSurface.RefreshView();
        }

        private void ResizeCards()
        {
            bool shortWindow=ClientSize.Height<740;
            overview.Height=shortWindow?76:86;
            detail.Height=shortWindow?82:105;
            int width = Math.Max(100, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 3);
            foreach (Control child in list.Controls) child.Width = width;
        }

        public void Rebuild()
        {
            if (InvokeRequired) { BeginInvoke(new Action(Rebuild)); return; }
            string active = store.ActiveId();
            List<AccountMeta> accounts = store.Settings.Accounts;
            if (!accounts.Any(delegate(AccountMeta a) { return a.Id == selected; })) selected = accounts.Any(delegate(AccountMeta a) { return a.Id == active; }) ? active : accounts.Select(delegate(AccountMeta a) { return a.Id; }).FirstOrDefault();
            overview.UpdateAccounts(accounts);
            count.Text = "已登记账户  /  " + accounts.Count;
            Point scroll = list.AutoScrollPosition;
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToArray()) { list.Controls.Remove(c); c.Dispose(); }
            if (accounts.Count == 0)
            {
                Label empty = Palette.Label("从这里开始\n\n登记 Codex 当前账户，或点击「添加账户」。\n其他电脑首次使用时，需要在当地重新登录。", 11, Palette.Muted, false);
                empty.TextAlign = ContentAlignment.MiddleCenter;
                empty.Height = 200;
                list.Controls.Add(empty);
            }
            for (int i = 0; i < accounts.Count; i++)
            {
                AccountMeta account = accounts[i];
                AccountRow row = new AccountRow(account, i, account.Id == active, account.Id == selected,detailedMode);
                string refreshState;
                row.SetRefreshState(refreshStates.TryGetValue(account.Id,out refreshState)?refreshState:"");
                row.SelectAccount += delegate { selected = account.Id; Rebuild(); };
                row.SwitchAccount += delegate { context.SwitchAccount(account); };
                row.DeleteAccount += delegate { context.DeleteAccount(account); };
                row.EditExpiry += delegate { EditExpiry(account); };
                row.OpenTools += delegate { OpenTools(account); };
                list.Controls.Add(row);
            }
            ResizeCards();
            list.ResumeLayout();
            list.AutoScrollPosition = new Point(0, -scroll.Y);
            detail.UpdateAccount(accounts.FirstOrDefault(delegate(AccountMeta a) { return a.Id == selected; }), selected == active);
            UpdateFooter();
        }

        private void OpenTools(AccountMeta account)
        {
            if (toolsView != null) CloseTools();
            toolsView = new AccountToolsView(store, account, delegate { if (!IsDisposed) Rebuild(); }, CloseTools, delegate { RoundedAppearanceMenu.ShowInside(referenceAppearance,this,Cursor.Position); },context==null?null:new Action<AccountMeta>(context.SwitchAccount)) { Dock = DockStyle.Fill };
            referenceSurface.Visible = false;
            ClientSize=DpiSupport.FitWindow(Screen.FromControl(this).WorkingArea,DpiSupport.DpiForPoint(Location));
            Controls.Add(toolsView); toolsView.BringToFront();
        }

        private void CloseTools()
        {
            AccountToolsView old = toolsView; toolsView = null;
            if (old != null) { Controls.Remove(old); old.Dispose(); }
            if (referenceSurface != null && !referenceSurface.IsDisposed) { referenceSurface.Visible = true; referenceSurface.BringToFront(); referenceSurface.FitContent(); }
        }

        private void ShowResetCredits(AccountMeta account) {
            var menu=new RoundedAppearanceMenu {Font=Palette.Font(11,false),Renderer=new BorderlessMenuRenderer(),ShowImageMargin=false,ForeColor=Color.White,BackColor=ThemeVisual.Current.PanelBottom};
            menu.Items.Add("重置卡 · "+(account.ResetCredits<0?"待查询":account.ResetCredits+" 次"));
            menu.Items.Add(new ToolStripSeparator());
            var cards=account.ResetCreditDetails??new List<ResetCreditInfo>();
            if(cards.Count==0)menu.Items.Add(account.ResetCredits==0?"暂无可用重置卡":"点击刷新额度，读取官方有效期");
            foreach(var c in cards){menu.Items.Add(c.Summary()+" · "+c.LocalExpiry());DateTimeOffset d;if(DateTimeOffset.TryParse(c.GrantedAt,out d))menu.Items.Add("发放："+d.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));}
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("到期前 3 天 / 1 天 / 1 小时提醒");
            menu.Items.Add("应用运行时提醒；时间按本机时区显示");
            DateTimeOffset queried;menu.Items.Add("上次查询："+(DateTimeOffset.TryParse(account.LastQuotaAt,out queried)?queried.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):"尚未读取"));
            menu.Closed+=delegate {BeginInvoke(new Action(menu.Dispose));};RoundedAppearanceMenu.ShowInside(menu,this,Cursor.Position);
        }
        private void ShowAccountMenu(AccountMeta account)
        {
            var menu = ThemeMenuColors.AccountMenu();
            menu.Items.Add("账号工具…", null, delegate { OpenTools(account); });
            menu.Items.Add("设置订阅到期日…", null, delegate { EditExpiry(account); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("删除账号…", null, delegate { context.DeleteAccount(account); });
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            RoundedAppearanceMenu.ShowInside(menu,this,Cursor.Position);
        }

        private void EditExpiry(AccountMeta account)
        {
            using (ExpiryGlassDialog dialog = new ExpiryGlassDialog(account))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string old = account.SubscriptionExpiresOn;
                try { account.SubscriptionExpiresOn = dialog.Value; store.Save(); Rebuild(); }
                catch (Exception ex) { account.SubscriptionExpiresOn = old; MessageBox.Show(this, "保存失败：" + ex.Message); }
            }
        }

        internal static bool RunExpiryDateTimeDiagnostic(string screenshotPath, out string details)
        {
            return ExpiryGlassDialog.Diagnostic(screenshotPath, out details);
        }

        // Shared by the stagger-plan date field so both features use the same
        // glass calendar and the same already-diagnosed date interaction model.
        internal static Form CreateDateCalendarPopup(DateTime initial,Action<DateTime,bool> completed)
        {
            ExpiryGlassDialog.CalendarPopup popup=new ExpiryGlassDialog.CalendarPopup(initial);
            popup.FormClosed+=delegate{if(completed!=null)completed(popup.SelectedDate,popup.Accepted);};
            return popup;
        }

        private sealed class ExpiryGlassDialog : Form
        {
            private readonly string email;
            private readonly ExpiryDateTimeEditor editor;
            private readonly DialogGlassButton save, clear, cancel;
            internal string Value { get; private set; }

            internal ExpiryGlassDialog(AccountMeta account)
            {
                email = account.Email;
                Text = "订阅到期时间";
                AutoScaleMode = AutoScaleMode.None;
                ClientSize = new Size(460, 330);
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false;
                DoubleBuffered = true;
                KeyPreview = true;
                UpdateOutline();

                DateTime initial; bool hasTime;
                if (!ExpiryDateTimeValue.TryParse(account.SubscriptionExpiresOn, out initial, out hasTime))
                    initial = DateTime.Today.AddHours(23).AddMinutes(59);
                else if (!hasTime)
                    initial = initial.Date.AddHours(23).AddMinutes(59);

                editor = new ExpiryDateTimeEditor(this, initial) { Bounds = new Rectangle(25, 132, 410, 62) };
                clear = new DialogGlassButton(this) { Text = "设为未知", Bounds = new Rectangle(34, 270, 114, 42) };
                cancel = new DialogGlassButton(this) { Text = "取消", Bounds = new Rectangle(158, 270, 114, 42) };
                save = new DialogGlassButton(this) { Text = "保存时间", Bounds = new Rectangle(286, 270, 140, 42), Emphasized = true };
                save.Click += delegate { Commit(false, true); };
                clear.Click += delegate { Commit(true, true); };
                cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
                Controls.AddRange(new Control[] { editor, save, clear, cancel });
                AcceptButton = save;
                CancelButton = cancel;
                MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Y < 110) Native.BeginWindowDrag(Handle); };
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                UpdateOutline();
            }

            private void UpdateOutline()
            {
                if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
                using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(ClientRectangle, 24))
                {
                    Region previous = Region;
                    Region = new Region(path);
                    if (previous != null) previous.Dispose();
                }
            }

            private void Commit(bool unknown, bool close)
            {
                Value = unknown ? string.Empty : ExpiryDateTimeValue.Serialize(editor.Value);
                DialogResult = DialogResult.OK;
                if (close) Close();
            }

            internal static bool Diagnostic(string screenshotPath, out string details)
            {
                string parserDetails;
                bool ok = ExpiryDateTimeValue.SelfTest(out parserDetails);
                AccountMeta synthetic = new AccountMeta { Email = "demo.account@example.com", SubscriptionExpiresOn = "2027-03-23" };
                using (ExpiryGlassDialog preview = new ExpiryGlassDialog(synthetic))
                using (Bitmap image = new Bitmap(preview.ClientSize.Width, preview.ClientSize.Height))
                {
                    preview.StartPosition = FormStartPosition.Manual;
                    preview.Location = new Point(-20000, -20000);
                    preview.Show();
                    Application.DoEvents();
                    preview.DrawToBitmap(image, preview.ClientRectangle);
                    preview.Hide();
                    if (!string.IsNullOrEmpty(screenshotPath)) image.Save(screenshotPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                using (ExpiryGlassDialog saved = new ExpiryGlassDialog(synthetic))
                {
                    saved.editor.Value = new DateTime(2027, 3, 23, 18, 45, 0);
                    saved.save.ReferenceClick();
                    ok = ok && saved.Value == "2027-03-23 18:45";
                }
                using (ExpiryGlassDialog unknown = new ExpiryGlassDialog(synthetic))
                {
                    unknown.clear.ReferenceClick();
                    ok = ok && unknown.Value == string.Empty;
                }
                using (ExpiryGlassDialog cancelled = new ExpiryGlassDialog(synthetic))
                {
                    cancelled.cancel.ReferenceClick();
                    ok = ok && cancelled.Value == null && cancelled.DialogResult == DialogResult.Cancel;
                }
                using (ExpiryGlassDialog interactions = new ExpiryGlassDialog(synthetic))
                {
                    interactions.editor.Value = new DateTime(2027, 3, 23, 18, 45, 0);
                    interactions.editor.ReferenceSelectPart(1);
                    interactions.editor.ReferenceArrow(true, true);
                    ok = ok && interactions.editor.Value == new DateTime(2027, 3, 23, 19, 45, 0);
                    bool downHandled = interactions.editor.ReferenceProcessKey(Keys.Down);
                    ok = ok && downHandled && interactions.editor.Value == new DateTime(2027, 3, 23, 18, 45, 0);
                    interactions.StartPosition = FormStartPosition.Manual; interactions.Location = new Point(-20000, -20000); interactions.Show(); Application.DoEvents();
                    interactions.editor.ReferenceSelectPart(0);
                    bool enterHandled = interactions.editor.ReferenceProcessKey(Keys.Enter); Application.DoEvents();
                    ok = ok && enterHandled && interactions.editor.ReferenceCalendarVisible && interactions.Value == null;
                    interactions.editor.ReferenceCloseCalendar(); interactions.Hide();
                    interactions.editor.ReferenceCalendarResult(new DateTime(2027, 4, 6), true);
                    ok = ok && interactions.editor.Value == new DateTime(2027, 4, 6, 18, 45, 0);
                    DateTime beforeCancel = interactions.editor.Value;
                    interactions.editor.ReferenceCalendarResult(new DateTime(2028, 1, 1), false);
                    ok = ok && interactions.editor.Value == beforeCancel;
                    interactions.editor.Value = DateTime.MaxValue;
                    interactions.editor.ReferenceAdjust(60);
                    ok = ok && interactions.editor.Value == new DateTime(9999, 12, 31, 23, 59, 0);
                    interactions.editor.Value = DateTime.MinValue;
                    interactions.editor.ReferenceAdjust(-1);
                    ok = ok && interactions.editor.Value == DateTime.MinValue;
                }
                using (CalendarPopup escapePopup = new CalendarPopup(new DateTime(2027, 3, 23)))
                {
                    escapePopup.StartPosition = FormStartPosition.Manual; escapePopup.Location = new Point(-20000, -20000); escapePopup.Show(); Application.DoEvents();
                    escapePopup.ReferenceKey(Keys.Escape);
                    ok = ok && !escapePopup.Accepted && !escapePopup.Visible;
                }
                using (CalendarPopup outsidePopup = new CalendarPopup(new DateTime(2027, 3, 23)))
                {
                    outsidePopup.StartPosition = FormStartPosition.Manual; outsidePopup.Location = new Point(-20000, -20000); outsidePopup.Show(); Application.DoEvents();
                    outsidePopup.ReferenceExternalDeactivate();
                    ok = ok && !outsidePopup.Accepted && !outsidePopup.Visible;
                }
                using (CalendarPopup boundaryPopup = new CalendarPopup(DateTime.MinValue))
                {
                    boundaryPopup.ReferenceShiftMonth(-1);
                    ok = ok && boundaryPopup.ReferenceVisibleMonth == new DateTime(1, 1, 1);
                    boundaryPopup.ReferenceWheel(120);
                    ok = ok && boundaryPopup.ReferenceVisibleMonth == new DateTime(1, 1, 1);
                    boundaryPopup.ReferenceSetVisibleMonth(new DateTime(9999, 12, 1)); boundaryPopup.ReferenceShiftMonth(1);
                    ok = ok && boundaryPopup.ReferenceVisibleMonth == new DateTime(9999, 12, 1);
                    boundaryPopup.ReferenceWheel(-120);
                    ok = ok && boundaryPopup.ReferenceVisibleMonth == new DateTime(9999, 12, 1);
                }
                string gridDetails;
                using (CalendarPopup gridPopup = new CalendarPopup(new DateTime(2027, 3, 23)))
                    ok = gridPopup.ReferenceValidateGridMappings(out gridDetails) && ok;
                if (!string.IsNullOrEmpty(screenshotPath))
                {
                    string directory = Path.GetDirectoryName(screenshotPath);
                    string calendarPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(screenshotPath) + "-calendar.png");
                    using (CalendarPopup popup = new CalendarPopup(new DateTime(2027, 3, 23))) popup.SaveDiagnosticScreenshot(calendarPath);
                }
                details = parserDetails + "；" + gridDetails + (ok ? "；按钮保存/未知/取消、真实ProcessCmdKey方向键与日期Enter、日历保留时分/取消不改值、键盘滚轮边界及两张截图通过" : "；对话框交互状态测试失败");
                return ok;
            }

            private void PaintSurface(Graphics graphics, bool drawOutline)
            {
                GraphicsState saved=graphics.Save();
                graphics.SmoothingMode=SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
                GlassBackdrop.DrawReferenceBackground(graphics, ClientRectangle);
                using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 24))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(220, ThemeVisual.Current.PanelBottom)))
                using (Pen rim = new Pen(ThemeVisual.Current.Rim, 1.2f))
                {
                    graphics.FillPath(brush, path);
                    if(drawOutline){rim.Alignment=PenAlignment.Inset;graphics.DrawPath(rim, path);}
                }
                graphics.Restore(saved);
            }

            private void PaintChildBackdrop(Graphics graphics, Control child)
            {
                GraphicsState saved = graphics.Save();
                graphics.TranslateTransform(-child.Left, -child.Top);
                // Child controls need the same glass pixels behind them, but
                // must not each repaint a translated fragment of the window rim.
                PaintSurface(graphics, false);
                graphics.Restore(saved);
            }

            protected override void OnPaintBackground(PaintEventArgs e) { PaintSurface(e.Graphics, true); }

            protected override void OnPaint(PaintEventArgs e)
            {
                using (Font title = new Font("楷体", 28, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font body = new Font("楷体", 17, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush ink = new SolidBrush(Color.White))
                {
                    e.Graphics.DrawString("订阅到期时间", title, ink, 26, 21);
                    AppTypography.Draw(e.Graphics, email, 17, FontStyle.Bold, new RectangleF(28, 65, 404, 25), Color.White, StringAlignment.Near, true);
                    e.Graphics.DrawString("本机记录 · 按账单下次续费或到期时间填写", body, ink, 28, 102);
                    e.Graphics.DrawString("点击日期打开日历；时间可用箭头、滚轮或键盘调整", body, ink, 28, 210);
                    e.Graphics.DrawString("旧日期记录继续兼容；未知不代表没有订阅", body, ink, 28, 235);
                }
            }

            private sealed class DialogGlassButton : Button
            {
                private readonly ExpiryGlassDialog owner;
                private bool hover, pressed;
                internal bool Emphasized { get; set; }
                internal DialogGlassButton(ExpiryGlassDialog owner)
                {
                    this.owner = owner;
                    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
                    FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
                    Font = new Font("楷体", 17, FontStyle.Bold, GraphicsUnit.Pixel); ForeColor = Color.White; Cursor = Cursors.Hand;
                }
                protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
                protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
                protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
                protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
                protected override void OnPaintBackground(PaintEventArgs e) { }
                protected override void OnPaint(PaintEventArgs e)
                {
                    owner.PaintChildBackdrop(e.Graphics, this);
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle box = new Rectangle(2, 2, Width - 5, Height - 5);
                    Color top = Emphasized ? ThemeVisual.Blend(ThemeVisual.Current.ButtonTop, ThemeVisual.Current.Accent, .48f) : ThemeVisual.Current.ButtonTop;
                    Color bottom = Emphasized ? ThemeVisual.Blend(ThemeVisual.Current.ButtonBottom, ThemeVisual.Current.Accent, .42f) : ThemeVisual.Current.ButtonBottom;
                    if (hover) top = ThemeVisual.Blend(top, Color.White, .15f);
                    if (pressed) { top = ThemeVisual.Blend(top, Color.Black, .12f); bottom = ThemeVisual.Blend(bottom, Color.Black, .16f); }
                    using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(box, 15))
                    using (LinearGradientBrush fill = new LinearGradientBrush(box, top, bottom, 90f))
                    using (Pen rim = new Pen(Color.FromArgb(230, Color.White), 1.1f))
                    { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(rim, path); }
                    TextRenderer.DrawText(e.Graphics, Text, Font, box, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }
                internal void ReferenceClick() { OnClick(EventArgs.Empty); }
            }

            private sealed class ExpiryDateTimeEditor : Control
            {
                private readonly ExpiryGlassDialog owner;
                private int selectedPart;
                private DateTime value;
                private CalendarPopup calendarPopup;
                internal DateTime Value { get { return value; } set { this.value = value; Invalidate(); } }
                private Rectangle DateBox { get { return new Rectangle(3, 3, 222, Height - 7); } }
                private Rectangle HourBox { get { return new Rectangle(232, 3, 82, Height - 7); } }
                private Rectangle MinuteBox { get { return new Rectangle(325, 3, 82, Height - 7); } }
                internal ExpiryDateTimeEditor(ExpiryGlassDialog owner, DateTime value)
                {
                    this.owner = owner; this.value = value; TabStop = true; Cursor = Cursors.Hand;
                    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque | ControlStyles.Selectable, true);
                    AccessibleName = "订阅到期日期和时间";
                }
                protected override void OnPaintBackground(PaintEventArgs e) { }
                protected override void OnPaint(PaintEventArgs e)
                {
                    owner.PaintChildBackdrop(e.Graphics, this);
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawSegment(e.Graphics, DateBox, value.ToString("yyyy-MM-dd"), selectedPart == 0, null);
                    DrawSegment(e.Graphics, HourBox, value.ToString("HH"), selectedPart == 1, "时");
                    DrawSegment(e.Graphics, MinuteBox, value.ToString("mm"), selectedPart == 2, "分");
                    using (Font colon = new Font("Times New Roman", 22, FontStyle.Bold, GraphicsUnit.Pixel))
                        TextRenderer.DrawText(e.Graphics, ":", colon, new Rectangle(314, 2, 12, Height - 5), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                private void DrawSegment(Graphics g, Rectangle box, string text, bool selected, string suffix)
                {
                    Color fillColor = ThemeVisual.Blend(ThemeVisual.Current.PanelTop, ThemeVisual.Current.ButtonTop, selected ? .50f : .30f);
                    using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(box, 14))
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(225, fillColor)))
                    using (Pen rim = new Pen(selected ? ThemeVisual.Current.Accent : Color.FromArgb(210, Color.White), selected ? 1.6f : 1.0f))
                    { g.FillPath(fill, path); g.DrawPath(rim, path); }
                    bool isDate = suffix == null;
                    using (Font textFont = new Font("Times New Roman", isDate ? 22 : 21, FontStyle.Bold, GraphicsUnit.Pixel))
                        TextRenderer.DrawText(g, text, textFont, isDate ? new Rectangle(box.X + 10, box.Y, box.Width - 56, box.Height) : new Rectangle(box.X + 4, box.Y, 40, box.Height), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    if (isDate)
                    {
                        DrawCalendarGlyph(g, new Rectangle(box.Right - 38, box.Y + (box.Height - 22) / 2, 22, 22));
                    }
                    else
                    {
                        using (Font suffixFont = new Font("楷体", 18, FontStyle.Bold, GraphicsUnit.Pixel))
                            TextRenderer.DrawText(g, suffix, suffixFont, new Rectangle(box.X + 43, box.Y, 20, box.Height), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        using (Pen arrow = new Pen(Color.FromArgb(225, Color.White), 1.4f))
                        {
                            int x = box.Right - 10;
                            g.DrawLines(arrow, new[] { new Point(x - 4, box.Top + 18), new Point(x, box.Top + 14), new Point(x + 4, box.Top + 18) });
                            g.DrawLines(arrow, new[] { new Point(x - 4, box.Bottom - 18), new Point(x, box.Bottom - 14), new Point(x + 4, box.Bottom - 18) });
                        }
                    }
                }
                private static void DrawCalendarGlyph(Graphics graphics, Rectangle box)
                {
                    using (Pen pen = new Pen(Color.White, 1.7f))
                    {
                        pen.LineJoin = LineJoin.Round;
                        Rectangle body = new Rectangle(box.X + 2, box.Y + 4, box.Width - 5, box.Height - 7);
                        graphics.DrawRectangle(pen, body);
                        graphics.DrawLine(pen, body.Left, body.Top + 5, body.Right, body.Top + 5);
                        graphics.DrawLine(pen, body.Left + 4, body.Top - 2, body.Left + 4, body.Top + 3);
                        graphics.DrawLine(pen, body.Right - 4, body.Top - 2, body.Right - 4, body.Top + 3);
                        graphics.FillRectangle(Brushes.White, body.Left + 4, body.Top + 8, 3, 3);
                        graphics.FillRectangle(Brushes.White, body.Left + 10, body.Top + 8, 3, 3);
                    }
                }
                protected override void OnMouseDown(MouseEventArgs e)
                {
                    Focus();
                    if (DateBox.Contains(e.Location)) { selectedPart = 0; ShowCalendar(); }
                    else if (HourBox.Contains(e.Location)) { selectedPart = 1; if (e.X > HourBox.Right - 22) Adjust(e.Y < HourBox.Top + HourBox.Height / 2 ? 60 : -60); }
                    else if (MinuteBox.Contains(e.Location)) { selectedPart = 2; if (e.X > MinuteBox.Right - 22) Adjust(e.Y < MinuteBox.Top + MinuteBox.Height / 2 ? 1 : -1); }
                    Invalidate(); base.OnMouseDown(e);
                }
                protected override void OnMouseWheel(MouseEventArgs e) { if (selectedPart == 1) Adjust(e.Delta > 0 ? 60 : -60); else if (selectedPart == 2) Adjust(e.Delta > 0 ? 1 : -1); base.OnMouseWheel(e); }
                protected override void OnKeyDown(KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) { selectedPart = Math.Max(0, Math.Min(2, selectedPart + (e.KeyCode == Keys.Right ? 1 : -1))); Invalidate(); e.Handled = true; }
                    else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) { if (selectedPart == 0) Value = SafeAddDays(Value, e.KeyCode == Keys.Up ? 1 : -1); else Adjust((e.KeyCode == Keys.Up ? 1 : -1) * (selectedPart == 1 ? 60 : 1)); e.Handled = true; }
                    else if (e.KeyCode == Keys.Enter && selectedPart == 0) { ShowCalendar(); e.Handled = true; }
                    base.OnKeyDown(e);
                }
                protected override bool IsInputKey(Keys keyData)
                {
                    Keys key = keyData & Keys.KeyCode;
                    if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || (key == Keys.Enter && selectedPart == 0)) return true;
                    return base.IsInputKey(keyData);
                }
                protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
                {
                    Keys key = keyData & Keys.KeyCode;
                    if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || (key == Keys.Enter && selectedPart == 0))
                    {
                        KeyEventArgs args = new KeyEventArgs(keyData);
                        OnKeyDown(args);
                        if (args.Handled) return true;
                    }
                    return base.ProcessCmdKey(ref msg, keyData);
                }
                private static DateTime SafeAddDays(DateTime source, int days)
                {
                    try { return source.AddDays(days); }
                    catch (ArgumentOutOfRangeException) { return days < 0 ? DateTime.MinValue : new DateTime(9999, 12, 31, 23, 59, 0); }
                }
                private void Adjust(int minutes)
                {
                    try { Value = Value.AddMinutes(minutes); }
                    catch (ArgumentOutOfRangeException) { Value = minutes < 0 ? DateTime.MinValue : new DateTime(9999, 12, 31, 23, 59, 0); }
                }
                private void ApplyCalendarResult(DateTime selectedDate, bool accepted)
                {
                    if (!accepted) return;
                    DateTime date = selectedDate.Date;
                    TimeSpan time = value.TimeOfDay;
                    Value = date.Add(time);
                }
                private void ShowCalendar()
                {
                    if (calendarPopup != null && !calendarPopup.IsDisposed) calendarPopup.Close();
                    CalendarPopup popup = new CalendarPopup(value.Date);
                    calendarPopup = popup;
                    Point screen = PointToScreen(new Point(DateBox.Left, Height + 3)); Rectangle area = Screen.FromPoint(screen).WorkingArea;
                    int y = screen.Y + popup.Height <= area.Bottom ? screen.Y : PointToScreen(new Point(0, 0)).Y - popup.Height - 3;
                    popup.Location = new Point(Math.Max(area.Left, Math.Min(screen.X, area.Right - popup.Width)), Math.Max(area.Top, Math.Min(y, area.Bottom - popup.Height)));
                    popup.FormClosed += delegate
                    {
                        bool accepted = popup.Accepted; DateTime chosen = popup.SelectedDate;
                        if (calendarPopup == popup) calendarPopup = null;
                        ApplyCalendarResult(chosen, accepted);
                        popup.Dispose();
                        if (!IsDisposed && !Disposing) { Focus(); Invalidate(); }
                    };
                    popup.Show(owner);
                    popup.Activate();
                }
                protected override void Dispose(bool disposing)
                {
                    if (disposing && calendarPopup != null && !calendarPopup.IsDisposed) { CalendarPopup popup = calendarPopup; calendarPopup = null; popup.Close(); }
                    base.Dispose(disposing);
                }
                internal void ReferenceSelectPart(int part) { selectedPart = Math.Max(0, Math.Min(2, part)); }
                internal void ReferenceAdjust(int minutes) { Adjust(minutes); }
                internal void ReferenceKey(Keys key) { OnKeyDown(new KeyEventArgs(key)); }
                internal bool ReferenceProcessKey(Keys key) { Message message = new Message(); return ProcessCmdKey(ref message, key); }
                internal void ReferenceMouseDown(Point point) { OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)); }
                internal void ReferenceArrow(bool hour, bool up)
                {
                    Rectangle box = hour ? HourBox : MinuteBox;
                    ReferenceMouseDown(new Point(box.Right - 8, up ? box.Top + 8 : box.Bottom - 8));
                }
                internal void ReferenceCalendarResult(DateTime selectedDate, bool accepted) { ApplyCalendarResult(selectedDate, accepted); }
                internal bool ReferenceCalendarVisible { get { return calendarPopup != null && !calendarPopup.IsDisposed && calendarPopup.Visible; } }
                internal void ReferenceCloseCalendar() { if (calendarPopup != null && !calendarPopup.IsDisposed) calendarPopup.Close(); }
            }

            internal sealed class CalendarPopup : Form
            {
                private readonly Font titleFont = new Font("楷体", 18, FontStyle.Bold, GraphicsUnit.Pixel);
                private readonly Font weekFont = new Font("楷体", 16, FontStyle.Bold, GraphicsUnit.Pixel);
                private readonly Font dayFont = new Font("Times New Roman", 18, FontStyle.Bold, GraphicsUnit.Pixel);
                private DateTime visibleMonth;
                private DateTime selectedDate;
                private int hoverDay;
                internal DateTime SelectedDate { get { return selectedDate; } }
                internal bool Accepted { get; private set; }
                private const int GridColumnWidth = 41;
                private const int GridRowHeight = 29;
                private Rectangle PreviousBox { get { return new Rectangle(18, 11, 38, 32); } }
                private Rectangle NextBox { get { return new Rectangle(264, 11, 38, 32); } }
                private Rectangle TodayBox { get { return new Rectangle(95, 253, 130, 34); } }
                private Rectangle GridBox { get { return new Rectangle(16, 74, GridColumnWidth * 7, GridRowHeight * 6); } }

                internal CalendarPopup(DateTime initial)
                {
                    selectedDate = initial.Date;
                    visibleMonth = new DateTime(initial.Year, initial.Month, 1);
                    AutoScaleMode = AutoScaleMode.None;
                    ClientSize = new Size(320, 310);
                    FormBorderStyle = FormBorderStyle.None;
                    StartPosition = FormStartPosition.Manual;
                    ShowInTaskbar = false;
                    KeyPreview = true;
                    DoubleBuffered = true;
                    TopMost = false;
                    using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(ClientRectangle, 22)) Region = new Region(path);
                }
                protected override void Dispose(bool disposing)
                {
                    if (disposing) { titleFont.Dispose(); weekFont.Dispose(); dayFont.Dispose(); }
                    base.Dispose(disposing);
                }
                protected override void OnPaintBackground(PaintEventArgs e)
                {
                    GlassBackdrop.DrawReferenceBackground(e.Graphics, ClientRectangle);
                    using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 22))
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(238, ThemeVisual.Current.PanelBottom)))
                    using (Pen rim = new Pen(Color.FromArgb(235, Color.White), 1.2f))
                    { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(rim, path); }
                }
                protected override void OnPaint(PaintEventArgs e)
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawNav(e.Graphics, PreviousBox, "‹"); DrawNav(e.Graphics, NextBox, "›");
                    AppTypography.Draw(e.Graphics, visibleMonth.ToString("yyyy 年 MM 月"), 17, FontStyle.Bold, new RectangleF(58, 9, 204, 36), Color.White, StringAlignment.Center, false);
                    string[] weeks = { "一", "二", "三", "四", "五", "六", "日" };
                    for (int i = 0; i < 7; i++) TextRenderer.DrawText(e.Graphics, weeks[i], weekFont, new Rectangle(GridBox.X + i * GridColumnWidth, 47, GridColumnWidth, 25), Palette.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    int firstOffset = ((int)visibleMonth.DayOfWeek + 6) % 7;
                    int days = DateTime.DaysInMonth(visibleMonth.Year, visibleMonth.Month);
                    for (int day = 1; day <= days; day++)
                    {
                        Rectangle box = DayBox(day);
                        bool selected = selectedDate.Year == visibleMonth.Year && selectedDate.Month == visibleMonth.Month && selectedDate.Day == day;
                        bool today = DateTime.Today.Year == visibleMonth.Year && DateTime.Today.Month == visibleMonth.Month && DateTime.Today.Day == day;
                        if (selected || hoverDay == day || today)
                        {
                            Color color = selected ? ThemeVisual.Current.Accent : hoverDay == day ? Color.FromArgb(95, Color.White) : Color.FromArgb(65, ThemeVisual.Current.Accent);
                            using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(box, 11))
                            using (SolidBrush fill = new SolidBrush(color))
                            using (Pen rim = new Pen(Color.FromArgb(selected ? 245 : 145, Color.White)))
                            { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(rim, path); }
                        }
                        TextRenderer.DrawText(e.Graphics, day.ToString(CultureInfo.InvariantCulture), dayFont, box, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    }
                    using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(TodayBox, 13))
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(105, ThemeVisual.Current.ButtonTop)))
                    using (Pen rim = new Pen(Color.FromArgb(205, Color.White)))
                    { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(rim, path); }
                    TextRenderer.DrawText(e.Graphics, "回到今天", weekFont, TodayBox, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    AppTypography.Draw(e.Graphics, "Esc 取消 · 点击日期确认", 15, FontStyle.Bold, new RectangleF(18, 289, 284, 18), Color.White, StringAlignment.Center, false);
                }
                private void DrawNav(Graphics graphics, Rectangle box, string text)
                {
                    using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(box, 13))
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(125, ThemeVisual.Current.ButtonTop)))
                    using (Pen rim = new Pen(Color.FromArgb(205, Color.White)))
                    { graphics.FillPath(fill, path); graphics.DrawPath(rim, path); }
                    TextRenderer.DrawText(graphics, text, titleFont, box, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                private int HitDay(Point point)
                {
                    if (!GridBox.Contains(point)) return 0;
                    int column = Math.Max(0, Math.Min(6, (point.X - GridBox.X) / GridColumnWidth));
                    int row = Math.Max(0, Math.Min(5, (point.Y - GridBox.Y) / GridRowHeight));
                    int firstOffset = ((int)visibleMonth.DayOfWeek + 6) % 7;
                    int day = row * 7 + column - firstOffset + 1;
                    return day >= 1 && day <= DateTime.DaysInMonth(visibleMonth.Year, visibleMonth.Month) ? day : 0;
                }
                private Rectangle DayBox(int day)
                {
                    int firstOffset = ((int)visibleMonth.DayOfWeek + 6) % 7;
                    int slot = firstOffset + day - 1;
                    int row = slot / 7, column = slot % 7;
                    return new Rectangle(GridBox.X + column * GridColumnWidth + 2, GridBox.Y + row * GridRowHeight + 1, GridColumnWidth - 4, GridRowHeight - 2);
                }
                protected override void OnMouseMove(MouseEventArgs e) { int next = HitDay(e.Location); if (hoverDay != next) { hoverDay = next; Invalidate(); } base.OnMouseMove(e); }
                protected override void OnMouseLeave(EventArgs e) { hoverDay = 0; Invalidate(); base.OnMouseLeave(e); }
                protected override void OnMouseDown(MouseEventArgs e)
                {
                    if (PreviousBox.Contains(e.Location)) { ShiftMonth(-1); return; }
                    if (NextBox.Contains(e.Location)) { ShiftMonth(1); return; }
                    if (TodayBox.Contains(e.Location)) { visibleMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); selectedDate = DateTime.Today; Invalidate(); return; }
                    int day = HitDay(e.Location);
                    if (day > 0) { selectedDate = new DateTime(visibleMonth.Year, visibleMonth.Month, day); Accepted = true; Close(); }
                    base.OnMouseDown(e);
                }
                protected override void OnMouseWheel(MouseEventArgs e)
                {
                    if (e.Delta != 0) ShiftMonth(e.Delta > 0 ? -1 : 1);
                    base.OnMouseWheel(e);
                }
                private void ShiftMonth(int delta)
                {
                    if ((delta < 0 && visibleMonth.Year == 1 && visibleMonth.Month == 1) || (delta > 0 && visibleMonth.Year == 9999 && visibleMonth.Month == 12)) return;
                    visibleMonth = visibleMonth.AddMonths(delta); hoverDay = 0; Invalidate();
                }
                private static DateTime SafeAddDate(DateTime source, int days)
                {
                    try { return source.AddDays(days); }
                    catch (ArgumentOutOfRangeException) { return days < 0 ? DateTime.MinValue.Date : DateTime.MaxValue.Date; }
                }
                protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
                {
                    Keys key = keyData & Keys.KeyCode;
                    if (key == Keys.Escape) { Accepted = false; Close(); return true; }
                    if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down)
                    {
                        int days = key == Keys.Left ? -1 : key == Keys.Right ? 1 : key == Keys.Up ? -7 : 7;
                        selectedDate = SafeAddDate(selectedDate, days);
                        visibleMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1); Invalidate(); return true;
                    }
                    if (key == Keys.PageUp || key == Keys.PageDown) { ShiftMonth(key == Keys.PageUp ? -1 : 1); return true; }
                    if (key == Keys.Enter) { Accepted = true; Close(); return true; }
                    return base.ProcessCmdKey(ref msg, keyData);
                }
                protected override void OnDeactivate(EventArgs e)
                {
                    base.OnDeactivate(e);
                    if (Visible && !Accepted) Close();
                }
                internal void SaveDiagnosticScreenshot(string path)
                {
                    StartPosition = FormStartPosition.Manual; Location = new Point(-20000, -20000); Show(); Application.DoEvents();
                    using (Bitmap image = new Bitmap(ClientSize.Width, ClientSize.Height)) { DrawToBitmap(image, ClientRectangle); image.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
                    Hide();
                }
                internal void ReferenceKey(Keys key) { Message message = new Message(); ProcessCmdKey(ref message, key); }
                internal void ReferenceExternalDeactivate() { OnDeactivate(EventArgs.Empty); }
                internal void ReferenceShiftMonth(int delta) { ShiftMonth(delta); }
                internal void ReferenceWheel(int delta) { OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, 0, 0, delta)); }
                internal DateTime ReferenceVisibleMonth { get { return visibleMonth; } }
                internal void ReferenceSetVisibleMonth(DateTime month) { visibleMonth = new DateTime(month.Year, month.Month, 1); }
                internal bool ReferenceValidateGridMappings(out string details)
                {
                    DateTime original = visibleMonth;
                    int checkedMonths = 0, checkedDays = 0;
                    bool sawSixthRow = false;
                    int[] years = { 1, 4, 100, 400, 2023, 2024, 9999 };
                    try
                    {
                        foreach (int year in years)
                        {
                            for (int month = 1; month <= 12; month++)
                            {
                                visibleMonth = new DateTime(year, month, 1);
                                int firstOffset = ((int)visibleMonth.DayOfWeek + 6) % 7;
                                int days = DateTime.DaysInMonth(year, month);
                                if (firstOffset + days > 35) sawSixthRow = true;
                                for (int slot = 0; slot < 42; slot++)
                                {
                                    int column = slot % 7, row = slot / 7;
                                    Point center = new Point(GridBox.X + column * GridColumnWidth + GridColumnWidth / 2, GridBox.Y + row * GridRowHeight + GridRowHeight / 2);
                                    int expected = slot - firstOffset + 1;
                                    if (expected < 1 || expected > days) expected = 0;
                                    if (HitDay(center) != expected)
                                    {
                                        details = "日历点击映射失败：" + year + "-" + month.ToString("00") + "，格位 " + slot;
                                        return false;
                                    }
                                }
                                for (int day = 1; day <= days; day++)
                                {
                                    Rectangle box = DayBox(day);
                                    if (box.Left < GridBox.Left || box.Top < GridBox.Top || box.Right > GridBox.Right || box.Bottom > GridBox.Bottom || HitDay(new Point(box.Left + box.Width / 2, box.Top + box.Height / 2)) != day)
                                    {
                                        details = "日历日期框越界：" + year + "-" + month.ToString("00") + "-" + day.ToString("00");
                                        return false;
                                    }
                                    checkedDays++;
                                }
                                if (HitDay(new Point(GridBox.Right, GridBox.Top)) != 0 || HitDay(new Point(GridBox.Left, GridBox.Bottom)) != 0)
                                {
                                    details = "日历网格外缘点击边界失败";
                                    return false;
                                }
                                checkedMonths++;
                            }
                        }
                    }
                    finally { visibleMonth = original; }
                    if (!sawSixthRow)
                    {
                        details = "日历诊断未覆盖第六周";
                        return false;
                    }
                    details = "日历固定像素布局 " + checkedMonths + " 个月/" + checkedDays + " 个日期及42格点击映射通过";
                    return true;
                }
            }
        }

        internal string ReferenceSelectedId { get { return selected; } }
        internal bool ReferenceDetailed { get { return detailedMode; } }
        internal bool ReferenceBusy {get{return busy;}}
        internal string ReferenceRefreshText { get { return refreshSummary.Text; } }
        internal string ReferenceRefreshState(string id) {string state;return refreshStates.TryGetValue(id,out state)?state:"";}
        internal async void ReferenceCommand(string command,AccountMeta account)
        {
            switch(command)
            {
                case "appearance":RoundedAppearanceMenu.ShowInside(referenceAppearance,this,Cursor.Position);break;
                case "tokens":
                    AccountMeta tokenAccount=store.Settings.Accounts.FirstOrDefault(a=>a.Id==selected);
                    MessageBox.Show(this,tokenAccount==null?"请先选择账号。":tokenAccount.Email+"\n\n账号累计 Token："+(tokenAccount.AccountLifetimeTokens<0?"尚未获取":tokenAccount.AccountLifetimeTokens.ToString("N0"))+"\n更新时间："+(string.IsNullOrEmpty(tokenAccount.AccountTokenUpdatedAt)?"尚未查询":tokenAccount.AccountTokenUpdatedAt)+"\n"+tokenAccount.AccountTokenError+"\n\n点击主界面“刷新额度”同步该账号的云端累计值；不是本机日志总和。","账号 Token 记录",MessageBoxButtons.OK,MessageBoxIcon.Information);break;
                case "pin":TopMost=!TopMost;break;
                case "minimize":case "close":Hide();break;
                case "add":context.BeginNewLogin();break;
                case "import":context.TryImportCurrent(true);break;
                case "library":WorkspaceWindow.Open(this,store,context.SwitchAccount,false);break;
                case "projects":WorkspaceWindow.Open(this,store,context.SwitchAccount,true);break;
                case "shop":AiShop.Open(this);break;
#if !NO_DESKTOP_PET
                case "desktop-pet":DesktopPet.Toggle(this,store.IsPreview);break;
#endif
                case "refresh":if(!busy)await context.RefreshAllAsync(true);break;
                case "refresh-one":if(!busy && !store.IsPreview && account!=null)await context.RefreshOneAsync(account);break;
                case "daily":DailyNews.Open(this);break;
                case "reset":ResetPanel.Open(this,store);break;
                case "stagger":StaggerPlan.Open(this,store);break;
                case "compact":case "detailed":detailedMode=command=="detailed";SaveDisplayMode();Rebuild();break;
                case "select":if(account!=null){selected=account.Id;Rebuild();}break;
                case "switch":if(account!=null)context.SwitchAccount(account);break;
                case "delete":if(account!=null)context.DeleteAccount(account);break;
                case "expiry":if(account!=null)EditExpiry(account);break;
                case "reset-credits":if(account!=null)ShowResetCredits(account);break;
                case "tools":if(account!=null)OpenTools(account);break;
                case "account-menu":if(account!=null)ShowAccountMenu(account);break;
            }
            if(referenceSurface!=null && !referenceSurface.IsDisposed)referenceSurface.RefreshView();
        }

        public void SavePreview(string path)
        {
            if(referenceSurface!=null){referenceSurface.SaveReferencePreview(path);return;}
            if(Environment.GetCommandLineArgs().Contains("--mockup-preview")) { ClientSize=new Size(680,1020); PerformLayout(); body.PerformLayout(); ResizeCards(); }
            Control header=Controls.Cast<Control>().FirstOrDefault(delegate(Control item){return item.Controls.OfType<HeaderButton>().Any();});
            if(header!=null) using(Bitmap top=new Bitmap(header.Width,header.Height))
            {header.DrawToBitmap(top,new Rectangle(0,0,header.Width,header.Height));top.Save(Path.Combine(Path.GetDirectoryName(path),"header-module.png"),System.Drawing.Imaging.ImageFormat.Png);}
            using (Bitmap bmp = new Bitmap(Width, Height))
            {
                DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png);
                Point top=PointToClient(overview.PointToScreen(Point.Empty));
                Control toolbar=refreshSummary.Parent;
                Point bottom=PointToClient(toolbar.PointToScreen(new Point(toolbar.Width,toolbar.Height)));
                Rectangle module=Rectangle.Intersect(new Rectangle(top.X,top.Y,overview.Width,bottom.Y-top.Y),new Rectangle(0,0,bmp.Width,bmp.Height));
                if(module.Width>0 && module.Height>0)using(Bitmap capture=bmp.Clone(module,bmp.PixelFormat))capture.Save(Path.Combine(Path.GetDirectoryName(path),"controls-module.png"),System.Drawing.Imaging.ImageFormat.Png);
                Point accountTop=PointToClient(count.Parent.PointToScreen(Point.Empty));
                Point detailBottom=PointToClient(detail.PointToScreen(new Point(detail.Width,detail.Height)));
                Rectangle accountsCapture=Rectangle.Intersect(new Rectangle(accountTop.X,accountTop.Y,detail.Width,detailBottom.Y-accountTop.Y),new Rectangle(0,0,bmp.Width,bmp.Height));
                if(accountsCapture.Width>0 && accountsCapture.Height>0)using(Bitmap capture=bmp.Clone(accountsCapture,bmp.PixelFormat))capture.Save(Path.Combine(Path.GetDirectoryName(path),"accounts-module.png"),System.Drawing.Imaging.ImageFormat.Png);
            }
            Point previous = list.AutoScrollPosition;
            list.AutoScrollPosition = new Point(0, 260);
            list.Invalidate(true);
            list.Update();
            using (Bitmap bmp = new Bitmap(Width, Height)) { DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height)); bmp.Save(Path.Combine(Path.GetDirectoryName(path), "dashboard-scrolled.png"), System.Drawing.Imaging.ImageFormat.Png); }
            list.AutoScrollPosition = new Point(0, -previous.Y);
        }

        public void PreviewQuotaFeedback()
        {
            if(!store.IsPreview) return;
            AccountMeta[] accounts=store.Settings.Accounts.ToArray();
            BeginQuotaRefresh(accounts);
            for(int i=0;i<accounts.Length;i++)
            {
                if(Environment.GetCommandLineArgs().Contains("--two-card-preview"))
                {SetQuotaRefreshState(accounts[i].Id,"success");continue;}
                if(i==1) accounts[i].LastError="登录已过期，请重新登录";
                SetQuotaRefreshState(accounts[i].Id,i==1?"failed":"success");
            }
            if(Environment.GetCommandLineArgs().Contains("--two-card-preview"))FinishQuotaRefresh(true);
        }

        internal bool VerifyResponsiveLayout(){return referenceSurface.VerifyLayout();}
        public bool RunHotkeyMessageTest()
        {
            Native.SendHotkeyMessage(Handle, HotkeyId);
            if (!Visible || !Screen.FromControl(this).WorkingArea.IntersectsWith(Bounds)) return false;
            foreach(Rectangle workArea in new[]{new Rectangle(0,0,1920,1040),new Rectangle(0,0,1366,728),new Rectangle(0,0,1024,576),new Rectangle(-800,0,800,480)})
            {
                Size fitted=ReferenceWindowSize(workArea);
                if(fitted.Width>workArea.Width-24 || fitted.Height>workArea.Height-24 || fitted.Width>574 || fitted.Height>861 || Math.Abs(fitted.Width*1.5-fitted.Height)>1)return false;
            }
            if(referenceSurface!=null)return referenceSurface.VerifyLayout();
            Size original = ClientSize;
            foreach (Size size in new Size[] { new Size(540, 620), new Size(620, 880), new Size(1000, 720) })
            {
                ClientSize = size;
                PerformLayout();
                body.PerformLayout();
                ResizeCards();
                foreach (Control area in Controls)
                foreach (Button button in area.Controls.OfType<Button>())
                {
                    if (button.Right > area.ClientSize.Width || button.Left < 0) return false;
                    HeaderButton headerButton=button as HeaderButton;
                    if(headerButton!=null && !headerButton.RepaintIsStable())return false;
                }
                foreach (AccountRow row in list.Controls.OfType<AccountRow>())
                foreach (Button button in row.Controls.OfType<Button>())
                    if (button.Visible && (button.Right > row.ClientSize.Width || button.Left < 0 || button.Bottom > row.Height)) return false;
                if (list.Height < 100 || detail.Bottom > body.ClientSize.Height) return false;
            }
            ClientSize = original;
            return true;
        }
    }

    internal sealed class BorderlessMenuRenderer : ToolStripProfessionalRenderer
    {
        internal BorderlessMenuRenderer():base(new ThemeMenuColors()){}
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){}
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){
            var r=new RectangleF(e.TextRectangle.X,0,e.TextRectangle.Width,e.Item.Height);
            AppTypography.Draw(e.Graphics,e.Text,e.TextFont.SizeInPoints*e.Graphics.DpiY/72f,FontStyle.Bold,r,e.Item.Enabled?Color.White:ThemeVisual.Blend(ThemeVisual.Current.PanelTop,Color.White,.4f),StringAlignment.Near,true);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){using(var b=new LinearGradientBrush(e.AffectedBounds,ThemeVisual.Current.PanelTop,ThemeVisual.Current.PanelBottom,90f))e.Graphics.FillRectangle(b,e.AffectedBounds);}
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){using(var p=new Pen(Color.FromArgb(70,ThemeVisual.Current.Rim)))e.Graphics.DrawLine(p,12,e.Item.Height/2,e.Item.Width-12,e.Item.Height/2);}
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){if(!e.Item.Selected)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var r=new Rectangle(3,1,e.Item.Width-6,e.Item.Height-2);using(var path=GraphicsExtensions.CreateRoundedPath(r,8))using(var b=new SolidBrush(ThemeVisual.Current.ButtonBottom))e.Graphics.FillPath(b,path);}
    }
    internal sealed class RoundedAppearanceMenu : ContextMenuStrip
    {
        internal static Point Fit(Rectangle bounds,Size size,Point anchor) {
            return new Point(Math.Max(bounds.Left,Math.Min(anchor.X,bounds.Right-size.Width)),Math.Max(bounds.Top,Math.Min(anchor.Y,bounds.Bottom-size.Height)));
        }
        internal static void ShowInside(ContextMenuStrip menu,Control owner,Point anchor) {
            Rectangle bounds=Rectangle.Intersect(owner.RectangleToScreen(owner.ClientRectangle),Screen.FromControl(owner).WorkingArea);
            bounds.Inflate(-12,-12);
            menu.MaximumSize=new Size(Math.Max(120,bounds.Width),Math.Max(120,bounds.Height));
            menu.PerformLayout();
            Size size=menu.GetPreferredSize(Size.Empty);
            menu.Show(owner,owner.PointToClient(Fit(bounds,size,anchor)));
            menu.Location=Fit(bounds,menu.Size,menu.Location);
        }
        internal RoundedAppearanceMenu(){DropShadowEnabled=false;Padding=new Padding(8);Font=Palette.Font(11,false);ItemAdded+=delegate(object sender,ToolStripItemEventArgs e){if(!(e.Item is ToolStripSeparator))e.Item.Padding=new Padding(5,4,5,4);};}
        protected override void OnOpened(EventArgs e){base.OnOpened(e);using(var path=GraphicsExtensions.CreateRoundedPath(ClientRectangle,12)){var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}}
    }
    internal sealed class ThemeMenuColors : ProfessionalColorTable
    {
        internal static ContextMenuStrip AccountMenu() { return new ContextMenuStrip { Renderer=new ToolStripProfessionalRenderer(new ThemeMenuColors()), ShowImageMargin=false, DropShadowEnabled=false, ForeColor=Color.White, BackColor=ThemeVisual.Current.PanelBottom, Font=Palette.Font(11,false) }; }
        public override Color ToolStripDropDownBackground {get{return ThemeVisual.Current.PanelBottom;}}
        public override Color MenuBorder {get{return ThemeVisual.Current.Rim;}}
        public override Color MenuItemSelected {get{return ThemeVisual.Current.ButtonTop;}}
        public override Color MenuItemBorder {get{return ThemeVisual.Current.Accent;}}
        public override Color MenuItemSelectedGradientBegin {get{return ThemeVisual.Current.ButtonTop;}}
        public override Color MenuItemSelectedGradientEnd {get{return ThemeVisual.Current.ButtonBottom;}}
        public override Color SeparatorDark {get{return ThemeVisual.Current.ButtonTop;}}
        public override Color SeparatorLight {get{return ThemeVisual.Current.PanelTop;}}
    }
    internal sealed class SpacedHeaderLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using(StringFormat format=(StringFormat)StringFormat.GenericTypographic.Clone())
            using(SolidBrush ink=new SolidBrush(ForeColor))
            {
                format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces;
                float x=0,y=(Height-Font.GetHeight(e.Graphics))/2f;
                foreach(char character in Text)
                {
                    string glyph=character.ToString();
                    e.Graphics.DrawString(glyph,Font,ink,x,y,format);
                    x+=e.Graphics.MeasureString(glyph,Font,100,format).Width+0.8f;
                }
            }
        }
    }

    internal sealed class HeaderButton : Button
    {
        private bool hover;
        public HeaderButton(string text)
        {
            Text=text;Font=Palette.Font(12,true);ForeColor=Color.White;Cursor=Cursors.Hand;
            FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;
            UseVisualStyleBackColor=false;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Opaque|ControlStyles.ResizeRedraw,true);
            UpdateStyles();
        }
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            // Button hover/press invalidation may skip background painting. Always
            // restore clean backdrop pixels before compositing translucent layers.
            Graphics g=e.Graphics;
            GlassBackdrop.Draw(g,this);
            g.SmoothingMode=SmoothingMode.AntiAlias;
            Rectangle box=new Rectangle(1,1,Width-3,Height-4);
            bool custom=ThemeSelection.Current=="custom";
            using(GraphicsPath shadow=GraphicsExtensions.CreateRoundedPath(new Rectangle(1,3,Width-3,Height-4),13))
            using(SolidBrush shade=new SolidBrush(Color.FromArgb(50,37,31,62)))g.FillPath(shade,shadow);
            using(GraphicsPath path=GraphicsExtensions.CreateRoundedPath(box,13))
            using(LinearGradientBrush fill=new LinearGradientBrush(box,Color.FromArgb(hover?190:155,custom?Color.FromArgb(171,161,198):Palette.ButtonTop),Color.FromArgb(205,custom?Color.FromArgb(73,69,103):Palette.ButtonBottom),90f))
            using(Pen edge=new Pen(Color.FromArgb(240,Color.White),1.2f))
            {
                g.FillPath(fill,path);g.DrawPath(edge,path);
                using(Pen sheen=new Pen(Color.FromArgb(65,Color.White),1f))g.DrawLine(sheen,14,3,Width-15,3);
            }
            float x=(Width-1)/2f,y=(Height-3)/2f;
            using(Pen symbol=new Pen(Color.White,2.3f))
            {
                symbol.StartCap=symbol.EndCap=LineCap.Round;
                if(Text=="−")g.DrawLine(symbol,x-7,y,x+7,y);
                else if(Text=="×"){g.DrawLine(symbol,x-6,y-6,x+6,y+6);g.DrawLine(symbol,x+6,y-6,x-6,y+6);}
                else
                {
                    bool arrow=Text.StartsWith("外观");
                    TextRenderer.DrawText(g,arrow?"外观":Text,Font,new Rectangle(0,0,Width-(arrow?14:0),Height-2),ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
                    if(arrow)g.DrawLines(symbol,new[]{new Point(Width-19,15),new Point(Width-15,19),new Point(Width-11,15)});
                }
            }
        }
        internal bool RepaintIsStable()
        {
            using(Bitmap bitmap=new Bitmap(Width,Height))
            using(Graphics graphics=Graphics.FromImage(bitmap))
            {
                OnPaint(new PaintEventArgs(graphics,ClientRectangle));
                using(Bitmap first=(Bitmap)bitmap.Clone())
                {
                    for(int n=0;n<20;n++)OnPaint(new PaintEventArgs(graphics,ClientRectangle));
                    for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
                        if(first.GetPixel(x,y)!=bitmap.GetPixel(x,y))return false;
                }
            }
            return true;
        }
    }

    internal sealed class DisplayModeSwitch : Control
    {
        public bool Detailed;
        public event Action SelectionChanged;
        public DisplayModeSwitch()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.Opaque,true);
            Cursor=Cursors.Hand;AccessibleName="账户显示模式";TabStop=true;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
            bool selected=e.X>=Width/2;
            if(Detailed==selected)return;Detailed=selected;Invalidate();if(SelectionChanged!=null)SelectionChanged();
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);if(e.KeyCode!=Keys.Left && e.KeyCode!=Keys.Right && e.KeyCode!=Keys.Space)return;
            Detailed=e.KeyCode==Keys.Left?false:e.KeyCode==Keys.Right?true:!Detailed;
            Invalidate();if(SelectionChanged!=null)SelectionChanged();e.Handled=true;
        }
        protected override void OnPaintBackground(PaintEventArgs e){}
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;GlassBackdrop.Draw(g,this);g.SmoothingMode=SmoothingMode.AntiAlias;
            Rectangle box=new Rectangle(1,1,Width-3,Height-3);
            using(GraphicsPath path=GraphicsExtensions.CreateRoundedPath(box,12))
            using(SolidBrush fill=new SolidBrush(Color.FromArgb(125,74,65,109)))
            using(Pen rim=new Pen(Color.FromArgb(220,Color.White))) {g.FillPath(fill,path);g.DrawPath(rim,path);}
            Rectangle selected=new Rectangle(Detailed?Width/2:3,3,Width/2-5,Height-7);
            using(GraphicsPath path=GraphicsExtensions.CreateRoundedPath(selected,10))
            using(LinearGradientBrush fill=new LinearGradientBrush(selected,Color.FromArgb(205,188,147,255),Color.FromArgb(205,126,84,219),90f))
            using(Pen rim=new Pen(Color.FromArgb(180,Color.White))){g.FillPath(fill,path);g.DrawPath(rim,path);}
            Palette.Text(g,"紧凑",9.5f,Color.White,new Rectangle(0,0,Width/2,Height-1),!Detailed,TextFormatFlags.HorizontalCenter);
            Palette.Text(g,"详细",9.5f,Color.White,new Rectangle(Width/2,0,Width/2,Height-1),Detailed,TextFormatFlags.HorizontalCenter);
        }
    }

    internal sealed class ModuleButton : Button
    {
        private readonly string icon;
        private bool hover;
        public ModuleButton(string caption,string icon)
        {
            Text=caption;this.icon=icon;Font=Palette.Font(10.5f,true);ForeColor=Color.White;
            FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;
            Cursor=Cursors.Hand;Size=new Size(160,43);
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Opaque|ControlStyles.ResizeRedraw,true);
        }
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnPaintBackground(PaintEventArgs e){}
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;GlassBackdrop.Draw(g,this);g.SmoothingMode=SmoothingMode.AntiAlias;
            Rectangle box=new Rectangle(1,1,Width-3,Height-4);
            GlassBackdrop.DrawFrosted(g,this,box,12);
            bool custom=ThemeSelection.Current=="custom";
            using(GraphicsPath path=GraphicsExtensions.CreateRoundedPath(box,12))
            using(LinearGradientBrush fill=new LinearGradientBrush(box,Color.FromArgb(hover?165:125,custom?Color.FromArgb(156,133,191):Palette.ButtonTop),Color.FromArgb(145,custom?Color.FromArgb(76,65,109):Palette.ButtonBottom),90f))
            using(Pen rim=new Pen(Color.FromArgb(225,Color.White),1.1f))
            {g.FillPath(fill,path);g.DrawPath(rim,path);}
            int tw=TextRenderer.MeasureText(Text,Font).Width;
            int left=Math.Max(8,(Width-tw-35)/2);
            Palette.Icon(g,icon,new Rectangle(left,(Height-21)/2,21,21));
            if(icon=="person")using(Pen plus=new Pen(Color.White,1.5f)){g.DrawLine(plus,left+23,Height/2-2,left+23,Height/2+6);g.DrawLine(plus,left+19,Height/2+2,left+27,Height/2+2);}
            TextRenderer.DrawText(g,Text,Font,new Rectangle(left+32,0,Math.Max(1,Width-left-35),Height-2),ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class RefreshBanner : Label
    {
        public RefreshBanner() { SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.Opaque,true); }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            GlassBackdrop.Draw(e.Graphics,this);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Rectangle box=new Rectangle(1,1,Width-3,Height-3);
            using(GraphicsPath path=GraphicsExtensions.CreateRoundedPath(box,12))
            using(LinearGradientBrush fill=new LinearGradientBrush(box,Color.FromArgb(175,64,164,120),Color.FromArgb(130,Palette.GlassBottom),0f))
            using(Pen rim=new Pen(Color.FromArgb(210,Color.White))) { e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(rim,path); }
            using(SolidBrush white=new SolidBrush(Color.White))e.Graphics.FillEllipse(white,12,8,18,18);
            using(Pen tick=new Pen(Color.FromArgb(58,151,111),2.1f))e.Graphics.DrawLines(tick,new[]{new Point(17,17),new Point(20,20),new Point(26,13)});
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(41,0,Width-77,Height-1),Color.White,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
            using(SolidBrush lens=new SolidBrush(Color.FromArgb(50,Color.White)))e.Graphics.FillEllipse(lens,Width-31,6,21,21);
            using(Pen arrow=new Pen(Color.White,1.6f))e.Graphics.DrawLines(arrow,new[]{new Point(Width-23,11),new Point(Width-18,16),new Point(Width-23,21)});
        }
    }

    internal sealed class SummaryView : Control
    {
        private int total, known, available;
        public SummaryView() { DoubleBuffered = true; }
        protected override void OnPaintBackground(PaintEventArgs e) { GlassBackdrop.Draw(e.Graphics, this); }
        public void UpdateAccounts(List<AccountMeta> accounts)
        {
            total = accounts.Count;
            known = accounts.Count(delegate(AccountMeta a) { return Palette.HasAnyQuota(a) && string.IsNullOrEmpty(a.LastError); });
            available = accounts.Count(delegate(AccountMeta a) { return Palette.AllPresentQuotaAvailable(a); });
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle box=new Rectangle(1,1,Width-3,Math.Max(50,Height-12));
            GlassBackdrop.DrawFrosted(g,this,box,16);
            bool custom=ThemeSelection.Current=="custom";
            using(GraphicsPath path=GraphicsExtensions.CreateRoundedPath(box,16))
            using(LinearGradientBrush fill=new LinearGradientBrush(box,Color.FromArgb(105,custom?Color.FromArgb(149,129,177):Palette.GlassTop),Color.FromArgb(128,custom?Color.FromArgb(74,64,103):Palette.GlassBottom),90f))
            using(Pen rim=new Pen(Color.FromArgb(200,Color.White),1f)){g.FillPath(fill,path);g.DrawPath(rim,path);}
            string[] labels = { "已登记", "上次可用", "待确认" };
            int[] values = { total, available, total - known };
            for (int i = 0; i < 3; i++)
            {
                int left=19+i*(Width/3),x=left+54;
                if(i>0) using(Pen divider=new Pen(Color.FromArgb(130,Color.White))) g.DrawLine(divider,left-12,14,left-12,Height-23);
                Palette.Round(g,new Rectangle(left,20,40,40),Color.FromArgb(105,Palette.Accents[1]),20);
                Palette.Icon(g,i==0?"person":i==1?"database":"clock",new Rectangle(left+10,29,20,20));
                Palette.Text(g, labels[i], 9, Palette.Ink, new Rectangle(x, 9, Width / 3 - 75, 22), false, TextFormatFlags.Left);
                Palette.Text(g, values[i].ToString("00"),24, Palette.Ink, new Rectangle(x, 29, 88, 39), true, TextFormatFlags.Left);
            }
        }
    }

    internal sealed class AccountRow : Control
    {
        private readonly AccountMeta account;
        private readonly int index;
        private readonly bool active, selected, detailed;
        private bool hovered;
        internal bool IsCurrent { get { return active; } }
        public event Action SelectAccount;
        public event Action SwitchAccount;
        public event Action DeleteAccount;
        public event Action EditExpiry;
        public event Action OpenTools;
        private readonly RoundButton switchButton, remove;
        private string refreshState="";
        private readonly ToolTip refreshTip=new ToolTip { AutoPopDelay=15000, InitialDelay=300, ReshowDelay=100 };
        internal string AccountId { get { return account.Id; } }
        internal void SetRefreshState(string state)
        {
            refreshState=state;
            refreshTip.SetToolTip(this,FeedbackText()+(string.IsNullOrEmpty(account.LastError)?"":"\n原因："+account.LastError));
            Invalidate();
        }
        private string FeedbackText()
        {
            bool cached=Palette.HasAnyQuota(account);
            if(refreshState=="waiting") return "等待更新 · 尚未轮到此账号";
            if(refreshState=="updating") return "正在更新 · 查询此账号额度…";
            if(account.LastError==DockContext.PlanUpdatedQuotaUnavailable)
                return "已更新为 "+(string.IsNullOrWhiteSpace(account.Plan)?"官方方案":account.Plan.ToUpperInvariant())+" · 额度待刷新";
            if(refreshState=="success") return "更新成功 · "+Palette.Updated(account).Split(' ').Last();
            if(refreshState=="failed") return "更新失败 · "+(cached?"保留上次额度":"暂无额度数据");
            if(!string.IsNullOrEmpty(account.LastError)) return "! 上次更新失败 · "+(cached?"仍为上次额度":"暂无可用额度数据");
            return string.IsNullOrEmpty(account.LastQuotaAt)?"尚未查询 · 点击上方刷新额度":"上次更新 "+Palette.Updated(account)+" · 缓存记录";
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing) refreshTip.Dispose();
            base.Dispose(disposing);
        }
        public AccountRow(AccountMeta account, int index, bool active, bool selected, bool detailed)
        {
            this.account = account; this.index = index; this.active = active; this.selected = selected; this.detailed=detailed;
            Height = 154; Margin = new Padding(0, 0, 0, 7); BackColor = Palette.Background;
            DoubleBuffered = true; Cursor = Cursors.Hand;
            switchButton = RoundButton.Create(active ? "✓ 当前" : "切换 →", Color.FromArgb(100, 113, 138), Color.White, 12);
            switchButton.Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold);
            switchButton.Size = new Size(110, 31);
            switchButton.Click += delegate { if (SwitchAccount != null) SwitchAccount(); };
            Controls.Add(switchButton);
            switchButton.BackColor = Palette.Surface;
            remove = RoundButton.Create("×", Palette.Surface, Palette.Muted, 12);
            remove.Size = new Size(24, 24);
            remove.AccessibleName = "删除已登记账户 " + account.Email;
            remove.Click += delegate { if (DeleteAccount != null) DeleteAccount(); };
            Controls.Add(remove);
            remove.BackColor = Palette.Surface;
            RoundButton expiry = RoundButton.Create("到期设置", Palette.Surface, Palette.Ink, 10);
            expiry.Size = new Size(80, 26);
            expiry.Click += delegate { if (EditExpiry != null) EditExpiry(); };
            expiry.Visible=false;
            ContextMenuStrip accountMenu=ThemeMenuColors.AccountMenu();
            accountMenu.Items.Add("账号工具…",null,delegate {if(OpenTools!=null)OpenTools();});
            accountMenu.Items.Add("设置订阅到期日…",null,delegate {if(EditExpiry!=null)EditExpiry();});
            ContextMenuStrip=accountMenu;
            Disposed+=delegate {accountMenu.Dispose();};
            Controls.Add(expiry);
            Resize += delegate { expiry.Location = new Point(Width - 97, 174); };
            Resize += delegate { switchButton.Location = new Point(Width - 129, 78); remove.Location = new Point(Width - 34, 8); };
            Click += delegate { if (SelectAccount != null) SelectAccount(); };
            MouseEnter+=delegate { hovered=true; Invalidate(); };
            MouseLeave+=delegate { hovered=ClientRectangle.Contains(PointToClient(Cursor.Position)); Invalidate(); };
            List<string> quotaNames=new List<string>();
            if(Palette.HasPrimary(account))quotaNames.Add("5 小时额度 "+Palette.Percent(account.PrimaryRemaining));
            if(Palette.IsPro(account) || Palette.HasSecondary(account))quotaNames.Add("周剩余 "+Palette.Percent(account.SecondaryRemaining));
            if(Palette.IsFree(account))quotaNames.Add("月额度剩余 "+Palette.Percent(Palette.HasMonthly(account)?account.MonthlyRemaining:-1));
            AccessibleName = account.Email + " " + (quotaNames.Count==0?"额度未知":string.Join(" ",quotaNames));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Color accent = Palette.Accents[index % Palette.Accents.Length];
            Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 5);
            DrawGlass(g,this);
            Palette.Round(g, new Rectangle(14, 14, 43, 43), Color.FromArgb(175,147,121,210), 22);
            Palette.Text(g, (index + 1).ToString("00"), 15, Color.White, new Rectangle(14, 14, 43, 43), true, TextFormatFlags.HorizontalCenter);
            Palette.Text(g, account.Email, 12, Palette.Ink, new Rectangle(71, 9, Math.Max(50, Width - 123), 25), true, TextFormatFlags.Left);
            string plan = string.IsNullOrEmpty(account.Plan) ? "计划未知" : account.Plan.ToUpperInvariant();
            bool isFreePlan=string.Equals(account.Plan,"free",StringComparison.OrdinalIgnoreCase);
            Palette.Text(g, (active && !isFreePlan?"♛ ":"")+plan + "  ·  " + (active ? "当前账户" : "已登记"), 10, Palette.Ink, new Rectangle(71, 32, Math.Max(50, Width - 225), 23), false, TextFormatFlags.Left);
            double mainRemaining=Palette.MainRemaining(account);
            Palette.Text(g, Palette.Percent(mainRemaining), 27, Palette.Quota(mainRemaining), new Rectangle(Width - 146, 15, 112, 40), true, TextFormatFlags.Right);
            Palette.Text(g, Palette.MainQuotaLabel(account), 8, Palette.Muted, new Rectangle(Width - 142, 53, 104, 19), false, TextFormatFlags.Right);
            int barWidth = Math.Max(60, Width - 250);
            var quotaRows=new List<Tuple<string,double,Color>>();
            if(Palette.HasPrimary(account))quotaRows.Add(Tuple.Create("5 小时额度",account.PrimaryRemaining,accent));
            if(Palette.IsPro(account) || Palette.HasSecondary(account))quotaRows.Add(Tuple.Create("周额度",account.SecondaryRemaining,Color.White));
            if(Palette.IsFree(account))quotaRows.Add(Tuple.Create("月额度",Palette.HasMonthly(account)?account.MonthlyRemaining:-1,Palette.Accents[(index+1)%Palette.Accents.Length]));
            if(quotaRows.Count==0)
                Palette.Text(g,"官方未返回可显示的额度窗口",8.5f,Palette.Muted,new Rectangle(15,76,Math.Max(120,Width-190),26),false,TextFormatFlags.Left);
            else
            {
                int rowPitch=quotaRows.Count>=3?18:24;
                int firstY=quotaRows.Count==1?82:quotaRows.Count==2?68:62;
                for(int row=0;row<quotaRows.Count;row++)
                {
                    int rowY=firstY+row*rowPitch;
                    Tuple<string,double,Color> quota=quotaRows[row];
                    Palette.Text(g,quota.Item1,8,Palette.Muted,new Rectangle(15,rowY-7,49,18),false,TextFormatFlags.Left);
                    Palette.Bar(g,new Rectangle(72,rowY,barWidth,quotaRows.Count>=3?8:11),quota.Item2,quota.Item3);
                    Palette.Text(g,Palette.Percent(quota.Item2),9.5f,Palette.Ink,new Rectangle(78+barWidth,rowY-8,50,22),false,TextFormatFlags.Left);
                }
            }
            bool planPending=account.LastError==DockContext.PlanUpdatedQuotaUnavailable;
            bool failed=!planPending && (refreshState=="failed" || (refreshState=="" && !string.IsNullOrEmpty(account.LastError)));
            Color feedbackColor=failed?Color.FromArgb(255,221,217):refreshState=="success"?Color.FromArgb(179,255,224):Palette.Ink;
            Color feedbackFill=planPending?Color.FromArgb(150,Palette.GlassBottom):failed?Color.FromArgb(150,158,70,90):refreshState=="success"?Color.FromArgb(145,51,164,127):Color.FromArgb(115,Palette.GlassBottom);
            Palette.Round(g,new Rectangle(10,115,Width-23,30),feedbackFill,12);
            using(GraphicsPath feedbackPath=GraphicsExtensions.CreateRoundedPath(new Rectangle(10,115,Width-23,30),12))
            using(Pen line=new Pen(Color.FromArgb(170,planPending?Palette.Ink:failed?Color.LightPink:Color.LightGreen)))g.DrawPath(line,feedbackPath);
            using(SolidBrush dot=new SolidBrush(Color.White))g.FillEllipse(dot,21,122,17,17);
            using(Pen symbol=new Pen(failed?Color.FromArgb(170,69,89):Color.FromArgb(45,143,107),2f))
            {
                if(failed){g.DrawLine(symbol,29,125,29,131);g.DrawLine(symbol,29,134,29,134.5f);}
                else g.DrawLines(symbol,new[]{new Point(25,130),new Point(28,133),new Point(34,126)});
            }
            int feedbackWidth=Math.Max(185,(int)(Width*(detailed?.40:.47)));
            Palette.Text(g,FeedbackText(),9.5f,Color.White,new Rectangle(46,115,feedbackWidth-46,30),true,TextFormatFlags.Left);
            using(Pen separator=new Pen(Color.FromArgb(140,Color.White)))g.DrawLine(separator,feedbackWidth+7,122,feedbackWidth+7,138);
            bool cachedQuota=Palette.HasAnyQuota(account);
            string statusExtra;
            if(planPending)statusExtra="额度不可用 · 请稍后刷新";
            else if(failed)statusExtra=cachedQuota?"请求失败 · 上次成功 "+Palette.Updated(account).Split(' ').Last():"请求失败 · 额度未知";
            else
            {
                var resetParts=new List<string>();
                if(!Palette.IsFree(account))resetParts.Add(Palette.VisibleResetLabel(account,detailed)+" "+Palette.CompactCountdown(Palette.VisibleResetAt(account,detailed)));
                if(Palette.HasMonthly(account))resetParts.Add("月 "+Palette.CompactCountdown(account.MonthlyResetAt));
                resetParts.Add(account.ResetCredits<0?"重置卡未知":"可重置 "+account.ResetCredits+" 次");
                statusExtra=string.Join(" · ",resetParts);
            }
            Palette.Text(g,statusExtra,8,Color.White,new Rectangle(feedbackWidth+16,115,Width-feedbackWidth-37,30),false,TextFormatFlags.Right);
        }
        internal void DrawGlass(Graphics g,Control surface)
        {
            int x=surface==this?0:surface.Left,y=surface==this?0:surface.Top;
            Rectangle box=new Rectangle(1-x,1-y,Width-3,Height-5);
            bool custom=ThemeSelection.Current=="custom";
            GlassBackdrop.DrawLiquid(g,surface,box,18);
            using(GraphicsPath path=GraphicsExtensions.CreateRoundedPath(box,18))
            using(LinearGradientBrush fill=new LinearGradientBrush(box,Color.FromArgb(active||selected||hovered?140:126,custom?Color.FromArgb(116,101,154):Palette.GlassTop),Color.FromArgb(165,custom?Color.FromArgb(68,59,104):Palette.GlassBottom),90f))
            using(Pen edge=new Pen(Color.FromArgb(220,Color.White),1.1f)){g.FillPath(fill,path);g.DrawPath(edge,path);}
        }
        protected override void OnPaintBackground(PaintEventArgs e) { GlassBackdrop.Draw(e.Graphics, this); }
    }

    internal sealed class DetailView : Control
    {
        private AccountMeta account;
        private bool active;
        public DetailView() { DoubleBuffered = true; }
        protected override void OnPaintBackground(PaintEventArgs e) { GlassBackdrop.Draw(e.Graphics, this); }
        public void UpdateAccount(AccountMeta value, bool isActive) { account = value; active = isActive; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle box=new Rectangle(1,6,Width-3,Height-10);
            GlassBackdrop.DrawLiquid(g,this,box,18);
            using(GraphicsPath path=GraphicsExtensions.CreateRoundedPath(box,18))
            using(LinearGradientBrush fill=new LinearGradientBrush(box,Color.FromArgb(130,Palette.GlassTop),Color.FromArgb(155,Palette.GlassBottom),90f))
            using(Pen rim=new Pen(Color.FromArgb(210,Color.White))){g.FillPath(fill,path);g.DrawPath(rim,path);}
            Palette.Text(g, "账户详情", 14, Palette.Ink, new Rectangle(16, 19, 95, 34), true, TextFormatFlags.Left);
            if (account == null) { Palette.Text(g, "添加账户后查看额度与重置时间", 9, Palette.Muted, new Rectangle(16, 60, Width - 32, 25), false, TextFormatFlags.Left); return; }
            var captions=new List<string>();var values=new List<string>();
            if(Palette.HasPrimary(account)){captions.Add("5 小时额度");values.Add(Palette.Percent(account.PrimaryRemaining));}
            if(Palette.IsPro(account) || Palette.HasSecondary(account)){captions.Add("周额度");values.Add(Palette.Percent(account.SecondaryRemaining));}
            if(Palette.IsFree(account)){captions.Add("月额度");values.Add(Palette.Percent(Palette.HasMonthly(account)?account.MonthlyRemaining:-1));}
            captions.Add("可重置");values.Add(account.ResetCredits<0?"未知":account.ResetCredits+" 次");
            captions.Add("Token 累计");values.Add("暂无可靠数据");
            int col=(Width-130)/captions.Count;
            for(int i=0;i<captions.Count;i++)
            {
                int x=124+i*col;
                using(Pen line=new Pen(Color.FromArgb(110,Color.White)))g.DrawLine(line,x-9,27,x-9,Height-18);
                Palette.Text(g,captions[i],10,Palette.Muted,new Rectangle(x,29,col-10,21),false,TextFormatFlags.Left);
                bool token=captions[i]=="Token 累计";
                Palette.Text(g,values[i],token?9:23,Palette.Ink,new Rectangle(x,52,col-10,34),captions[i]!="可重置",TextFormatFlags.Left);
            }
        }
    }

    internal static class DashboardNative
    {
        public const string WindowTitle = "Codex Account Dock · 账户仪表盘";
        public const int OpenMessage = 0x8011;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        public static void OpenExisting() { IntPtr window = FindWindow(null, WindowTitle); if (window != IntPtr.Zero) PostMessage(window, OpenMessage, IntPtr.Zero, IntPtr.Zero); }
        public static void Activate(IntPtr window) { ShowWindow(window, 9); SetForegroundWindow(window); }
        public static void DarkCaption(IntPtr window)
        {
            try { int dark = 0; DwmSetWindowAttribute(window, 20, ref dark, 4); int color = ColorTranslator.ToWin32(Palette.Background); DwmSetWindowAttribute(window, 35, ref color, 4); } catch { }
        }
    }
}
