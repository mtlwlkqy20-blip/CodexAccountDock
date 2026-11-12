using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexAccountDock
{
    // The web site is only a manually requested data source. The reader itself is
    // a lightweight, theme-aware WinForms canvas and never writes a news database.
    internal static class DailyNews
    {
        private static NewsWindow window;
        private static readonly NewsSession session = new NewsSession();
        internal sealed class NewsSession
        {
            internal List<NewsItem> Items = new List<NewsItem>();
            internal string Category="全部",Query="",Expanded,Status;
            internal int Scroll;
        }

        internal static bool IsGlassWindow(Form form){return SidePanels.IsGlassWindow(form);}
        internal static void RefreshAppearance(){SidePanels.RefreshAppearance();}
        internal static Rectangle DockBounds(Rectangle account,Rectangle area){return SidePanels.DockBounds(account,area);}

        public static void Open(IWin32Window owner)
        {
            Form account=owner as Form;
            if(account==null || account.IsDisposed)return;
            if(window==null || window.IsDisposed)window=new NewsWindow(null,session);
            SidePanels.Activate(account,window);
        }

        // Compatibility for the existing parser regression test. Production uses
        // NewsFeed.Parse and does not call this method.
        internal static List<Dictionary<string,object>> Parse(string json)
        {
            var root=new JavaScriptSerializer {MaxJsonLength=2097152}.Deserialize<Dictionary<string,object>>(json);
            var data=JsonUtil.Dict(JsonUtil.Value(root,"data"));
            IEnumerable raw=JsonUtil.Value(data,"items") as IEnumerable;
            if(raw==null)throw new FormatException("日报数据格式无法识别");
            var result=new List<Dictionary<string,object>>();
            foreach(object value in raw)
            {
                var item=JsonUtil.Dict(value);
                if(item!=null && object.Equals(JsonUtil.Value(item,"selected"),true))result.Add(item);
            }
            return result;
        }

        private static void StartDiagnostic(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);}

        internal static int RunOfflineDiagnostic()
        {
            StartDiagnostic();int code=1,heartbeats=0;
            Func<CancellationToken,Task<List<NewsItem>>> fake=delegate(CancellationToken token){return Task.FromResult(BuildFakeItems(150));};
            using(var test=new NewsWindow(fake))
            using(var pulse=new System.Windows.Forms.Timer {Interval=25})
            using(var stop=new System.Windows.Forms.Timer {Interval=12000})
            {
                pulse.Tick+=delegate{heartbeats++;};
                stop.Tick+=delegate{Console.WriteLine("DAILY_OFFLINE_FAILED deadline");test.Close();};
                test.Shown+=async delegate
                {
                    pulse.Start();stop.Start();
                    try
                    {
                        if(test.ItemCount!=0 || test.RefreshCount!=0)throw new Exception("Opening fetched data");
                        await test.RefreshNow();
                        if(test.ItemCount!=150 || test.RefreshCount!=1)throw new Exception("Fake refresh count");
                        test.Canvas.SetDiagnosticState("模型","模型",true);
                        int before=heartbeats;
                        for(int i=0;i<18;i++){test.Canvas.DiagnosticScroll(i%2==0?90:-70);Application.DoEvents();await Task.Delay(15);}
                        if(heartbeats-before<5)throw new Exception("UI heartbeat starved");
                        test.Canvas.SetDiagnosticState("模型","模型",true);
                        SaveShot(test,"daily-offline.png");SaveShot(test,"daily-cards.png");
                        Console.WriteLine("DAILY_OFFLINE_OK items=150 search tab expand virtual_canvas host_heartbeats="+(heartbeats-before)+" manual_only no_favorites no_cache no_network");code=0;
                    }
                    catch(Exception ex){Console.WriteLine("DAILY_OFFLINE_FAILED "+ex.GetType().Name+": "+ex.Message);}
                    finally{pulse.Stop();stop.Stop();test.Close();}
                };
                Application.Run(test);
            }
            return code;
        }

        internal static int RunResponsiveDiagnostic()
        {
            StartDiagnostic();int code=1;
            using(var test=new NewsWindow(delegate(CancellationToken token){return Task.FromResult(BuildFakeItems(40));}))
            {
                test.Shown+=async delegate
                {
                    try
                    {
                        await test.RefreshNow();
                        string editFailure=test.Canvas.RunSearchEditingDiagnostic();if(editFailure!=null)throw new Exception("Search input/edit/selection/undo diagnostic: "+editFailure);
                        foreach(Size size in new[]{new Size(320,620),new Size(384,700),new Size(720,860)})
                        {
                            test.Size=size;test.Update();Application.DoEvents();
                            if(!test.Canvas.AllHitsInside())throw new Exception("Hit bounds at "+size.Width);
                            if(!test.Canvas.HasAllCategoryTabs())throw new Exception("Seven category tabs at "+size.Width);
                            SaveShot(test,"daily-width-"+size.Width+".png");
                            test.Canvas.SetDiagnosticState("全部","ai 中文",false);test.Update();Application.DoEvents();
                            string searchMetric;if(!test.Canvas.IsSearchCentered(out searchMetric))throw new Exception("Search input not vertically centered at "+size.Width+" ("+searchMetric+")");
                            Console.WriteLine("DAILY_SEARCH_METRIC width="+size.Width+" "+searchMetric);
                            SaveShot(test,"daily-search-"+size.Width+".png");test.Canvas.SetDiagnosticState("全部","",false);
                        }
                        foreach(string tab in new[]{"模型","产品","行业","论文","教程","观点"})
                        {
                            test.Canvas.SetDiagnosticState(tab,"",false);
                            if(test.Canvas.DiagnosticVisibleCount()!=BuildFakeItems(40).Count(item=>item.Category==tab))throw new Exception("Category filter "+tab);
                        }
                        test.Canvas.SetDiagnosticState("全部","",true);test.Update();
                        if(test.Canvas.DiagnosticVisibleCount()!=40 || test.RefreshCount!=1)throw new Exception("All must be the same local union");
                        SaveShot(test,"daily-responsive.png");
                        var retained=new NewsSession();int retentionFetches=0;
                        Func<CancellationToken,Task<List<NewsItem>>> retainSource=delegate(CancellationToken token){retentionFetches++;return Task.FromResult(BuildFakeItems(18));};
                        using(var first=new NewsWindow(retainSource,retained))
                        {
                            first.Size=new Size(500,620);first.Show();await first.RefreshNow();first.Canvas.SetDiagnosticState("模型","模型",true);
                            first.Update();first.Canvas.DiagnosticScroll(75);first.Update();first.Close();
                        }
                        string retainedQuery=retained.Query;int retainedScroll=retained.Scroll;
                        if(retained.Items.Count!=18 || retained.Category!="模型" || retainedQuery!="模型" || retainedScroll<=0)throw new Exception("Session capture");
                        using(var reopened=new NewsWindow(retainSource,retained))
                        {
                            reopened.Size=new Size(500,620);reopened.Show();reopened.Update();Application.DoEvents();
                            var restored=new NewsSession();reopened.Canvas.CaptureSession(restored);
                            if(reopened.ItemCount!=18 || retentionFetches!=1 || restored.Category!="模型" || restored.Query!=retainedQuery || restored.Scroll!=retainedScroll || restored.Expanded!=retained.Expanded)throw new Exception("Reopen must retain page without fetching: count="+reopened.ItemCount+" calls="+retentionFetches+" tab="+restored.Category+" query="+restored.Query+" scroll="+restored.Scroll+" expected="+retainedScroll+" expanded="+restored.Expanded);
                            reopened.Close();
                        }
                        int calls=0;
                        using(var requestTest=new NewsWindow(delegate(CancellationToken token)
                        {
                            calls++;
                            if(calls==2 || calls==5)return new TaskCompletionSource<List<NewsItem>>().Task;
                            if(calls==4){var failure=new TaskCompletionSource<List<NewsItem>>();failure.SetException(new TimeoutException("离线超时测试"));return failure.Task;}
                            return Task.FromResult(BuildFakeItems(3));
                        }))
                        {
                            requestTest.Show();await requestTest.RefreshNow();
                            if(!requestTest.LastRefreshSucceeded || requestTest.ItemCount!=3)throw new Exception("Initial request");
                            Task pending=requestTest.RefreshNow();await Task.Delay(200);await requestTest.RefreshNow();
                            if(await Task.WhenAny(pending,Task.Delay(1500))!=pending || requestTest.ItemCount!=3)throw new Exception("Cancellation responsiveness");
                            await requestTest.RefreshNow();if(!requestTest.LastRefreshSucceeded || calls!=3)throw new Exception("Retry after cancel");
                            await requestTest.RefreshNow();if(requestTest.LastRefreshSucceeded || requestTest.ItemCount!=3 || requestTest.StatusText.IndexOf("超时",StringComparison.Ordinal)<0)throw new Exception("Timeout preserved data");
                            pending=requestTest.RefreshNow();requestTest.Close();
                            if(await Task.WhenAny(pending,Task.Delay(1500))!=pending)throw new Exception("Closing did not cancel");
                        }
                        Console.WriteLine("DAILY_RESPONSIVE_OK widths=320,384,720 seven_tabs local_filters all_union no_favorites hit_clip no_overflow cancel retry timeout_preserves close_cancels session_reopen_query_tab_expand_scroll no_network");code=0;
                    }
                    catch(Exception ex){Console.WriteLine("DAILY_RESPONSIVE_FAILED "+ex.Message);}
                    finally{test.Close();}
                };
                Application.Run(test);
            }
            return code;
        }

        internal static int RunDockDiagnostic()
        {
            StartDiagnostic();int code=1;
            using(var account=new Form {StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(800,60,540,700)})
            using(var news=new NewsWindow(delegate(CancellationToken token){return Task.FromResult(new List<NewsItem>());}))
            {
                account.Shown+=delegate
                {
                    try
                    {
                        SidePanels.Activate(account,news);Rectangle before=news.Bounds;account.Left-=40;
                        if(news.Bounds!=DockBounds(account.Bounds,Screen.FromControl(account).WorkingArea) || news.Bounds==before || news.ShowInTaskbar)throw new Exception("Dock bounds");
                        account.Hide();if(news.Visible)throw new Exception("Dock hide");account.Show();if(!news.Visible)throw new Exception("Dock restore");
                        foreach(Rectangle area in new[]{new Rectangle(0,0,1920,1080),new Rectangle(-1920,0,1920,1080),new Rectangle(0,0,800,600)})
                            if(!area.Contains(DockBounds(new Rectangle(area.Right-540,area.Top,540,620),area)))throw new Exception("Screen bounds");
                        if(news.RefreshCount!=0)throw new Exception("Docking fetched data");
                        Console.WriteLine("DAILY_DOCK_OK move hide restore manual_only no_network");code=0;
                    }
                    catch(Exception ex){Console.WriteLine("DAILY_DOCK_FAILED "+ex.Message);}
                    finally{account.Close();}
                };
                Application.Run(account);
            }
            return code;
        }

        // This is the only diagnostic allowed to touch the real endpoint.
        internal static int RunDiagnostic()
        {
            StartDiagnostic();int code=1;
            using(var test=new NewsWindow(null))
            using(var stop=new System.Windows.Forms.Timer {Interval=110000})
            {
                stop.Tick+=delegate{Console.WriteLine("DAILY_LIVE_FAILED deadline");test.Close();};
                test.Shown+=async delegate
                {
                    stop.Start();
                    try
                    {
                        await test.RefreshNow();
                        if(!test.LastRefreshSucceeded)throw new Exception(test.StatusText);
                        var counts=new List<string>();
                        foreach(string tab in new[]{"模型","产品","行业","论文","教程","观点"})
                        {
                            test.Canvas.SetDiagnosticState(tab,"",false);int count=test.Canvas.DiagnosticVisibleCount();
                            if(count>3)throw new Exception("Category exceeds three: "+tab);counts.Add(tab+"="+count);
                        }
                        test.Canvas.SetDiagnosticState("全部","",true);
                        if(test.ItemCount>18 || test.Canvas.DiagnosticVisibleCount()!=test.ItemCount)throw new Exception("All union limit");
                        test.Update();SaveShot(test,"daily-live.png");
                        Console.WriteLine("DAILY_LIVE_OK items="+test.ItemCount+" "+string.Join(" ",counts)+" manual_request=1 no_cache");code=0;
                    }
                    catch(Exception ex){Console.WriteLine("DAILY_LIVE_FAILED "+ex.GetType().Name+": "+ex.Message);}
                    finally{stop.Stop();test.Close();}
                };
                Application.Run(test);
            }
            return code;
        }

        private static List<NewsItem> BuildFakeItems(int count)
        {
            var list=new List<NewsItem>();string[] categories={"模型","产品","行业","论文","教程","观点"};
            for(int i=0;i<count;i++)list.Add(new NewsItem {
                Id="demo-"+i,Title=(i%3==0?"新一代开源模型：":"开发工具更新：")+"离线布局测试 "+(i+1),
                Summary="这是用于验证卡片绘制、搜索、分类、展开与滚动响应的离线演示内容。真实内容只会在用户点击手动刷新后从网页接口读取。",
                Source="离线测试",Category=categories[i%categories.Length],OriginalUrl="",Stamp="10:"+(i%60).ToString("00")
            });
            return list;
        }

        private static void SaveShot(Form form,string name)
        {
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(folder);
            using(var image=new Bitmap(form.Width,form.Height))
            {
                form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                NewsWindow news=form as NewsWindow;if(news!=null)news.Canvas.CompositeDiagnosticNativeInput(image,form);
                image.Save(Path.Combine(folder,name));
            }
        }

        internal sealed class NewsWindow : DockedGlassWindow
        {
            private readonly Func<CancellationToken,Task<List<NewsItem>>> fetch;
            internal readonly NewsCanvas Canvas;
            private CancellationTokenSource operation;
            private bool busy,disposed;
            private int requestGeneration;
            private readonly NewsSession retained;
            internal int RefreshCount;
            internal bool LastRefreshSucceeded;
            internal int ItemCount {get{return Canvas.ItemCount;}}
            internal string StatusText {get{return Canvas.StatusText;}}

            internal NewsWindow(Func<CancellationToken,Task<List<NewsItem>>> source) : this(source,null) { }
            internal NewsWindow(Func<CancellationToken,Task<List<NewsItem>>> source,NewsSession state)
            {
                retained=state;
                Text="AI 日报";Size=new Size(720,860);MinimumSize=new Size(320,520);
                fetch=source??NewsFeed.FetchAsync;
                Canvas=new NewsCanvas(this) {Dock=DockStyle.Fill};Controls.Add(Canvas);
                if(retained!=null)Shown+=delegate{Canvas.RestoreSession(retained);};
                VisibleChanged+=delegate{if(!Visible && busy)CancelRefresh("刷新已取消；再次打开后可手动重试。");};
            }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                if(busy)CancelRefresh("刷新已取消；现有内容保持不变。");
                if(retained!=null)Canvas.CaptureSession(retained);
                base.OnFormClosing(e);
            }

            internal async Task RefreshNow()
            {
                if(busy){CancelRefresh("已取消本次刷新；现有内容保持不变。");return;}
                busy=true;LastRefreshSucceeded=false;RefreshCount++;
                int generation=++requestGeneration;var request=new CancellationTokenSource();operation=request;CancellationToken token=request.Token;
                Canvas.SetBusy(true,"正在连接 · 必要时尝试备用网络 · 最长约95秒，可取消");
                try
                {
                    Task<List<NewsItem>> pending=fetch(token);
                    if(await Task.WhenAny(pending,Task.Delay(100000,token))!=pending)
                    {
                        ObserveRequest(pending);token.ThrowIfCancellationRequested();request.Cancel();throw new TimeoutException("日报连接超时");
                    }
                    List<NewsItem> received=await pending;
                    if(token.IsCancellationRequested || disposed || generation!=requestGeneration)return;
                    Canvas.SetItems(received??new List<NewsItem>());
                    LastRefreshSucceeded=true;
                    Canvas.SetStatus(received==null || received.Count==0?"网页暂时没有可显示的日报内容。":"更新完成 · "+received.Count+" 篇精选 · 仅手动刷新");
                }
                catch(OperationCanceledException){if(!disposed && generation==requestGeneration)Canvas.SetStatus("刷新已取消；现有内容保持不变。");}
                catch(Exception ex){if(!disposed && generation==requestGeneration)Canvas.SetStatus("刷新失败："+Friendly(ex)+" · 已保留当前内容");}
                finally
                {
                    if(object.ReferenceEquals(operation,request))operation=null;request.Dispose();
                    if(!disposed && generation==requestGeneration){busy=false;Canvas.SetBusy(false,null);}
                }
            }
            private static void ObserveRequest(Task task){task.ContinueWith(t=>{var observed=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}

            private static string Friendly(Exception ex)
            {
                if(ex is TimeoutException || ex is TaskCanceledException)return "请求超时，请稍后手动重试";
                string text=(ex.Message??"网页接口暂时不可用").Replace('\r',' ').Replace('\n',' ').Trim();
                return text.Length>54?text.Substring(0,54)+"…":text;
            }

            private void CancelRefresh(string message)
            {
                requestGeneration++;busy=false;
                if(operation!=null && !operation.IsCancellationRequested)operation.Cancel();
                if(!disposed)Canvas.SetBusy(false,message);
            }

            internal override void RefreshGlass(){base.RefreshGlass();Canvas.ApplyTheme();}
            protected override void Dispose(bool disposing)
            {
                if(disposing && !disposed){disposed=true;CancelRefresh("已关闭");if(operation!=null)operation.Dispose();}
                base.Dispose(disposing);
            }
        }

        internal sealed class NewsCanvas : Control
        {
            private float ContentScale{get{return Math.Max(1f,owner.ClientSize.Height/860f);}}
            private int LayoutWidth{get{return Math.Max(1,(int)(base.Width/ContentScale));}}
            private int LayoutHeight{get{return Math.Max(1,(int)(base.Height/ContentScale));}}
            private Rectangle LayoutRectangle{get{return new Rectangle(0,0,LayoutWidth,LayoutHeight);}}
            private MouseEventArgs LogicalMouse(MouseEventArgs e){return new MouseEventArgs(e.Button,e.Clicks,(int)(e.X/ContentScale),(int)(e.Y/ContentScale),e.Delta);}
            private sealed class Hit {internal string Kind,Id,Value;internal Rectangle Box;}

            private readonly NewsWindow owner;
            private readonly PairedTextBox search;
            private readonly PairedCue searchCue;
            private readonly List<NewsItem> items=new List<NewsItem>();
            private readonly List<Hit> hits=new List<Hit>();
            private string category="全部",expandedId,statusText="尚未加载 · 点击手动刷新读取网页 AI 日报";
            private string observedQuery="";
            private bool busy;
            private int scrollY,totalContentHeight;
            private int restoredScroll=-1;
            private Rectangle viewport;
            internal int ItemCount {get{return items.Count;}}
            internal string StatusText {get{return statusText;}}
            internal void CaptureSession(NewsSession state)
            {
                state.Items=items.Take(18).ToList();
                state.Category=category;state.Query=search.Text;state.Expanded=expandedId;
                state.Scroll=scrollY;state.Status=statusText;
            }
            internal void RestoreSession(NewsSession state)
            {
                items.Clear();items.AddRange(state.Items.Take(18));
                category=state.Category??"全部";search.Text=state.Query??"";expandedId=state.Expanded;
                restoredScroll=Math.Max(0,state.Scroll);
                if(state.Status!=null)statusText=state.Status;
                UpdateCue();Invalidate();
            }

            internal NewsCanvas(NewsWindow value)
            {
                owner=value;DoubleBuffered=true;TabStop=true;
                SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
                search=new PairedTextBox(18,false) {ForeColor=Color.White,ShortcutsEnabled=true};
                searchCue=new PairedCue {Text="搜索 AI 资讯、关键词或话题…",ForeColor=Color.FromArgb(230,245,250)};
                search.TextChanged+=delegate{string query=search.Text;if(query!=observedQuery){observedQuery=query;scrollY=0;}UpdateCue();Invalidate();};search.GotFocus+=delegate{UpdateCue();};search.LostFocus+=delegate{UpdateCue();};searchCue.Click+=delegate{search.Focus();};
                Controls.Add(search);Controls.Add(searchCue);ApplyTheme();
            }

            private static Color SearchFill(){return ThemeVisual.Blend(ThemeVisual.Current.PanelTop,Color.White,.48f);}
            internal void ApplyTheme(){search.BackColor=SearchFill();searchCue.BackColor=search.BackColor;search.ForeColor=ThemeVisual.Current.PanelBottom;searchCue.ForeColor=ThemeVisual.Current.PanelBottom;Invalidate();}
            private void UpdateCue(){searchCue.Visible=search.TextLength==0 && !search.Focused;if(searchCue.Visible)searchCue.BringToFront();}
            internal void SetItems(IEnumerable<NewsItem> source){items.Clear();if(source!=null)items.AddRange(source.Take(150));scrollY=0;expandedId=items.Count>0?items[0].Id:null;Invalidate();}
            internal void SetBusy(bool value,string message){busy=value;if(!string.IsNullOrEmpty(message))statusText=message;Invalidate();}
            internal void SetStatus(string value){statusText=value??string.Empty;Invalidate();}

            internal void SetDiagnosticState(string tab,string query,bool expand)
            {
                category=tab;search.Text=query;scrollY=0;List<NewsItem> view=Filtered();
                expandedId=expand && view.Count>0?view[0].Id:null;Invalidate();
            }
            internal void DiagnosticScroll(int delta){SetScroll(scrollY+delta);}
            internal bool AllHitsInside(){return hits.All(delegate(Hit hit){return LayoutRectangle.Contains(hit.Box);});}
            internal string RunSearchEditingDiagnostic(){return search.RunEditingDiagnostic();}
            internal bool IsSearchCentered(out string metric)
            {
                int top=LayoutWidth<480?91:106;
                int controlOffset=Math.Abs((search.Top+search.Height/2)-(top+23));
                search.FlushFormattingForDiagnostic();search.Select(search.TextLength,0);
                int pixels;Rectangle ink=search.NativeInkBounds(out pixels);
                float inkCenter=(ink.Top+ink.Bottom)/2f,controlCenter=search.Height/2f;
                float inkOffset=Math.Abs(inkCenter-controlCenter);bool fonts=search.CheckFontPair();
                metric="control_offset="+controlOffset+" ink="+ink+" pixels="+pixels+" ink_offset="+inkOffset.ToString("0.0")+" fonts="+fonts;
                return controlOffset<=1 && pixels>=20 && !ink.IsEmpty && inkOffset<=4f && fonts;
            }
            internal void CompositeDiagnosticNativeInput(Bitmap destination,Form host)
            {
                if(destination==null || host==null || search.Width<=0 || search.Height<=0)return;
                using(Bitmap native=search.CaptureNativeBitmap())
                using(Graphics graphics=Graphics.FromImage(destination))
                {
                    Point location=host.PointToClient(search.PointToScreen(Point.Empty));
                    graphics.DrawImageUnscaled(native,location);
                }
            }
            internal int DiagnosticVisibleCount(){return Filtered().Count;}
            internal bool HasAllCategoryTabs()
            {
                Hit[] tabs=hits.Where(hit=>hit.Kind=="tab").ToArray();
                return tabs.Select(hit=>hit.Value).SequenceEqual(new[]{"全部","模型","产品","行业","论文","教程","观点"})
                    && !hits.Any(hit=>hit.Kind=="favorite")
                    && !tabs.Where((tab,i)=>tabs.Skip(i+1).Any(other=>tab.Box.IntersectsWith(other.Box))).Any();
            }

            protected override void OnResize(EventArgs e){base.OnResize(e);LayoutSearch();ClampScroll();Invalidate();}
            private void LayoutSearch(){if(search==null)return;bool narrow=LayoutWidth<480;int y=narrow?91:106;search.SetPixelScale(ContentScale);searchCue.PixelScale=ContentScale;int height=(int)(search.PreferredLineHeight/ContentScale);search.SetBounds((int)(73*ContentScale),(int)((y+(46-height)/2)*ContentScale),(int)(Math.Max(80,LayoutWidth-103)*ContentScale),(int)(height*ContentScale));searchCue.SetBounds((int)(73*ContentScale),(int)((y+3)*ContentScale),search.Width,(int)(44*ContentScale));UpdateCue();}
            protected override void OnPaintBackground(PaintEventArgs e){GlassBackdrop.Draw(e.Graphics,this);}

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g=e.Graphics;g.ScaleTransform(ContentScale,ContentScale);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;hits.Clear();
                ThemeLook theme=ThemeVisual.Current;
                using(var shade=new SolidBrush(Color.FromArgb(ThemeVisual.IsReferenceCustom?0:78,theme.PanelBottom)))g.FillRectangle(shade,LayoutRectangle);
                DrawHeader(g,theme);int tabBottom=DrawTabs(g,theme);
                viewport=new Rectangle(12,tabBottom+10,Math.Max(1,LayoutWidth-24),Math.Max(1,LayoutHeight-tabBottom-62));
                DrawCards(g,theme);DrawStatus(g,theme);
            }

            private void DrawHeader(Graphics g,ThemeLook theme)
            {
                bool narrow=LayoutWidth<480;
                DrawLabel(g,"AI 日报",LayoutWidth<380?24:(narrow?28:38),new Rectangle(18,10,LayoutWidth<380?100:(narrow?112:250),48),true,ThemeVisual.HeaderInk,false);
                DrawLabel(g,"每日精选 · 发现 AI 的新可能",narrow?15:18,new Rectangle(20,narrow?53:57,Math.Max(1,LayoutWidth-40),30),true,ThemeVisual.HeaderInk,false);
                int buttonY=narrow?14:18,closeW=36,minW=36,refreshW=narrow?82:110;
                int x=LayoutWidth-12-closeW;Button(g,new Rectangle(x,buttonY,closeW,36),"×","close",theme);
                x-=minW+7;Button(g,new Rectangle(x,buttonY,minW,36),"−","hide",theme);
                x-=refreshW+7;Button(g,new Rectangle(x,buttonY,refreshW,36),busy?"取消":"手动刷新","refresh",theme);
                Rectangle field=new Rectangle(14,narrow?91:106,Math.Max(1,LayoutWidth-28),46);
                // The native edit and its rounded surround share one opaque
                // colour; there is no darker rectangle behind the text.
                Glass(g,field,19,SearchFill(),theme.Rim);
                using(var pen=new Pen(Color.White,2)){g.DrawEllipse(pen,27,field.Y+14,13,13);g.DrawLine(pen,38,field.Y+25,45,field.Y+32);}
                LayoutSearch();
            }

            private int DrawTabs(Graphics g,ThemeLook theme)
            {
                string[] tabs={"全部","模型","产品","行业","论文","教程","观点"};bool narrow=LayoutWidth<480;int y=narrow?147:166;
                int gap=narrow?4:6,left=14,total=LayoutWidth-28,w=Math.Max(1,(total-gap*(tabs.Length-1))/tabs.Length);
                for(int i=0;i<tabs.Length;i++)
                {
                    Rectangle box=new Rectangle(left+i*(w+gap),y,w,40);bool active=category==tabs[i];
                    Color fill=active?Color.FromArgb(248,theme.PanelBottom):Color.FromArgb(175,theme.ButtonTop);
                    Glass(g,box,Math.Min(18,w/2),fill,theme.Rim);DrawLabel(g,tabs[i],narrow?15:18,box,true,Color.White,true);
                    hits.Add(new Hit {Kind="tab",Value=tabs[i],Box=box});
                }
                return y+40;
            }

            private void DrawCards(Graphics g,ThemeLook theme)
            {
                List<NewsItem> view=Filtered();GraphicsState saved=g.Save();g.SetClip(viewport);
                if(view.Count==0)
                {
                    string main=items.Count==0?"还没有日报内容":"没有符合条件的内容";
                    string note=items.Count==0?"点击“手动刷新”后，才会读取网页 AI 日报。":"请更换关键词或分类。";
                    DrawLabel(g,main,26,new Rectangle(viewport.X+20,viewport.Y+viewport.Height/2-70,viewport.Width-40,45),true,Color.White,true);
                    DrawLabel(g,note,17,new Rectangle(viewport.X+20,viewport.Y+viewport.Height/2-22,viewport.Width-40,65),false,Color.White,true);
                    DrawLabel(g,"不会自动联网 · 不写本地新闻库",14,new Rectangle(viewport.X+20,viewport.Y+viewport.Height/2+42,viewport.Width-40,34),false,ThemeVisual.Blend(theme.Accent,Color.White,.55f),true);
                    totalContentHeight=0;scrollY=0;g.Restore(saved);return;
                }
                if(restoredScroll>=0){totalContentHeight=view.Sum(item=>item.Id==expandedId?330:122)-10;scrollY=restoredScroll;restoredScroll=-1;ClampScroll();}
                int y=viewport.Y-scrollY,cardGap=10;
                foreach(NewsItem item in view)
                {
                    bool expanded=item.Id==expandedId;int height=expanded?320:112;Rectangle card=new Rectangle(viewport.X,y,viewport.Width,height);
                    if(card.Bottom>=viewport.Top && card.Top<=viewport.Bottom)DrawCard(g,card,item,expanded,theme);
                    y+=height+cardGap;
                }
                totalContentHeight=y-(viewport.Y-scrollY)-cardGap;g.Restore(saved);ClampScroll();DrawScrollBar(g,theme);
            }

            private void DrawCard(Graphics g,Rectangle card,NewsItem item,bool expanded,ThemeLook theme)
            {
                using(var path=GraphicsExtensions.CreateRoundedPath(card,22))
                using(var fill=new LinearGradientBrush(card,Color.FromArgb(205,theme.PanelTop),Color.FromArgb(222,theme.PanelBottom),90f))
                using(var rim=new Pen(Color.FromArgb(225,theme.Rim),1.4f)){g.FillPath(fill,path);g.DrawPath(rim,path);}
                using(var dot=new SolidBrush(theme.Accent))g.FillEllipse(dot,card.X+18,card.Y+25,11,11);
                Rectangle toggle=new Rectangle(card.Right-58,card.Y+13,42,38);
                AddCardHit("toggle",item.Id,new Rectangle(card.X+30,card.Y+8,Math.Max(1,card.Width-100),95));
                CardLabel(g,item.Title??"未命名内容",expanded?22:20,new Rectangle(card.X+42,card.Y+12,Math.Max(40,card.Width-110),56),true,Color.White,false,true);
                CardLabel(g,(item.Source??"网页来源")+"  ·  "+(item.Stamp??""),15,new Rectangle(card.X+42,card.Y+72,Math.Max(40,card.Width-170),26),false,Color.White,false,false);
                CardLabel(g,expanded?"⌃":"⌄",27,toggle,true,Color.White,true,false);
                AddCardHit("toggle",item.Id,toggle);
                if(!expanded)
                {
                    string label=string.IsNullOrEmpty(item.Category)?"AI 日报":item.Category;
                    CardLabel(g,label,14,new Rectangle(card.Right-165,card.Y+76,100,25),true,ThemeVisual.Blend(theme.Accent,Color.White,.45f),true,false);return;
                }
                using(var pen=new Pen(Color.FromArgb(125,theme.Rim),1))g.DrawLine(pen,card.X+22,card.Y+108,card.Right-22,card.Y+108);
                CardLabel(g,"内容摘要",18,new Rectangle(card.X+25,card.Y+122,130,30),true,Color.White,false,false);
                string summary=string.IsNullOrWhiteSpace(item.Summary)?"网页没有提供摘要，可点击“阅读原文”查看。":item.Summary.Trim();
                CardLabel(g,summary,18,new Rectangle(card.X+26,card.Y+161,card.Width-52,100),false,Color.White,false,true);
                bool hasOriginal=IsSafeArticleLink(item.OriginalUrl);
                if(hasOriginal)
                {
                    Rectangle original=new Rectangle(card.Right-176,card.Bottom-48,146,36);
                    Button(g,original,"阅读原文 ↗","",theme);AddCardHit("open",item.Id,original);
                }
            }

            private void DrawStatus(Graphics g,ThemeLook theme)
            {
                Rectangle bar=new Rectangle(12,LayoutHeight-48,Math.Max(1,LayoutWidth-24),36);Glass(g,bar,16,Color.FromArgb(190,theme.PanelBottom),theme.Rim);
                DrawLabel(g,statusText,16,new Rectangle(bar.X+13,bar.Y,Math.Max(1,bar.Width-26),bar.Height),true,Color.White,false);
            }

            private void DrawScrollBar(Graphics g,ThemeLook theme)
            {
                int max=Math.Max(0,totalContentHeight-viewport.Height);if(max<=0)return;
                int h=Math.Max(30,viewport.Height*viewport.Height/Math.Max(viewport.Height,totalContentHeight));int y=viewport.Y+(viewport.Height-h)*scrollY/max;
                using(var brush=new SolidBrush(Color.FromArgb(160,theme.Accent)))using(var path=GraphicsExtensions.CreateRoundedPath(new Rectangle(viewport.Right-6,y,4,h),2))g.FillPath(brush,path);
            }

            private List<NewsItem> Filtered()
            {
                string query=(search.Text??string.Empty).Trim();IEnumerable<NewsItem> result=items;
                if(category!="全部")result=result.Where(delegate(NewsItem item){return string.Equals(item.Category,category,StringComparison.Ordinal);});
                if(query.Length>0)result=result.Where(delegate(NewsItem item)
                {
                    string text=(item.Title??"")+"\n"+(item.Summary??"")+"\n"+(item.Source??"");
                    return text.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0;
                });
                return result.ToList();
            }

            private void ClampScroll(){int max=Math.Max(0,totalContentHeight-Math.Max(1,viewport.Height));scrollY=Math.Max(0,Math.Min(scrollY,max));}
            private void SetScroll(int value){scrollY=value;ClampScroll();Invalidate();}
            protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);SetScroll(scrollY-(e.Delta>0?95:-95));}

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);e=LogicalMouse(e);if(e.Button!=MouseButtons.Left)return;Focus();
                for(int i=hits.Count-1;i>=0;i--)if(hits[i].Box.Contains(e.Location))
                {
                    Hit hit=hits[i];
                    if(hit.Kind=="refresh")RefreshClicked();else if(hit.Kind=="hide")owner.Hide();else if(hit.Kind=="close")owner.Close();
                    else if(hit.Kind=="tab"){category=hit.Value;scrollY=0;expandedId=null;Invalidate();}
                    else if(hit.Kind=="toggle"){expandedId=expandedId==hit.Id?null:hit.Id;ClampScroll();Invalidate();}
                    else if(hit.Kind=="open")
                    {
                        NewsItem item=items.FirstOrDefault(delegate(NewsItem n){return n.Id==hit.Id;});if(item!=null)OpenExternal(item.OriginalUrl);
                    }
                    return;
                }
                if(e.Y<82)Native.BeginWindowDrag(owner.Handle);
            }
            private async void RefreshClicked(){await owner.RefreshNow();}
            protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);e=LogicalMouse(e);Cursor=hits.Any(delegate(Hit hit){return hit.Box.Contains(e.Location);})?Cursors.Hand:Cursors.Default;}

            private static void OpenExternal(string value)
            {
                Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri) || !IsSafeArticleLink(value))return;
                try{Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}catch{}
            }

            private static bool IsSafeArticleLink(string value)
            {
                Uri uri;return Uri.TryCreate(value,UriKind.Absolute,out uri) && uri.Scheme=="https" && string.IsNullOrEmpty(uri.UserInfo);
            }

            private void AddCardHit(string kind,string id,Rectangle box)
            {
                Rectangle clipped=Rectangle.Intersect(viewport,box);if(clipped.Width>0 && clipped.Height>0)hits.Add(new Hit {Kind=kind,Id=id,Box=clipped});
            }

            private void CardLabel(Graphics g,string value,int size,Rectangle box,bool bold,Color color,bool center,bool wrap)
            {
                // GDI+ text shares the card viewport clip, including partial scrolls.
                DrawLabel(g,value,size,box,bold,color,center,wrap);
            }

            private void Button(Graphics g,Rectangle box,string label,string key,ThemeLook theme)
            {
                Glass(g,box,17,Color.FromArgb(214,theme.ButtonBottom),theme.Rim);DrawLabel(g,label,label.Length<=2?22:18,box,true,Color.White,true);
                if(!string.IsNullOrEmpty(key))hits.Add(new Hit {Kind=key,Box=box});
            }
            private static void Glass(Graphics g,Rectangle box,int radius,Color fill,Color rim)
            {
                using(var path=GraphicsExtensions.CreateRoundedPath(box,radius))using(var brush=new SolidBrush(fill))using(var pen=new Pen(Color.FromArgb(225,rim),1.25f)){g.FillPath(brush,path);g.DrawPath(pen,path);}
            }
            private static void DrawLabel(Graphics g,string value,int size,Rectangle box,bool bold,Color color,bool center){DrawLabel(g,value,size,box,bold,color,center,false);}
            private static void DrawLabel(Graphics g,string value,int size,Rectangle box,bool bold,Color color,bool center,bool wrap)
            {
                if(box.Width<=0 || box.Height<=0)return;
                size=Math.Max(16,size);
                using(var cn=new Font("楷体",size,bold?FontStyle.Bold:FontStyle.Bold,GraphicsUnit.Pixel))
                using(var latin=new Font("Times New Roman",size,bold?FontStyle.Bold:FontStyle.Bold,GraphicsUnit.Pixel))
                using(var ink=new SolidBrush(color))
                using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())
                {
                    format.FormatFlags|=StringFormatFlags.NoWrap|StringFormatFlags.MeasureTrailingSpaces;
                    Func<string,float> width=delegate(string text){return TextRuns(text).Sum(run=>g.MeasureString(run.Value,run.Key?cn:latin,2000,format).Width);};
                    float ascent=Math.Max(cn.Size*cn.FontFamily.GetCellAscent(cn.Style)/cn.FontFamily.GetEmHeight(cn.Style),latin.Size*latin.FontFamily.GetCellAscent(latin.Style)/latin.FontFamily.GetEmHeight(latin.Style));
                    float descent=Math.Max(cn.Size*cn.FontFamily.GetCellDescent(cn.Style)/cn.FontFamily.GetEmHeight(cn.Style),latin.Size*latin.FontFamily.GetCellDescent(latin.Style)/latin.FontFamily.GetEmHeight(latin.Style));
                    float lineHeight=Math.Max(cn.GetHeight(g),latin.GetHeight(g));int maximum=wrap?Math.Max(1,(int)(box.Height/lineHeight)):1;
                    var lines=new List<string>();string line="";bool overflow=false;
                    foreach(char c in value??"")
                    {
                        if(c=='\r')continue;
                        if(c=='\n' && !wrap){line+=" ";continue;}
                        if(wrap && (c=='\n' || (line.Length>0 && width(line+c)>box.Width)))
                        {
                            lines.Add(line);line="";
                            if(lines.Count>=maximum){overflow=true;break;}
                            if(c=='\n')continue;
                        }
                        line+=c;
                    }
                    if(lines.Count<maximum)lines.Add(line);
                    if(lines.Count==0)lines.Add("");
                    for(int index=0;index<lines.Count;index++)
                    {
                        string text=lines[index];
                        if(width(text)>box.Width || (overflow && index==lines.Count-1))
                        {
                            while(text.Length>0 && width(text+"…")>box.Width)text=text.Substring(0,text.Length-1);
                            text+="…";
                        }
                        float x=center?box.X+(box.Width-width(text))/2:box.X;
                        float baseline=(wrap?box.Y+index*lineHeight:box.Y+(box.Height-ascent-descent)/2)+ascent;
                        GraphicsState saved=g.Save();g.SetClip(box,CombineMode.Intersect);
                        foreach(var run in TextRuns(text))
                        {
                            Font font=run.Key?cn:latin;
                            float cellAscent=font.Size*font.FontFamily.GetCellAscent(font.Style)/font.FontFamily.GetEmHeight(font.Style);
                            g.DrawString(run.Value,font,ink,x,baseline-cellAscent,format);
                            x+=g.MeasureString(run.Value,font,2000,format).Width;
                        }
                        g.Restore(saved);
                    }
                }
            }
            private static List<KeyValuePair<bool,string>> TextRuns(string text)
            {
                var result=new List<KeyValuePair<bool,string>>();
                foreach(char c in text??"")
                {
                    bool chinese=c>=0x2e80;
                    if(result.Count==0 || result[result.Count-1].Key!=chinese)result.Add(new KeyValuePair<bool,string>(chinese,c.ToString()));
                    else{var previous=result[result.Count-1];result[result.Count-1]=new KeyValuePair<bool,string>(chinese,previous.Value+c);}
                }
                return result;
            }
        }
    }
}
