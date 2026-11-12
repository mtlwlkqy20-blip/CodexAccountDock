using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexAccountDock
{
    // The reference is a 1024 x 1536 artboard. Paint and hit testing share its coordinates.
    internal sealed class ReferenceSurface : Control
    {
        private readonly AccountPopup owner;
        private readonly AccountStore store;
        private readonly List<Hit> hits=new List<Hit>();
        private readonly List<Tuple<RectangleF,string>> resetTips=new List<Tuple<RectangleF,string>>();
        private readonly ToolTip resetTip=new ToolTip {InitialDelay=300,AutoPopDelay=15000};
        private readonly Timer resetClock=new Timer {Interval=60000};
        private string lastResetTip="";
        private Bitmap background;
        private string backgroundKey;
        private int pageIndex;
        private readonly Color white=Color.White;
        private static readonly string ChineseFamily=ChooseChineseFamily();
        private string PreviewBodyFamily
        {
            get
            {
                if(!store.IsPreview)return null;
                string argument=Environment.GetCommandLineArgs().FirstOrDefault(t=>t.StartsWith("--font-preview="));
                if(argument==null)return null;
                switch(argument.Substring("--font-preview=".Length))
                {
                    case "kai":return "楷体";
                    case "xingkai":return "华文行楷";
                    case "fangsong":return "仿宋";
                    case "recommended":return "Microsoft YaHei UI";
                    case "times-kai":return "楷体";
                    default:return null;
                }
            }
        }
        private bool RecommendedPreview {get{return store.IsPreview && Environment.GetCommandLineArgs().Contains("--font-preview=recommended");}}
        private bool UseTimesKai {get{return !store.IsPreview || PreviewBodyFamily==null || Environment.GetCommandLineArgs().Contains("--font-preview=times-kai");}}
        private sealed class FontRun {internal string Value;internal bool Chinese;}
        private static List<FontRun> FontRuns(string text)
        {
            var runs=new List<FontRun>();
            foreach(char c in text??"")
            {
                bool cn=(c>=0x3400 && c<=0x9fff)||(c>=0x3000 && c<=0x303f)||(c>=0xff00 && c<=0xffef);
                if(runs.Count==0 || runs[runs.Count-1].Chinese!=cn)runs.Add(new FontRun{Value="",Chinese=cn});
                runs[runs.Count-1].Value+=c;
            }
            return runs;
        }
        private float MixedWidth(Graphics g,string text,float size,bool bold)
        {
            using(Font cn=new Font("楷体",size+2,bold?FontStyle.Bold:FontStyle.Bold,GraphicsUnit.Pixel))
            using(Font latin=new Font("Times New Roman",size+2,bold?FontStyle.Bold:FontStyle.Bold,GraphicsUnit.Pixel))
            using(StringFormat format=(StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces;
                return FontRuns(text).Sum(r=>g.MeasureString(r.Value,r.Chinese?cn:latin,2000,format).Width);
            }
        }
        private void MixedText(Graphics g,string text,float size,RectangleF box,Color color,bool bold,bool center,bool right)
        {
            string visible=text??"";
            if(MixedWidth(g,visible,size,bold)>box.Width)
            {
                while(visible.Length>0 && MixedWidth(g,visible+"…",size,bold)>box.Width)visible=visible.Substring(0,visible.Length-1);
                visible+="…";
            }
            float width=MixedWidth(g,visible,size,bold),x=right?box.Right-width:center?box.X+(box.Width-width)/2:box.X;
            using(Font cn=new Font("楷体",size+2,bold?FontStyle.Bold:FontStyle.Bold,GraphicsUnit.Pixel))
            using(Font latin=new Font("Times New Roman",size+2,bold?FontStyle.Bold:FontStyle.Bold,GraphicsUnit.Pixel))
            using(SolidBrush ink=new SolidBrush(color))
            using(StringFormat format=(StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces|StringFormatFlags.NoWrap|StringFormatFlags.NoClip;
                float ca=cn.Size*cn.FontFamily.GetCellAscent(cn.Style)/cn.FontFamily.GetEmHeight(cn.Style);
                float la=latin.Size*latin.FontFamily.GetCellAscent(latin.Style)/latin.FontFamily.GetEmHeight(latin.Style);
                float cd=cn.Size*cn.FontFamily.GetCellDescent(cn.Style)/cn.FontFamily.GetEmHeight(cn.Style);
                float ld=latin.Size*latin.FontFamily.GetCellDescent(latin.Style)/latin.FontFamily.GetEmHeight(latin.Style);
                float ascent=Math.Max(ca,la),descent=Math.Max(cd,ld),baseline=box.Y+(box.Height-ascent-descent)/2+ascent;
                foreach(FontRun run in FontRuns(visible))
                {
                    Font font=run.Chinese?cn:latin;
                    g.DrawString(run.Value,font,ink,x,baseline-(run.Chinese?ca:la),format);
                    x+=g.MeasureString(run.Value,font,2000,format).Width;
                }
            }
        }
        private static string ChooseChineseFamily()
        {
            using(InstalledFontCollection fonts=new InstalledFontCollection())
            {
                foreach(string family in new[]{"隶书","LiSu","华文隶书","STLiti"})
                    if(fonts.Families.Any(f=>f.Name==family))return family;
            }
            return "Microsoft YaHei UI";
        }
        private sealed class Hit { public RectangleF Box; public string Command; public AccountMeta Account; }
        public ReferenceSurface(AccountPopup owner,AccountStore store,DockContext context)
        {
            this.owner=owner;this.store=store;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Opaque,true);
            TabStop=true;AccessibleName="账户总览";
            if(store.IsPreview)
            {
                string previewPage=Environment.GetCommandLineArgs().FirstOrDefault(t=>t.StartsWith("--preview-page="));
                int page;
                if(previewPage!=null && int.TryParse(previewPage.Substring("--preview-page=".Length),out page))pageIndex=Math.Max(0,page-1);
            }
            ClampPage();
            resetClock.Tick+=delegate {if(Visible)Invalidate();};
            resetClock.Start();
        }
        private static int PageCountFor(int accountCount){return Math.Max(1,(Math.Max(0,accountCount)+1)/2);}
        private static int ClampPageFor(int page,int accountCount){return Math.Max(0,Math.Min(page,PageCountFor(accountCount)-1));}
        private int PageCount {get{return PageCountFor(store.Settings.Accounts.Count);}}
        private void ClampPage(){pageIndex=ClampPageFor(pageIndex,store.Settings.Accounts.Count);}
        private void ChangePage(int next){int clamped=ClampPageFor(next,store.Settings.Accounts.Count);if(clamped==pageIndex)return;pageIndex=clamped;RefreshView();}
        public void RefreshView(){ClampPage();FitContent();Invalidate();}
        private float CanvasScaleX {get{return Math.Max(.01f,Width/1024f);}}
        private float CanvasScaleY {get{return CanvasScaleX;}}
        private PointF Logical(Point point){return new PointF(point.X/CanvasScaleX,point.Y/CanvasScaleY);}
        protected override void OnPaintBackground(PaintEventArgs e){}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(ThemeVisual.Blend(ThemeVisual.Current.PanelBottom,ThemeVisual.Current.Accent,.3f));
            float scaleX=CanvasScaleX,scaleY=CanvasScaleY;GraphicsState state=e.Graphics.Save();
            // The reference layout is authored at 1024 x 1536.  Scale each axis
            // to the exact client extent so DPI rounding cannot leave unequal
            // solid bands above and below the illustrated surface.
            e.Graphics.ScaleTransform(scaleX,scaleY);
            PaintCanvas(e.Graphics);e.Graphics.Restore(state);
            // The form region owns the only window silhouette.  Draw its rim in
            // physical pixels after the artboard transform so resizing cannot
            // turn the old inset/scaled outline into a second rounded border.
            ReferenceGlass.DrawWindowRim(e.Graphics,ClientSize);
        }
        private void EnsureBackground(Graphics graphics)
        {
            float[] transform;using(Matrix matrix=graphics.Transform)transform=matrix.Elements;
            Size pixels=new Size(Math.Min(4096,Math.Max(1024,(int)Math.Ceiling(1024*Math.Abs(transform[0])))),Math.Min(4096,Math.Max(ArtHeight,(int)Math.Ceiling(ArtHeight*Math.Abs(transform[3])))));
            string file=DynamicBackground.PosterPath;
            string key=ThemeSelection.Current+"|"+AppearanceOptions.Brightness+"|"+(File.Exists(file)?File.GetLastWriteTimeUtc(file).Ticks:0)+"|"+(ThemeSelection.Current=="custom"?DynamicBackground.FrameVersion:0);
            if(background!=null && key==backgroundKey && background.Size==pixels)return;
            if(background!=null)background.Dispose();
            background=new Bitmap(pixels.Width,pixels.Height,PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(background))
            {
                g.Clear(ThemeVisual.Blend(ThemeVisual.Current.PanelBottom,ThemeVisual.Current.Accent,.3f));
                // Fill the complete artboard.  The native form region performs
                // the final corner clipping; leaving a 22-unit gutter here was
                // visible as a dark crescent at all four window corners.
                GlassBackdrop.DrawReferenceBackground(g,new Rectangle(Point.Empty,pixels),false);
            }
            backgroundKey=key;
        }
        private GraphicsPath Round(RectangleF r,float radius)
        {
            float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));GraphicsPath p=new GraphicsPath();
            p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
        }
        private void Glass(Graphics g,RectangleF r,float radius,bool dark)
        {
            ReferenceGlass.Draw(g,r,radius,dark,r.Height<70);
        }
        private new void Text(Graphics g,string text,float size,RectangleF box,Color color,bool bold,bool serif,bool right)
        {
            bool chinese=text!=null && text.Any(c=>c>=0x3400 && c<=0x9fff);
            bool decorative=text=="账户总览" && serif && size>=45;
            if(UseTimesKai && !decorative){MixedText(g,text,size,box,color,bold,false,right);return;}
            if(UseTimesKai)size+=2;
            string previewFont=PreviewBodyFamily;
            string family=decorative?ChineseFamily:chinese?(previewFont??"楷体"):"Segoe UI";
            using(Font font=new Font(family,size,decorative?FontStyle.Bold:bold?FontStyle.Bold:FontStyle.Bold,GraphicsUnit.Pixel))
            using(SolidBrush ink=new SolidBrush(color))
            using(StringFormat format=new StringFormat(StringFormat.GenericTypographic))
            {
                format.Alignment=right?StringAlignment.Far:StringAlignment.Near;format.LineAlignment=StringAlignment.Center;
                format.Trimming=StringTrimming.EllipsisCharacter;format.FormatFlags=StringFormatFlags.NoWrap|StringFormatFlags.NoClip;
                g.DrawString(text??"",font,ink,box,format);
                if(decorative){RectangleF heavier=box;heavier.X+=.35f;g.DrawString(text??"",font,ink,heavier,format);}
            }
        }
        private void Center(Graphics g,string text,float size,RectangleF box,bool bold,Color? color=null)
        {
            bool chinese=text!=null && text.Any(c=>c>=0x3400 && c<=0x9fff);
            Color textColor=color??white;
            if(UseTimesKai){MixedText(g,text,size,box,textColor,chinese||bold,true,false);return;}
            using(Font font=new Font(chinese?(PreviewBodyFamily??"楷体"):"Segoe UI",size,chinese||bold?FontStyle.Bold:FontStyle.Bold,GraphicsUnit.Pixel))
            using(SolidBrush ink=new SolidBrush(textColor))
            using(StringFormat f=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(text,font,ink,box,f);
        }
        private void HitBox(RectangleF r,string command,AccountMeta account)
        {
            if(measuredPaint)r=new RectangleF(r.X*ReferenceFactor,r.Y*ReferenceFactor,r.Width*ReferenceFactor,r.Height*ReferenceFactor);
            if(r.Width>0 && r.Height>0)hits.Add(new Hit{Box=r,Command=command,Account=account});
        }
        private void Icon(Graphics g,string icon,RectangleF r)
        {
            GraphicsState state=g.Save();g.TranslateTransform(r.X,r.Y);g.ScaleTransform(r.Width/24,r.Height/24);
            Palette.Icon(g,icon,new Rectangle(0,0,24,24));g.Restore(state);
        }
        private void Button(Graphics g,RectangleF r,string label,string command,string icon)
        {
            Glass(g,r,19,true);HitBox(r,command,null);
            if(icon!=null)
            {
                Icon(g,icon,new RectangleF(r.X+49,r.Y+14,32,32));
                if(command=="add")using(Pen plus=new Pen(white,2.5f)){g.DrawLine(plus,r.X+82,r.Y+20,r.X+82,r.Y+34);g.DrawLine(plus,r.X+75,r.Y+27,r.X+89,r.Y+27);}
                Text(g,label,28,new RectangleF(r.X+96,r.Y,r.Width-104,r.Height),white,true,false,false);
            }
            else if(command=="minimize" || command=="close")
            {
                using(Pen p=new Pen(white,3.5f))
                {float x=r.X+r.Width/2,y=r.Y+r.Height/2;p.StartCap=p.EndCap=LineCap.Round;if(command=="minimize")g.DrawLine(p,x-10,y,x+10,y);else{g.DrawLine(p,x-9,y-9,x+9,y+9);g.DrawLine(p,x+9,y-9,x-9,y+9);}}
            }
            else if(command=="appearance")
            {
                Center(g,"外观",24,new RectangleF(r.X+6,r.Y,r.Width-34,r.Height),true);
                using(Pen chevron=new Pen(white,2.2f)){chevron.StartCap=chevron.EndCap=LineCap.Round;g.DrawLines(chevron,new[]{new PointF(r.Right-28,r.Y+21),new PointF(r.Right-22,r.Y+27),new PointF(r.Right-16,r.Y+21)});}
            }
            else Center(g,label,25,r,true);
        }
        private List<Tuple<string,double,long>> Meters(AccountMeta a) {
            var rows=new List<Tuple<string,double,long>>();
            if(Palette.HasPrimary(a))rows.Add(Tuple.Create("5 小时额度",a.PrimaryRemaining,a.PrimaryResetAt));
            if(Palette.IsPro(a)||Palette.HasSecondary(a))rows.Add(Tuple.Create("周额度",a.SecondaryRemaining,a.SecondaryResetAt));
            if(Palette.IsFree(a))rows.Add(Tuple.Create("月额度",Palette.HasMonthly(a)?a.MonthlyRemaining:-1,a.MonthlyResetAt));
            return rows;
        }
        private static readonly Bitmap Mark=LoadMark();
        private static readonly Bitmap Dragon=LoadDragon();
        private static Bitmap LoadDragon(){using(var stream=typeof(ReferenceSurface).Assembly.GetManifestResourceStream("CodexAccountDock.Dragon.png"))using(var src=new Bitmap(stream)){var b=new Bitmap(src);b.MakeTransparent(b.GetPixel(0,0));return b;}}
        private static string ResetClock(long seconds){if(seconds<=0)return "重置时间待查询";var t=DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime();return (t.Date==DateTime.Today?"今天":t.Date==DateTime.Today.AddDays(1)?"明天":t.ToString("MM月dd日"))+" "+t.ToString("HH:mm")+" 重置";}
        private static Bitmap LoadMark(){using(var stream=typeof(ReferenceSurface).Assembly.GetManifestResourceStream("CodexAccountDock.V3Wordmark.png"))using(var bitmap=new Bitmap(stream))return new Bitmap(bitmap);}
        private const float ReferenceFactor=1024f/719f;
        private bool measuredPaint;
        private bool SingleAccount {get{return store.Settings.Accounts.Count<=1;}}
        private const int CardBaseHeight=167;
        private const int MeterHeight=70;
        private float MeasuredCardHeight(AccountMeta a){
            return CardBaseHeight+Math.Max(1,Meters(a).Count)*MeterHeight;
        }
        private float CardsStart {get{return SingleAccount?348:365;}}
        private float CardsEnd {get{return CardsStart+store.Settings.Accounts.Skip(pageIndex*2).Take(2).Sum(a=>MeasuredCardHeight(a)+10)+(store.Settings.Accounts.Count==0?150:0);}}
        private float PageHeight {
            get {
                if(store.Settings.Accounts.Count==0)return CardsEnd+77;
                // Preserve the previous full two-card window height.
                float content=2*(191+2*MeterHeight+10);
                for(int i=0;i<store.Settings.Accounts.Count;i+=2){
                    float pair=store.Settings.Accounts.Skip(i).Take(2).Sum(a=>MeasuredCardHeight(a)+10);
                    content=Math.Max(content,pair);
                }
                return 327+content+117;
            }
        }
        internal int ArtHeight {get{return (int)Math.Ceiling(PageHeight*ReferenceFactor);}}
        internal void FitContent(){
            if(owner.Controls.OfType<AccountToolsView>().Any())return;
            Rectangle area=Screen.FromControl(owner).WorkingArea;
            int dpi=owner.IsHandleCreated?DpiSupport.DpiForWindow(owner.Handle):DpiSupport.DpiForPoint(owner.Location);
            int width=Math.Min((int)Math.Round(620*dpi/96.0),Math.Max(1,area.Width-24));
            int height=(int)Math.Round(width*ArtHeight/1024.0);
            if(height>area.Height-24){height=area.Height-24;width=(int)Math.Round(height*1024.0/ArtHeight);}
            owner.MinimumSize=new Size(Math.Min(280,width),Math.Min(420,height));owner.ClientSize=new Size(width,height);
            if(owner.Visible)owner.Location=new Point(Math.Max(area.Left,Math.Min(owner.Left,area.Right-owner.Width)),Math.Max(area.Top,Math.Min(owner.Top,area.Bottom-owner.Height)));
        }
        private void MText(Graphics g,string value,float size,RectangleF box,Color color,bool bold=true,StringAlignment align=StringAlignment.Near){AppTypography.Draw(g,value,size,bold?FontStyle.Bold:FontStyle.Bold,box,color,align,true,"华文楷体");}
        private Color Ink {get{return ThemeVisual.IsReferenceCustom?Color.FromArgb(65,44,109):white;}}
        private void MGlass(Graphics g,RectangleF r,bool button=false,bool outer=false){
            Color top=ThemeVisual.IsReferenceCustom?Color.FromArgb(116,106,154):ThemeVisual.Current.PanelTop;
            Color bottom=ThemeVisual.IsReferenceCustom?Color.FromArgb(82,78,118):ThemeVisual.Current.PanelBottom;
            if(button){top=ThemeVisual.IsReferenceCustom?Color.FromArgb(169,145,205):ThemeVisual.Current.ButtonTop;bottom=ThemeVisual.Current.ButtonBottom;}
            using(var path=Round(r,button?13:19))
            using(var brush=new LinearGradientBrush(r,Color.FromArgb(AppearanceOptions.GlassAlpha(outer?145:button?192:211),top),Color.FromArgb(AppearanceOptions.GlassAlpha(outer?160:button?217:229),bottom),90f))
            using(var edge=new LinearGradientBrush(r,Color.FromArgb(outer?170:215,white),Color.FromArgb(outer?75:115,ThemeVisual.Current.Rim),90f))
            using(var pen=new Pen(edge,1.15f)){
                g.FillPath(brush,path);
                if(button){
                    using(var light=new LinearGradientBrush(r,Color.FromArgb(32,white),Color.Transparent,90f))g.FillPath(light,path);
                }
                g.DrawPath(pen,path);
            }
        }
        private void MButton(Graphics g,RectangleF r,string label,string command,AccountMeta account=null,string icon=null,bool enabled=true){
            MGlass(g,r,true);if(icon!=null){float textWidth=label.Length>6?133:label.Length>4?90:84;
                float start=r.X+(r.Width-textWidth-36)/2;
                MIcon(g,icon,new RectangleF(start,r.Y+(r.Height-24)/2,24,24),white);
                MText(g,label,21,new RectangleF(start+36,r.Y,textWidth+3,r.Height),white);}else if(command=="appearance"){MText(g,"外观",20,new RectangleF(r.X+4,r.Y,r.Width-32,r.Height),white,true,StringAlignment.Center);using(var pen=new Pen(white,1.6f))g.DrawLines(pen,new[]{new PointF(r.Right-19,r.Y+14),new PointF(r.Right-15,r.Y+18),new PointF(r.Right-11,r.Y+14)});}else MText(g,label,20,r,Color.FromArgb(enabled?255:140,white),true,StringAlignment.Center);
            if(enabled)HitBox(r,command,account);
        }
        private void MIcon(Graphics g,string icon,RectangleF r,Color color){
            var state=g.Save();g.TranslateTransform(r.X,r.Y);g.ScaleTransform(r.Width/24,r.Height/24);
            using(var p=new Pen(color,1.8f)){p.StartCap=p.EndCap=LineCap.Round;p.LineJoin=LineJoin.Round;
                if(icon=="crown"){
                    using(var gold=new LinearGradientBrush(new RectangleF(2,3,20,18),Color.FromArgb(255,242,179),Color.FromArgb(231,178,61),90f)){
                        g.FillPolygon(gold,new[]{new PointF(2,7),new PointF(8,12),new PointF(12,4),new PointF(16,12),new PointF(22,7),new PointF(19,18),new PointF(5,18)});
                        g.FillRectangle(gold,5,20,14,2);
                        foreach(PointF tip in new[]{new PointF(2,7),new PointF(12,4),new PointF(22,7)})g.FillEllipse(gold,tip.X-1.6f,tip.Y-1.6f,3.2f,3.2f);
                    }
                }
                else if(icon=="calendar"){g.DrawRectangle(p,3,5,18,17);g.DrawLine(p,3,10,21,10);g.DrawLine(p,7,2,7,7);g.DrawLine(p,17,2,17,7);foreach(int x in new[]{7,12,17})foreach(int y in new[]{14,18})g.DrawEllipse(p,x,y,0.7f,0.7f);}
                else if(icon=="layers"){for(int y=0;y<=10;y+=5)g.DrawLines(p,new[]{new PointF(2,8+y),new PointF(12,13+y),new PointF(22,8+y)});g.DrawLines(p,new[]{new PointF(2,8),new PointF(12,3),new PointF(22,8)});}
                else if(icon=="refresh"){g.DrawArc(p,3,3,18,18,205,140);g.DrawArc(p,3,3,18,18,25,140);g.DrawLines(p,new[]{new PointF(20,3),new PointF(21,9),new PointF(16,9)});g.DrawLines(p,new[]{new PointF(4,21),new PointF(3,15),new PointF(8,15)});}
                else if(icon=="bell"){g.DrawLines(p,new[]{new PointF(3,19),new PointF(5,16),new PointF(5,9)});g.DrawArc(p,5,3,14,12,180,180);g.DrawLines(p,new[]{new PointF(19,9),new PointF(19,16),new PointF(21,19),new PointF(3,19)});g.DrawArc(p,9,18,6,5,0,180);}
                else if(icon=="shop"){g.DrawLines(p,new[]{new PointF(5,7),new PointF(3,22),new PointF(21,22),new PointF(19,7),new PointF(5,7)});g.DrawArc(p,8,1,8,12,180,180);}
                else if(icon=="cat"){g.DrawEllipse(p,2,7,20,15);g.DrawLines(p,new[]{new PointF(3,11),new PointF(3,2),new PointF(9,7)});g.DrawLines(p,new[]{new PointF(15,7),new PointF(21,2),new PointF(21,11)});g.DrawEllipse(p,7,13,1,1);g.DrawEllipse(p,16,13,1,1);g.DrawLine(p,11,17,13,17);}
                else if(icon=="person"){using(var b=new SolidBrush(color)){g.FillEllipse(b,4,2,7,7);g.FillPie(b,0,12,15,18,180,180);}g.DrawLine(p,20,5,20,12);g.DrawLine(p,16.5f,8.5f,23.5f,8.5f);}
                else if(icon=="news"){using(var b=new SolidBrush(color)){g.FillRectangle(b,2,15,4,8);g.FillRectangle(b,9,9,4,14);g.FillRectangle(b,16,2,4,21);}}
            }g.Restore(state);
        }
        private void DrawPetPortrait(Graphics g,RectangleF box){
            var state=g.Save();
            try{
                using(var circle=new GraphicsPath()){
                    circle.AddEllipse(box);
                    g.SetClip(circle,CombineMode.Intersect);
                    g.DrawImage(Dragon,box,new RectangleF(100,54,288,288),GraphicsUnit.Pixel);
                }
            }finally{g.Restore(state);}
        }
        private void MCheck(Graphics g,float x,float y,bool failed=false){using(var b=new SolidBrush(failed?Color.IndianRed:Color.FromArgb(76,181,156)))g.FillEllipse(b,x,y,21,21);using(var p=new Pen(white,2)){p.StartCap=p.EndCap=LineCap.Round;if(failed){g.DrawLine(p,x+7,y+6,x+14,y+14);g.DrawLine(p,x+14,y+6,x+7,y+14);}else g.DrawLines(p,new[]{new PointF(x+5,y+10),new PointF(x+9,y+14),new PointF(x+16,y+6)});}}
        private void PaintCanvas(Graphics g){
            ClampPage();EnsureBackground(g);hits.Clear();resetTips.Clear();g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.DrawImage(background,new Rectangle(0,0,1024,ArtHeight));
            
            var state=g.Save();g.ScaleTransform(ReferenceFactor,ReferenceFactor);measuredPaint=true;
            try{
                if(ThemeVisual.IsReferenceCustom){g.DrawImage(Mark,new RectangleF(0,0,264,116));}else{
                using(var shade=new LinearGradientBrush(new RectangleF(0,0,719,145),Color.FromArgb(95,ThemeVisual.Current.PanelBottom),Color.FromArgb(0,ThemeVisual.Current.PanelBottom),90f))g.FillRectangle(shade,0,0,719,145);
                MText(g,"ACCOUNT DOCK",16,new RectangleF(55,15,280,26),Color.FromArgb(170,ThemeVisual.Current.PanelBottom));
                MText(g,"ACCOUNT DOCK",16,new RectangleF(54,14,280,26),Ink);
                bool customHeader=ThemeSelection.Current=="custom";
                string heading=customHeader?"账户总览":ThemeSelection.Name;
                string subtitle=customHeader?"自定义背景 · 随心而换":ThemeSelection.Subtitle;
                using(var titleFont=new Font("思源宋体 CN Heavy",46,FontStyle.Regular,GraphicsUnit.Pixel))using(var ink=new SolidBrush(Ink))using(var shadow=new SolidBrush(Color.FromArgb(180,ThemeVisual.Current.PanelBottom))){g.DrawString(heading,titleFont,shadow,new PointF(50,37),StringFormat.GenericTypographic);g.DrawString(heading,titleFont,ink,new PointF(49,36),StringFormat.GenericTypographic);}
                MText(g,subtitle,21,new RectangleF(55,106,340,30),Color.FromArgb(180,ThemeVisual.Current.PanelBottom));MText(g,subtitle,21,new RectangleF(54,105,340,30),Ink);
                }
                MButton(g,new RectangleF(442,16,81,32),"外观⌄","appearance");MButton(g,new RectangleF(534,16,57,32),"置顶","pin");
                foreach(var pair in new[]{Tuple.Create(604f,"minimize"),Tuple.Create(660f,"close")}){
                    var r=new RectangleF(pair.Item1,16,43,32);MGlass(g,RectangleF.Inflate(r,-3,-2),true);using(var p=new Pen(white,1.6f)){p.StartCap=p.EndCap=LineCap.Round;float cx=r.X+21,cy=r.Y+16;if(pair.Item2=="minimize")g.DrawLine(p,cx-6,cy,cx+6,cy);else{g.DrawLine(p,cx-5,cy-5,cx+5,cy+5);g.DrawLine(p,cx+5,cy-5,cx-5,cy+5);}}HitBox(r,pair.Item2,null);
                }
                string[] captions={"添加账号","刷新额度","错峰计划","重置 / IP 查询","AI 日报","AI 小铺"},commands={"add","refresh","stagger","reset","daily","shop"},icons={"person","refresh","calendar","bell","news","shop"};
                for(int i=0;i<6;i++)MButton(g,new RectangleF(new[]{33,254,475}[i%3],156+i/3*54,new[]{212,212,212}[i%3],48),captions[i],commands[i],null,icons[i]);
                MGlass(g,new RectangleF(33,269,653,30),false,true);MCheck(g,41,273.5f);MText(g,owner.ReferenceRefreshText,18,new RectangleF(70,269,600,30),white);
                if(!SingleAccount){MGlass(g,new RectangleF(25,304,674,CardsEnd-304),false,true);MText(g,"我的账号",26,new RectangleF(43,304,380,CardsStart-304),white);}
                if(!SingleAccount)MeasuredMode(g,526,304+(CardsStart-304-31)/2);
                else MeasuredMode(g,526,299+(CardsStart-299-31)/2);
                float y=CardsStart;string active=store.ActiveId();var accounts=store.Settings.Accounts;
                if(accounts.Count==0)MText(g,"添加账号，开始使用",26,new RectangleF(40,y,640,120),white,true,StringAlignment.Center);
                for(int i=pageIndex*2;i<Math.Min(accounts.Count,pageIndex*2+2);i++){MeasuredCard(g,accounts[i],i,y,accounts[i].Id==active);y+=MeasuredCardHeight(accounts[i])+10;}
                y=PageHeight-(SingleAccount?77:117);
                if(!SingleAccount){MButton(g,new RectangleF(200,y+8,106,31),"‹ 上一页","page-prev",null,null,pageIndex>0);MText(g,(pageIndex+1)+" / "+PageCount,23,new RectangleF(316,y+3,100,40),white,true,StringAlignment.Center);MButton(g,new RectangleF(423,y+8,107,31),"下一页 ›","page-next",null,null,pageIndex<PageCount-1);y+=40;}
#if !NO_DESKTOP_PET
                var pet=new RectangleF(25,y+10,674,50);MGlass(g,pet,true);DrawPetPortrait(g,new RectangleF(44,y+11,48,48));MText(g,"Codex 桌宠",22,new RectangleF(99,y+10,480,50),white);MText(g,"›",29,new RectangleF(654,y+10,30,50),white);HitBox(pet,"desktop-pet",null);
#endif
            }finally{measuredPaint=false;g.Restore(state);}
        }
        private void MeasuredMode(Graphics g,float x,float y){var r=new RectangleF(x,y,154,31);MGlass(g,r,true);using(var path=Round(new RectangleF(x+(owner.ReferenceDetailed?78:2),y+2,74,27),13))using(var b=new SolidBrush(Color.FromArgb(225,ThemeVisual.Blend(ThemeVisual.Current.ButtonBottom,ThemeVisual.Current.Accent,.27f))))g.FillPath(b,path);MText(g,"简洁",18,new RectangleF(x,y,77,31),white,true,StringAlignment.Center);MText(g,"详细",18,new RectangleF(x+77,y,77,31),white,true,StringAlignment.Center);HitBox(new RectangleF(x,y,77,31),"compact",null);HitBox(new RectangleF(x+77,y,77,31),"detailed",null);}
        private void MeasuredCard(Graphics g,AccountMeta a,int index,float y,bool active){
            float h=MeasuredCardHeight(a);float x=SingleAccount?20:38,w=SingleAccount?679:650;var card=new RectangleF(x,y,w,h);MGlass(g,card);HitBox(card,"select",a);
            if(active)using(var path=Round(card,19))using(var p=new Pen(Color.FromArgb(245,ThemeVisual.Current.Rim),1.7f))g.DrawPath(p,path);
            float left=x+22;
            using(var b=new LinearGradientBrush(new RectangleF(left,y+10,47,47),ThemeVisual.Blend(ThemeVisual.Current.ButtonTop,ThemeVisual.Current.Accent,.16f),ThemeVisual.Current.ButtonBottom,90f))g.FillEllipse(b,left,y+10,47,47);
            MText(g,(index+1).ToString("00"),26,new RectangleF(left,y+10,47,47),white,true,StringAlignment.Center);
            MText(g,a.Email,27,new RectangleF(x+92,y+10,w-156,36),white);
            string plan=string.IsNullOrEmpty(a.Plan)?"未知":a.Plan.ToUpperInvariant();bool crown=plan=="PLUS"||plan=="PRO";
            if(crown)MIcon(g,"crown",new RectangleF(x+95,y+62,21,19),Color.FromArgb(246,232,189));
            MText(g,plan,21,new RectangleF(x+130,y+57,76,32),white);
            if(active){var badge=new RectangleF(x+219,y+60,95,26);using(var path=Round(badge,7))using(var b=new SolidBrush(ThemeVisual.Blend(ThemeVisual.Current.Accent,white,.38f)))g.FillPath(b,path);MText(g,"当前账号",18,badge,ThemeVisual.Blend(ThemeVisual.Current.PanelBottom,Color.Black,.36f),true,StringAlignment.Center);}
            if(!SingleAccount){MeasuredArrow(g,new RectangleF(x+w-213,y+58,29,24),true,index>0,a);MeasuredArrow(g,new RectangleF(x+w-176,y+58,29,24),false,index<store.Settings.Accounts.Count-1,a);}
            MButton(g,new RectangleF(x+w-129,y+53,113,33),"账号工具 ›","tools",a);
            var remove=new RectangleF(x+w-44,y+12,28,28);
            MGlass(g,remove,true);
            using(var cross=new Pen(Color.FromArgb(255,white),2f)){
                float cx=remove.X+14,cy=remove.Y+14;
                g.DrawLine(cross,cx-5,cy-5,cx+5,cy+5);
                g.DrawLine(cross,cx+5,cy-5,cx-5,cy+5);
            }
            HitBox(remove,"delete",a);
            resetTips.Add(Tuple.Create(new RectangleF(remove.X*ReferenceFactor,remove.Y*ReferenceFactor,remove.Width*ReferenceFactor,remove.Height*ReferenceFactor),"删除账号"));
            float row=y+(owner.ReferenceDetailed?104:94);
                            string date="订阅 待读取";var s=a.SubscriptionRead;DateTimeOffset parsed;
                if(s!=null){string raw=!string.IsNullOrEmpty(s.CancelsAtUtc)?s.CancelsAtUtc:!string.IsNullOrEmpty(s.ExpiresAtUtc)?s.ExpiresAtUtc:s.RenewsAtUtc;string label=!string.IsNullOrEmpty(s.CancelsAtUtc)?"订阅结束":!string.IsNullOrEmpty(s.ExpiresAtUtc)?"权益截止":"下次续费";date=DateTimeOffset.TryParse(raw,out parsed)?label+" "+parsed.ToLocalTime().ToString("MM月dd日 HH:mm"):"订阅 官方未提供";}
            if(owner.ReferenceDetailed){
                var band=new RectangleF(x+2,row-9,w-4,37);
                using(var tint=new SolidBrush(Color.FromArgb(52,ThemeVisual.Current.PanelBottom)))g.FillRectangle(tint,band);
                MLine(g,band.Left+12,band.Top,band.Right-12,band.Top);MLine(g,band.Left+12,band.Bottom,band.Right-12,band.Bottom);
                MIcon(g,"calendar",new RectangleF(left,row,21,21),white);MText(g,date,17,new RectangleF(left+32,row-4,224,30),white);
                MLine(g,left+259,row,left+259,row+19);MIcon(g,"refresh",new RectangleF(left+277,row+1,20,20),white);MText(g,"自动读取",17,new RectangleF(left+308,row-4,85,30),white);MLine(g,left+393,row,left+393,row+19);
                MIcon(g,"layers",new RectangleF(left+416,row,21,21),white);long token=a.AccountLifetimeTokens;string value=token<0?"待刷新":token>=100000000?(token/100000000d).ToString("0.##")+"亿":token>=10000?(token/10000d).ToString("0.#")+"万":token.ToString("N0");MText(g,"本账号Token "+value,17,new RectangleF(left+447,row-4,w-482,30),white);
                HitBox(new RectangleF(left,row-4,w-40,30),"tools",a);row+=32;
            }
            // The quota area is partitioned by its real boundaries, not text origins.
            float foot=y+h-40;
            float quotaTop=owner.ReferenceDetailed?row-4:row;
            float quotaBottom=foot-5;
            var meters=Meters(a);
            float pitch=(quotaBottom-quotaTop)/Math.Max(1,meters.Count);
            if(!owner.ReferenceDetailed)MLine(g,left,quotaTop,x+w-22,quotaTop);
            if(meters.Count==0)MText(g,"点击刷新，读取本账号额度",20,new RectangleF(left,quotaTop,w-44,pitch),white);
            for(int i=0;i<meters.Count;i++){
                float top=quotaTop+i*pitch;
                float center=top+pitch/2;
                float contentTop=center-28;
                var meter=meters[i];
                MText(g,meter.Item1,21,new RectangleF(left+19,contentTop,180,28),white);
                var bar=new RectangleF(x+209,center-10,w-326,20);
                MeasuredBar(g,bar,meter.Item2);
                MText(g,Palette.Percent(meter.Item2),25,new RectangleF(x+w-105,center-18,69,36),white,true,StringAlignment.Far);
                string countdown=meter.Item3<=0?"恢复时间待查询":meter.Item3<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()?"已到恢复时间 · 待刷新":Palette.CompactCountdown(meter.Item3)+"后恢复";
                MText(g,owner.ReferenceDetailed?countdown:ResetClock(meter.Item3),18,new RectangleF(left+19,center,180,28),white);
                resetTips.Add(Tuple.Create(new RectangleF(left*ReferenceFactor,top*ReferenceFactor,(w-44)*ReferenceFactor,pitch*ReferenceFactor),meter.Item1+"："+(owner.ReferenceDetailed?Palette.Countdown(meter.Item3):ResetClock(meter.Item3))));
                MLine(g,left,top+pitch,x+w-22,top+pitch);
            }
            MLine(g,left,foot-5,x+w-22,foot-5);
            string state=owner.ReferenceRefreshState(a.Id);bool failed=state=="failed"||(state!="success"&&!string.IsNullOrEmpty(a.LastError));MCheck(g,left-5,foot+7,failed);
            string status=state=="updating"?"正在刷新…":state=="waiting"?"等待刷新":failed?"刷新失败 · 保留记录":"上次更新 "+TimeOnly(a);
            MText(g,status,17,new RectangleF(left+28,foot,145,35),white);
            if(owner.ReferenceDetailed){MLine(g,left+173,foot+10,left+173,foot+24);MText(g,a.ResetCredits<0?"重置待查询":"可重置 "+a.ResetCredits+" 次",18,new RectangleF(left+186,foot,100,35),white);MLine(g,left+290,foot+10,left+290,foot+24);}
            var details=owner.ReferenceDetailed?new RectangleF(left+300,foot,125,35):new RectangleF(left+165,foot,w-370,35);MText(g,owner.ReferenceDetailed?"重置卡 ›":date,17,details,white,true,StringAlignment.Center);HitBox(details,owner.ReferenceDetailed?"reset-credits":"tools",a);
            MButton(g,new RectangleF(x+w-179,foot+2,76,32),active?"当前":"切换","switch",a,null,!active);MButton(g,new RectangleF(x+w-91,foot+2,76,32),owner.ReferenceBusy?"稍候":"刷新","refresh-one",a);
        }
        private void MLine(Graphics g,float x1,float y1,float x2,float y2){using(var p=new Pen(Color.FromArgb(110,white),1))g.DrawLine(p,x1,y1,x2,y2);}
        private void MeasuredArrow(Graphics g,RectangleF r,bool up,bool enabled,AccountMeta a){MGlass(g,r,true);using(var p=new Pen(Color.FromArgb(enabled?255:120,white),1.7f)){float x=r.X+r.Width/2,y=r.Y+r.Height/2;g.DrawLines(p,new[]{new PointF(x-5,y+(up?3:-3)),new PointF(x,y+(up?-3:3)),new PointF(x+5,y+(up?3:-3))});}if(enabled)HitBox(r,up?"sort-up":"sort-down",a);}
        private void MeasuredBar(Graphics g,RectangleF r,double value){using(var path=Round(r,10))using(var b=new SolidBrush(Color.FromArgb(180,ThemeVisual.Current.PanelBottom)))using(var p=new Pen(Color.FromArgb(210,ThemeVisual.Current.Rim),1)){g.FillPath(b,path);g.DrawPath(p,path);}if(value<=0)return;var fill=new RectangleF(r.X,r.Y,Math.Max(1,r.Width*(float)Math.Min(100,value)/100),r.Height);using(var path=Round(fill,10))using(var b=new LinearGradientBrush(fill,ThemeVisual.Current.BarFrom,ThemeVisual.Current.BarTo,0f))g.FillPath(b,path);}

        private void Summary(Graphics g)
        {
            Glass(g,new RectangleF(60,224,904,108),21,false);
            int total=store.Settings.Accounts.Count,available=store.Settings.Accounts.Count(a=>Palette.AllPresentQuotaAvailable(a));
            int unknown=store.Settings.Accounts.Count(a=>!Palette.HasAnyQuota(a) || !string.IsNullOrEmpty(a.LastError));
            string[] labels={"已登记","上次可用","待确认"};int[] numbers={total,available,unknown};
            for(int i=0;i<3;i++)
            {
                float x=90+i*302;
                if(i>0)using(Pen line=new Pen(Color.FromArgb(180,white),1.3f))g.DrawLine(line,x-49,247,x-49,314);
                using(SolidBrush fill=new SolidBrush(Color.FromArgb(145,ThemeVisual.Current.Accent)))g.FillEllipse(fill,x,250,60,60);
                Icon(g,i==0?"person":i==1?"database":"clock",new RectangleF(x+15,265,30,30));
                Text(g,labels[i],26,new RectangleF(x+87,239,190,36),white,true,false,false);
                Text(g,numbers[i].ToString("00"),52,new RectangleF(x+85,269,170,56),white,true,false,false);
            }
        }
        private void Banner(Graphics g)
        {
            RectangleF r=new RectangleF(60,491,904,56);
            bool custom=ThemeSelection.Current=="custom";ThemeLook look=ThemeVisual.Current;
            // Card status strips already sit over glass panels. Give the standalone
            // completion strip a matching dark backing so pale wallpaper cannot wash it out.
            using(GraphicsPath path=Round(r,17))
            using(SolidBrush backing=new SolidBrush(Color.FromArgb(AppearanceOptions.GlassAlpha(150),look.PanelBottom)))
            using(LinearGradientBrush fill=new LinearGradientBrush(r,Color.FromArgb(175,look.SuccessTop),Color.FromArgb(150,look.SuccessBottom),0f))
            using(Pen rim=new Pen(Color.FromArgb(220,look.Rim),1.3f)){g.FillPath(backing,path);g.FillPath(fill,path);g.DrawPath(rim,path);}
            StatusIcon(g,85,504,false,28);
            Text(g,owner.ReferenceRefreshText,28,new RectangleF(142,492,751,53),white,true,false,false);
            Center(g,"›",35,new RectangleF(917,493,38,50),false);
        }
        private void Mode(Graphics g)
        {
            RectangleF r=new RectangleF(726,562,214,42);Glass(g,r,19,true);
            bool custom=ThemeSelection.Current=="custom";ThemeLook look=ThemeVisual.Current;
            using(GraphicsPath path=Round(new RectangleF(owner.ReferenceDetailed?833:729,565,104,36),17))using(LinearGradientBrush fill=new LinearGradientBrush(new Rectangle(729,565,208,36),Color.FromArgb(210,look.Accent),Color.FromArgb(225,look.ButtonBottom),90f))g.FillPath(fill,path);
            Center(g,"紧凑",24,new RectangleF(728,562,105,42),!owner.ReferenceDetailed);Center(g,"详细",24,new RectangleF(833,562,105,42),owner.ReferenceDetailed);
            HitBox(new RectangleF(726,562,107,42),"compact",null);HitBox(new RectangleF(833,562,107,42),"detailed",null);
        }
        private void Pagination(Graphics g)
        {
            int pages=PageCount;bool canBack=pageIndex>0,canNext=pageIndex<pages-1;
            RectangleF previous=new RectangleF(246,1232,202,58),next=new RectangleF(578,1232,202,58);
            PageButton(g,previous,"‹  上一页",canBack,"page-prev");
            Center(g,(pageIndex+1)+" / "+pages,32,new RectangleF(452,1232,122,58),true);
            PageButton(g,next,"下一页  ›",canNext,"page-next");
        }
        private void PageButton(Graphics g,RectangleF r,string label,bool enabled,string command)
        {
            Glass(g,r,17,true);
            Center(g,label,28,r,true);
            if(!enabled)
            {
                using(GraphicsPath path=Round(r,17))using(SolidBrush veil=new SolidBrush(Color.FromArgb(94,ThemeVisual.Current.PanelBottom)))g.FillPath(veil,path);
            }
            else HitBox(r,command,null);
        }
        private void Bar(Graphics g,RectangleF r,double remaining)
        {
            bool custom=ThemeSelection.Current=="custom";ThemeLook look=ThemeVisual.Current;
            using(GraphicsPath path=Round(r,9))using(SolidBrush bg=new SolidBrush(Color.FromArgb(155,look.PanelBottom)))using(Pen rim=new Pen(Color.FromArgb(100,white))) {g.FillPath(bg,path);g.DrawPath(rim,path);}
            if(remaining<=0)return;RectangleF fill=new RectangleF(r.X,r.Y,Math.Max(2,r.Width*(float)Math.Min(100,remaining)/100),r.Height);
            using(GraphicsPath path=Round(fill,9))using(LinearGradientBrush brush=new LinearGradientBrush(fill,look.BarFrom,look.BarTo,0f))using(Pen rim=new Pen(Color.FromArgb(220,white))) {g.FillPath(brush,path);g.DrawPath(rim,path);}
        }
        private void StatusIcon(Graphics g,float x,float y,bool failed,float size)
        {
            using(SolidBrush b=new SolidBrush(white))g.FillEllipse(b,x,y,size,size);
            using(Pen p=new Pen(failed?Color.FromArgb(173,59,79):ThemeVisual.Current.SuccessBottom,2.8f))
            {if(failed){g.DrawLine(p,x+size/2,y+6,x+size/2,y+size-12);g.DrawEllipse(p,x+size/2-1,y+size-7,2,2);}else g.DrawLines(p,new[]{new PointF(x+6,y+size/2),new PointF(x+size*.43f,y+size*.72f),new PointF(x+size-5,y+7)});}
        }
        private string TimeOnly(AccountMeta a){DateTime value;return DateTime.TryParse(a.LastQuotaAt,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out value)?value.ToLocalTime().ToString("HH:mm"):"尚未查询";}
        private void Card(Graphics g,AccountMeta a,int index,float y,bool active)
        {
            RectangleF card=new RectangleF(56,y,912,294);Glass(g,card,25,true);HitBox(card,"select",a);
            bool custom=ThemeSelection.Current=="custom";ThemeLook look=ThemeVisual.Current;
            using(LinearGradientBrush badge=new LinearGradientBrush(new RectangleF(80,y+18,74,74),look.Accent,ThemeVisual.Blend(look.ButtonTop,look.Accent,.45f),90f))g.FillEllipse(badge,80,y+18,74,74);
            Center(g,(index+1).ToString("00"),34,new RectangleF(80,y+18,74,74),true);
            float emailSize=35;
            while(emailSize>30 && MixedWidth(g,a.Email,emailSize,true)>455)emailSize-=.5f;
            Text(g,a.Email,emailSize,new RectangleF(180,y+14,455,52),white,true,false,false);
            SortArrow(g,new RectangleF(650,y+18,44,38),true,index>0,a);
            SortArrow(g,new RectangleF(702,y+18,44,38),false,index<store.Settings.Accounts.Count-1,a);
            string plan=string.IsNullOrEmpty(a.Plan)?"计划未知":a.Plan.ToUpperInvariant();
            bool isFreePlan=string.Equals(a.Plan,"free",StringComparison.OrdinalIgnoreCase);
            if(active && !isFreePlan)
            {
                using(Pen gold=new Pen(Color.FromArgb(255,216,139),2.2f))
                using(SolidBrush light=new SolidBrush(Color.FromArgb(105,255,209,110)))
                {
                    // Match the optical center of the PLUS text row (y+64, height 42).
                    PointF[] crown={new PointF(182,y+79),new PointF(189,y+85),new PointF(194,y+75),new PointF(200,y+85),new PointF(207,y+79),new PointF(203,y+92),new PointF(186,y+92)};
                    g.FillPolygon(light,crown);g.DrawPolygon(gold,crown);g.DrawLine(gold,187,y+96,203,y+96);
                    foreach(PointF point in new[]{crown[0],crown[2],crown[4]})g.DrawEllipse(gold,point.X-1.6f,point.Y-1.6f,3.2f,3.2f);
                }
            }
            DateTime expiry=DateTime.MinValue;
            bool expiryHasTime;
            bool hasExpiry=ExpiryDateTimeValue.TryParse(a.SubscriptionExpiresOn,out expiry,out expiryHasTime);
            string planText=plan+" · "+(active?"当前账户":"已登记");
            float planX=active && !isFreePlan?220:180,planWidth=(owner.ReferenceDetailed?480:730)-planX-20,planSize=28;
            while(planSize>24 && MixedWidth(g,planText,planSize,true)>planWidth)planSize-=.5f;
            Text(g,planText,planSize,new RectangleF(planX,y+64,planWidth,42),white,true,false,false);
            if(owner.ReferenceDetailed) {
                RectangleF expiryBox=new RectangleF(480,y+64,260,44);
                string expiryText=SubscriptionDisplay.Compact(a);
                if(string.IsNullOrEmpty(expiryText)) expiryText=hasExpiry?"到期已设 · 工具":"订阅 / 邀请 / 模型";
                Text(g,expiryText,24,expiryBox,white,true,false,true);
                HitBox(expiryBox,"tools",a);
            }
            Center(g,"×",34,new RectangleF(918,y+13,38,38),false);using(Pen rim=new Pen(Color.FromArgb(215,white),1.7f))g.DrawEllipse(rim,918,y+13,38,38);HitBox(new RectangleF(912,y+7,50,50),"delete",a);
            double mainRemaining=Palette.MainRemaining(a);
            Text(g,Palette.Percent(mainRemaining),62,new RectangleF(764,y+28,162,75),white,true,false,true);
            Text(g,Palette.MainQuotaLabel(a),28,new RectangleF(742,y+91,184,40),white,true,false,true);
            var quotaRows=new List<Tuple<string,double>>();
            if(Palette.HasPrimary(a))quotaRows.Add(Tuple.Create("5 小时额度",a.PrimaryRemaining));
            if(Palette.IsPro(a) || Palette.HasSecondary(a))quotaRows.Add(Tuple.Create("周额度",a.SecondaryRemaining));
            if(Palette.IsFree(a))quotaRows.Add(Tuple.Create("月额度",Palette.HasMonthly(a)?a.MonthlyRemaining:-1));
            if(quotaRows.Count==0)
                Text(g,"官方未返回可显示的额度窗口",25,new RectangleF(84,y+143,580,43),white,true,false,false);
            else
            {
                float pitch=quotaRows.Count>=3?34:46;
                float first=quotaRows.Count==1?148:quotaRows.Count==2?117:111;
                for(int row=0;row<quotaRows.Count;row++)
                {
                    float rowY=y+first+row*pitch;
                    Text(g,quotaRows[row].Item1,quotaRows.Count>=3?25:28,new RectangleF(84,rowY,105,38),white,true,false,false);
                    Bar(g,new RectangleF(195,rowY+12,425,quotaRows.Count>=3?19:24),quotaRows[row].Item2);
                    long resetAt=quotaRows[row].Item1=="周额度"?a.SecondaryResetAt:quotaRows[row].Item1=="月额度"?a.MonthlyResetAt:a.PrimaryResetAt;
                    string exact=resetAt<=0?"官方尚未返回重置时间":DateTimeOffset.FromUnixTimeSeconds(resetAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz",CultureInfo.InvariantCulture);
                    resetTips.Add(Tuple.Create(new RectangleF(195,rowY,425,43),quotaRows[row].Item1+"："+Palette.Countdown(resetAt)+"\r\n"+exact+"\r\n额度以刷新结果为准"));
                    Text(g,Palette.Percent(quotaRows[row].Item2),quotaRows.Count>=3?25:28,new RectangleF(636,rowY+2,83,38),white,true,false,false);
                }
            }
            RectangleF button=new RectangleF(746,y+136,97,65);Glass(g,button,17,true);Center(g,active?"当前":"切换",28,button,true);HitBox(button,"switch",a);
            RectangleF refreshOne=new RectangleF(851,y+136,97,65);Glass(g,refreshOne,17,true);Center(g,owner.ReferenceBusy?"稍候":"刷新",28,refreshOne,true);HitBox(refreshOne,"refresh-one",a);
            string state=owner.ReferenceRefreshState(a.Id);bool planPending=a.LastError==DockContext.PlanUpdatedQuotaUnavailable;bool failed=!planPending && (state=="failed" || (!string.IsNullOrEmpty(a.LastError) && state!="success"));bool cached=Palette.HasAnyQuota(a);
            RectangleF strip=new RectangleF(68,y+222,888,60);
            using(GraphicsPath path=Round(strip,17))using(LinearGradientBrush fill=new LinearGradientBrush(strip,Color.FromArgb(175,failed?Color.FromArgb(176,74,97):look.SuccessTop),Color.FromArgb(150,failed?Color.FromArgb(128,69,99):look.SuccessBottom),0f))using(Pen rim=new Pen(Color.FromArgb(195,failed?Color.LightPink:look.Rim),1.3f)){g.FillPath(fill,path);g.DrawPath(rim,path);}
            StatusIcon(g,90,y+237,failed,30);
            string result=planPending?"已更新为 "+plan+" · 额度待刷新":failed?"更新失败 · "+(cached?"保留上次额度":"暂无额度数据"):state=="updating"?"正在更新…":state=="waiting"?"等待更新":(state=="success"?"更新成功 · ":"上次更新 · ")+TimeOnly(a);
            var parts=new List<string>{result};
            if(planPending)parts.Add("额度不可用 · 请稍后刷新");
            else if(failed)parts.Add(cached?"上次成功 "+TimeOnly(a):"额度未知 · 请稍后刷新");
            else
            {
                long visibleReset=Palette.VisibleResetAt(a,owner.ReferenceDetailed);
                string visibleLabel=Palette.VisibleResetLabel(a,owner.ReferenceDetailed);
                parts.Add(visibleLabel+" "+Palette.CompactCountdown(visibleReset));
                string resetExact=visibleReset<=0?"官方尚未返回重置时间":DateTimeOffset.FromUnixTimeSeconds(visibleReset).ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz",CultureInfo.InvariantCulture);
                resetTips.Add(Tuple.Create(new RectangleF(390,y+224,360,56),(Palette.IsFree(a)?"月额度":Palette.WeeklyReset(a,owner.ReferenceDetailed)?"周额度":"5 小时额度")+"重置："+Palette.Countdown(visibleReset)+"\r\n"+resetExact));
                parts.Add(a.ResetCredits<0?"重置卡未知":"可重置 "+a.ResetCredits+" 次");
            }
            float fontSize=28;float[] widths;
            while(true)
            {
                using(Font f=new Font(PreviewBodyFamily??"楷体",fontSize,FontStyle.Bold,GraphicsUnit.Pixel))
                    widths=parts.Select(t=>(UseTimesKai?MixedWidth(g,t,fontSize,true):g.MeasureString(t,f,2000,StringFormat.GenericTypographic).Width)+5).ToArray();
                if(widths.Sum()+18*(parts.Count-1)<=797 || fontSize<=24)break;
                fontSize-=.5f;
            }
            float left=140,gap=parts.Count>1?Math.Max(18,(797-widths.Sum())/(parts.Count-1)):0;
            for(int i=0;i<parts.Count;i++)
            {
                Text(g,parts[i],fontSize,new RectangleF(left,y+224,widths[i],56),white,true,false,false);
                left+=widths[i];
                if(i<parts.Count-1)
                {using(Pen divider=new Pen(Color.FromArgb(165,white),1.5f))g.DrawLine(divider,left+gap/2,y+236,left+gap/2,y+269);left+=gap;}
            }
        }
        private void Detail(Graphics g,AccountMeta a)
        {
            Glass(g,new RectangleF(56,1314,912,134),23,true);
            Text(g,"账户详情",32,new RectangleF(82,1328,156,50),white,true,true,false);
            var label=new List<string>();var value=new List<string>();
            if(a!=null && Palette.HasPrimary(a)){label.Add("5 小时额度");value.Add(Palette.Percent(a.PrimaryRemaining));}
            if(a!=null && (Palette.IsPro(a) || Palette.HasSecondary(a))){label.Add("周额度");value.Add(Palette.Percent(a.SecondaryRemaining));}
            if(a!=null && Palette.IsFree(a)){label.Add("月额度");value.Add(Palette.Percent(Palette.HasMonthly(a)?a.MonthlyRemaining:-1));}
            label.Add("可重置");value.Add(a==null||a.ResetCredits<0?"未知":a.ResetCredits+" 次");
            label.Add("账号 Token");
            long tokens=a==null?-1:a.AccountLifetimeTokens;
            string token=tokens<0?"待刷新":tokens>=100000000?(tokens/100000000d).ToString("0.00")+" 亿":tokens>=10000?(tokens/10000d).ToString("0.0")+" 万":tokens.ToString("N0");
            value.Add(token);
            float metricsLeft=250,metricWidth=(708f/label.Count);
            for(int i=0;i<label.Count;i++)
            {
                float x=metricsLeft+i*metricWidth+30;
                float separator=metricsLeft+i*metricWidth;
                using(Pen p=new Pen(Color.FromArgb(175,white),1.4f))g.DrawLine(p,separator,1334,separator,1428);
                bool tokenMetric=label[i]=="账号 Token";
                Text(g,label[i],label.Count>=5?23:26,new RectangleF(x,1328,metricWidth-35,40),white,true,false,false);
                Text(g,value[i],tokenMetric?30:label.Count>=5?40:46,new RectangleF(x,1367,metricWidth-35,61),white,true,false,false);
            }
            HitBox(new RectangleF(metricsLeft+(label.Count-1)*metricWidth,1323,metricWidth,112),"tokens",null);
        }
        private void SortArrow(Graphics g,RectangleF r,bool up,bool enabled,AccountMeta account)
        {
            Glass(g,r,12,true);
            using(Pen pen=new Pen(Color.FromArgb(enabled?255:75,white),2.7f))
            {
                float cx=r.X+r.Width/2,cy=r.Y+r.Height/2;
                g.DrawLines(pen,new[]{new PointF(cx-8,cy+(up?4:-4)),new PointF(cx,cy+(up?-4:4)),new PointF(cx+8,cy+(up?4:-4))});
            }
            if(enabled)HitBox(r,up?"sort-up":"sort-down",account);
        }
        private void MoveAccount(AccountMeta account,int delta)
        {
            var list=store.Settings.Accounts;
            int current=list.FindIndex(a=>a.Id==account.Id),target=current+delta;
            if(current<0 || target<0 || target>=list.Count)return;
            var moving=list[current];list.RemoveAt(current);list.Insert(target,moving);
            try{store.Save();}
            catch(Exception ex){list.RemoveAt(target);list.Insert(current,moving);MessageBox.Show(owner,"保存排序失败："+ex.Message);return;}
            ChangePage(target/2);Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);Focus();PointF p=Logical(e.Location);
#if !NO_DESKTOP_PET
            if(e.Button==MouseButtons.Right && hits.Any(h=>h.Command=="desktop-pet" && h.Box.Contains(p))){if(!store.IsPreview)DesktopPet.MissingMenu(owner);return;}
#endif
            if(e.Button==MouseButtons.Right){Hit card=hits.LastOrDefault(h=>h.Account!=null && h.Box.Contains(p));if(card!=null)owner.ReferenceCommand("account-menu",card.Account);return;}
            if(e.Button!=MouseButtons.Left)return;
            Hit hit=hits.LastOrDefault(h=>h.Box.Contains(p));
            if(hit!=null)
            {
                if(hit.Command=="sort-up" || hit.Command=="sort-down")MoveAccount(hit.Account,hit.Command=="sort-up"?-1:1);
                else if(hit.Command=="page-prev")ChangePage(pageIndex-1);
                else if(hit.Command=="page-next")ChangePage(pageIndex+1);
                else owner.ReferenceCommand(hit.Command,hit.Account);
            }
            else if(p.Y<220)Native.BeginWindowDrag(owner.Handle);
        }
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);PointF p=Logical(e.Location);Cursor=hits.Any(h=>h.Box.Contains(p))?Cursors.Hand:Cursors.Default;var match=resetTips.LastOrDefault(t=>t.Item1.Contains(p));string next=match==null?"":match.Item2;if(next!=lastResetTip){lastResetTip=next;resetTip.SetToolTip(this,next);}}
        protected override void OnMouseLeave(EventArgs e){lastResetTip="";resetTip.SetToolTip(this,"");base.OnMouseLeave(e);}
        protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);if(e.Delta!=0)ChangePage(pageIndex+(e.Delta<0?1:-1));}
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key=keyData&Keys.KeyCode;
            if(key==Keys.PageUp || key==Keys.PageDown || key==Keys.Home || key==Keys.End)return true;
            return base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if(e.KeyCode==Keys.PageUp)ChangePage(pageIndex-1);
            else if(e.KeyCode==Keys.PageDown)ChangePage(pageIndex+1);
            else if(e.KeyCode==Keys.Home)ChangePage(0);
            else if(e.KeyCode==Keys.End)ChangePage(PageCount-1);
            else return;
            e.Handled=true;e.SuppressKeyPress=true;
        }
        public void SaveReferencePreview(string path)
        {
            using(Bitmap image=new Bitmap(1024,ArtHeight))using(Graphics g=Graphics.FromImage(image))
            {
                PaintCanvas(g);image.Save(path,ImageFormat.Png);
                using(Bitmap header=image.Clone(new Rectangle(0,0,1024,218),image.PixelFormat))header.Save(Path.Combine(Path.GetDirectoryName(path),"v3-header.png"),ImageFormat.Png);
                if(image.Height>1440 && PreviewBodyFamily!=null)
                {
                    using(Bitmap crop=image.Clone(new Rectangle(70,1320,884,120),image.PixelFormat))
                        crop.Save(Path.Combine(Path.GetDirectoryName(path),"font-detail.png"),ImageFormat.Png);
                }
            }
            Size previous=Size;DockStyle oldDock=Dock;Dock=DockStyle.None;
            try
            {
                Size=previous;
                using(Bitmap window=new Bitmap(Width,Height))using(Graphics graphics=Graphics.FromImage(window))
                using(PaintEventArgs paint=new PaintEventArgs(graphics,ClientRectangle))
                {
                    OnPaint(paint);
                    window.Save(Path.Combine(Path.GetDirectoryName(path),"dashboard-window.png"),ImageFormat.Png);
                }
            }
            finally{Size=previous;Dock=oldDock;}
            if(Environment.GetCommandLineArgs().Contains("--dpi-preview"))SaveDpiPreviews(Path.GetDirectoryName(path));
        }
        private void SaveDpiPreviews(string directory)
        {
            Size previous=Size;
            var measurements=new List<object>();
            DockStyle previousDock=Dock;Dock=DockStyle.None;
            try
            {
                foreach(int dpi in new[]{96,120,144,192})
                {
                    Size=DpiSupport.FitWindow(new Rectangle(0,0,3840,2160),dpi);
                    using(Bitmap image=new Bitmap(Width,Height))using(Graphics g=Graphics.FromImage(image))
                    using(PaintEventArgs paint=new PaintEventArgs(g,ClientRectangle))
                    {OnPaint(paint);image.Save(Path.Combine(directory,"dashboard-dpi-"+dpi+".png"),ImageFormat.Png);}
                    measurements.Add(new{simulated_dpi=dpi,pixel_width=Width,pixel_height=Height});
                }
                File.WriteAllText(Path.Combine(directory,"dpi-render.json"),new JavaScriptSerializer().Serialize(new{kind="physical_canvas_size_simulation_not_monitor_setting_change",renders=measurements}));
            }
            finally{Size=previous;Dock=previousDock;Invalidate();}
        }
        public bool VerifyLayout(){
            if(!store.IsPreview)return false;
            var saved=store.Settings.Accounts;int page=pageIndex;
            try{for(int count=0;count<=5;count++){
                store.Settings.Accounts=Enumerable.Range(0,count).Select(i=>new AccountMeta{Id="v3-test-"+i,Email="test"+i+"@example.com",Plan=i%3==0?"plus":i%3==1?"pro":"free",PrimaryRemaining=70,SecondaryRemaining=40}).ToList();
                for(pageIndex=0;pageIndex<PageCount;pageIndex++)using(var bitmap=new Bitmap(1024,ArtHeight))using(var g=Graphics.FromImage(bitmap)){
                    PaintCanvas(g);var bounds=new RectangleF(0,0,1024,ArtHeight);
                    if(hits.Any(h=>!bounds.Contains(h.Box)))return false;
                    var visible=store.Settings.Accounts.Skip(pageIndex*2).Take(2).ToArray();
                    foreach(var a in visible){
                        foreach(string command in (owner.ReferenceDetailed?new[]{"tools","refresh-one","account-menu","delete"}:new[]{"tools","refresh-one","delete"}))if(!hits.Any(h=>h.Account==a&&h.Command==command))return false;
                        if(Palette.IsPro(a)&&Meters(a).Any(m=>m.Item1.StartsWith("短")))return false;
                    }
#if NO_DESKTOP_PET
                    if(hits.Any(h=>h.Command=="desktop-pet"))return false;
#else
                    if(hits.Count(h=>h.Command=="desktop-pet")!=1)return false;
#endif
                }
            }return true;}finally{store.Settings.Accounts=saved;pageIndex=page;Invalidate();}
        }
        public bool VerifyLegacyLayout()

        {
            if(!store.IsPreview)return false;
            var originalAccounts=store.Settings.Accounts;
            int originalPage=pageIndex;Size originalSize=Size;
            using(Bitmap image=new Bitmap(1024,1536))using(Graphics g=Graphics.FromImage(image))
            {
                try
                {
                    var fixtures=Enumerable.Range(0,5).Select(i=>new AccountMeta {Id="layout-"+i,Email=i==4?"long.email.for.layout.check@example.com":"layout"+i+"@example.com",Plan="plus",PrimaryRemaining=i==0?100:i==4?-1:70,SecondaryRemaining=82,ResetCredits=i,LastQuotaAt=DateTime.UtcNow.ToString("o"),PrimaryResetAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+5*3600,SubscriptionExpiresOn=DateTime.Today.AddDays(150).ToString("yyyy-MM-dd")}).ToList();
                    fixtures[1].SecondaryResetAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+5*86400+10*3600;
                    fixtures[2].SecondaryResetAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()-60;
                    fixtures[3].Plan="free";fixtures[3].MonthlyRemaining=100;fixtures[3].HasMonthlyLimit=true;fixtures[3].MonthlyResetAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+30*86400;
                    fixtures[4].Plan="pro";fixtures[4].PrimaryRemaining=0;fixtures[4].SecondaryResetAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+4*86400;
                    RectangleF viewport=new RectangleF(48,500,928,ArtHeight-580);
                    for(int count=0;count<=5;count++)
                    {
                        store.Settings.Accounts=fixtures.Take(count).ToList();
                        int pages=PageCountFor(count);
                        if(pages!=Math.Max(1,(count+1)/2) || ClampPageFor(-2,count)!=0 || ClampPageFor(pages+2,count)!=pages-1)return false;
                        for(int page=0;page<pages;page++)
                        {
                            pageIndex=page;PaintCanvas(g);
                            foreach(AccountMeta shown in store.Settings.Accounts.Skip(page*2).Take(2))
                            {
                                // Visible reset captions must follow the available official meters,
                                // including unknown/elapsed time and Free's monthly-only display.
                                if(shown.Id=="layout-0" && !resetTips.Any(t=>t.Item2.StartsWith("周额度：时间未知")))return false;
                                if(shown.Id=="layout-1" && !resetTips.Any(t=>t.Item2.StartsWith("周额度：5天")))return false;
                                if(shown.Id=="layout-2" && !resetTips.Any(t=>t.Item2.StartsWith("周额度：已到重置时间，待刷新")))return false;
                                if(shown.Id=="layout-3" && !resetTips.Any(t=>t.Item2.StartsWith("月额度：")))return false;
                            }
                            if(store.Settings.Accounts.Skip(page*2).Take(2).Any(a=>Palette.IsPro(a)) && (!resetTips.Any(t=>t.Item2.StartsWith("周额度重置：")) || resetTips.Any(t=>t.Item2.StartsWith("5 小时额度"))))return false;
                            if(store.Settings.Accounts.Skip(page*2).Take(2).All(a=>Palette.IsFree(a)) && resetTips.Any(t=>t.Item2.StartsWith("周额度：")))return false;
                            string[] expected=store.Settings.Accounts.Skip(page*2).Take(2).Select(a=>a.Id).ToArray();
                            string[] actual=hits.Where(h=>h.Account!=null).Select(h=>h.Account.Id).Distinct().ToArray();
                            if(!expected.SequenceEqual(actual) || hits.Any(h=>h.Box.Width<=0 || h.Box.Height<=0))return false;
                            if(hits.Where(h=>h.Account!=null).Any(h=>!viewport.Contains(h.Box)))return false;
                            if(hits.Count(h=>h.Command=="refresh-one")!=expected.Length)return false;
                            foreach(Hit single in hits.Where(h=>h.Command=="refresh-one"))
                            {
                                if(single.Account==null || hits.Any(h=>h.Command!="select" && h!=single && h.Box.IntersectsWith(single.Box)))return false;
                            }
                            if(hits.Count(h=>h.Command=="select")!=expected.Length || hits.Count(h=>h.Command=="switch")!=expected.Length || hits.Count(h=>h.Command=="delete")!=expected.Length)return false;
                            if(hits.Any(h=>h.Command=="page-prev")!=(page>0) || hits.Any(h=>h.Command=="page-next")!=(page<pages-1))return false;
                            foreach(Size size in new[]{new Size(573,860),new Size(384,576),new Size(320,480),new Size(716,1075),new Size(860,1290),new Size(1146,1720)})
                            {
                                Size=size;float scaleX=CanvasScaleX,scaleY=CanvasScaleY;
                                foreach(Hit hit in hits)
                                {
                                    float cx=hit.Box.X+hit.Box.Width/2,cy=hit.Box.Y+hit.Box.Height/2;
                                    Point point=new Point((int)Math.Round(cx*scaleX),(int)Math.Round(cy*scaleY));
                                    PointF logical=Logical(point);
                                    if(Math.Abs(logical.X-cx)>2 || Math.Abs(logical.Y-cy)>2)return false;
                                    Hit mapped=hits.LastOrDefault(h=>h.Box.Contains(logical));
                                    if(mapped==null || (hit.Command!="select" && mapped!=hit))return false;
                                }
                            }
                            Hit next=hits.LastOrDefault(h=>h.Command=="page-next");
                            if(next!=null)
                            {
                                float scaleX=CanvasScaleX,scaleY=CanvasScaleY;
                                Point point=new Point((int)((next.Box.X+next.Box.Width/2)*scaleX),(int)((next.Box.Y+next.Box.Height/2)*scaleY));
                                OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0));
                                if(pageIndex!=page+1)return false;
                            }
                        }
                        OnKeyDown(new KeyEventArgs(Keys.End));if(pageIndex!=pages-1)return false;
                        OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,0,0,-120));if(pageIndex!=pages-1)return false;
                        OnKeyDown(new KeyEventArgs(Keys.Home));if(pageIndex!=0)return false;
                        OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,0,0,120));if(pageIndex!=0)return false;
                        OnKeyDown(new KeyEventArgs(Keys.PageDown));if(pageIndex!=Math.Min(1,pages-1))return false;
                        OnKeyDown(new KeyEventArgs(Keys.PageUp));if(pageIndex!=0)return false;
                        pageIndex=pages-1;
                        store.Settings.Accounts=fixtures.Take(Math.Max(0,count-1)).ToList();RefreshView();
                        if(pageIndex!=PageCountFor(Math.Max(0,count-1))-1)return false;
                    }
                    if(MixedWidth(g,"100%",62,true)>162)return false;
                    string[] successParts={"更新成功 · 16:14","2小时47分重置","可重置 999 次"};
                    if(successParts.Sum(t=>MixedWidth(g,t,24,true)+5)+36>797)return false;
                    string[] failureParts={"更新失败 · 保留上次额度","上次成功 16:14"};
                    if(failureParts.Sum(t=>MixedWidth(g,t,24,true)+5)+18>797)return false;
                    return true;
                }
                finally{store.Settings.Accounts=originalAccounts;pageIndex=originalPage;Size=originalSize;PaintCanvas(g);}
            }
        }
        protected override void Dispose(bool disposing){if(disposing){resetClock.Dispose();resetTip.Dispose();if(background!=null)background.Dispose();}base.Dispose(disposing);}
    }
    internal static class LocalTokenTotals
    {
        private static bool loaded;
        private static long total;
        private static int sessions,failed;
        private static bool reading;
        private static string Stamp="";
        private static string Cache {get{return Path.Combine(PortablePaths.Data,"local-token-summary.json");}}
        private static void Load()
        {
            if(loaded)return;loaded=true;
            try{var item=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Cache));total=Convert.ToInt64(item["total"]);sessions=Convert.ToInt32(item["sessions"]);Stamp=Convert.ToString(item["at"]);}catch{}
        }
        internal static string Display {get{Load();return reading?"正在读取…":sessions==0?"点击读取本地":total>=100000000?(total/100000000d).ToString("0.00")+" 亿":total>=10000?(total/10000d).ToString("0.0")+" 万":total.ToString("N0");}}
        internal static async Task RefreshAsync()
        {if(reading)return;reading=true;try{await Task.Run((Action)Read);}finally{reading=false;}}
        internal static void Read()
        {
            string root=Environment.GetEnvironmentVariable("CODEX_HOME");
            if(string.IsNullOrWhiteSpace(root))root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
            var values=new Dictionary<string,long>();failed=0;var json=new JavaScriptSerializer{MaxJsonLength=2097152};
            foreach(string folder in new[]{"sessions","archived_sessions"})
            {
                string directory=Path.Combine(root,folder);if(!Directory.Exists(directory))continue;
                foreach(string file in Directory.EnumerateFiles(directory,"*.jsonl",SearchOption.AllDirectories))
                {
                    try
                    {
                        string id=Path.GetFileName(file);long maximum=0;
                        using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                        using(var reader=new StreamReader(stream))
                        {
                            string line;while((line=reader.ReadLine())!=null)
                            {
                                if(line.Length>2097152)continue;
                                if(line.IndexOf("\"session_meta\"",StringComparison.Ordinal)>=0)
                                {try{var item=json.Deserialize<Dictionary<string,object>>(line);var payload=JsonUtil.Dict(JsonUtil.Value(item,"payload"));string found=JsonUtil.Text(payload,"id");if(found.Length>0)id=found;}catch{}continue;}
                                if(line.IndexOf("\"token_count\"",StringComparison.Ordinal)<0)continue;
                                try
                                {
                                    var item=json.Deserialize<Dictionary<string,object>>(line);var payload=JsonUtil.Dict(JsonUtil.Value(item,"payload"));
                                    if(JsonUtil.Text(payload,"type")!="token_count")continue;
                                    var info=JsonUtil.Dict(JsonUtil.Value(payload,"info"));var usage=JsonUtil.Dict(JsonUtil.Value(info,"total_token_usage"));
                                    object number=JsonUtil.Value(usage,"total_tokens");if(number!=null)maximum=Math.Max(maximum,Convert.ToInt64(number));
                                }catch{}
                            }
                        }
                        if(maximum>0){long previous;values[id]=values.TryGetValue(id,out previous)?Math.Max(previous,maximum):maximum;}
                    }catch{failed++;}
                }
            }
            total=values.Values.Sum();sessions=values.Count;Stamp=DateTime.Now.ToString("yyyy-MM-dd HH:mm");loaded=true;
            Directory.CreateDirectory(PortablePaths.Data);
            string temp=Cache+".tmp";File.WriteAllText(temp,json.Serialize(new{total=total,sessions=sessions,failed=failed,at=Stamp}));
            if(File.Exists(Cache))File.Replace(temp,Cache,null);else File.Move(temp,Cache);
        }
        internal static string Description {get{Load();return "本机记录 Token 累计："+total.ToString("N0")+"\n有用量记录的会话："+sessions+"\n未能读取的文件："+failed+"\n统计时间："+Stamp+"\n\n覆盖本机 Codex 会话记录，含缓存输入；不代表所选账号，也不含其他电脑或网页用量。按会话取累计最大值去重，未逐条相加。\n点击此区域可手动重新读取。";}}
    }
}
