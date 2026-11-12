using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexAccountDock
{
    // Shared glass tint and continuous window highlight for the overview and tools.
    internal static class ReferenceGlass
    {
        internal static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height)); var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right-d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right-d, r.Bottom-d, d, d, 0, 90); p.AddArc(r.X, r.Bottom-d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        internal static void Draw(Graphics g, RectangleF r, float radius, bool dark, bool button)
        {
            bool custom = ThemeSelection.Current == "custom"; ThemeLook look = ThemeVisual.Current;
            Color top = button ? look.ButtonTop : look.PanelTop, bottom = button ? look.ButtonBottom : look.PanelBottom;
            using (GraphicsPath path = Round(r, radius))
            using (var fill = new LinearGradientBrush(r, Color.FromArgb(AppearanceOptions.GlassAlpha(button?192:dark?211:145), top), Color.FromArgb(AppearanceOptions.GlassAlpha(button?217:dark?229:170), bottom), 90))
            using (var rim = new Pen(Color.FromArgb(224, custom?Color.White:look.Rim), custom?1.5f:2f)) {
                g.FillPath(fill, path);
                GraphicsState state=g.Save();g.SetClip(path,CombineMode.Intersect);
                RectangleF sheen=new RectangleF(r.X,r.Y,r.Width,Math.Max(1,Math.Min(42,r.Height*.35f)));
                using(var light=new LinearGradientBrush(sheen,Color.FromArgb(button?20:12,Color.White),Color.FromArgb(0,Color.White),90f))g.FillRectangle(light,sheen);
                g.Restore(state);g.DrawPath(rim, path);
            }
        }
        internal static int WindowRadius(Size size) { return Math.Max(12,(int)Math.Round(28*size.Width/573f)); }
        internal static void DrawWindowRim(Graphics g,Size size)
        {
            if(size.Width<10 || size.Height<10)return;
            GraphicsState state=g.Save();
            g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
            float scale=Math.Max(.5f,size.Width/573f),inset=2.5f*scale;
            using(GraphicsPath path=Round(new RectangleF(inset,inset,size.Width-2*inset,size.Height-2*inset),Math.Max(1,WindowRadius(size)-inset)))
            using(var rim=new Pen(Color.FromArgb(225,ThemeSelection.Current=="custom"?Color.White:ThemeVisual.Current.Rim),1.25f*scale))
            {rim.LineJoin=LineJoin.Round;rim.DashStyle=DashStyle.Solid;g.DrawPath(rim,path);}
            g.Restore(state);
        }
        internal static void DrawTool(Graphics g, RectangleF r, float radius, bool button)
        {
            Draw(g,r,radius,true,button);
        }
    }

    // Same 1024 x 1536 artboard, backdrop and typography as the account overview.
    // The email editor is the only native child.
    internal sealed class AccountToolsCanvas : Control
    {
        private sealed class Hit { internal RectangleF Box; internal string Command; }
        private readonly AccountToolsView owner;
        private readonly List<Hit> hits = new List<Hit>();
        private Bitmap background;
        private Bitmap frame;
        private IntPtr emailBackgroundBrush;
        private string backgroundKey;
        private int scroll, maxScroll;
        private RectangleF textViewport;
        private readonly ToolTip tips = new ToolTip();
        private static readonly string[] captions = {"订阅时间", "邀请好友", "会话与存储", "项目中心"};
        private float ScaleX { get { return Math.Max(.01f, Width / 1024f); } }
        private float ScaleY { get { return Math.Max(.01f, Height / 1536f); } }
        internal AccountToolsCanvas(AccountToolsView owner)
        {
            this.owner = owner; Dock = DockStyle.Fill; TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            Controls.Add(owner.RecipientEditor);
            owner.RecipientEditor.Dock = DockStyle.None;
            tips.SetToolTip(owner.RecipientEditor, "填写一个收件邮箱；发送前会再次确认。");
        }
        internal void ResetScroll() { scroll = 0; Invalidate(); }
        internal void RefreshAppearance()
        {
            backgroundKey = null;
            owner.RecipientEditor.BackColor = ThemeVisual.Blend(ThemeVisual.Current.PanelTop,ThemeVisual.Current.ButtonTop,.65f);
            owner.RecipientEditor.ForeColor = Color.White;
            Invalidate(true);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        private void EnsureBackground()
        {
            string file = DynamicBackground.PosterPath;
            string key = ThemeSelection.Current + "|" + AppearanceOptions.Brightness + "|" + (File.Exists(file)?File.GetLastWriteTimeUtc(file).Ticks:0) + "|" + (ThemeSelection.Current=="custom"?DynamicBackground.FrameVersion:0);
            if (key == backgroundKey && background != null) return;
            if (background != null) background.Dispose(); background = new Bitmap(1024,1536);
            using (Graphics g = Graphics.FromImage(background)) GlassBackdrop.DrawReferenceBackground(g, new Rectangle(0,0,1024,1536),false);
            backgroundKey = key;
            owner.RecipientEditor.BackColor=ThemeVisual.Blend(ThemeVisual.Current.PanelTop,ThemeVisual.Current.ButtonTop,.65f);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            EnsureBackground();
            if (frame == null || frame.Size != ClientSize) {
                if (frame != null) frame.Dispose();
                frame = new Bitmap(Math.Max(1,Width),Math.Max(1,Height));
            }
            using (Graphics g = Graphics.FromImage(frame)) {
            g.Clear(ThemeVisual.Current.PanelBottom);
            GraphicsState state = g.Save(); g.ScaleTransform(ScaleX, ScaleY); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.DrawImageUnscaled(background,0,0); hits.Clear(); maxScroll = 0; textViewport = RectangleF.Empty;
            using(var veil=new SolidBrush(Color.FromArgb(ThemeVisual.IsReferenceCustom?12:60,ThemeVisual.Current.PanelBottom)))g.FillRectangle(veil,new Rectangle(0,0,1024,1536));
            Color title = ThemeVisual.HeaderInk;
            Text(g,"ACCOUNT TOOLS",24,new RectangleF(64,32,500,38),title,false,false);
            Text(g,"账号工具",70,new RectangleF(57,72,560,86),title,true,false);
            Text(g,owner.Account.Email,32,new RectangleF(65,156,890,48),title,true,false);
            Button(g,new RectangleF(624,30,174,54),"返回总览","back",28);
            Button(g,new RectangleF(810,30,82,54),"外观","appearance",27);
            Button(g,new RectangleF(904,30,68,54),"×","close",42);
            FlatGlass(g,new RectangleF(60,220,904,80),25,Color.FromArgb(150,ThemeVisual.Current.PanelBottom));
            for (int i=0; i<captions.Length; i++) {
                RectangleF r = new RectangleF(68+i*222,228,222,64);
                if(owner.SelectedTab==i)FlatGlass(g,r,20,Color.FromArgb(245,ThemeVisual.Current.PanelBottom));
                Text(g,captions[i],31,r,Color.White,true,true);
                hits.Add(new Hit{Box=r,Command="tab:"+i});
            }
            FlatGlass(g,new RectangleF(40,311,944,1108),28,Color.FromArgb(150,ThemeVisual.Current.PanelBottom));
            owner.RecipientEditor.Visible = owner.SelectedTab==1;
            if (owner.SelectedTab==0) Subscription(g);
            else if (owner.SelectedTab==1) Invites(g);
            string footer = owner.Busy ? "正在处理，请稍候…" : owner.Notice;
            if (string.IsNullOrEmpty(footer)) footer = "所选账号 · " + owner.Account.Email;
            Text(g,footer,28,new RectangleF(65,1450,900,58),title,true,false);
            g.Restore(state);
            ReferenceGlass.DrawWindowRim(g,ClientSize);
            }
            UpdateEmailBackground();
            e.Graphics.DrawImageUnscaled(frame,0,0);
        }
        private void UpdateEmailBackground()
        {
            if (owner.SelectedTab != 1) return;
            Rectangle area = Rectangle.Intersect(owner.RecipientEditor.Bounds,ClientRectangle);
            if (area.Width < 1 || area.Height < 1) return;
            using (Bitmap patch = frame.Clone(area,frame.PixelFormat)) {
                IntPtr bitmap = patch.GetHbitmap();
                IntPtr nextBrush;
                try { nextBrush = CreatePatternBrush(bitmap); }
                finally { DeleteObject(bitmap); }
                if (emailBackgroundBrush != IntPtr.Zero) DeleteObject(emailBackgroundBrush);
                emailBackgroundBrush = nextBrush;
            }
            owner.RecipientEditor.Invalidate();
        }
        protected override void WndProc(ref Message message)
        {
            const int WmCtlColorEdit = 0x0133;
            if (message.Msg == WmCtlColorEdit && emailBackgroundBrush != IntPtr.Zero &&
                owner.RecipientEditor.IsHandleCreated && message.LParam == owner.RecipientEditor.Handle) {
                SetBkMode(message.WParam,1);
                SetTextColor(message.WParam,ColorTranslator.ToWin32(Color.White));
                SetBrushOrgEx(message.WParam,0,0,IntPtr.Zero);
                message.Result = emailBackgroundBrush;
                return;
            }
            base.WndProc(ref message);
        }
        [DllImport("gdi32.dll")] private static extern IntPtr CreatePatternBrush(IntPtr bitmap);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc,int mode);
        [DllImport("gdi32.dll")] private static extern int SetTextColor(IntPtr dc,int color);
        [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr dc,int x,int y,IntPtr previous);
        private static void FlatGlass(Graphics g,RectangleF r,float radius,Color fill)
        {
            using(GraphicsPath path=ReferenceGlass.Round(r,radius))
            using(var brush=new SolidBrush(fill))
            using(var rim=new Pen(ThemeVisual.Current.Rim,1.5f)){g.FillPath(brush,path);g.DrawPath(rim,path);}
        }
        private static void Block(Graphics g,RectangleF r,string heading)
        {ReferenceGlass.DrawTool(g,r,24,false);Text(g,heading,36,new RectangleF(r.X+26,r.Y+14,r.Width-52,60),Color.White,true,false);}
        private static void Metric(Graphics g,RectangleF r,string label,string value,float size)
        {ReferenceGlass.DrawTool(g,r,24,false);Text(g,label,31,new RectangleF(r.X+26,r.Y+15,r.Width-52,48),Color.White,true,false);Text(g,value,size,new RectangleF(r.X+26,r.Y+70,r.Width-52,r.Height-85),Color.White,true,false);}
        private static string LocalDate(string value)
        {DateTimeOffset date;return DateTimeOffset.TryParse(value,out date)?date.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):"未提供";}
        private void Subscription(Graphics g)
        {
            SubscriptionSnapshot s=owner.Account.SubscriptionRead;
            string compact=SubscriptionDisplay.Compact(owner.Account);string[] remaining=compact.Split('·');
            Metric(g,new RectangleF(60,322,444,160),"订阅状态",s==null?"尚未读取":s.HasActiveSubscription==true?"有效":s.HasActiveSubscription==false?"未确认有效":"官方未提供",46);
            Metric(g,new RectangleF(520,322,444,160),remaining.Length>1?remaining[0].Trim():"当前周期剩余",remaining.Length>1?remaining[1].Trim():"未知",46);
            Metric(g,new RectangleF(60,508,444,160),"当前权益截止",s==null?"未读取":LocalDate(s.ExpiresAtUtc),33);
            Metric(g,new RectangleF(520,508,444,160),"下次续费",s==null?"未读取":LocalDate(s.RenewsAtUtc),33);
            Metric(g,new RectangleF(60,694,444,160),"计划结束",s==null?"未读取":LocalDate(s.CancelsAtUtc),33);
            Metric(g,new RectangleF(520,694,444,160),"自动续费",s==null?"未读取":s.WillRenew==true?"是":s.WillRenew==false?"否":"官方未提供",44);
            Block(g,new RectangleF(60,880,904,248),"读取记录");
            string record="上次读取："+(s==null?"尚未读取":LocalDate(s.FetchedAtUtc))+"\r\n手填记录："+(string.IsNullOrEmpty(owner.Account.SubscriptionExpiresOn)?"未设置":owner.Account.SubscriptionExpiresOn);
            if(!string.IsNullOrEmpty(owner.Account.SubscriptionReadError))record+="\r\n最近读取失败："+owner.Account.SubscriptionReadError+"；保留上次数据。";
            record+="\r\n日期按本机时区显示；空缺项表示官方未返回该字段。";
            Body(g,record,new RectangleF(86,954,852,153),29);
            Button(g,new RectangleF(60,1160,904,68),"读取官方订阅时间","subscription",35);
            ReferenceGlass.DrawTool(g,new RectangleF(60,1260,904,138),24,false);
            RectangleF box=new RectangleF(86,1288,32,32);
            ReferenceGlass.DrawTool(g,box,7,true);
            if(owner.AutoRead) {
                using(var pen=new Pen(Color.White,3.4f)) {
                    pen.StartCap=pen.EndCap=System.Drawing.Drawing2D.LineCap.Round;
                    pen.LineJoin=System.Drawing.Drawing2D.LineJoin.Round;
                    g.DrawLines(pen,new[]{new PointF(box.X+7,box.Y+16),new PointF(box.X+14,box.Y+23),new PointF(box.X+25,box.Y+9)});
                }
            }
            Text(g,"手动刷新额度时，同时读取订阅日期",31,new RectangleF(137,1275,792,62),Color.White,true,false);
            Text(g,"开启后，在手动刷新额度时一并读取。",32,new RectangleF(86,1332,852,46),Color.White,false,false);
            hits.Add(new Hit{Box=new RectangleF(60,1260,904,138),Command="auto"});
        }
        private void Invites(Graphics g)
        {
            Block(g,new RectangleF(60,322,904,390),"1  查看邀请资格");
            Button(g,new RectangleF(86,411,414,62),(owner.ProgramIndex==0?"✓  ":"")+"个人邀请","program:0",32);
            Button(g,new RectangleF(520,411,418,62),(owner.ProgramIndex==1?"✓  ":"")+"工作区邀请","program:1",32);
            Paragraph(g,owner.InvitationSummary,31,new RectangleF(88,494,844,104),Color.White);
            Button(g,new RectangleF(86,622,852,62),"读取当前账号邀请资格","eligibility-auth",33);
            Block(g,new RectangleF(60,738,904,222),"2  填写收件邮箱");
            FlatGlass(g,new RectangleF(86,845,852,70),18,Color.Transparent);
            SetChild(owner.RecipientEditor,new RectangleF(108,860,808,44));
            Block(g,new RectangleF(60,986,904,412),"3  确认并发送");
            Body(g,owner.InvitationText,new RectangleF(88,1067,844,174),29);
            Button(g,new RectangleF(86,1296,852,68),"确认并发送一封邀请","send",35);
        }
        private void Models(Graphics g)
        {
            Block(g,new RectangleF(60,322,904,208),"本机 Codex 模型记录");
            Button(g,new RectangleF(656,344,282,62),"读取模型记录","models",30);
            Paragraph(g,"读取这台电脑已有的模型记录，无需额外网页登录。",31,new RectangleF(88,413,844,99),Color.White);
            Block(g,new RectangleF(60,556,904,600),"核对结果");
            Body(g,owner.ModelText,new RectangleF(88,641,844,478),32);
            Block(g,new RectangleF(60,1182,904,216),"如何理解结果");
            Paragraph(g,"“所选模型”是请求时选择的模型。“实际模型”需要响应记录明确提供。缺少字段时无法核对。",31,new RectangleF(88,1267,844,114),Color.White);
        }
        private void SetChild(Control child,RectangleF r)
        {
            Rectangle actual=Rectangle.Round(new RectangleF(r.X*ScaleX,r.Y*ScaleY,r.Width*ScaleX,r.Height*ScaleY));
            if(child.Bounds!=actual)child.Bounds=actual;
            if(child==owner.RecipientEditor){float size=Math.Max(12,32*ScaleX);if(Math.Abs(child.Font.Size-size)>.1f){Font old=child.Font;child.Font=new Font("Times New Roman",size,FontStyle.Bold,GraphicsUnit.Pixel);old.Dispose();}}
        }
        private void Button(Graphics g,RectangleF r,string text,string command,float size)
        {
            if(r.Y>=220)size=32;
            if(command=="back"||command=="appearance"||command=="close"){
                using(var path=ReferenceGlass.Round(r,19))using(var fill=new LinearGradientBrush(r,Color.FromArgb(230,ThemeVisual.Current.ButtonTop),Color.FromArgb(245,ThemeVisual.Current.ButtonBottom),90f))using(var rim=new Pen(ThemeVisual.Current.Rim,1.5f)){g.FillPath(fill,path);g.DrawPath(rim,path);}
            }else ReferenceGlass.DrawTool(g,r,19,true);
            Text(g,text,size,r,Color.White,true,true); hits.Add(new Hit{Box=r,Command=command});
        }
        private new static void Text(Graphics g,string text,float size,RectangleF box,Color color,bool bold,bool center)
        {
            AppTypography.Draw(g,text,size,bold?FontStyle.Bold:FontStyle.Bold,box,color,center?StringAlignment.Center:StringAlignment.Near,true);
        }
        private static List<string> Wrap(Graphics g,string value,float size,float width)
        {
            var result=new List<string>(); var widths=new Dictionary<char,float>();
            using(var cn=new Font(AppTypography.ChineseFamily,size,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var en=new Font(AppTypography.LatinFamily,size,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var format=(StringFormat)StringFormat.GenericTypographic.Clone()){
                format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces;
                foreach(string paragraph in (value??"").Replace("\r","").Split('\n')){
                    string line="";float length=0;
                    foreach(char c in paragraph){float advance;if(!widths.TryGetValue(c,out advance)){advance=g.MeasureString(c.ToString(),AppTypography.IsChinese(c)?cn:en,2000,format).Width;widths[c]=advance;}
                        if(line.Length>0 && length+advance>width){result.Add(line);line="";length=0;}line+=c;length+=advance;}
                    result.Add(line);
                }
            }
            return result;
        }
        private static void Paragraph(Graphics g,string text,float size,RectangleF r,Color color)
        {
            size=32;
            List<string> lines=Wrap(g,text,size,r.Width); float lineHeight=size*1.52f;
            GraphicsState state=g.Save();g.SetClip(r,CombineMode.Intersect);
            for(int i=0;i<lines.Count;i++)AppTypography.Draw(g,lines[i],size,FontStyle.Bold,new RectangleF(r.X,r.Y+i*lineHeight,r.Width,lineHeight),color,StringAlignment.Near,false);
            g.Restore(state);
        }
        private void Body(Graphics g,string text,RectangleF r,float size)
        {
            size=32;
            float lineHeight=size*1.52f;var lines=Wrap(g,text,size,r.Width-22);int visible=Math.Max(1,(int)(r.Height/lineHeight));
            maxScroll=Math.Max(0,lines.Count-visible);scroll=Math.Min(scroll,maxScroll);textViewport=r;
            GraphicsState state=g.Save();g.SetClip(r,CombineMode.Intersect);
            for(int i=scroll;i<Math.Min(lines.Count,scroll+visible);i++)AppTypography.Draw(g,lines[i],size,FontStyle.Bold,new RectangleF(r.X,r.Y+(i-scroll)*lineHeight,r.Width-22,lineHeight),Color.White,StringAlignment.Near,false);
            g.Restore(state);
            if(maxScroll>0){float track=r.Height-40,thumb=Math.Max(48,track*visible/lines.Count);
                using(var pen=new Pen(Color.FromArgb(ThemeVisual.IsReferenceCustom?12:60,Color.White),4))g.DrawLine(pen,r.Right-5,r.Y+20,r.Right-5,r.Bottom-20);
                using(var pen=new Pen(Color.FromArgb(205,Color.White),7))g.DrawLine(pen,r.Right-5,r.Y+20+(track-thumb)*scroll/maxScroll,r.Right-5,r.Y+20+(track-thumb)*scroll/maxScroll+thumb);
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);PointF p=new PointF(e.X/ScaleX,e.Y/ScaleY);Cursor=hits.Any(h=>h.Box.Contains(p))?Cursors.Hand:Cursors.Default;
        }
        protected override async void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Focus();PointF p=new PointF(e.X/ScaleX,e.Y/ScaleY);
            Hit hit=hits.LastOrDefault(h=>h.Box.Contains(p));
            if(hit!=null){await owner.Act(hit.Command);return;}
            if(p.Y<210 && FindForm()!=null)Native.BeginWindowDrag(FindForm().Handle);
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);PointF p=new PointF(e.X/ScaleX,e.Y/ScaleY);
            if(!textViewport.Contains(p)||maxScroll==0)return;scroll=Math.Max(0,Math.Min(maxScroll,scroll+(e.Delta>0?-3:3)));Invalidate();
        }
        protected override bool IsInputKey(Keys keyData) { return keyData==Keys.Left || keyData==Keys.Right || keyData==Keys.PageDown || keyData==Keys.PageUp || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);if(e.KeyCode==Keys.Left||e.KeyCode==Keys.Right){owner.SelectedTab=(owner.SelectedTab+(e.KeyCode==Keys.Left?2:1))%3;e.Handled=true;}
            if(e.KeyCode==Keys.PageDown||e.KeyCode==Keys.PageUp){scroll=Math.Max(0,Math.Min(maxScroll,scroll+(e.KeyCode==Keys.PageUp?-5:5)));Invalidate();e.Handled=true;}
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing){
                if(background!=null)background.Dispose();
                if(frame!=null)frame.Dispose();
                if(emailBackgroundBrush!=IntPtr.Zero){DeleteObject(emailBackgroundBrush);emailBackgroundBrush=IntPtr.Zero;}
                tips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
