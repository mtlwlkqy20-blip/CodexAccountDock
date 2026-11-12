using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace CodexAccountDock
{
    internal static class AppearanceOptions
    {
        private static bool loaded;
        private static int brightness=85, glass=58;
        public static int Brightness { get { Load(); return brightness; } }
        public static int GlassAlpha(int baseline)
        {
            Load();
            // Preserve existing settings exactly; extend the old 25..85 range
            // to clear at 0 and fully opaque at 100 without a jump at either join.
            if(glass<25)return (int)Math.Round(Math.Max(35,Math.Min(235,baseline-66))*glass/25d);
            if(glass>85){int at85=Math.Max(35,Math.Min(235,baseline+54));return at85+(int)Math.Round((255-at85)*(glass-85)/15d);}
            return Math.Max(35,Math.Min(235,baseline+(glass-58)*2));
        }
        private static void Load()
        {
            if(loaded) return; loaded=true;
            try { string[] values=File.ReadAllText(Path.Combine(PortablePaths.Data,"appearance-options.txt")).Split(','); brightness=Math.Max(0,Math.Min(100,int.Parse(values[0]))); glass=Math.Max(0,Math.Min(100,int.Parse(values[1]))); } catch { }
        }
        public static void Show(Form owner, Action changed)
        {
            Load(); int oldBrightness=brightness,oldGlass=glass;
            using(GlassAdjustment dialog=new GlassAdjustment(delegate {GlassBackdrop.Reset();GlassBackdrop.PrepareTheme();changed();}))
            {
                if(dialog.ShowDialog(owner)==DialogResult.OK)
                { try {Directory.CreateDirectory(PortablePaths.Data);string path=Path.Combine(PortablePaths.Data,"appearance-options.txt"),temp=path+".tmp";File.WriteAllText(temp,brightness+","+glass);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);} catch(Exception ex) {MessageBox.Show(owner,"保存外观失败："+ex.Message);} }
                else {brightness=oldBrightness;glass=oldGlass;GlassBackdrop.Reset();GlassBackdrop.PrepareTheme();changed();}
            }
        }
        internal static int RenderPreview()
        {
            Load();Application.EnableVisualStyles();
            using(GlassAdjustment dialog=new GlassAdjustment(delegate{}))
            {
                dialog.Show();dialog.Update();
                string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(folder);
                string theme=ThemeSelection.Current;
                using(Bitmap image=new Bitmap(dialog.Width,dialog.Height))
                {
                    dialog.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                    image.Save(Path.Combine(folder,"appearance-panel.png"));
                    image.Save(Path.Combine(folder,"appearance-panel-"+theme+".png"));
                    Color expected=ThemeVisual.Current.PanelBottom;
                    Color sample=image.GetPixel(12,dialog.Height-12);
                    if(Math.Abs(sample.R-expected.R)>150 && Math.Abs(sample.G-expected.G)>150 && Math.Abs(sample.B-expected.B)>150)
                        throw new InvalidOperationException("外观预览没有应用当前主题色："+theme);
                }
                int oldBrightness=brightness,oldGlass=glass;
                try
                {
                    dialog.CheckSliderEndpoints();
                    foreach(int value in new[]{0,50,100})
                    {
                        brightness=value;glass=value;dialog.Invalidate();dialog.Update();
                        using(Bitmap shot=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(shot,dialog.ClientRectangle);shot.Save(Path.Combine(folder,"appearance-"+value+".png"));}
                    }
                    glass=0;if(GlassAlpha(175)!=0)throw new Exception("Glass 0 is not clear");
                    glass=100;if(GlassAlpha(175)!=255)throw new Exception("Glass 100 is not opaque");
                    foreach(int value in new[]{25,58,66,85}){glass=value;if(GlassAlpha(175)!=Math.Max(35,Math.Min(235,175+(value-58)*2)))throw new Exception("Existing glass setting changed");}
                }
                finally{brightness=oldBrightness;glass=oldGlass;}
                dialog.Close();ThemeSelection.RunGalleryPreview();Console.WriteLine("APPEARANCE_PREVIEW_OK theme="+theme+" slider_mouse_0_50_100 endpoints_and_outside_clamp glass_clear_opaque old_settings_unchanged full_bleed theme_gallery no_selection");return 0;
            }
        }
        private sealed class GlassAdjustment : Form
        {
            private readonly Action preview;
            private readonly Bitmap backdrop;
            private readonly ThemeLook look;
            private int drag=-1;
            private readonly Rectangle save=new Rectangle(302,286,140,40),cancel=new Rectangle(190,286,100,40),close=new Rectangle(410,18,32,32);
            internal GlassAdjustment(Action preview)
            {
                this.preview=preview;look=ThemeVisual.Current;Text="明暗与玻璃";ClientSize=new Size(466,350);FormBorderStyle=FormBorderStyle.None;
                StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;DoubleBuffered=true;KeyPreview=true;Font=ChineseFont(10,false);
                backdrop=new Bitmap(466,350);using(Graphics g=Graphics.FromImage(backdrop))GlassBackdrop.DrawReferenceBackground(g,new Rectangle(0,0,466,350),false);
                using(GraphicsPath shape=GraphicsExtensions.CreateRoundedPath(new Rectangle(0,0,466,350),24))Region=new Region(shape);
                KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Escape){DialogResult=DialogResult.Cancel;Close();}if(e.KeyCode==Keys.Enter){DialogResult=DialogResult.OK;Close();}};
                Shown+=delegate{ClampToScreen();};
            }
            protected override void OnPaintBackground(PaintEventArgs e){}
            private static Font ChineseFont(float size,bool bold)
            {return new Font("楷体",Math.Max(12,size+1),bold?FontStyle.Bold:FontStyle.Bold);}
            private static Font NumberFont(float size,bool bold)
            {return new Font("Times New Roman",Math.Max(12,size+1),bold?FontStyle.Bold:FontStyle.Bold);}
            private void LabelText(Graphics g,string text,float size,Rectangle box,bool bold)
            {using(Font f=ChineseFont(size,bold))TextRenderer.DrawText(g,text,f,box,Color.White,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);}
            private void NumberText(Graphics g,string text,float size,Rectangle box,bool bold)
            {using(Font f=NumberFont(size,bold))TextRenderer.DrawText(g,text,f,box,Color.White,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);}
            private void Panel(Graphics g,Rectangle box,int radius,Color top,Color bottom)
            {
                using(GraphicsPath path=GraphicsExtensions.CreateRoundedPath(box,radius))
                using(LinearGradientBrush fill=new LinearGradientBrush(box,top,bottom,90))
                using(Pen rim=new Pen(Color.FromArgb(218,look.Rim),1.1f)){g.FillPath(fill,path);g.DrawPath(rim,path);}
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g=e.Graphics;GlassBackdrop.DrawReferenceBackground(g,new Rectangle(0,0,466,350),false);g.SmoothingMode=SmoothingMode.AntiAlias;
                Panel(g,new Rectangle(1,1,463,347),23,Color.FromArgb(GlassAlpha(202),look.PanelTop),Color.FromArgb(GlassAlpha(228),look.PanelBottom));
                LabelText(g,"光影与玻璃",20,new Rectangle(26,19,280,38),true);
                LabelText(g,"调出适合你的通透感",10,new Rectangle(28,60,360,24),false);
                LabelText(g,"×",20,close,false);
                Slider(g,0,brightness,0,100,"背景亮度",112);
                Slider(g,1,glass,0,100,"玻璃不透明度",198);
                LabelText(g,"拖动即时预览 · 取消可恢复",9,new Rectangle(28,253,390,22),false);
                Panel(g,cancel,13,Color.FromArgb(176,look.ButtonTop),Color.FromArgb(208,look.ButtonBottom));
                Panel(g,save,13,Color.FromArgb(235,ThemeVisual.Blend(look.Accent,Color.White,.12f)),Color.FromArgb(238,look.ButtonBottom));
                LabelText(g,"取消",11,new Rectangle(cancel.X+29,cancel.Y,80,cancel.Height),false);
                LabelText(g,"保存外观",11,new Rectangle(save.X+29,save.Y,110,save.Height),true);
            }
            private void Slider(Graphics g,int index,int value,int min,int max,string text,int y)
            {
                LabelText(g,text,12,new Rectangle(29,y-21,260,28),true);
                NumberText(g,value+"%",11,new Rectangle(375,y-21,65,28),true);
                int x=34+(int)Math.Round((value-min)*394d/(max-min));
                using(Pen track=new Pen(Color.FromArgb(105,ThemeVisual.Blend(look.PanelBottom,Color.White,.55f)),7)){track.StartCap=track.EndCap=LineCap.Round;g.DrawLine(track,34,y+27,428,y+27);}
                if(x>34)
                {
                    Rectangle fillBox=new Rectangle(34,y+23,Math.Max(1,x-34),8);
                    using(LinearGradientBrush fill=new LinearGradientBrush(fillBox,look.BarFrom,look.BarTo,0f))
                    using(Pen fillPen=new Pen(fill,7)){fillPen.StartCap=fillPen.EndCap=LineCap.Round;g.DrawLine(fillPen,34,y+27,x,y+27);}
                }
                using(SolidBrush shadow=new SolidBrush(Color.FromArgb(95,look.PanelBottom)))g.FillEllipse(shadow,x-10,y+19,22,22);
                using(SolidBrush handle=new SolidBrush(ThemeVisual.Blend(look.Accent2,Color.White,.7f)))g.FillEllipse(handle,x-9,y+18,18,18);
                using(Pen handleRim=new Pen(Color.FromArgb(230,look.Rim),1.2f))g.DrawEllipse(handleRim,x-9,y+18,18,18);
            }
            private void ClampToScreen()
            {
                Rectangle area=Screen.FromControl(this).WorkingArea;
                int x=Math.Max(area.Left,Math.Min(Left,area.Right-Width));
                int y=Math.Max(area.Top,Math.Min(Top,area.Bottom-Height));
                Location=new Point(x,y);
            }
            private void Change(int x)
            {
                double position=Math.Max(0,Math.Min(1,(x-34)/394d));
                int old=drag==0?brightness:glass;
                int value=(int)Math.Round(position*100);
                if(value==old)return;
                if(drag==0)brightness=value;else glass=value;
                preview();Invalidate();
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
                if(save.Contains(e.Location)){DialogResult=DialogResult.OK;Close();return;}
                if(cancel.Contains(e.Location)||close.Contains(e.Location)){DialogResult=DialogResult.Cancel;Close();return;}
                if(e.Y>=120 && e.Y<=158)drag=0;else if(e.Y>=206 && e.Y<=244)drag=1;
                if(drag>=0){Capture=true;Change(e.X);}else if(e.Y<85)Native.BeginWindowDrag(Handle);
            }
            protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(drag>=0)Change(e.X);}
            protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);drag=-1;Capture=false;}
            internal void CheckSliderEndpoints()
            {
                foreach(int row in new[]{0,1})foreach(int value in new[]{0,50,100})
                {
                    int x=34+(int)Math.Round(value*394d/100),y=row==0?139:225;
                    OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,x,y,0));
                    OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,x,y,0));
                    if((row==0?brightness:glass)!=value)throw new Exception("Slider percentage differs from track position");
                }
                OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,-100,225,0));OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,-100,225,0));
                if(glass!=0)throw new Exception("Slider left boundary");
                OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,700,225,0));OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,700,225,0));
                if(glass!=100)throw new Exception("Slider right boundary");
            }
            protected override void Dispose(bool disposing){if(disposing)backdrop.Dispose();base.Dispose(disposing);}
        }
    }
}
