using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexAccountDock
{
    internal static class PlanVisualPainting
    {
        internal const TextFormatFlags PreserveGraphics =
            TextFormatFlags.PreserveGraphicsTranslateTransform |
            TextFormatFlags.PreserveGraphicsClipping;

        internal static void PaintBackdrop(Control control, Graphics graphics)
        {
            if(control==null || graphics==null || control.Width<1 || control.Height<1)return;
            Control root=control.FindForm();
            if(root==null){root=control;while(root.Parent!=null)root=root.Parent;}
            Point origin=RootOrigin(control,root);
            GraphicsState saved=graphics.Save();
            try
            {
                graphics.TranslateTransform(-origin.X,-origin.Y);
                GlassBackdrop.DrawReferenceBackground(graphics,new Rectangle(Point.Empty,root.ClientSize),false);
                IPlanBackdropLayer rootLayer=root as IPlanBackdropLayer;
                if(rootLayer!=null)rootLayer.PaintPlanBackdropLayer(graphics,new Rectangle(Point.Empty,root.ClientSize));
                else if(root.Width>3 && root.Height>3)
                {
                    Rectangle box=new Rectangle(1,1,root.Width-3,root.Height-3);
                    using(GraphicsPath path=GraphicsExtensions.CreateRoundedPath(box,28))
                    using(SolidBrush veil=new SolidBrush(Color.FromArgb(42,ThemeVisual.Current.PanelBottom)))
                    {graphics.SmoothingMode=SmoothingMode.AntiAlias;graphics.FillPath(veil,path);}
                }
                List<Control> ancestors=new List<Control>();
                for(Control p=control.Parent;p!=null && p!=root;p=p.Parent)ancestors.Add(p);
                ancestors.Reverse();
                foreach(Control parent in ancestors)
                {
                    IPlanBackdropLayer layer=parent as IPlanBackdropLayer;
                    if(layer==null)continue;
                    Rectangle bounds=new Rectangle(RootOrigin(parent,root),parent.ClientSize);
                    // Keep every intersection for the rest of the ancestor
                    // chain. A nested surface must remain clipped by all of
                    // its outer viewports, especially the scrolling body.
                    graphics.SetClip(bounds,CombineMode.Intersect);
                    IPlanBackdropReplay replay=parent as IPlanBackdropReplay;
                    if(replay!=null)
                    {
                        replay.IntersectPlanBackdropViewport(graphics,bounds);
                        replay.PaintPlanBackdropUnderlay(graphics,bounds);
                    }
                    else layer.PaintPlanBackdropLayer(graphics,bounds);
                }
            }
            finally{graphics.Restore(saved);}
        }

        internal static Point RootOrigin(Control control,Control root)
        {
            Point result=Point.Empty;
            for(Control p=control;p!=null && p!=root;p=p.Parent)result.Offset(p.Left,p.Top);
            return result;
        }

        internal static void DrawText(Graphics graphics,string text,Font font,Rectangle bounds,Color color,StringAlignment alignment,bool ellipsis)
        {
            float size=font.Unit==GraphicsUnit.Pixel?font.Size:font.SizeInPoints*graphics.DpiY/72f;
            AppTypography.Draw(graphics,text,size,font.Style,bounds,color,alignment,ellipsis);
        }

        internal static Color InputFill(ThemeLook look)
        {
            return ThemeVisual.Blend(look.PanelBottom, look.PanelTop, .18f);
        }
    }

    internal interface IPlanBackdropLayer
    {
        void PaintPlanBackdropLayer(Graphics graphics,Rectangle rootBounds);
    }

    /// <summary>
    /// Optional contract for layered controls whose descendants need the
    /// translucent surface beneath them, but must not replay the ancestor's
    /// decorative border or divider.
    /// </summary>
    internal interface IPlanBackdropReplay
    {
        void IntersectPlanBackdropViewport(Graphics graphics,Rectangle rootBounds);
        void PaintPlanBackdropUnderlay(Graphics graphics,Rectangle rootBounds);
    }

    /// <summary>
    /// Glass action button used by the stagger-plan surface.  The control lets
    /// its parent paint the backdrop first, so translucent fills remain aligned
    /// with the single continuous glass panel behind it.
    /// </summary>
    internal sealed class PlanGlassButton : Button
    {
        private bool active;
        private bool emphasized;
        internal bool UsePanelBackground {get;set;}
        private int radius = 16;
        private bool hover;
        private bool pressed;

        internal bool Active
        {
            get { return active; }
            set { if (active == value) return; active = value; Invalidate(); }
        }

        internal bool Emphasized
        {
            get { return emphasized; }
            set { if (emphasized == value) return; emphasized = value; Invalidate(); }
        }

        internal int Radius
        {
            get { return radius; }
            set { int next = Math.Max(2, value); if (radius == next) return; radius = next; Invalidate(); }
        }

        internal PlanGlassButton()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor,false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Opaque, true);
            BackColor=ThemeVisual.Current.PanelBottom;
            ForeColor = Color.White;
            Font = new Font("楷体", 17, FontStyle.Bold, GraphicsUnit.Pixel);
            FlatStyle = FlatStyle.Standard;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (UsePanelBackground) g.Clear(BackColor);
            else PlanVisualPainting.PaintBackdrop(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (ClientSize.Width < 4 || ClientSize.Height < 4) return;

            ThemeLook look = ThemeVisual.Current;
            Rectangle box = new Rectangle(2, 2, ClientSize.Width - 5, ClientSize.Height - 5);
            int curve = Math.Min(Radius, Math.Max(2, box.Height / 2));
            bool highlighted = Active || Emphasized;

            Color top = highlighted
                ? ThemeVisual.Blend(look.ButtonTop, look.Accent, Emphasized ? .58f : .38f)
                : look.ButtonTop;
            Color bottom = highlighted
                ? ThemeVisual.Blend(look.ButtonBottom, look.Accent, Emphasized ? .54f : .30f)
                : look.ButtonBottom;

            if (hover)
            {
                top = ThemeVisual.Blend(top, Color.White, .13f);
                bottom = ThemeVisual.Blend(bottom, look.Accent2, .10f);
            }
            if (pressed)
            {
                top = ThemeVisual.Blend(top, look.PanelBottom, .18f);
                bottom = ThemeVisual.Blend(bottom, look.PanelBottom, .25f);
                box.Offset(0, 1);
            }
            if (!Enabled)
            {
                top = ThemeVisual.Blend(look.PanelBottom, Color.Gray, .38f);
                bottom = ThemeVisual.Blend(look.PanelBottom, look.ButtonBottom, .35f);
            }

            using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(box, curve))
            using (LinearGradientBrush fill = new LinearGradientBrush(
                box, Color.FromArgb(hover ? 214 : 194, top), Color.FromArgb(pressed ? 215 : 190, bottom), 90f))
            using (Pen rim = new Pen(Color.FromArgb(235, highlighted ? look.Accent : look.Rim), 1.2f))
            {
                g.FillPath(fill, path);
                g.DrawPath(rim, path);
            }

            Color textColor = Enabled ? Color.White : Color.FromArgb(150, 225, 230, 232);
            int inset=Width<65?3:8;
            Rectangle textBox = new Rectangle(box.X + inset, box.Y, Math.Max(1, box.Width - inset*2), box.Height);
            PlanVisualPainting.DrawText(g,Text,Font,textBox,textColor,StringAlignment.Center,false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Font != null) Font.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Theme-aware circular A/B/C badge.</summary>
    internal sealed class PlanBadge : Control
    {
        private int variant;

        internal int Variant
        {
            get { return variant; }
            set { if (variant == value) return; variant = value; Invalidate(); }
        }

        internal PlanBadge()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor,false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Opaque, true);
            BackColor=ThemeVisual.Current.PanelBottom;
            Text = "A";
            ForeColor = Color.White;
            Font = new Font("Times New Roman", 20, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            PlanVisualPainting.PaintBackdrop(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int diameter = Math.Max(1, Math.Min(ClientSize.Width, ClientSize.Height) - 3);
            Rectangle circle = new Rectangle((ClientSize.Width - diameter) / 2, (ClientSize.Height - diameter) / 2, diameter, diameter);
            ThemeLook look = ThemeVisual.Current;
            Color fill = BadgeColor(look, Variant);
            using (LinearGradientBrush brush = new LinearGradientBrush(circle,
                ThemeVisual.Blend(fill, Color.White, .17f),
                ThemeVisual.Blend(fill, look.PanelBottom, .12f), 90f))
            using (Pen rim = new Pen(Color.FromArgb(120, Color.White), 1f))
            {
                g.FillEllipse(brush, circle);
                g.DrawEllipse(rim, circle);
            }
            PlanVisualPainting.DrawText(g,Text,Font,circle,Enabled ? Color.White : Color.FromArgb(150, Color.White),StringAlignment.Center,false);
        }

        internal static Color BadgeColor(ThemeLook look, int badgeVariant)
        {
            if (badgeVariant == 1) return ThemeVisual.Blend(look.ButtonBottom, look.Accent2, .32f);
            if (badgeVariant == 2) return ThemeVisual.Blend(look.ButtonBottom, look.Rim, .38f);
            return ThemeVisual.Blend(look.ButtonBottom, look.Accent, .44f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Font != null) Font.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Full-row account selector.  It keeps the native CheckBox input behavior,
    /// while drawing the square, badge and label as one aligned visual target.
    /// </summary>
    internal sealed class PlanAccountCheck : CheckBox
    {
        private string badge = "A";
        private int variant;
        private bool hover;

        internal string Badge
        {
            get { return badge; }
            set { string next = string.IsNullOrEmpty(value) ? "A" : value; if (badge == next) return; badge = next; Invalidate(); }
        }

        internal int Variant
        {
            get { return variant; }
            set { if (variant == value) return; variant = value; Invalidate(); }
        }

        internal PlanAccountCheck()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor,false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Opaque, true);
            BackColor=ThemeVisual.Current.PanelBottom;
            ForeColor = Color.White;
            Font = new Font("Times New Roman", 17, FontStyle.Bold, GraphicsUnit.Pixel);
            AutoEllipsis = true;
            UseVisualStyleBackColor = false;
            FlatStyle = FlatStyle.Standard;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            PlanVisualPainting.PaintBackdrop(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            ThemeLook look = ThemeVisual.Current;

            if (hover || Focused)
            {
                Rectangle hoverBox = new Rectangle(0, 1, Math.Max(1, Width - 1), Math.Max(1, Height - 2));
                using (GraphicsPath hoverPath = GraphicsExtensions.CreateRoundedPath(hoverBox, Math.Min(13, Math.Max(4, Height / 3))))
                using (SolidBrush hoverFill = new SolidBrush(Color.FromArgb(hover ? 42 : 25, look.Accent)))
                    g.FillPath(hoverFill, hoverPath);
            }

            int squareSize = Math.Min(28, Math.Max(18, Height - 10));
            Rectangle checkBox = new Rectangle(5, (Height - squareSize) / 2, squareSize, squareSize);
            Color checkFill = Checked
                ? ThemeVisual.Blend(look.Accent, Color.FromArgb(27, 179, 255), .50f)
                : Color.FromArgb(95, look.PanelBottom);
            using (GraphicsPath checkPath = GraphicsExtensions.CreateRoundedPath(checkBox, 5))
            using (LinearGradientBrush fill = new LinearGradientBrush(checkBox,
                Color.FromArgb(Checked ? 245 : 150, ThemeVisual.Blend(checkFill, Color.White, Checked ? .15f : .04f)),
                Color.FromArgb(Checked ? 230 : 135, checkFill), 90f))
            using (Pen rim = new Pen(Color.FromArgb(Checked ? 235 : 150, Checked ? Color.White : look.Rim), 1.1f))
            {
                g.FillPath(fill, checkPath);
                g.DrawPath(rim, checkPath);
            }

            if (Checked)
            {
                using (Pen tick = new Pen(Color.White, 2.7f))
                {
                    tick.StartCap = tick.EndCap = LineCap.Round;
                    g.DrawLines(tick, new[]
                    {
                        new Point(checkBox.X + 7, checkBox.Y + checkBox.Height / 2),
                        new Point(checkBox.X + 12, checkBox.Bottom - 7),
                        new Point(checkBox.Right - 6, checkBox.Y + 7)
                    });
                }
            }

            int badgeSize = Math.Min(31, Math.Max(20, Height - 7));
            Rectangle badgeBox = new Rectangle(checkBox.Right + 9, (Height - badgeSize) / 2, badgeSize, badgeSize);
            Color badgeFill = PlanBadge.BadgeColor(look, Variant);
            using (LinearGradientBrush fill = new LinearGradientBrush(badgeBox,
                ThemeVisual.Blend(badgeFill, Color.White, .16f),
                ThemeVisual.Blend(badgeFill, look.PanelBottom, .10f), 90f))
            using (Pen rim = new Pen(Color.FromArgb(110, Color.White), 1f))
            {
                g.FillEllipse(fill, badgeBox);
                g.DrawEllipse(rim, badgeBox);
            }
            using (Font badgeFont = new Font("Times New Roman", 18, FontStyle.Bold, GraphicsUnit.Pixel))
                PlanVisualPainting.DrawText(g,Badge,badgeFont,badgeBox,Enabled ? Color.White : Color.FromArgb(145, Color.White),StringAlignment.Center,false);

            int textLeft = badgeBox.Right + 10;
            Rectangle textBox = new Rectangle(textLeft, 0, Math.Max(1, Width - textLeft - 7), Height);
            Color textColor = Enabled ? Color.White : Color.FromArgb(145, 225, 230, 232);
            PlanVisualPainting.DrawText(g,Text,Font,textBox,textColor,StringAlignment.Near,true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Font != null) Font.Dispose();
            base.Dispose(disposing);
        }
    }
}
