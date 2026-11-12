using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CodexAccountDock
{
    internal static class GlassBackdrop
    {
        private const int MaximumDimension = 4096;
        private static readonly object SyncRoot = new object();
        private static Bitmap sourceImage;
        private static Bitmap renderedImage;
        private static Bitmap frostedImage;
        private static Size renderedFor = Size.Empty;
        private static bool sourceLoadAttempted;
        private static int outerOutlinePaintCount;
        private static bool suppressOuterOutlineForDiagnostic;
        private static readonly Dictionary<Size, Bitmap[]> FrameCache = new Dictionary<Size, Bitmap[]>();
        public static void PrepareTheme() { lock(SyncRoot) { EnsureSourceImage(); } }
        internal static void DrawReferenceBackground(Graphics graphics,Rectangle frame)
        { DrawReferenceBackground(graphics,frame,true); }
        internal static void DrawReferenceBackground(Graphics graphics,Rectangle frame,bool rounded)
        {
            lock(SyncRoot)
            {
                EnsureSourceImage();
                GraphicsState saved=graphics.Save();
                if(rounded)using(GraphicsPath clip=CreateRoundedRectangle(frame,32))graphics.SetClip(clip,CombineMode.Intersect);
                using(SolidBrush backing=new SolidBrush(Color.FromArgb(226,222,236)))graphics.FillRectangle(backing,frame);
                if(sourceImage!=null)
                {
                    bool bundled=ThemeSelection.Current=="custom" && !File.Exists(DynamicBackground.PosterPath);
                    float lift=bundled?(rounded?65f:frame.Height*65f/1536f):0f;
                    double scale=Math.Max((double)frame.Width/sourceImage.Width,(frame.Height+lift)/sourceImage.Height);
                    float width=(float)(sourceImage.Width*scale),height=(float)(sourceImage.Height*scale);
                    RectangleF destination=new RectangleF(frame.X+(frame.Width-width)/2,frame.Y-lift,width,height);
                    graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(sourceImage,destination);
                }
                Color veilColor=ThemeVisual.Current.PanelBottom;
                using(SolidBrush veil=new SolidBrush(Color.FromArgb(Math.Max(0,(100-AppearanceOptions.Brightness)*255/100),veilColor)))graphics.FillRectangle(veil,frame);
                if(ThemeVisual.IsReferenceCustom)using(var lavender=new SolidBrush(Color.FromArgb(38,207,192,244)))graphics.FillRectangle(lavender,frame);
                graphics.Restore(saved);
            }
        }

        public static void DrawLiquid(Graphics graphics, Control control, Rectangle box, int radius)
        {
            if (graphics == null || control == null || box.Width <= 0 || box.Height <= 0) return;
            // A clear lens preserves image detail. Only the edge compresses the
            // background, creating optical thickness without continuous animation.
            Form form = control as Form ?? control.FindForm();
            Size size = form != null ? form.ClientSize : control.ClientSize;
            lock (SyncRoot)
            {
                EnsureRendered(size);
                if (renderedImage != null)
                {
                    Point origin = GetClientOrigin(control, form);
                    GraphicsState lensState = graphics.Save();
                    using (GraphicsPath lens = CreateRoundedRectangle(box, radius))
                        graphics.SetClip(lens, CombineMode.Intersect);
                    RectangleF source = new RectangleF(origin.X + box.X, origin.Y + box.Y, box.Width, box.Height);
                    graphics.DrawImage(renderedImage, box, source, GraphicsUnit.Pixel);
                    Rectangle core = Rectangle.Inflate(box, -7, -7);
                    if (core.Width > 0 && core.Height > 0)
                    {
                        using (GraphicsPath inner = CreateRoundedRectangle(core, Math.Max(1, radius - 7)))
                            graphics.SetClip(inner, CombineMode.Intersect);
                        graphics.DrawImage(renderedImage, core, new Rectangle(origin.X + core.X, origin.Y + core.Y, core.Width, core.Height), GraphicsUnit.Pixel);
                    }
                    graphics.Restore(lensState);
                }
            }
            // Palette.Glass owns the single highlight rim; do not double-stroke it here.
        }

        public static void Draw(Graphics graphics, Control control)
        {
            if (graphics == null || control == null || control.ClientSize.Width <= 0 || control.ClientSize.Height <= 0)
                return;

            Form form = control as Form ?? control.FindForm();
            Size formSize = form != null ? form.ClientSize : control.ClientSize;
            if (formSize.Width <= 0 || formSize.Height <= 0)
            {
                graphics.Clear(Color.FromArgb(22, 22, 43));
                return;
            }

            lock (SyncRoot)
            {
                EnsureRendered(formSize);
                if (renderedImage == null)
                {
                    graphics.Clear(Color.FromArgb(22, 22, 43));
                    return;
                }

                Point origin = GetClientOrigin(control, form);
                if(!ReferenceEquals(control,form) && form is DockedGlassWindow && formSize.Width>8 && formSize.Height>8)
                {
                    // Native child Regions are not consistently honored by
                    // WM_PRINT/DrawToBitmap. Keep this paint pass inside the
                    // same inset curve as the actual child window region.
                    Rectangle inside=new Rectangle(4-origin.X,4-origin.Y,formSize.Width-8,formSize.Height-8);
                    using(GraphicsPath inner=CreateRoundedRectangle(inside,25))graphics.SetClip(inner,CombineMode.Intersect);
                }
                float scaleX = (float)renderedImage.Width / Math.Max(1, formSize.Width);
                float scaleY = (float)renderedImage.Height / Math.Max(1, formSize.Height);
                RectangleF source = new RectangleF(
                    origin.X * scaleX,
                    origin.Y * scaleY,
                    control.ClientSize.Width * scaleX,
                    control.ClientSize.Height * scaleY);

                GraphicsState state = graphics.Save();
                try
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighSpeed;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(renderedImage, control.ClientRectangle, source, GraphicsUnit.Pixel);
                    // Paint the window outline exactly once. Repainting translated
                    // fragments from every docked child creates uneven, dashed-looking
                    // segments where independently clipped anti-aliased strokes overlap.
                    if (ReferenceEquals(control, form) && form is DockedGlassWindow && !suppressOuterOutlineForDiagnostic)
                    {
                        graphics.CompositingMode = CompositingMode.SourceOver;
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        Rectangle frame = new Rectangle(1 - origin.X, 1 - origin.Y, formSize.Width - 3, formSize.Height - 3);
                        using (GraphicsPath outline = CreateRoundedRectangle(frame, 28))
                        using (Pen pen = new Pen(Color.FromArgb(225, Color.White), 1.25f))
                        {
                            pen.Alignment = PenAlignment.Inset;
                            graphics.DrawPath(pen, outline);
                            outerOutlinePaintCount++;
                        }
                    }
                }
                finally
                {
                    graphics.Restore(state);
                }
            }
        }

        public static void DrawFrosted(Graphics graphics, Control control, Rectangle box, int radius)
        {
            if (graphics == null || control == null || box.Width <= 0 || box.Height <= 0 ||
                control.ClientSize.Width <= 0 || control.ClientSize.Height <= 0)
                return;

            Form form = control as Form ?? control.FindForm();
            Size formSize = form != null ? form.ClientSize : control.ClientSize;
            if (formSize.Width <= 0 || formSize.Height <= 0) return;

            lock (SyncRoot)
            {
                EnsureRendered(formSize);
                if (frostedImage == null) return;

                Point origin = GetClientOrigin(control, form);
                float scaleX = (float)frostedImage.Width / Math.Max(1, formSize.Width);
                float scaleY = (float)frostedImage.Height / Math.Max(1, formSize.Height);
                RectangleF source = new RectangleF(
                    origin.X * scaleX,
                    origin.Y * scaleY,
                    control.ClientSize.Width * scaleX,
                    control.ClientSize.Height * scaleY);

                GraphicsState state = graphics.Save();
                try
                {
                    using (GraphicsPath clip = CreateRoundedRectangle(box, radius))
                        graphics.SetClip(clip, CombineMode.Intersect);

                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighSpeed;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(frostedImage, control.ClientRectangle, source, GraphicsUnit.Pixel);
                }
                finally
                {
                    graphics.Restore(state);
                }
            }
        }

        public static void Reset()
        {
            lock (SyncRoot)
            {
                ClearFrameCache();
                if (sourceImage != null) sourceImage.Dispose();
                frostedImage = null;
                renderedImage = null;
                sourceImage = null;
                renderedFor = Size.Empty;
                sourceLoadAttempted = false;
            }
        }

        internal static void ResetOuterOutlineDiagnostic() { lock (SyncRoot) outerOutlinePaintCount = 0; }
        internal static int OuterOutlinePaintCount { get { lock (SyncRoot) return outerOutlinePaintCount; } }
        internal static bool SuppressOuterOutlineForDiagnostic
        {
            get { lock (SyncRoot) return suppressOuterOutlineForDiagnostic; }
            set { lock (SyncRoot) suppressOuterOutlineForDiagnostic = value; }
        }

        private static void ClearFrameCache()
        {
            foreach(Bitmap[] frame in FrameCache.Values){frame[0].Dispose();frame[1].Dispose();}
            FrameCache.Clear();renderedImage=null;frostedImage=null;renderedFor=Size.Empty;
        }

        // Called only on the UI thread. Ownership of this single frame transfers
        // here; replacing it releases both the prior source and derived caches.
        internal static void SetAnimatedFrame(Bitmap frame)
        {
            lock(SyncRoot)
            {
                ClearFrameCache();if(sourceImage!=null)sourceImage.Dispose();
                sourceImage=frame;sourceLoadAttempted=true;
            }
        }

        private static void EnsureRendered(Size formSize)
        {
            Size safeSize = new Size(
                Math.Max(1, Math.Min(MaximumDimension, formSize.Width)),
                Math.Max(1, Math.Min(MaximumDimension, formSize.Height)));
            if (renderedImage != null && renderedFor == formSize && renderedImage.Size == safeSize) return;
            Bitmap[] cached;
            if(FrameCache.TryGetValue(safeSize,out cached))
            {
                renderedImage=cached[0]; frostedImage=cached[1]; renderedFor=formSize; return;
            }

            EnsureSourceImage();
            Bitmap next = new Bitmap(safeSize.Width, safeSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(next))
            {
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                DrawReferenceBackground(graphics,new Rectangle(Point.Empty,safeSize),false);
            }

            Bitmap nextFrosted = CreateFrostedImage(next, safeSize);
            // Keep a bounded pair of window sizes plus a spare resize frame.
            // Alternating account/news paints must not rebuild the backdrop.
            if(FrameCache.Count>=3)
            {
                Size evict=Size.Empty;
                foreach(Size size in FrameCache.Keys) { evict=size; break; }
                Bitmap[] discarded=FrameCache[evict];
                FrameCache.Remove(evict); discarded[0].Dispose(); discarded[1].Dispose();
            }
            FrameCache[safeSize]=new[] { next,nextFrosted };
            renderedImage = next;
            frostedImage = nextFrosted;
            renderedFor = formSize;
        }

        private static Bitmap CreateFrostedImage(Bitmap rendered, Size safeSize)
        {
            int sampleWidth = Math.Max(2, safeSize.Width / 4);
            int sampleHeight = Math.Max(2, safeSize.Height / 4);
            using (Bitmap sample = new Bitmap(sampleWidth, sampleHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (Graphics graphics = Graphics.FromImage(sample))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(rendered, new Rectangle(0, 0, sampleWidth, sampleHeight));
                }

                Bitmap frosted = new Bitmap(safeSize.Width, safeSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (Graphics output = Graphics.FromImage(frosted))
                {
                    output.CompositingMode = CompositingMode.SourceCopy;
                    output.CompositingQuality = CompositingQuality.HighQuality;
                    output.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    output.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    output.DrawImage(sample, new Rectangle(Point.Empty, safeSize));
                }
                return frosted;
            }
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle box, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int corner = Math.Max(0, Math.Min(radius, Math.Min(box.Width, box.Height) / 2));
            if (corner == 0)
            {
                path.AddRectangle(box);
                return path;
            }

            int diameter = corner * 2;
            Rectangle arc = new Rectangle(box.X, box.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = box.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = box.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = box.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void EnsureSourceImage()
        {
            if (sourceLoadAttempted) return;
            sourceLoadAttempted = true;
            try
            {
                if(ThemeSelection.Current!="custom")
                {
                    sourceImage=ThemeArt.Render(ThemeSelection.Current,new Size(1200,1700));
                    ThemeArt.ApplyPalette(ThemeSelection.Current);
                    return;
                }
                string custom = DynamicBackground.PosterPath;
                if (File.Exists(custom))
                {
                    try
                    {
                        using (Image decoded = Image.FromFile(custom)) sourceImage = new Bitmap(decoded);
                        Palette.FromBackground(sourceImage);
                        return;
                    }
                    catch { /* A damaged custom image falls back to the bundled background. */ }
                }
                Assembly assembly = typeof(GlassBackdrop).Assembly;
                Stream stream = assembly.GetManifestResourceStream("CodexAccountDock.Background.jpg");
                if (stream == null) return;
                using (stream)
                using (Image decoded = Image.FromStream(stream, true, true))
                {
                    sourceImage = new Bitmap(decoded);
                    Palette.FromBackground(sourceImage);
                    ThemeArt.ApplyPalette("custom");
                }
            }
            catch
            {
                sourceImage = null;
            }
        }

        private static Rectangle CoverSource(Size imageSize, Size targetSize)
        {
            double sourceRatio = (double)imageSize.Width / imageSize.Height;
            double targetRatio = (double)targetSize.Width / targetSize.Height;
            if (sourceRatio > targetRatio)
            {
                int width = Math.Max(1, (int)Math.Round(imageSize.Height * targetRatio));
                // Bias the crop to the right so the face remains visible behind the upper controls.
                int x = Math.Max(0, imageSize.Width - width);
                return new Rectangle(x, 0, Math.Min(width, imageSize.Width - x), imageSize.Height);
            }

            int height = Math.Max(1, (int)Math.Round(imageSize.Width / targetRatio));
            int y = Math.Max(0, (imageSize.Height - height) / 4);
            return new Rectangle(0, y, imageSize.Width, Math.Min(height, imageSize.Height - y));
        }

        private static Point GetClientOrigin(Control control, Form form)
        {
            if (form == null || ReferenceEquals(control, form)) return Point.Empty;
            try
            {
                Point controlScreen = control.PointToScreen(Point.Empty);
                Point formScreen = form.PointToScreen(Point.Empty);
                return new Point(controlScreen.X - formScreen.X, controlScreen.Y - formScreen.Y);
            }
            catch
            {
                int x = 0;
                int y = 0;
                Control current = control;
                while (current != null && !ReferenceEquals(current, form))
                {
                    x += current.Left;
                    y += current.Top;
                    current = current.Parent;
                }
                return new Point(x, y);
            }
        }
    }

    internal sealed class GlassPanel : Panel
    {
        public GlassPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            UpdateStyles();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            GlassBackdrop.Draw(e.Graphics, this);
        }
    }

    internal sealed class GlassFlowPanel : FlowLayoutPanel
    {
        private const int SbBoth = 3;
        private const int ScrollHitWidth = 18;
        private const int ScrollVisualWidth = 4;
        private const int ScrollInset = 8;
        private const int MinimumThumbHeight = 34;
        private const int GwlStyle = -16;
        private const int WsHScroll = 0x00100000;
        private const int WsVScroll = 0x00200000;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private bool hideScrollBarsPending;
        private bool scrollHover;
        private bool scrollDragging;
        private int scrollDragOffset;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        public GlassFlowPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            UpdateStyles();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            GlassBackdrop.Draw(e.Graphics, this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawThemedScrollBar(e.Graphics);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            QueueHideNativeScrollBars();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            QueueHideNativeScrollBars();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            QueueHideNativeScrollBars();
            Invalidate();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            QueueHideNativeScrollBars();
            Invalidate(true);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            QueueHideNativeScrollBars();
            Invalidate(true);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Rectangle thumb;
            Rectangle hit;
            int maximum;
            if (e.Button == MouseButtons.Left && TryGetScrollGeometry(out thumb, out hit, out maximum) && hit.Contains(e.Location))
            {
                Focus();
                if (thumb.Contains(e.Location))
                {
                    scrollDragging = true;
                    scrollDragOffset = e.Y - thumb.Top;
                    Capture = true;
                }
                else
                {
                    SetScrollValue(ScrollValueFromThumbTop(e.Y - thumb.Height / 2, thumb.Height, maximum));
                    scrollDragging = true;
                    scrollDragOffset = thumb.Height / 2;
                    Capture = true;
                }
                scrollHover = true;
                Invalidate();
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (scrollDragging)
            {
                Rectangle thumb;
                Rectangle hit;
                int maximum;
                if (TryGetScrollGeometry(out thumb, out hit, out maximum))
                    SetScrollValue(ScrollValueFromThumbTop(e.Y - scrollDragOffset, thumb.Height, maximum));
                return;
            }

            Rectangle hoverThumb;
            Rectangle hoverHit;
            int hoverMaximum;
            bool hover = TryGetScrollGeometry(out hoverThumb, out hoverHit, out hoverMaximum) && hoverHit.Contains(e.Location);
            if (hover != scrollHover)
            {
                scrollHover = hover;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (scrollDragging && e.Button == MouseButtons.Left)
            {
                scrollDragging = false;
                Capture = false;
                Invalidate();
                return;
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (!scrollDragging && scrollHover)
            {
                scrollHover = false;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // WinForms scrolls existing pixels as an optimization. A glass
            // surface must resample the fixed window backdrop at its new position.
            if (m.Msg == 0x0115 || m.Msg == 0x020A || m.Msg == 0x0114)
            {
                QueueHideNativeScrollBars();
                Invalidate(true);
            }
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            e.Control.LocationChanged += ChildMoved;
            QueueHideNativeScrollBars();
        }

        protected override void OnControlRemoved(ControlEventArgs e)
        {
            e.Control.LocationChanged -= ChildMoved;
            base.OnControlRemoved(e);
        }

        private void ChildMoved(object sender, EventArgs e)
        {
            ((Control)sender).Invalidate(true);
            Invalidate();
        }

        private void QueueHideNativeScrollBars()
        {
            if (!IsHandleCreated || IsDisposed || Disposing || hideScrollBarsPending) return;
            hideScrollBarsPending = true;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    hideScrollBarsPending = false;
                    if (!IsHandleCreated || IsDisposed || Disposing) return;
                    ShowScrollBar(Handle, SbBoth, false);
                    int style = GetWindowLong(Handle, GwlStyle);
                    int cleanStyle = style & ~(WsHScroll | WsVScroll);
                    if (cleanStyle != style)
                    {
                        SetWindowLong(Handle, GwlStyle, cleanStyle);
                        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
                    }
                    Invalidate();
                });
            }
            catch (InvalidOperationException) { hideScrollBarsPending = false; }
        }

        private int GetMaximumScroll()
        {
            int maximum = VerticalScroll.Maximum - Math.Max(1, VerticalScroll.LargeChange) + 1;
            if (maximum > 0) return maximum;

            int contentBottom = 0;
            foreach (Control child in Controls)
                if (child.Visible) contentBottom = Math.Max(contentBottom, child.Bottom - AutoScrollPosition.Y);
            return Math.Max(0, contentBottom + Padding.Bottom - ClientSize.Height);
        }

        private bool TryGetScrollGeometry(out Rectangle thumb, out Rectangle hit, out int maximum)
        {
            thumb = Rectangle.Empty;
            hit = Rectangle.Empty;
            maximum = GetMaximumScroll();
            if (maximum <= 0 || ClientSize.Height <= ScrollInset * 2 + MinimumThumbHeight) return false;

            int trackHeight = ClientSize.Height - ScrollInset * 2;
            int contentHeight = trackHeight + maximum;
            int thumbHeight = Math.Max(MinimumThumbHeight, (int)Math.Round(trackHeight * (double)trackHeight / contentHeight));
            thumbHeight = Math.Min(trackHeight, thumbHeight);
            int travel = Math.Max(1, trackHeight - thumbHeight);
            int value = Math.Max(0, Math.Min(maximum, -AutoScrollPosition.Y));
            int thumbTop = ScrollInset + (int)Math.Round(travel * (double)value / maximum);
            int thumbLeft = ClientSize.Width - ScrollInset - ScrollVisualWidth;
            thumb = new Rectangle(thumbLeft, thumbTop, ScrollVisualWidth, thumbHeight);
            hit = new Rectangle(ClientSize.Width - ScrollHitWidth, ScrollInset, ScrollHitWidth, trackHeight);
            return true;
        }

        private int ScrollValueFromThumbTop(int thumbTop, int thumbHeight, int maximum)
        {
            int trackTop = ScrollInset;
            int travel = Math.Max(1, ClientSize.Height - ScrollInset * 2 - thumbHeight);
            int clamped = Math.Max(trackTop, Math.Min(trackTop + travel, thumbTop));
            return (int)Math.Round((clamped - trackTop) * (double)maximum / travel);
        }

        private void SetScrollValue(int value)
        {
            int maximum = GetMaximumScroll();
            value = Math.Max(0, Math.Min(maximum, value));
            int horizontal = Math.Max(0, -AutoScrollPosition.X);
            AutoScrollPosition = new Point(horizontal, value);
            QueueHideNativeScrollBars();
            Invalidate(true);
        }

        private void DrawThemedScrollBar(Graphics graphics)
        {
            Rectangle thumb;
            Rectangle hit;
            int maximum;
            if (!TryGetScrollGeometry(out thumb, out hit, out maximum)) return;

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle track = new Rectangle(thumb.Left, ScrollInset, ScrollVisualWidth, ClientSize.Height - ScrollInset * 2);
            Color accent = Palette.Accents[0];
            using (GraphicsPath trackPath = RoundedRect(track, 2))
            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(32, accent)))
                graphics.FillPath(trackBrush, trackPath);
            using (GraphicsPath thumbPath = RoundedRect(thumb, 2))
            using (SolidBrush thumbBrush = new SolidBrush(Color.FromArgb(scrollDragging ? 225 : scrollHover ? 185 : 120, accent)))
                graphics.FillPath(thumbBrush, thumbPath);
        }

        private static GraphicsPath RoundedRect(Rectangle box, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(1, radius * 2);
            path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
            path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
            path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
