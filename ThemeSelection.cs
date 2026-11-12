using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CodexAccountDock
{
    internal static class ThemeSelection
    {
        private static bool motion=true;
        private static event EventHandler MotionChanged;
        public static bool Motion {get{return motion;}set{if(motion==value)return;motion=value;if(MotionChanged!=null)MotionChanged(null,EventArgs.Empty);}}
        private static readonly System.Diagnostics.Stopwatch Clock=System.Diagnostics.Stopwatch.StartNew();
        public static void AttachMotion(Control header)
        {
            var timer=new System.Windows.Forms.Timer { Interval=100 };
            EventHandler update=delegate { timer.Enabled=header.Visible && Motion && Current!="custom"; };
            header.VisibleChanged+=update;
            MotionChanged+=update;
            timer.Tick+=delegate { if(!Motion || Current=="custom") return; header.Invalidate(true); };
            header.Disposed+=delegate { MotionChanged-=update;timer.Stop(); timer.Dispose(); };
            header.HandleCreated+=update;
            header.Invalidated+=delegate{update(null,EventArgs.Empty);};
            header.Paint+=delegate(object sender,PaintEventArgs e)
            {
                if(!Motion || Current=="custom") return;
                double seconds=Clock.Elapsed.TotalSeconds;
                Graphics g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
                for(int i=0;i<12;i++)
                {
                    float x=(float)((i*73+seconds*(Current=="summer"?7:11))%Math.Max(1,header.Width));
                    float y=(float)(20+(i*19+seconds*(Current=="summer"?-3:5)+10000)%Math.Max(1,header.Height-24));
                    int alpha=38+(int)(30*(1+Math.Sin(seconds+i)));
                    Color color=Current=="spring"?Color.FromArgb(255,207,226):Current=="autumn"?Color.FromArgb(255,197,104):Current=="winter"?Color.White:Palette.Accents[0];
                    using(SolidBrush brush=new SolidBrush(Color.FromArgb(alpha,color)))
                    {
                        if(Current=="spring" || Current=="autumn")
                        {
                            var saved=g.Save(); g.TranslateTransform(x,y); g.RotateTransform((float)(seconds*12+i*30)); g.FillEllipse(brush,-4,-2,8,4); g.Restore(saved);
                        }
                        else g.FillEllipse(brush,x,y,Current=="winter"?3:2,Current=="winter"?3:2);
                    }
                }
                float sweep=(float)((seconds*18)%Math.Max(1,header.Width));
                using(var glow=new LinearGradientBrush(new RectangleF(sweep-60,header.Height-2,120,1),Color.Transparent,Color.FromArgb(105,Palette.Accents[0]),0f))
                    g.FillRectangle(glow,sweep-60,header.Height-2,120,1);
            };
        }
        private static string current;
        public static string Current
        {
            get
            {
                if(current!=null) return current;
                try { current=File.ReadAllText(Path.Combine(PortablePaths.Data,"appearance-theme.txt")).Trim(); } catch { current="heritage"; }
                if(!Valid(current)) current="heritage";
                return current;
            }
        }
        public static bool Valid(string id) { return id=="custom" || ThemeArt.Ids.Contains(id); }
        public static void Select(string id, bool persist)
        {
            if(!Valid(id)) throw new ArgumentException("未知主题");
            if(persist)
            {
                Directory.CreateDirectory(PortablePaths.Data);
                string path=Path.Combine(PortablePaths.Data,"appearance-theme.txt"), temp=path+".tmp";
                File.WriteAllText(temp,id);
                if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
            }
            current=id;
            DynamicBackground.Reload();
            GlassBackdrop.Reset();
            GlassBackdrop.PrepareTheme();
        }
        public static string Name { get { return Current=="custom" ? "自选光影" : ThemeArt.Name(Current); } }
        public static string Subtitle { get { return Current=="custom" ? "让喜欢的风景，成为日常。" : ThemeArt.Subtitle(Current); } }

        public static string Choose(IWin32Window owner)
        {
            string chosen=null;
            using(Form dialog=new ThemeGallery { Text="主题典藏", ClientSize=new Size(756,580), BackColor=ThemeVisual.Current.PanelBottom, ForeColor=Color.White, StartPosition=FormStartPosition.CenterParent, FormBorderStyle=FormBorderStyle.None, MaximizeBox=false, MinimizeBox=false, ShowInTaskbar=false, AutoScaleMode=AutoScaleMode.Dpi })
            {
                var close=new PlanGlassButton {Text="×",Bounds=new Rectangle(706,16,32,32)};
                close.Click+=delegate{dialog.Close();};dialog.Controls.Add(close);
                Label title=Palette.Label("四时有序 · 山河有色",23,ThemeVisual.HeaderInk,false);
                title.Font=Palette.DisplayFont(23); title.SetBounds(24,18,670,42); dialog.Controls.Add(title);
                Label caption=Palette.Label("选择一套完整主题 · 四季微动与玻璃流光 · 账户与日报同步换装",12,ThemeVisual.HeaderInk,false);
                caption.SetBounds(26,64,690,27); dialog.Controls.Add(caption);
                string[] choices=ThemeArt.Ids.Concat(new[]{"custom"}).ToArray();
                for(int i=0;i<choices.Length;i++)
                {
                    string id=choices[i];
                    ThemeTile tile=new ThemeTile(id) { Bounds=new Rectangle(24+(i%3)*238,106+(i/3)*220,230,208) };
                    tile.Click+=delegate { chosen=id; dialog.DialogResult=DialogResult.OK; dialog.Close(); };
                    dialog.Controls.Add(tile);
                }
                Label hint=Palette.Label("自选光影保留你原来的背景；内置主题不覆盖图片。",12,Color.White,false);
                hint.SetBounds(26,548,690,22); dialog.Controls.Add(hint);
                dialog.ShowDialog(owner);
            }
            return chosen;
        }

        internal static void RunGalleryPreview()
        {
            bool captured=false;
            using(var timer=new System.Windows.Forms.Timer {Interval=100})
            {
                timer.Tick+=delegate
                {
                    Form gallery=Application.OpenForms.Cast<Form>().FirstOrDefault(form=>form is ThemeGallery);
                    if(gallery==null)return;
                    timer.Stop();gallery.Update();
                    string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(folder);
                    using(var image=new Bitmap(gallery.Width,gallery.Height))
                    {gallery.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(folder,"theme-gallery.png"));}
                    captured=true;gallery.DialogResult=DialogResult.Cancel;gallery.Close();
                };
                timer.Start();Choose(null);
            }
            if(!captured)throw new InvalidOperationException("Theme gallery preview missing");
        }
        private sealed class ThemeGallery : DockedGlassWindow
        {
            internal ThemeGallery(){DoubleBuffered=true;}
            protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);ReferenceGlass.DrawWindowRim(e.Graphics,ClientSize);}
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                GlassBackdrop.DrawReferenceBackground(e.Graphics,ClientRectangle,false);
                using(SolidBrush veil=new SolidBrush(Color.FromArgb(42,ThemeVisual.Current.PanelBottom)))e.Graphics.FillRectangle(veil,ClientRectangle);
            }
        }
        private sealed class ThemeTile : Control
        {
            private readonly string id;
            private readonly Bitmap art;
            private bool hover;
            public ThemeTile(string id)
            {
                this.id=id; DoubleBuffered=true; Cursor=Cursors.Hand; TabStop=true;
                SetStyle(ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;
                AccessibleName=id=="custom" ? "自选光影" : ThemeArt.Name(id);
                if(id!="custom") art=ThemeArt.Render(id,new Size(460,416));
                else
                {
                    try { using(Image src=Image.FromFile(DynamicBackground.PosterPath)) art=new Bitmap(src,new Size(460,416)); }
                    catch { using(Stream stream=typeof(ThemeSelection).Assembly.GetManifestResourceStream("CodexAccountDock.Background.jpg")) using(Image src=Image.FromStream(stream)) art=new Bitmap(src,new Size(460,416)); }
                }
            }
            protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover=false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnKeyDown(KeyEventArgs e) { if(e.KeyCode==Keys.Enter || e.KeyCode==Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
                Rectangle box=new Rectangle(2,2,Width-5,Height-5);
                using(GraphicsPath path=GraphicsExtensions.CreateRoundedPath(box,18))
                {
                    var saved=g.Save(); g.SetClip(path); g.DrawImage(art,box);
                    using(LinearGradientBrush shade=new LinearGradientBrush(box,Color.FromArgb(0,0,0,0),Color.FromArgb(225,ThemeVisual.Current.PanelBottom),90)) g.FillRectangle(shade,box);
                    g.Restore(saved);
                    using(Pen rim=new Pen(id==Current?ThemeVisual.Current.Rim:hover?Color.White:Color.FromArgb(95,255,255,255),id==Current?2:1)) g.DrawPath(rim,path);
                }
                using(Font f=Palette.DisplayFont(18)) TextRenderer.DrawText(g,AccessibleName,f,new Rectangle(16,127,Width-30,34),Color.White,TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis);
                using(Font f=Palette.Font(12,false)) TextRenderer.DrawText(g,id==Current?"● 正在使用":id=="custom"?"你的背景，自动配色":"点击应用这一季的光",f,new Rectangle(17,170,Width-30,24),Color.FromArgb(235,238,244),TextFormatFlags.NoPadding);
            }
            protected override void Dispose(bool disposing) { if(disposing) art.Dispose(); base.Dispose(disposing); }
        }
    }
}
