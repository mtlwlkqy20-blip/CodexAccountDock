using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace CodexAccountDock
{
    // Small editable glass inputs; the scheduler still consumes the same DateTime/decimal values.
    internal abstract class PlanField : UserControl
    {
        protected readonly TextBox Input;
        private bool syncing;
        protected abstract bool HasClock {get;}
        protected virtual bool HasCalendar {get{return false;}}
        protected virtual bool HasSpinner {get{return true;}}
        protected abstract string DisplayValue {get;}
        protected abstract void AcceptText(string text);
        protected abstract void Step(int direction);
        protected PlanField()
        {
            DoubleBuffered=true;MinimumSize=new Size(62,32);Size=new Size(150,38);
            SetStyle(ControlStyles.SupportsTransparentBackColor,false);
            SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|
                ControlStyles.UserPaint|ControlStyles.ResizeRedraw|ControlStyles.Opaque,true);
            BackColor=ThemeVisual.Current.PanelBottom;
            Input=new TextBox {BorderStyle=BorderStyle.None,Font=new Font("Times New Roman",20,FontStyle.Bold,GraphicsUnit.Pixel),ForeColor=Color.White,BackColor=PlanVisualPainting.InputFill(ThemeVisual.Current),TextAlign=HorizontalAlignment.Center};
            Input.TextChanged+=delegate{if(!syncing)AcceptText(Input.Text);};
            Input.Leave+=delegate{SyncText();};
            Input.KeyDown+=delegate(object sender,KeyEventArgs e)
            {
                if(e.KeyCode==Keys.Up || e.KeyCode==Keys.Down){Step(e.KeyCode==Keys.Up?1:-1);e.Handled=true;e.SuppressKeyPress=true;}
                else if(e.KeyCode==Keys.Enter){AcceptText(Input.Text);SyncText();e.Handled=true;e.SuppressKeyPress=true;}
            };
            Controls.Add(Input);Resize+=delegate{PlaceInput();};PlaceInput();
        }
        protected void SyncText()
        {
            if(Input==null)return;syncing=true;
            try{Input.Text=DisplayValue;}finally{syncing=false;}
            Invalidate();
        }
        protected void NotifyChanged(){Invalidate();}
        internal void DiagnosticEdit(string text){Input.Text=text;}
        internal void DiagnosticCommit(){AcceptText(Input.Text);SyncText();}
        internal void DiagnosticStep(int direction,int selection){Input.SelectionStart=Math.Min(selection,Input.TextLength);Step(direction);}
        internal void RefreshTheme(){Input.BackColor=PlanVisualPainting.InputFill(ThemeVisual.Current);Input.ForeColor=Color.White;Invalidate(true);}
        internal bool IsReadable {get{return Input.ForeColor==Color.White && Input.BackColor==PlanVisualPainting.InputFill(ThemeVisual.Current);}}
        private void PlaceInput()
        {
            if(Input==null)return;int left=(HasClock||HasCalendar)?30:8;
            Input.SetBounds(left,Math.Max(3,(Height-Input.PreferredHeight)/2),Math.Max(12,Width-left-(HasSpinner?26:8)),Input.PreferredHeight);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            PlanVisualPainting.PaintBackdrop(this,e.Graphics);Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;ThemeLook look=ThemeVisual.Current;
            Rectangle box=new Rectangle(1,1,Width-3,Height-3);
            using(var path=GraphicsExtensions.CreateRoundedPath(box,Math.Min(12,Height/3)))
            using(var brush=new SolidBrush(PlanVisualPainting.InputFill(look)))
            using(var pen=new Pen(Color.FromArgb(205,look.Rim),1.2f)){g.FillPath(brush,path);g.DrawPath(pen,path);}
            Color inputFill=PlanVisualPainting.InputFill(look);
            if(Input.BackColor!=inputFill)Input.BackColor=inputFill;
            if(Input.ForeColor!=Color.White)Input.ForeColor=Color.White;
            if(HasClock)
            {
                int cx=19,cy=Height/2;using(var pen=new Pen(Color.White,1.6f)){g.DrawEllipse(pen,cx-8,cy-8,16,16);g.DrawLine(pen,cx,cy,cx,cy-5);g.DrawLine(pen,cx,cy,cx+4,cy+2);}
            }
            else if(HasCalendar)
            {
                Rectangle calendar=new Rectangle(11,Height/2-8,16,16);
                using(var pen=new Pen(Color.White,1.5f))
                {
                    g.DrawRectangle(pen,calendar);g.DrawLine(pen,calendar.Left,calendar.Top+5,calendar.Right,calendar.Top+5);
                    g.DrawLine(pen,calendar.Left+4,calendar.Top-2,calendar.Left+4,calendar.Top+3);
                    g.DrawLine(pen,calendar.Right-4,calendar.Top-2,calendar.Right-4,calendar.Top+3);
                }
            }
            if(HasSpinner)using(var pen=new Pen(Color.White,1.7f))
            {
                int cx=Width-16,upper=Height/2-6,lower=Height/2+6;
                g.DrawLines(pen,new[]{new Point(cx-4,upper+2),new Point(cx,upper-2),new Point(cx+4,upper+2)});
                g.DrawLines(pen,new[]{new Point(cx-4,lower-2),new Point(cx,lower+2),new Point(cx+4,lower-2)});
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
            if(HasSpinner && e.X>=Width-30)Step(e.Y<Height/2?1:-1);else Input.Focus();
        }
        protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);if(Input.Focused)Step(e.Delta>0?1:-1);}
        protected override void Dispose(bool disposing){if(disposing && Input!=null)Input.Font.Dispose();base.Dispose(disposing);}
    }

    internal sealed class PlanTimeInput : PlanField
    {
        private ToolStripDropDown activePicker;
        protected override void Dispose(bool disposing){if(disposing&&activePicker!=null){activePicker.Dispose();activePicker=null;}base.Dispose(disposing);}
        private DateTime value=DateTime.Today.AddHours(9);
        public event EventHandler ValueChanged;
        protected override bool HasClock {get{return true;}}
        protected override string DisplayValue {get{return value.ToString("HH:mm",CultureInfo.InvariantCulture);}}
        public DateTime Value
        {
            get{return value;}
            set{DateTime next=value.Date.AddHours(value.Hour).AddMinutes(value.Minute);if(this.value==next){SyncText();return;}this.value=next;SyncText();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}
        }
        internal PlanTimeInput(){
            SyncText();
            Input.MouseClick+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left)OpenPicker();};
        }
        protected override void OnMouseDown(MouseEventArgs e){
            base.OnMouseDown(e);
            if(e.Button==MouseButtons.Left && e.X<Width-30)OpenPicker();
        }
        private static ComboBox TimeList(int count,int selected,int x){
            var list=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Left=x,Width=61,Font=SystemFonts.MessageBoxFont,FlatStyle=FlatStyle.Flat,BackColor=ThemeVisual.Current.PanelBottom,ForeColor=Color.White};
            for(int i=0;i<count;i++)list.Items.Add(i.ToString("00"));
            list.SelectedIndex=selected;return list;
        }
        private ToolStripDropDown OpenPicker(){
            if(activePicker!=null&&!activePicker.IsDisposed){activePicker.Focus();return activePicker;}
            var popup=new ToolStripDropDown {AutoClose=false,AutoSize=true,Padding=Padding.Empty,DropShadowEnabled=false,BackColor=ThemeVisual.Current.PanelBottom,Renderer=new ToolStripProfessionalRenderer(new ThemeMenuColors())};
            var panel=new Panel {Size=new Size(326,48),BackColor=ThemeVisual.Current.PanelBottom};
            activePicker=popup;
            var hour=TimeList(24,value.Hour,10);
            var minute=TimeList(60,value.Minute,83);
            var apply=new PlanGlassButton {Text="确定",Left=158,Width=80,Height=32,UsePanelBackground=true,Emphasized=true,Radius=9};
            var cancel=new PlanGlassButton{Text="取消",Left=246,Width=70,Height=32,UsePanelBackground=true,Radius=9};cancel.Click+=delegate{popup.Close();};
            panel.Controls.AddRange(new Control[]{hour,minute,apply,cancel});
            foreach(Control item in panel.Controls)item.Top=(panel.Height-item.Height)/2;
            popup.Items.Add(new ToolStripControlHost(panel){AutoSize=false,Size=panel.Size,Margin=Padding.Empty,Padding=Padding.Empty});
            apply.Click+=delegate{Value=value.Date.AddHours(hour.SelectedIndex).AddMinutes(minute.SelectedIndex);if(!popup.IsDisposed)popup.Close();};
            popup.Closed+=delegate{activePicker=null;if(IsHandleCreated && !IsDisposed)BeginInvoke((Action)delegate{popup.Dispose();});};
            popup.Show(this,new Point(0,Height+3));return popup;
        }
        protected override void AcceptText(string text)
        {
            DateTime parsed;
            if(DateTime.TryParseExact(text,new[]{"H:mm","HH:mm"},CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed))
            {
                DateTime next=value.Date.AddHours(parsed.Hour).AddMinutes(parsed.Minute);
                if(next!=value){value=next;NotifyChanged();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}
            }
        }
        protected override void Step(int direction)
        {
            int total=value.Hour*60+value.Minute+direction*(Input.SelectionStart<2?60:1);total=(total%1440+1440)%1440;
            int selection=Input.SelectionStart;Value=value.Date.AddMinutes(total);Input.Focus();Input.SelectionStart=Math.Min(selection,Input.TextLength);
        }
    }

    internal sealed class PlanDateInput : PlanField
    {
        private DateTime value=DateTime.Today;
        private Form calendarPopup;
        public event EventHandler ValueChanged;
        protected override bool HasClock {get{return false;}}
        protected override bool HasCalendar {get{return true;}}
        protected override bool HasSpinner {get{return false;}}
        protected override string DisplayValue {get{return value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}}
        public DateTime Value
        {
            get{return value;}
            set
            {
                DateTime next=value.Date;
                if(this.value==next){SyncText();return;}
                this.value=next;SyncText();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);
            }
        }
        internal PlanDateInput()
        {
            Size=new Size(174,38);Font oldFont=Input.Font;Input.Font=new Font("Times New Roman",17,FontStyle.Bold,GraphicsUnit.Pixel);oldFont.Dispose();
            Input.ReadOnly=false;Input.Cursor=Cursors.IBeam;Input.AccessibleName="日期，可输入 yyyy-MM-dd，点击日历选择";
            Input.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.F4 || (e.Alt && e.KeyCode==Keys.Down)){ShowCalendar();e.Handled=true;e.SuppressKeyPress=true;}};
            SyncText();
        }
        protected override void AcceptText(string text)
        {
            DateTime parsed;
            if(DateTime.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed))Value=parsed;
        }
        protected override void Step(int direction)
        {
            try{Value=value.AddDays(direction);}catch(ArgumentOutOfRangeException){Value=direction<0?DateTime.MinValue:DateTime.MaxValue.Date;}
            Input.Focus();Input.SelectAll();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if(e.Button==MouseButtons.Left && e.X<30)ShowCalendar();
        }
        private void ShowCalendar()
        {
            if(calendarPopup!=null && !calendarPopup.IsDisposed){calendarPopup.Close();calendarPopup.Dispose();}
            Form owner=FindForm();
            Form popup=AccountPopup.CreateDateCalendarPopup(value,delegate(DateTime selected,bool accepted){if(accepted)Value=selected.Date;});
            calendarPopup=popup;
            Point screen=PointToScreen(new Point(0,Height+3));Rectangle area=Screen.FromPoint(screen).WorkingArea;
            int y=screen.Y+popup.Height<=area.Bottom?screen.Y:PointToScreen(Point.Empty).Y-popup.Height-3;
            popup.Location=new Point(Math.Max(area.Left,Math.Min(screen.X,area.Right-popup.Width)),Math.Max(area.Top,Math.Min(y,area.Bottom-popup.Height)));
            popup.FormClosed+=delegate{if(calendarPopup==popup)calendarPopup=null;popup.Dispose();if(!IsDisposed){Input.Focus();Invalidate();}};
            if(owner==null)popup.Show();else popup.Show(owner);popup.Activate();
        }
        internal bool CalendarVisible {get{return calendarPopup!=null && !calendarPopup.IsDisposed && calendarPopup.Visible;}}
        internal void SaveCalendarDiagnosticScreenshot(string path)
        {
            ShowCalendar();Application.DoEvents();
            if(calendarPopup==null || calendarPopup.IsDisposed)throw new InvalidOperationException("Calendar did not open");
            using(Bitmap image=new Bitmap(Math.Max(1,calendarPopup.Width),Math.Max(1,calendarPopup.Height)))
            {calendarPopup.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(path,System.Drawing.Imaging.ImageFormat.Png);}
            calendarPopup.Close();
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing && calendarPopup!=null && !calendarPopup.IsDisposed){Form popup=calendarPopup;calendarPopup=null;popup.Close();popup.Dispose();}
            base.Dispose(disposing);
        }
    }

    internal sealed class PlanNumberInput : PlanField
    {
        private decimal minimum=0,maximum=100,value;
        public event EventHandler ValueChanged;
        protected override bool HasClock {get{return false;}}
        protected override string DisplayValue {get{return value.ToString("0",CultureInfo.InvariantCulture);}}
        public decimal Minimum {get{return minimum;}set{minimum=value;if(maximum<minimum)maximum=minimum;Value=this.value;}}
        public decimal Maximum {get{return maximum;}set{maximum=Math.Max(minimum,value);Value=this.value;}}
        public HorizontalAlignment TextAlign {get{return Input.TextAlign;}set{Input.TextAlign=value;}}
        public decimal Value
        {
            get{return value;}
            set{decimal next=Math.Max(minimum,Math.Min(maximum,decimal.Truncate(value)));if(this.value==next){SyncText();return;}this.value=next;SyncText();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}
        }
        internal PlanNumberInput(){Size=new Size(80,38);SyncText();}
        protected override void AcceptText(string text)
        {
            decimal parsed;
            if(decimal.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out parsed) && parsed>=minimum && parsed<=maximum && parsed==decimal.Truncate(parsed) && parsed!=value)
            {value=parsed;NotifyChanged();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}
        }
        protected override void Step(int direction){Value=value+direction;Input.Focus();Input.SelectAll();}
    }

    internal sealed class PlanRepeatSwitch : CheckBox
    {
        internal PlanRepeatSwitch()
        {
            AutoSize=false;Text="每天重复";Font=new Font("楷体",16,FontStyle.Bold,GraphicsUnit.Pixel);
            SetStyle(ControlStyles.SupportsTransparentBackColor,false);
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|
                ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Opaque,true);
            BackColor=ThemeVisual.Current.PanelBottom;FlatStyle=FlatStyle.Standard;UseVisualStyleBackColor=false;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            PlanVisualPainting.PaintBackdrop(this,e.Graphics);Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;ThemeLook theme=ThemeVisual.Current;
            var box=new Rectangle(1,Math.Max(1,(Height-25)/2),46,25);
            using(var path=GraphicsExtensions.CreateRoundedPath(box,12))using(var brush=new SolidBrush(Checked?theme.Accent:ThemeVisual.Blend(theme.PanelTop,Color.Gray,.4f)))g.FillPath(brush,path);
            using(var brush=new SolidBrush(Color.White))g.FillEllipse(brush,Checked?box.Right-23:box.X+3,box.Y+3,19,19);
            PlanVisualPainting.DrawText(g,"每天重复",Font,new Rectangle(54,0,Math.Max(1,Width-54),Height),Color.White,StringAlignment.Near,false);
            if(Focused)using(var ring=new Pen(ThemeVisual.Current.Accent,1.5f))g.DrawRectangle(ring,1,1,Width-3,Height-3);
        }
        protected override void OnCheckedChanged(EventArgs e){base.OnCheckedChanged(e);Invalidate();}
    }

    internal static class PlanInputDiagnostics
    {
        internal static int Run()
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            using(var time=new PlanTimeInput())
            using(var date=new PlanDateInput())
            using(var number=new PlanNumberInput())
            {
                int changes=0;time.ValueChanged+=delegate{changes++;};
                time.Value=DateTime.Today.AddHours(9);time.DiagnosticEdit("22:30");
                if(time.Value.Hour!=22 || time.Value.Minute!=30 || changes!=1)throw new Exception("Editable time");
                time.DiagnosticEdit("25:99");time.DiagnosticCommit();if(time.Value.Hour!=22 || time.Value.Minute!=30)throw new Exception("Invalid time changed value");
                time.Value=DateTime.Today.AddHours(23).AddMinutes(59);time.DiagnosticStep(1,3);
                if(time.Value.Hour!=0 || time.Value.Minute!=0)throw new Exception("Minute arrow rollover");
                time.DiagnosticStep(-1,0);if(time.Value.Hour!=23 || time.Value.Minute!=0)throw new Exception("Hour arrow rollover");
                number.Minimum=0;number.Maximum=59;number.Value=40;number.DiagnosticEdit("59");if(number.Value!=59)throw new Exception("Editable number");
                number.DiagnosticStep(1,0);if(number.Value!=59)throw new Exception("Maximum clamp");
                number.DiagnosticEdit("invalid");number.DiagnosticCommit();if(number.Value!=59)throw new Exception("Invalid number changed value");
                number.Value=-4;if(number.Value!=0)throw new Exception("Minimum clamp");
                int dateChanges=0;date.ValueChanged+=delegate{dateChanges++;};date.Value=new DateTime(2026,10,26);
                int beforeDateStep=dateChanges;date.DiagnosticStep(1,0);if(date.Value!=new DateTime(2026,10,27) || dateChanges!=beforeDateStep+1)throw new Exception("Date arrow step");
                date.DiagnosticEdit("invalid");date.DiagnosticCommit();if(date.Value!=new DateTime(2026,10,27))throw new Exception("Invalid date changed value");
                date.Value=DateTime.MinValue;date.DiagnosticStep(-1,0);if(date.Value!=DateTime.MinValue)throw new Exception("Minimum date clamp");
                date.Value=DateTime.MaxValue.Date;date.DiagnosticStep(1,0);if(date.Value!=DateTime.MaxValue.Date)throw new Exception("Maximum date clamp");
            }
            Console.WriteLine("PLAN_INPUT_OK editable_time HHmm invalid_rejected hour_minute_arrows rollover bounded_numbers calendar_date day_step no_network");return 0;
        }
    }
}
