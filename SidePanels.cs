using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAccountDock
{
    internal static class SidePanels
    {
        private static readonly HashSet<Form> panels = new HashSet<Form>();
        private static Form owner;
        private static Form active;
        private static bool aligning;

        internal static Rectangle DockBounds(Rectangle account, Rectangle area)
        {
            const int gap = 8;
            int left = account.Left - area.Left - gap;
            int right = area.Right - account.Right - gap;
            int preferredWidth = Math.Max(320, (int)Math.Round(account.Height * (720d / 860d)));
            int width = Math.Min(preferredWidth, Math.Max(320, Math.Max(left, right)));
            width = Math.Min(width, area.Width);
            int height = Math.Min(Math.Max(300, account.Height), area.Height);
            int x = left >= 320 && left >= right ? account.Left - gap - width : right >= 320 ? account.Right + gap : area.Left;
            x = Math.Max(area.Left, Math.Min(x, area.Right - width));
            int y = Math.Max(area.Top, Math.Min(account.Top, area.Bottom - height));
            return new Rectangle(x, y, width, height);
        }

        internal static void Activate(Form account, Form panel)
        {
            if (account == null || account.IsDisposed || panel == null || panel.IsDisposed) return;
            AttachOwner(account);
            if (active != null && active != panel && !active.IsDisposed) active.Hide();
            active = panel;
            panels.Add(panel);
            panel.FormClosed -= PanelClosed;
            panel.FormClosed += PanelClosed;
            panel.Owner = account;
            panel.ShowInTaskbar = false;
            panel.StartPosition = FormStartPosition.Manual;
            Align();
            if (account.Visible && account.WindowState != FormWindowState.Minimized)
            {
                if (!panel.Visible) panel.Show(account);
                panel.Activate();
            }
        }

        internal static bool IsGlassWindow(Form form)
        {
            return form is DockedGlassWindow || (form != null && panels.Contains(form));
        }

        internal static void RefreshAppearance()
        {
            Form[] snapshot = new Form[panels.Count];
            panels.CopyTo(snapshot);
            foreach (Form panel in snapshot)
            {
                if (panel == null || panel.IsDisposed) continue;
                DockedGlassWindow glass = panel as DockedGlassWindow;
                if (glass != null) glass.RefreshGlass();
                else { Palette.UpdateControls(panel); panel.Invalidate(true); }
            }
        }

        internal static void Align()
        {
            if (aligning || owner == null || owner.IsDisposed || active == null || active.IsDisposed) return;
            aligning = true;
            try
            {
                Rectangle target = DockBounds(owner.Bounds, Screen.FromControl(owner).WorkingArea);
                if(active.Text=="错峰计划" && target.Width<500)
                {
                    Rectangle area=Screen.FromControl(owner).WorkingArea;
                    target.Width=Math.Min(500,area.Width);
                    target.X=Math.Max(area.Left,Math.Min(target.X,area.Right-target.Width));
                }
                active.MinimumSize = new Size(Math.Min(320, target.Width), Math.Min(300, target.Height));
                if (active.Bounds != target) active.Bounds = target;
            }
            finally { aligning = false; }
        }

        private static void AttachOwner(Form value)
        {
            if (owner == value) return;
            DetachOwner();
            owner = value;
            owner.LocationChanged += OwnerMoved;
            owner.SizeChanged += OwnerMoved;
            owner.VisibleChanged += OwnerVisibility;
            owner.FormClosed += OwnerClosed;
        }

        private static void DetachOwner()
        {
            if (owner == null) return;
            owner.LocationChanged -= OwnerMoved;
            owner.SizeChanged -= OwnerMoved;
            owner.VisibleChanged -= OwnerVisibility;
            owner.FormClosed -= OwnerClosed;
            owner = null;
        }

        private static void OwnerMoved(object sender, EventArgs e) { Align(); }

        private static void OwnerVisibility(object sender, EventArgs e)
        {
            if (owner == null || owner.IsDisposed || active == null || active.IsDisposed) return;
            if (!owner.Visible || owner.WindowState == FormWindowState.Minimized) active.Hide();
            else
            {
                Align();
                if (!active.Visible) active.Show(owner);
            }
        }

        private static void OwnerClosed(object sender, FormClosedEventArgs e)
        {
            Form[] snapshot = new Form[panels.Count];
            panels.CopyTo(snapshot);
            active = null;
            DetachOwner();
            foreach (Form panel in snapshot) if (panel != null && !panel.IsDisposed) panel.Close();
            panels.Clear();
        }

        private static void PanelClosed(object sender, FormClosedEventArgs e)
        {
            Form panel = sender as Form;
            if (panel == null) return;
            panels.Remove(panel);
            if (active == panel) active = null;
        }

        internal static bool RunOuterBorderDiagnostic(string screenshotPath, out string details)
        {
            using (OutlineDiagnosticWindow window = new OutlineDiagnosticWindow())
            using (Bitmap image = new Bitmap(window.ClientSize.Width, window.ClientSize.Height))
            using (Bitmap baseline = new Bitmap(window.ClientSize.Width, window.ClientSize.Height))
            {
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(-20000, -20000);
                window.Show();
                Application.DoEvents();
                int outlinePaints;
                try
                {
                    GlassBackdrop.SuppressOuterOutlineForDiagnostic = false;
                    GlassBackdrop.ResetOuterOutlineDiagnostic();
                    window.DrawToBitmap(image, window.ClientRectangle);
                    outlinePaints = GlassBackdrop.OuterOutlinePaintCount;
                    GlassBackdrop.SuppressOuterOutlineForDiagnostic = true;
                    window.DrawToBitmap(baseline, window.ClientRectangle);
                }
                finally
                {
                    GlassBackdrop.SuppressOuterOutlineForDiagnostic = false;
                }
                int topSamples = 0, topContinuous = 0, sideSamples = 0, sideContinuous = 0;
                for (int x = 42; x < image.Width - 42; x += 4)
                {
                    topSamples++;
                    if (MaxPixelDifference(image, baseline, x, 0, 2) >= 12) topContinuous++;
                }
                for (int y = 42; y < image.Height - 42; y += 4)
                {
                    sideSamples++;
                    if (MaxPixelDifference(image, baseline, 0, y, 2, true) >= 12) sideContinuous++;
                }
                bool continuous = topSamples > 0 && sideSamples > 0 && topContinuous * 100 >= topSamples * 92 && sideContinuous * 100 >= sideSamples * 92;
                int[] cornerHits = new int[4];
                for (int corner = 0; corner < 4; corner++)
                    cornerHits[corner] = CornerContinuity(image, baseline, corner, 28);
                bool cornersContinuous = cornerHits.All(value => value >= 7);
                bool regionBounded;
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    RectangleF bounds = window.Region.GetBounds(graphics);
                    const float tolerance = 0.01f;
                    regionBounded = bounds.Left >= -tolerance && bounds.Top >= -tolerance &&
                        bounds.Right <= window.ClientSize.Width + tolerance && bounds.Bottom <= window.ClientSize.Height + tolerance;
                }
                if (!string.IsNullOrEmpty(screenshotPath)) SaveCompositeDiagnostic(image, screenshotPath, cornerHits);
                window.Hide();
                bool ok = outlinePaints == 1 && continuous && cornersContinuous && regionBounded;
                details = "outerPaints=" + outlinePaints + "; top=" + topContinuous + "/" + topSamples + "; side=" + sideContinuous + "/" + sideSamples +
                    "; corners=" + string.Join(",", cornerHits.Select(value => value + "/9")) + "; regionBounded=" + regionBounded +
                    "; composite=" + (image.Width + 216) + "x" + Math.Max(image.Height, 244);
                return ok;
            }
        }

        private static int CornerContinuity(Bitmap actual, Bitmap baseline, int corner, int radius)
        {
            int hits = 0;
            double start = corner == 0 ? Math.PI : corner == 1 ? Math.PI * 1.5 : corner == 2 ? 0 : Math.PI * .5;
            int centerX = corner == 0 || corner == 3 ? 1 + radius : actual.Width - 2 - radius;
            int centerY = corner < 2 ? 1 + radius : actual.Height - 2 - radius;
            for (int sample = 1; sample <= 9; sample++)
            {
                double angle = start + sample * (Math.PI / 2d) / 10d;
                int x = (int)Math.Round(centerX + radius * Math.Cos(angle));
                int y = (int)Math.Round(centerY + radius * Math.Sin(angle));
                if (MaxAreaDifference(actual, baseline, x, y, 2) >= 10) hits++;
            }
            return hits;
        }

        private static int MaxAreaDifference(Bitmap actual, Bitmap baseline, int x, int y, int radius)
        {
            int maximum = 0;
            for (int yy = Math.Max(0, y - radius); yy <= Math.Min(actual.Height - 1, y + radius); yy++)
                for (int xx = Math.Max(0, x - radius); xx <= Math.Min(actual.Width - 1, x + radius); xx++)
                {
                    Color a = actual.GetPixel(xx, yy), b = baseline.GetPixel(xx, yy);
                    maximum = Math.Max(maximum, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
                }
            return maximum;
        }

        private static void SaveCompositeDiagnostic(Bitmap source, string path, int[] cornerHits)
        {
            const int crop = 64, preview = 96, gutter = 8;
            using (Bitmap composite = new Bitmap(source.Width + preview * 2 + gutter * 3, Math.Max(source.Height, preview * 2 + gutter * 3 + 28)))
            using (Graphics graphics = Graphics.FromImage(composite))
            using (Font font = new Font("Times New Roman", 12, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush ink = new SolidBrush(Color.White))
            {
                graphics.Clear(Color.FromArgb(25, 25, 42));
                graphics.DrawImageUnscaled(source, 0, 0);
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                Rectangle[] sources =
                {
                    new Rectangle(0,0,crop,crop), new Rectangle(source.Width-crop,0,crop,crop),
                    new Rectangle(source.Width-crop,source.Height-crop,crop,crop), new Rectangle(0,source.Height-crop,crop,crop)
                };
                for (int i = 0; i < 4; i++)
                {
                    int column = i % 2, row = i / 2;
                    Rectangle destination = new Rectangle(source.Width + gutter + column * (preview + gutter), gutter + row * (preview + 28 + gutter), preview, preview);
                    graphics.DrawImage(source, destination, sources[i], GraphicsUnit.Pixel);
                    graphics.DrawString("corner " + (i + 1) + "  " + cornerHits[i] + "/9", font, ink, destination.X, destination.Bottom + 3);
                }
                composite.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private static int MaxPixelDifference(Bitmap actual, Bitmap baseline, int x, int y, int span, bool vertical = false)
        {
            int maximum = 0;
            for (int i = 0; i <= span; i++)
            {
                int sampleX = vertical ? x + i : x;
                int sampleY = vertical ? y : y + i;
                Color a = actual.GetPixel(sampleX, sampleY);
                Color b = baseline.GetPixel(sampleX, sampleY);
                int difference = Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
                if (difference > maximum) maximum = difference;
            }
            return maximum;
        }

        private sealed class OutlineDiagnosticWindow : DockedGlassWindow
        {
            internal OutlineDiagnosticWindow()
            {
                ClientSize = new Size(520, 700);
                GlassPanel content = new GlassPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };
                GlassPanel header = new GlassPanel { Dock = DockStyle.Top, Height = 130 };
                GlassFlowPanel body = new GlassFlowPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
                for (int i = 0; i < 5; i++) body.Controls.Add(new Label { Text = "边框连续性诊断 " + (i + 1), ForeColor = Color.White, BackColor = Color.Transparent, Width = 430, Height = 72, TextAlign = ContentAlignment.MiddleCenter });
                content.Controls.Add(body);
                content.Controls.Add(header);
                Controls.Add(content);
            }
        }
    }

    internal class DockedGlassWindow : Form
    {
        protected DockedGlassWindow()
        {
            GlassBackdrop.PrepareTheme();
            DynamicBackground.Attach(this);
            Size = new Size(720, 720);
            MinimumSize = new Size(320, 300);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            Padding = new Padding(5);
            StartPosition = FormStartPosition.Manual;
            BackColor = Palette.Background;
            ForeColor = Palette.Ink;
            Font = Palette.Font(10, false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.EnableRoundedCorners(Handle);
            UpdateOutline();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateOutline();
            Invalidate(true);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { GlassBackdrop.Draw(e.Graphics, this); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if(ClientSize.Width<60 || ClientSize.Height<60)return;
            // DockFill children otherwise cover the middle of a curved rim:
            // a 5px rectangular padding protects straight edges, not corners.
            // Clip just the direct children to the inner window silhouette.
            foreach(Control child in Controls)
            {
                if(child.Width<1 || child.Height<1)continue;
                Rectangle inner=new Rectangle(4-child.Left,4-child.Top,ClientSize.Width-8,ClientSize.Height-8);
                using(GraphicsPath path=GraphicsExtensions.CreateRoundedPath(inner,25))
                {
                    Region clipped=new Region(path);clipped.Intersect(child.ClientRectangle);
                    Region previous=child.Region;child.Region=clipped;if(previous!=null)previous.Dispose();
                }
            }
        }

        internal virtual void RefreshGlass()
        {
            Palette.UpdateControls(this);
            Invalidate(true);
        }

        private void UpdateOutline()
        {
            if (ClientSize.Width < 60 || ClientSize.Height < 60) return;
            using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(ClientRectangle, 28))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }
    }

}
