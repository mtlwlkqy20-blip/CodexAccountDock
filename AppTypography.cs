using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexAccountDock
{
    // One body-text policy. The account-overview display title is deliberately separate.
    internal static class AppTypography
    {
        internal const string ChineseFamily="楷体", LatinFamily="Times New Roman";
        internal static bool IsChinese(char value){return value>=0x2e80;}
        internal static void Draw(Graphics g,string value,float size,FontStyle style,RectangleF box,Color color,StringAlignment alignment,bool ellipsis,string chineseFamily=ChineseFamily)
        {
            style=FontStyle.Bold;
            if(box.Width<=0 || box.Height<=0)return;
            using(Font cn=new Font(chineseFamily,size,style,GraphicsUnit.Pixel))
            using(Font latin=new Font(LatinFamily,size,style,GraphicsUnit.Pixel))
            using(SolidBrush ink=new SolidBrush(color))
            using(StringFormat format=(StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags|=StringFormatFlags.NoWrap|StringFormatFlags.MeasureTrailingSpaces;
                Func<string,float> width=candidate=>Measure(g,candidate,cn,latin,format);
                string text=(value??"").Replace('\r',' ').Replace('\n',' ');
                if(ellipsis && width(text)>box.Width){while(text.Length>0 && width(text+"…")>box.Width)text=text.Substring(0,text.Length-1);text+="…";}
                float ca=Ascent(cn),la=Ascent(latin),ascent=Math.Max(ca,la),descent=Math.Max(Descent(cn),Descent(latin));
                float x=alignment==StringAlignment.Far?box.Right-width(text):alignment==StringAlignment.Center?box.X+(box.Width-width(text))/2:box.X;
                float baseline=box.Y+(box.Height-ascent-descent)/2+ascent;
                GraphicsState saved=g.Save();g.SetClip(box,CombineMode.Intersect);g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                try{foreach(KeyValuePair<bool,string> run in Runs(text)){Font f=run.Key?cn:latin;g.DrawString(run.Value,f,ink,x,baseline-(run.Key?ca:la),format);x+=g.MeasureString(run.Value,f,2000,format).Width;}}
                finally{g.Restore(saved);}
            }
        }
        private static float Ascent(Font f){return f.Size*f.FontFamily.GetCellAscent(f.Style)/f.FontFamily.GetEmHeight(f.Style);}
        private static float Descent(Font f){return f.Size*f.FontFamily.GetCellDescent(f.Style)/f.FontFamily.GetEmHeight(f.Style);}
        private static float Measure(Graphics g,string value,Font cn,Font latin,StringFormat format)
        {float width=0;foreach(KeyValuePair<bool,string> run in Runs(value))width+=g.MeasureString(run.Value,run.Key?cn:latin,2000,format).Width;return width;}
        internal static List<KeyValuePair<bool,string>> Runs(string text)
        {
            var result=new List<KeyValuePair<bool,string>>();
            foreach(char c in text??""){bool chinese=IsChinese(c);if(result.Count==0 || result[result.Count-1].Key!=chinese)result.Add(new KeyValuePair<bool,string>(chinese,c.ToString()));else{var last=result[result.Count-1];result[result.Count-1]=new KeyValuePair<bool,string>(chinese,last.Value+c);}}
            return result;
        }
    }

    internal static class ReadableCaret
    {
        [StructLayout(LayoutKind.Sequential)]private struct CaretInfo{public int Size,Flags;public IntPtr Active,Focus,Capture,Menu,Move,Caret;public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")]private static extern bool GetGUIThreadInfo(uint thread,ref CaretInfo info);
        [DllImport("user32.dll")]private static extern bool GetCaretPos(out Point point);
        [DllImport("user32.dll")]private static extern bool CreateCaret(IntPtr window,IntPtr bitmap,int width,int height);
        [DllImport("user32.dll")]private static extern bool SetCaretPos(int x,int y);
        [DllImport("user32.dll")]private static extern bool ShowCaret(IntPtr window);
        internal static void Apply(Control control){if(!control.Focused||!control.IsHandleCreated)return;Point point;var info=new CaretInfo{Size=Marshal.SizeOf(typeof(CaretInfo))};if(!GetCaretPos(out point)||!GetGUIThreadInfo(0,ref info)||info.Bottom<=info.Top)return;CreateCaret(control.Handle,IntPtr.Zero,2,info.Bottom-info.Top);var input=control as TextBoxBase;int caretY=input!=null&&!input.Multiline?Math.Max(0,(control.ClientSize.Height-(info.Bottom-info.Top))/2):point.Y;SetCaretPos(point.X,caretY);ShowCaret(control.Handle);}
    }
    internal sealed class PairedTextBox : RichTextBox
    {
        private sealed class EditState
        {
            internal readonly string Text;internal readonly int Start,Length;
            internal EditState(string text,int start,int length){Text=text??string.Empty;Start=start;Length=length;}
        }
        private Font chinese,latin;
        private float basePixelSize,displayPixelScale=1;
        private FontStyle basePixelStyle;
        internal void SetPixelScale(float scale){scale=Math.Max(1,scale);if(Math.Abs(scale-displayPixelScale)<.01f)return;displayPixelScale=scale;chinese.Dispose();latin.Dispose();chinese=new Font(AppTypography.ChineseFamily,basePixelSize*scale,basePixelStyle,GraphicsUnit.Pixel);latin=new Font(AppTypography.LatinFamily,basePixelSize*scale,basePixelStyle,GraphicsUnit.Pixel);Font=latin;QueueFormat();}
        private readonly List<EditState> undoStates=new List<EditState>(),redoStates=new List<EditState>();
        private bool formatting,imeComposing,formatQueued,formatAfterComposition,restoring;
        private int formatGeneration,formatPassCount;
        private string lastText=string.Empty;
        private int lastSelectionStart,lastSelectionLength;
        private EditState compositionStart;
        private const int WmSetRedraw=0x000b,EmFormatRange=0x0439,WmImeStartComposition=0x010d,WmImeEndComposition=0x010e;
        private const int PrfClient=0x00000004,PrfEraseBackground=0x00000008;
        [StructLayout(LayoutKind.Sequential)]private struct NativeRect {internal int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)]private struct CharacterRange {internal int First,Last;}
        [StructLayout(LayoutKind.Sequential)]private struct FormatRange {internal IntPtr Hdc,Target;internal NativeRect Area,Page;internal CharacterRange Characters;}
        [DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wparam,IntPtr lparam);
        internal PairedTextBox(float size,bool bold)
        {
            FontStyle style=FontStyle.Bold;
            basePixelSize=size;basePixelStyle=style;chinese=new Font(AppTypography.ChineseFamily,size,style,GraphicsUnit.Pixel);
            latin=new Font(AppTypography.LatinFamily,size,style,GraphicsUnit.Pixel);
            Font=chinese;BorderStyle=BorderStyle.None;DetectUrls=false;ScrollBars=RichTextBoxScrollBars.None;Multiline=false;WordWrap=false;
        }
        internal int PreferredLineHeight {get{return (int)Math.Ceiling(Math.Max(chinese.GetHeight(),latin.GetHeight()))+4;}}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);QueueFormat();}
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);BeginInvoke((MethodInvoker)delegate{if(!IsDisposed)ReadableCaret.Apply(this);});}
        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if(formatting)return;
            if(imeComposing){formatAfterComposition=true;return;}
            if(!restoring && !ReadOnly && Text!=lastText){Push(undoStates,new EditState(lastText,lastSelectionStart,lastSelectionLength));redoStates.Clear();}
            lastText=Text;lastSelectionStart=SelectionStart;lastSelectionLength=SelectionLength;
            QueueFormat();
        }
        protected override void OnSelectionChanged(EventArgs e)
        {
            base.OnSelectionChanged(e);
            if(!Multiline && !formatting && !imeComposing && Focused)ReadableCaret.Apply(this);
            if(!formatting && !restoring && !imeComposing){lastSelectionStart=SelectionStart;lastSelectionLength=SelectionLength;}
        }
        protected override void WndProc(ref Message message)
        {
            if(message.Msg==WmImeStartComposition)
            {
                imeComposing=true;compositionStart=new EditState(lastText,lastSelectionStart,lastSelectionLength);
            }
            base.WndProc(ref message);
            if(message.Msg==WmImeEndComposition)
            {
                imeComposing=false;
                if(!ReadOnly && compositionStart!=null && Text!=compositionStart.Text){Push(undoStates,compositionStart);redoStates.Clear();}
                compositionStart=null;lastText=Text;lastSelectionStart=SelectionStart;lastSelectionLength=SelectionLength;
                if(formatAfterComposition){formatAfterComposition=false;QueueFormat();}
            }
        }
        private void QueueFormat()
        {
            if(formatQueued || formatting || !IsHandleCreated || IsDisposed)return;
            formatQueued=true;int generation=++formatGeneration;
            BeginInvoke((MethodInvoker)delegate
            {
                if(generation!=formatGeneration)return;
                formatQueued=false;
                if(IsDisposed || !IsHandleCreated)return;
                if(imeComposing){formatAfterComposition=true;return;}
                FormatText();
            });
        }
        private void FormatText()
        {
            if(chinese==null || latin==null || formatting || imeComposing || !IsHandleCreated)return;
            int start=SelectionStart,length=SelectionLength;bool redrawSuspended=false;formatting=true;
            try
            {
                SendMessage(Handle,WmSetRedraw,IntPtr.Zero,IntPtr.Zero);redrawSuspended=true;
                int position=0;
                foreach(KeyValuePair<bool,string> run in AppTypography.Runs(Text)){Select(position,run.Value.Length);SelectionFont=run.Key?chinese:latin;SelectionColor=ForeColor;position+=run.Value.Length;}
                Select(Math.Min(start,TextLength),Math.Min(length,Math.Max(0,TextLength-start)));
                base.ClearUndo();formatPassCount++;
            }
            finally
            {
                if(redrawSuspended)SendMessage(Handle,WmSetRedraw,new IntPtr(1),IntPtr.Zero);
                formatting=false;Invalidate();
            }
        }
        private static void Push(List<EditState> states,EditState value)
        {
            if(states.Count==64)states.RemoveAt(0);states.Add(value);
        }
        private EditState CurrentState(){return new EditState(Text,SelectionStart,SelectionLength);}
        private void Restore(EditState state)
        {
            restoring=true;
            try{Text=state.Text;Select(Math.Min(state.Start,TextLength),Math.Min(state.Length,Math.Max(0,TextLength-state.Start)));lastText=Text;lastSelectionStart=SelectionStart;lastSelectionLength=SelectionLength;}
            finally{restoring=false;}
            QueueFormat();
        }
        internal new bool CanUndo {get{return undoStates.Count>0;}}
        internal new bool CanRedo {get{return redoStates.Count>0;}}
        internal new void ClearUndo(){undoStates.Clear();redoStates.Clear();base.ClearUndo();lastText=Text;lastSelectionStart=SelectionStart;lastSelectionLength=SelectionLength;}
        internal new void Undo()
        {
            if(!CanUndo || ReadOnly)return;EditState target=undoStates[undoStates.Count-1];undoStates.RemoveAt(undoStates.Count-1);Push(redoStates,CurrentState());Restore(target);
        }
        internal new void Redo()
        {
            if(!CanRedo || ReadOnly)return;EditState target=redoStates[redoStates.Count-1];redoStates.RemoveAt(redoStates.Count-1);Push(undoStates,CurrentState());Restore(target);
        }
        protected override bool ProcessCmdKey(ref Message message,Keys keyData)
        {
            if(keyData==(Keys.Control|Keys.Z)){Undo();return true;}
            if(keyData==(Keys.Control|Keys.Y) || keyData==(Keys.Control|Keys.Shift|Keys.Z)){Redo();return true;}
            return base.ProcessCmdKey(ref message,keyData);
        }
        internal bool CheckFontPair()
        {
            int start=SelectionStart,length=SelectionLength;bool ok=true;
            try{for(int i=0;i<TextLength;i++){Select(i,1);using(Font f=SelectionFont){if(f==null || f.FontFamily.Name!=(AppTypography.IsChinese(Text[i])?chinese.FontFamily.Name:latin.FontFamily.Name))ok=false;}}}
            finally{Select(start,length);}return ok;
        }
        internal Bitmap CaptureNativeBitmap()
        {
            if(!IsHandleCreated || Width<=0 || Height<=0)throw new InvalidOperationException("Text input is not ready");
            Update();
            var bitmap=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using(Graphics graphics=Graphics.FromImage(bitmap))
            {
                graphics.Clear(BackColor);
                float dx=1440f/graphics.DpiX,dy=1440f/graphics.DpiY;
                Point first=GetPositionFromCharIndex(0);
                FormatRange range=new FormatRange {Area=new NativeRect {Left=(int)(first.X*dx),Top=(int)(first.Y*dy),Right=(int)(Width*dx),Bottom=(int)(Height*dy)},Page=new NativeRect {Right=(int)(Width*dx),Bottom=(int)(Height*dy)},Characters=new CharacterRange {First=0,Last=TextLength}};
                IntPtr hdc=graphics.GetHdc(),buffer=Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(FormatRange)));
                try{range.Hdc=range.Target=hdc;Marshal.StructureToPtr(range,buffer,false);SendMessage(Handle,EmFormatRange,new IntPtr(1),buffer);}
                finally{SendMessage(Handle,EmFormatRange,IntPtr.Zero,IntPtr.Zero);Marshal.FreeCoTaskMem(buffer);graphics.ReleaseHdc(hdc);}
            }
            return bitmap;
        }
        internal Rectangle NativeInkBounds(out int inkPixels)
        {
            using(Bitmap bitmap=CaptureNativeBitmap())
            {
                int left=bitmap.Width,top=bitmap.Height,right=-1,bottom=-1,count=0;
                for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)
                {
                    Color pixel=bitmap.GetPixel(x,y);
                    int backgroundDistance=Math.Abs(pixel.R-BackColor.R)+Math.Abs(pixel.G-BackColor.G)+Math.Abs(pixel.B-BackColor.B);
                    int foregroundDistance=Math.Abs(pixel.R-ForeColor.R)+Math.Abs(pixel.G-ForeColor.G)+Math.Abs(pixel.B-ForeColor.B);
                    if(backgroundDistance<60 || foregroundDistance>=backgroundDistance)continue;
                    count++;left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);
                }
                inkPixels=count;
                return count==0?Rectangle.Empty:Rectangle.FromLTRB(left,top,right+1,bottom+1);
            }
        }
        internal string RunEditingDiagnostic()
        {
            string saved=Text;int savedStart=SelectionStart,savedLength=SelectionLength;
            try
            {
                Text=string.Empty;FlushFormattingForDiagnostic();ClearUndo();
                SelectedText="ai ";SelectedText="中文";FlushFormattingForDiagnostic();
                if(Text!="ai 中文")return "inserted text changed";
                if(!CheckFontPair())return "font pair after insert";
                Select(1,3);if(SelectedText!="i 中")return "selection was not preserved";
                Select(TextLength,0);string before=Text;SelectedText="X";FlushFormattingForDiagnostic();
                if(Text!=before+"X")return "typed suffix changed";
                if(!CanUndo)return "typing did not create undo";
                Undo();if(Text!=before)return "first undo was consumed by formatting";
                if(CanRedo){Redo();if(Text!=before+"X")return "redo did not restore text";Undo();if(Text!=before)return "second undo did not restore text";}
                Clear();FlushFormattingForDiagnostic();if(TextLength!=0)return "clear left text";
                Text="ai 中文";FlushFormattingForDiagnostic();Select(TextLength,0);
                if(!CheckFontPair())return "font pair after clear/refill";
                int beforeComposition=formatPassCount;imeComposing=true;formatAfterComposition=false;compositionStart=new EditState(lastText,lastSelectionStart,lastSelectionLength);
                Text=string.Empty;SelectedText="中";SelectedText="文";
                if(formatPassCount!=beforeComposition)return "formatting ran during IME composition";
                imeComposing=false;if(Text!=compositionStart.Text){Push(undoStates,compositionStart);redoStates.Clear();}compositionStart=null;lastText=Text;lastSelectionStart=SelectionStart;lastSelectionLength=SelectionLength;
                if(formatAfterComposition){formatAfterComposition=false;QueueFormat();}FlushFormattingForDiagnostic();
                if(formatPassCount!=beforeComposition+1 || Text!="中文" || !CheckFontPair())return "IME completion formatting";
                return null;
            }
            finally
            {
                Text=saved;FlushFormattingForDiagnostic();
                Select(Math.Min(savedStart,TextLength),Math.Min(savedLength,Math.Max(0,TextLength-savedStart)));
            }
        }
        internal void FlushFormattingForDiagnostic()
        {
            if(imeComposing)throw new InvalidOperationException("IME composition is active");
            formatQueued=false;formatGeneration++;FormatText();
        }
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing){chinese.Dispose();latin.Dispose();}}
    }

    internal sealed class PairedCue : Control
    {
        internal float PixelScale=1;
        internal PairedCue(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
        protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
        protected override void OnPaint(PaintEventArgs e){AppTypography.Draw(e.Graphics,Text,17*PixelScale,FontStyle.Bold,ClientRectangle,ForeColor,StringAlignment.Near,true);}
    }
}
