using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAccountDock
{
    internal static class ResetPanel
    {
        private static ResetWindow window;
        internal static void Open(Form owner,AccountStore store)
        {
            if(owner==null || store==null)return;
            if(window==null || window.IsDisposed)window=new ResetWindow(store,null);
            else window.SetStore(store);
            SidePanels.Activate(owner,window);
        }

        private sealed class ResetWindow : DockedGlassWindow
        {
            internal readonly ResetCanvas Canvas;
            private readonly Func<CancellationToken,Task<ResetForecast>> forecastFetch;
            private readonly Func<CancellationToken,Task<IpQueryResult>> ipFetch;
            private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=60000};
            private CancellationTokenSource forecastOperation,ipOperation;
            private bool disposed,forecastBusy,ipBusy;
            private int forecastGeneration,ipGeneration;
            internal int ForecastRequests,IpRequests;
            internal bool TimerRunning {get{return timer.Enabled;}}
            internal ResetWindow(AccountStore store,Func<CancellationToken,Task<ResetForecast>> source,Func<CancellationToken,Task<IpQueryResult>> ipSource=null)
            {
                Text="重置 / IP 查询";Size=new Size(720,860);MinimumSize=new Size(320,300);
                forecastFetch=source??ResetFeed.FetchAsync;ipFetch=ipSource??IpQueryClient.FetchAsync;
                Canvas=new ResetCanvas(this,store) {Dock=DockStyle.Fill};Controls.Add(Canvas);
                timer.Tick+=delegate{Canvas.Invalidate();};
                VisibleChanged+=delegate
                {
                    timer.Enabled=Visible;
                    if(!Visible)CancelAll();
                    if(Visible)Canvas.Invalidate();
                };
            }
            internal void SetStore(AccountStore store){Canvas.Store=store;Canvas.Invalidate();}
            internal async Task RefreshNow()
            {
                if(Canvas.ForecastTab)await RefreshForecast();else await RefreshIp();
            }
            private async Task RefreshIp()
            {
                if(Canvas.IpProvider!=0){await RefreshOtherIp();return;}
                if(ipBusy){CancelIp("查询已取消 · 已保留上次成功结果");return;}
                ipBusy=true;IpRequests++;int current=++ipGeneration;
                var request=new CancellationTokenSource();ipOperation=request;CancellationToken token=request.Token;
                Canvas.IpBusy=true;Canvas.IpStatus="正在查询当前软件出口 IP · 可取消";Canvas.Invalidate();
                try
                {
                    Task<IpQueryResult> pending=ipFetch(token);
                    if(await Task.WhenAny(pending,Task.Delay(35000,token))!=pending)
                    {
                        Observe(pending);token.ThrowIfCancellationRequested();request.Cancel();throw new TimeoutException();
                    }
                    IpQueryResult value=await pending;
                    if(disposed || token.IsCancellationRequested || current!=ipGeneration)return;
                    if(value==null || string.IsNullOrWhiteSpace(value.Ip))throw new FormatException("IP 查询结果无效");
                    Canvas.IpResult=value;Canvas.IpStatus=value.Warning.Length>0?"查询完成 · 部分信息待核实":"查询完成 · IP 属地和评分为第三方参考";
                }
                catch(OperationCanceledException){if(!disposed && current==ipGeneration)Canvas.IpStatus="查询已取消 · 已保留上次成功结果";}
                catch(Exception ex){if(!disposed && current==ipGeneration)Canvas.IpStatus=(ex is TimeoutException?"IP 查询超时":ex is System.Net.Http.HttpRequestException?"IP 查询网络连接失败":"IP 查询读取失败")+" · 保留上次成功结果，可重试";}
                finally
                {
                    if(object.ReferenceEquals(ipOperation,request))ipOperation=null;
                    request.Dispose();
                    if(!disposed && current==ipGeneration){ipBusy=false;Canvas.IpBusy=false;Canvas.Invalidate();}
                }
            }
            private async Task RefreshOtherIp()
            {
                if(ipBusy){CancelIp("查询已取消");return;}
                ipBusy=true;Canvas.IpBusy=true;int provider=Canvas.IpProvider;
                int current=++ipGeneration;var request=new CancellationTokenSource();ipOperation=request;
                Canvas.IpStatus="正在查询所选来源…";Canvas.Invalidate();
                try{
                    using(var client=new System.Net.Http.HttpClient{Timeout=TimeSpan.FromSeconds(25)}){
                        client.MaxResponseContentBufferSize=1024*1024;
                        var response=await client.GetAsync(provider==1?"https://my.ippure.com/v1/info":"https://cleanip.io/cli",request.Token);
                        using(response){response.EnsureSuccessStatusCode();string raw=await response.Content.ReadAsStringAsync();string report;
                        if(provider==1){var r=JsonUtil.Dict(JsonUtil.Serializer.DeserializeObject(raw));if(string.IsNullOrEmpty(JsonUtil.Text(r,"ip")))throw new FormatException();
                            report="出口 IP："+JsonUtil.Text(r,"ip")+"\n\n地区："+JsonUtil.Text(r,"country")+" / "+JsonUtil.Text(r,"city")+"\n\n运营商："+JsonUtil.Text(r,"asOrganization")+"\n\nASN："+Convert.ToString(JsonUtil.Value(r,"asn"))+"\n\n风险分："+Convert.ToString(JsonUtil.Value(r,"fraudScore"))+"（不是原来源的信任分）\n\n住宅 IP："+(JsonUtil.Value(r,"isResidential") is bool?((bool)JsonUtil.Value(r,"isResidential")?"是":"否"):"未提供");
                        }else{if(raw.IndexOf("<html",StringComparison.OrdinalIgnoreCase)>=0||raw.IndexOf("<!doctype",StringComparison.OrdinalIgnoreCase)>=0)throw new FormatException();report=System.Text.RegularExpressions.Regex.Replace(raw,@"\x1B\[[0-?]*[ -/]*[@-~]","");}
                        if(disposed||request.IsCancellationRequested||current!=ipGeneration)return;
                        Canvas.ProviderReports[provider]=report;Canvas.ProviderTimes[provider]=DateTime.Now.ToString("MM-dd HH:mm:ss");Canvas.IpStatus="查询完成 · 所选网站的出口可能与 ChatGPT 出口不同";
                        }
                    }
                }catch(Exception){if(!disposed&&current==ipGeneration)Canvas.IpStatus=request.IsCancellationRequested?"查询已取消":"查询失败 · 保留上次结果，可重试";}
                finally{if(object.ReferenceEquals(ipOperation,request))ipOperation=null;request.Dispose();if(!disposed&&current==ipGeneration){ipBusy=false;Canvas.IpBusy=false;Canvas.Invalidate();}}
            }
            private async Task RefreshForecast()
            {
                if(forecastBusy){CancelForecast("刷新已取消 · 已保留上次预告");return;}
                forecastBusy=true;ForecastRequests++;int current=++forecastGeneration;
                var request=new CancellationTokenSource();forecastOperation=request;CancellationToken token=request.Token;
                Canvas.ForecastBusy=true;Canvas.ForecastStatus="正在查询预告，可取消";Canvas.Invalidate();
                try
                {
                    Task<ResetForecast> pending=forecastFetch(token);
                    if(await Task.WhenAny(pending,Task.Delay(100000,token))!=pending)
                    {
                        Observe(pending);token.ThrowIfCancellationRequested();request.Cancel();throw new TimeoutException();
                    }
                    ResetForecast value=await pending;
                    if(disposed || token.IsCancellationRequested || current!=forecastGeneration)return;
                    if(value==null)throw new FormatException("预告内容无效");
                    Canvas.Forecast=value;Canvas.ForecastStatus=value.Stale?"查询完成 · 当前为旧数据，建议稍后重试":"预告查询完成";
                }
                catch(OperationCanceledException){if(!disposed && current==forecastGeneration)Canvas.ForecastStatus=Canvas.Forecast.Ready?"刷新已取消 · 已保留上次预告":"刷新已取消 · 尚无查询结果，可重试";}
                catch(Exception ex)
                {
                    if(!disposed && current==forecastGeneration)
                    {
                        string reason=ex is TimeoutException?"网络读取超时":ex is System.Net.Http.HttpRequestException?"网络连接失败":"预告格式或读取失败";
                        Canvas.ForecastStatus=reason+(Canvas.Forecast.Ready?" · 保留上次预告，可重试":" · 尚无查询结果，可重试");
                    }
                }
                finally
                {
                    if(object.ReferenceEquals(forecastOperation,request))forecastOperation=null;
                    request.Dispose();
                    if(!disposed && current==forecastGeneration){forecastBusy=false;Canvas.ForecastBusy=false;Canvas.Invalidate();}
                }
            }
            private static void Observe(Task task){task.ContinueWith(value=>{var error=value.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
            internal void SwitchTab(bool forecast)
            {
                if(forecast==Canvas.ForecastTab)return;
                if(forecast && ipBusy)CancelIp("查询已取消 · 已保留上次成功结果");
                else if(!forecast && forecastBusy)CancelForecast("刷新已取消 · 已保留上次预告");
                Canvas.ApplyTab(forecast);
            }
            private void CancelIp(string status)
            {
                ipGeneration++;ipBusy=false;
                if(ipOperation!=null && !ipOperation.IsCancellationRequested)ipOperation.Cancel();
                if(!disposed){Canvas.IpBusy=false;Canvas.IpStatus=status;Canvas.Invalidate();}
            }
            private void CancelForecast(string status)
            {
                forecastGeneration++;forecastBusy=false;
                if(forecastOperation!=null && !forecastOperation.IsCancellationRequested)forecastOperation.Cancel();
                if(!disposed){Canvas.ForecastBusy=false;Canvas.ForecastStatus=Canvas.Forecast.Ready?status:"刷新已取消 · 尚无查询结果，可重试";Canvas.Invalidate();}
            }
            private void CancelAll()
            {
                if(ipBusy)CancelIp("查询已取消 · 已保留上次成功结果");
                if(forecastBusy)CancelForecast("刷新已取消 · 已保留上次预告");
            }
            internal override void RefreshGlass(){base.RefreshGlass();Canvas.Invalidate();}
            protected override void Dispose(bool disposing)
            {
                if(disposing && !disposed)
                {
                    disposed=true;ipGeneration++;forecastGeneration++;
                    if(ipOperation!=null && !ipOperation.IsCancellationRequested)ipOperation.Cancel();
                    if(forecastOperation!=null && !forecastOperation.IsCancellationRequested)forecastOperation.Cancel();
                    timer.Stop();timer.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        private sealed class ResetCanvas : Control
        {
            private float ContentScale{get{return Math.Max(1f,owner.ClientSize.Height/860f);}}
            private int LayoutWidth{get{return Math.Max(1,(int)(base.Width/ContentScale));}}
            private int LayoutHeight{get{return Math.Max(1,(int)(base.Height/ContentScale));}}
            private Rectangle LayoutRectangle{get{return new Rectangle(0,0,LayoutWidth,LayoutHeight);}}
            private MouseEventArgs LogicalMouse(MouseEventArgs e){return new MouseEventArgs(e.Button,e.Clicks,(int)(e.X/ContentScale),(int)(e.Y/ContentScale),e.Delta);}
            private sealed class Hit {internal string Kind,Tip;internal Rectangle Box;}
            private readonly ResetWindow owner;
            private readonly List<Hit> hits=new List<Hit>();
            private readonly ToolTip tooltip=new ToolTip {AutoPopDelay=15000,InitialDelay=350};
            internal AccountStore Store;
            internal ResetForecast Forecast=new ResetForecast();
            internal IpQueryResult IpResult;
            internal int IpProvider;
            internal readonly string[] ProviderReports=new string[3],ProviderTimes=new string[3];
            internal string IpStatus="尚未查询 · 点击“手动刷新”读取当前软件出口 IP";
            internal string ForecastStatus="尚未查询 · 点击“刷新预告”读取公开消息";
            internal bool IpBusy,ForecastBusy;
            private bool forecastTab=true;
            internal bool ForecastTab {get{return forecastTab;}}
            private int ipScroll,ipScrollMax;
            private Rectangle ipViewport;
            private int forecastScroll,forecastScrollMax;
            private Rectangle forecastViewport;
            private Rectangle? hitClip;
            private string lastTip="";
            internal ResetCanvas(ResetWindow value,AccountStore store)
            {
                owner=value;Store=store;DoubleBuffered=true;TabStop=true;
                SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            }
            protected override void OnPaintBackground(PaintEventArgs e){GlassBackdrop.Draw(e.Graphics,this);}
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g=e.Graphics;g.ScaleTransform(ContentScale,ContentScale);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;hits.Clear();
                ThemeLook theme=ThemeVisual.Current;bool narrow=LayoutWidth<480,shortView=LayoutHeight<650;
                using(var veil=new SolidBrush(Color.FromArgb(ThemeVisual.IsReferenceCustom?0:60,theme.PanelBottom)))g.FillRectangle(veil,LayoutRectangle);
                int top=narrow?97:101;
                Label(g,"重置 / IP 查询",narrow?29:38,new Rectangle(18,12,narrow?LayoutWidth-36:280,48),true,ThemeVisual.HeaderInk,false);
                int buttonY=narrow?60:22;
                Button(g,new Rectangle(LayoutWidth-60,buttonY,40,36),"×","close",theme);
                bool activeBusy=forecastTab?ForecastBusy:IpBusy;
                string refreshText=activeBusy?"取消":forecastTab?"刷新预告":"查询 IP";
                Button(g,new Rectangle(LayoutWidth-178,buttonY,108,36),refreshText,"refresh",theme);
                string subtitle=forecastTab?"第三方额度重置消息 · 手动查询":"当前出口 · IP 纯净度";
                Label(g,subtitle,narrow?16:18,new Rectangle(20,narrow?48:60,narrow?LayoutWidth-210:LayoutWidth-40,narrow?0:28),true,ThemeVisual.HeaderInk,false);
                Rectangle tabs=new Rectangle(14,top,LayoutWidth-28,48);Glass(g,tabs,20,Color.FromArgb(150,theme.PanelBottom),theme.Rim);
                int half=(tabs.Width-10)/2;
                Tab(g,new Rectangle(tabs.X+5,tabs.Y+5,half,38),"第三方预告",forecastTab,"forecast",theme);
                Tab(g,new Rectangle(tabs.X+5+half,tabs.Y+5,half,38),"IP 查询",!forecastTab,"ip",theme);
                int footerY=LayoutHeight-48,contentY=tabs.Bottom+14;
                if(forecastTab)
                {
                    forecastViewport=new Rectangle(14,contentY,LayoutWidth-28,Math.Max(36,footerY-contentY-8));
                    DrawForecastPanel(g,forecastViewport,theme,narrow);
                }
                else
                {
                    ipViewport=new Rectangle(14,contentY,LayoutWidth-28,Math.Max(36,footerY-contentY-52));
                    if(IpProvider==0)DrawIpPanel(g,ipViewport,theme,narrow,shortView);else DrawProvider(g,ipViewport,theme);
                    SourceButtons(g,24,footerY-42,LayoutWidth-48,theme);
                }
                Glass(g,new Rectangle(14,footerY,LayoutWidth-28,38),14,Color.FromArgb(235,theme.PanelBottom),theme.Rim);
                Label(g,forecastTab?ForecastStatus:IpStatus,LayoutWidth<480?14:16,new Rectangle(24,footerY,LayoutWidth-48,38),true,Color.White,false);
            }
            private void DrawIpPanel(Graphics g,Rectangle view,ThemeLook theme,bool narrow,bool compact)
            {
                Glass(g,view,22,Color.FromArgb(150,theme.PanelBottom),theme.Rim);
                int innerX=view.X+10,innerW=view.Width-20;
                bool twoColumns=innerW>=590;
                int scoreHeight=narrow?206:190,ipHeight=narrow?144:132;
                int propertiesHeight=narrow?282:252,securityHeight=narrow?274:252;
                int detailHeight=twoColumns?Math.Max(propertiesHeight,securityHeight):propertiesHeight+12+securityHeight;
                int contentHeight=IpResult==null?398:scoreHeight+12+ipHeight+12+detailHeight+12+182+10;
                ipScrollMax=Math.Max(0,contentHeight-view.Height+12);ipScroll=Math.Max(0,Math.Min(ipScrollMax,ipScroll));
                int y=view.Y+10-ipScroll;
                GraphicsState saved=g.Save();g.SetClip(Rectangle.Inflate(view,-2,-2),CombineMode.Intersect);hitClip=Rectangle.Inflate(view,-4,-4);
                if(IpResult==null)
                {
                    Rectangle empty=new Rectangle(innerX,y,innerW,238);Glass(g,empty,18,Color.FromArgb(162,theme.PanelTop),theme.Rim);
                    Label(g,"IP 纯净度",narrow?21:24,new Rectangle(empty.X+16,empty.Y+13,empty.Width-32,30),true,Color.White,false);
                    Label(g,"尚未检测",narrow?30:38,new Rectangle(empty.X+16,empty.Y+48,empty.Width-32,48),true,theme.Accent,false);
                    DrawPurityGauge(g,new Rectangle(empty.X+16,empty.Y+105,empty.Width-32,28),null,theme);
                    Field(g,"手动查询后显示第三方信任评分；未知时不会估算。",16,new Rectangle(empty.X+16,empty.Y+142,empty.Width-32,25),false,Color.White);
                    Field(g,"评分不是 OpenAI 官方结论，也不保证账号或服务可用。",16,new Rectangle(empty.X+16,empty.Y+168,empty.Width-32,25),true,Color.FromArgb(252,225,151));
                    Button(g,new Rectangle(empty.X+16,empty.Bottom-39,Math.Min(154,empty.Width-32),32),"立即查询","refresh",theme);
                    Rectangle note=new Rectangle(innerX,empty.Bottom+12,innerW,138);Glass(g,note,18,Color.FromArgb(112,theme.PanelTop),theme.Rim);
                    Label(g,"查询范围",19,new Rectangle(note.X+16,note.Y+9,note.Width-32,28),true,Color.White,false);
                    Field(g,"只查询当前软件访问 ChatGPT 链路的出口，不读取账号。",16,new Rectangle(note.X+16,note.Y+39,note.Width-32,24),false,Color.White);
                    Field(g,"不查询或保存历史登录 IP；不同分流线路的结果可能不同。",16,new Rectangle(note.X+16,note.Y+65,note.Width-32,24),false,Color.White);
                    Button(g,new Rectangle(note.X+16,note.Bottom-38,Math.Min(190,note.Width-32),30),"打开原站 ↗","providerWeb",theme);
                }
                else
                {
                    IpQueryResult value=IpResult;
                    Rectangle scoreCard=new Rectangle(innerX,y,innerW,scoreHeight);Glass(g,scoreCard,18,Color.FromArgb(170,theme.PanelTop),theme.Rim);
                    Label(g,"IP 纯净度",narrow?21:24,new Rectangle(scoreCard.X+16,scoreCard.Y+10,scoreCard.Width-32,30),true,Color.White,false);
                    string scoreScope="第三方网段评分"+(string.IsNullOrWhiteSpace(value.RiskCidr)?"":" · "+value.RiskCidr)+" · 非 OpenAI 官方";
                    Field(g,scoreScope,16,new Rectangle(scoreCard.X+16,scoreCard.Y+38,scoreCard.Width-32,24),false,Color.FromArgb(232,245,248));
                    int shown=value.TrustScore.HasValue?Math.Max(0,Math.Min(100,value.TrustScore.Value)):0;
                    string score=value.TrustScore.HasValue?shown.ToString(CultureInfo.InvariantCulture):"—";
                    Color scoreInk=value.TrustScore.HasValue?PurityColor(shown,theme):theme.Accent;
                    Label(g,score,narrow?42:50,new Rectangle(scoreCard.X+16,scoreCard.Y+64,narrow?86:104,58),true,scoreInk,true);
                    Label(g,value.TrustScore.HasValue?PurityText(shown):"评分未知",narrow?19:22,new Rectangle(scoreCard.X+(narrow?110:130),scoreCard.Y+75,scoreCard.Width-(narrow?126:146),35),true,scoreInk,false);
                    DrawPurityGauge(g,new Rectangle(scoreCard.X+16,scoreCard.Y+128,scoreCard.Width-32,30),value.TrustScore,theme);
                    DrawGaugeLabels(g,new Rectangle(scoreCard.X+16,scoreCard.Y+159,scoreCard.Width-32,23));

                    Rectangle identity=new Rectangle(innerX,scoreCard.Bottom+12,innerW,ipHeight);Glass(g,identity,18,Color.FromArgb(158,theme.PanelTop),theme.Rim);
                    Label(g,"ChatGPT 出口 IP",20,new Rectangle(identity.X+16,identity.Y+10,identity.Width-32,27),true,Color.White,false);
                    DrawIpAddress(g,value.Ip,new Rectangle(identity.X+16,identity.Y+39,identity.Width-32,narrow?48:42),narrow,theme);
                    string place=Join(" · ",value.CountryCode,value.Country,value.Region,value.City);
                    Field(g,"地区  "+Unknown(place),17,new Rectangle(identity.X+16,identity.Y+(narrow?91:86),identity.Width-32,27),true,Color.White);
                    Field(g,"线路  "+Unknown(value.Route)+"   ·   "+Unknown(value.FetchedAt),16,new Rectangle(identity.X+16,identity.Y+(narrow?117:111),identity.Width-32,24),false,Color.FromArgb(226,243,248));

                    int detailY=identity.Bottom+12;
                    int gap=twoColumns?12:0;
                    int columnW=twoColumns?(innerW-gap)/2:innerW;
                    Rectangle properties=new Rectangle(innerX,detailY,columnW,propertiesHeight);
                    Rectangle security=new Rectangle(twoColumns?properties.Right+gap:innerX,twoColumns?detailY:properties.Bottom+12,columnW,securityHeight);
                    Glass(g,properties,18,Color.FromArgb(145,theme.PanelTop),theme.Rim);
                    Label(g,"IP 属性",20,new Rectangle(properties.X+16,properties.Y+10,properties.Width-32,28),true,Color.White,false);
                    InfoRow(g,properties,"ISP / 运营商",Unknown(value.Isp),0,theme);
                    InfoRow(g,properties,"ASN",Unknown(value.Asn),1,theme);
                    InfoRow(g,properties,"网络类型",Unknown(value.NetworkType),2,theme);
                    FlagRow(g,properties,"住宅网络",value.IsResidential,3,theme,false);
                    FlagRow(g,properties,"数据中心",value.IsDatacenter,4,theme,false);

                    Glass(g,security,18,Color.FromArgb(145,theme.PanelTop),theme.Rim);
                    Label(g,"出口 IP 安全检测",20,new Rectangle(security.X+16,security.Y+10,security.Width-32,28),true,Color.White,false);
                    FlagRow(g,security,"VPN",value.IsVpn,0,theme,true);
                    FlagRow(g,security,"代理（Proxy）",value.IsProxy,1,theme,true);
                    FlagRow(g,security,"Tor",value.IsTor,2,theme,true);
                    FlagRow(g,security,"机器人（Crawler）",value.IsCrawler,3,theme,true);
                    FlagRow(g,security,"滥用记录",value.IsAbuser,4,theme,true);

                    Rectangle explain=new Rectangle(innerX,(twoColumns?properties.Bottom:security.Bottom)+12,innerW,182);Glass(g,explain,18,Color.FromArgb(108,theme.PanelTop),theme.Rim);
                    Label(g,"结果说明",19,new Rectangle(explain.X+16,explain.Y+10,explain.Width-32,27),true,Color.White,false);
                    Field(g,string.IsNullOrWhiteSpace(value.Warning)?"纯净度和属地为第三方参考，不是 OpenAI 官方结论或访问保证。":"提示 · "+value.Warning,16,new Rectangle(explain.X+16,explain.Y+39,explain.Width-32,25),true,Color.FromArgb(252,225,151));
                    Field(g,"这是当前软件的 ChatGPT 出口，不是账号历史 IP；浏览器分流可能不同。",16,new Rectangle(explain.X+16,explain.Y+68,explain.Width-32,24),false,Color.White);
                    Field(g,"属地 "+(value.GeoAvailable?"已读取":"未知")+"   ·   风险信息 "+(value.RiskAvailable?"已读取":"未知"),16,new Rectangle(explain.X+16,explain.Y+93,explain.Width-32,24),false,Color.FromArgb(226,243,248));
                    Button(g,new Rectangle(explain.X+16,explain.Bottom-39,Math.Min(190,explain.Width-32),30),"打开原站 ↗","providerWeb",theme);
                }
                hitClip=null;g.Restore(saved);
                if(ipScrollMax>0)
                {
                    int trackH=Math.Max(20,view.Height-24);Rectangle track=new Rectangle(view.Right-7,view.Y+12,3,trackH);
                    using(var rail=new SolidBrush(Color.FromArgb(80,Color.White)))g.FillRectangle(rail,track);
                    int thumbH=Math.Max(20,(int)Math.Round(trackH*(double)view.Height/contentHeight));
                    int thumbY=track.Y+(int)Math.Round((trackH-thumbH)*(double)ipScroll/ipScrollMax);
                    using(var thumb=new SolidBrush(Color.FromArgb(210,theme.Accent)))g.FillRectangle(thumb,new Rectangle(track.X,thumbY,track.Width,thumbH));
                    string more=ipScroll>=ipScrollMax?"顶部":"更多";
                    Rectangle moreBox=LayoutWidth<480?new Rectangle(20,60,52,27):new Rectangle(LayoutWidth-240,22,52,36);
                    Button(g,moreBox,more,"more",theme);
                }
            }
            private void SourceButtons(Graphics g,int x,int y,int width,ThemeLook theme)
            {
                int w=(width-12)/3;string[] names={"原来源","IPPure","CleanIP"},commands={"ipSource","ipPure","cleanIp"};
                for(int i=0;i<3;i++){
                    var box=new Rectangle(x+i*(w+6),y,w,31);bool selected=IpProvider==i;
                    Glass(g,box,14,Color.FromArgb(selected?248:175,selected?theme.PanelBottom:theme.ButtonTop),selected?Color.White:theme.Rim);
                    Label(g,names[i],LayoutWidth<480?18:21,box,true,Color.White,true);AddHit(new Hit{Kind=commands[i],Box=box});
                }
            }
            private void DrawProvider(Graphics g,Rectangle view,ThemeLook theme){
                Glass(g,view,22,Color.FromArgb(165,theme.PanelBottom),theme.Rim);
                Label(g,IpProvider==1?"IPPure":"CleanIP",26,new Rectangle(view.X+18,view.Y+12,view.Width-36,36),true,Color.White,false);
                Label(g,ProviderTimes[IpProvider]==null?"尚未查询":("上次成功："+ProviderTimes[IpProvider]),15,new Rectangle(view.X+18,view.Y+50,view.Width-36,26),false,Color.White,false);
                string text=ProviderReports[IpProvider]??"";bool ready=text.Length>0;
                string ip=ProviderField(text,IpProvider==1?"出口 IP":"IP"),place=ProviderField(text,IpProvider==1?"地区":"归属地"),isp=ProviderField(text,IpProvider==1?"运营商":"运营商 / ASN");
                string scoreLine=ProviderField(text,IpProvider==1?"风险分":"CleanIP 评分");
                var scoreMatch=System.Text.RegularExpressions.Regex.Match(scoreLine,IpProvider==1?@"^\d{1,3}":@"(\d{1,3})/100");
                string score=scoreMatch.Success?(IpProvider==1?scoreMatch.Value:scoreMatch.Groups[1].Value):"—";
                int width=view.Width-36;int total=570;ipScrollMax=Math.Max(0,total-(view.Height-132));ipScroll=Math.Min(ipScroll,ipScrollMax);
                var state=g.Save();var body=new Rectangle(view.X+12,view.Y+84,view.Width-24,Math.Max(1,view.Height-132));g.SetClip(body,CombineMode.Intersect);hitClip=body;
                int x=view.X+18,y=view.Y+84-ipScroll;
                var identity=new Rectangle(x,y,width,108);Glass(g,identity,18,Color.FromArgb(190,theme.PanelTop),theme.Rim);
                Label(g,"当前来源出口",17,new Rectangle(x+16,y+10,width-32,23),false,Color.White,false);
                DrawIpAddress(g,ready?Unknown(ip):"等待查询",new Rectangle(x+16,y+38,width-32,40),LayoutWidth<480,theme);
                Field(g,ready?Unknown(place):"点击右上角查询 IP",17,new Rectangle(x+16,y+79,width-32,23),false,Color.White);
                y+=120;var rating=new Rectangle(x,y,width,132);Glass(g,rating,18,Color.FromArgb(190,theme.PanelTop),theme.Rim);
                Label(g,IpProvider==1?"风险评分":"CleanIP 纯净度",20,new Rectangle(x+16,y+12,width-32,28),true,Color.White,false);
                Label(g,score,42,new Rectangle(x+16,y+48,100,50),true,theme.Accent,false);
                Label(g,"/ 100",21,new Rectangle(x+116,y+65,80,27),false,Color.White,false);
                Label(g,IpProvider==1?"越低越好 · 原站风险分":"越高越好 · 原站纯净度",17,new Rectangle(x+16,y+105,width-32,23),false,Color.White,false);
                y+=144;var facts=new Rectangle(x,y,width,234);Glass(g,facts,18,Color.FromArgb(190,theme.PanelTop),theme.Rim);
                Label(g,"网络与风险信息",20,new Rectangle(x+16,y+10,width-32,28),true,Color.White,false);
                InfoRow(g,facts,"运营商",Unknown(isp),0,theme);
                InfoRow(g,facts,IpProvider==1?"ASN":"网络类型",Unknown(ProviderField(text,IpProvider==1?"ASN":"网络类型")),1,theme);
                InfoRow(g,facts,IpProvider==1?"住宅 IP":"IP 类型",Unknown(ProviderField(text,IpProvider==1?"住宅 IP":"IP 类型")),2,theme);
                InfoRow(g,facts,IpProvider==1?"风险说明":"风险标记",IpProvider==1?"分数来自 IPPure":Unknown(ProviderField(text,"风险标记")),3,theme);
                y+=246;Field(g,"仅表示访问此来源的出口；不代表 Codex 实际线路。",16,new Rectangle(x+4,y,width-8,42),false,Color.White);
                hitClip=null;g.Restore(state);
                if(ipScrollMax>0){int h=Math.Max(20,body.Height*body.Height/total);int sy=body.Y+(body.Height-h)*ipScroll/ipScrollMax;using(var brush=new SolidBrush(Color.FromArgb(200,theme.Accent)))g.FillRectangle(brush,new Rectangle(view.Right-7,sy,3,h));}
                Button(g,new Rectangle(view.X+18,view.Bottom-40,150,30),"打开原站 ↗","providerWeb",theme);
            }
            internal static string ProviderField(string report,string key){
                foreach(string raw in (report??"").Split('\n')){string line=raw.Trim();if(!line.StartsWith(key,StringComparison.OrdinalIgnoreCase))continue;
                    string tail=line.Substring(key.Length);if(tail.Length==0||!(char.IsWhiteSpace(tail[0])||tail[0]==':'||tail[0]=='：'))continue;
                    string value=tail.TrimStart(' ', '\t', ':','：');
                    if(key=="IP"){System.Net.IPAddress parsed;string candidate=value.Split(' ','\t')[0];if(!System.Net.IPAddress.TryParse(candidate,out parsed))continue;}
                    return value;
                }return "";
            }
            private void Field(Graphics g,string value,int size,Rectangle box,bool bold,Color color)
            {
                Label(g,value,size,box,bold,color,false);AddHit(new Hit {Kind="tip",Box=box,Tip=value});
            }
            private static string Join(string separator,params string[] values)
            {
                return string.Join(separator,(values??new string[0]).Where(value=>!string.IsNullOrWhiteSpace(value)).ToArray());
            }
            private static string Unknown(string value){return string.IsNullOrWhiteSpace(value)?"未知":value.Trim();}
            private static string Flag(bool? value){return !value.HasValue?"未知":value.Value?"是":"未检测到";}
            private static string PurityText(int score){return score>=95?"极度纯净":score>=80?"纯净":score>=50?"良好":score>=25?"中性":"高风险";}
            private static Color PurityColor(int score,ThemeLook theme)
            {
                Color semantic=score>=80?Color.FromArgb(64,224,151):score>=50?Color.FromArgb(255,204,94):score>=25?Color.FromArgb(244,153,79):Color.FromArgb(245,105,99);
                return ThemeVisual.Blend(semantic,theme.Accent,.22f);
            }
            private static void DrawPurityGauge(Graphics g,Rectangle box,int? score,ThemeLook theme)
            {
                int barY=box.Y+8,barH=10;Rectangle bar=new Rectangle(box.X,barY,Math.Max(1,box.Width),barH);
                Color low=ThemeVisual.Blend(Color.FromArgb(232,82,73),theme.Accent,.12f);
                Color high=ThemeVisual.Blend(Color.FromArgb(45,219,142),theme.Accent,.18f);
                using(var path=GraphicsExtensions.CreateRoundedPath(bar,5))using(var gradient=new LinearGradientBrush(bar,low,high,0f))g.FillPath(gradient,path);
                if(!score.HasValue)
                {
                    using(var veil=new SolidBrush(Color.FromArgb(175,theme.PanelBottom)))using(var path=GraphicsExtensions.CreateRoundedPath(bar,5))g.FillPath(veil,path);
                    return;
                }
                int clamped=Math.Max(0,Math.Min(100,score.Value));int x=bar.Left+5+(int)Math.Round(Math.Max(0,bar.Width-11)*clamped/100d);
                using(var halo=new Pen(Color.FromArgb(230,Color.White),4f))
                using(var marker=new Pen(PurityColor(clamped,theme),1.6f))
                {halo.StartCap=halo.EndCap=LineCap.Round;marker.StartCap=marker.EndCap=LineCap.Round;g.DrawLine(halo,x,barY-4,x,barY+barH+4);g.DrawLine(marker,x,barY-3,x,barY+barH+3);}
            }
            private static void DrawGaugeLabels(Graphics g,Rectangle box)
            {
                string[] labels={"0","25","50","75","100"};int labelW=42;
                for(int i=0;i<labels.Length;i++)
                {
                    int center=box.Left+(int)Math.Round((box.Width-1)*i/4d);
                    int x=Math.Max(box.Left,Math.Min(box.Right-labelW,center-labelW/2));
                    Label(g,labels[i],16,new Rectangle(x,box.Y,labelW,box.Height),false,Color.FromArgb(225,240,245),true);
                }
            }
            private void InfoRow(Graphics g,Rectangle card,string name,string value,int index,ThemeLook theme)
            {
                int y=card.Y+43+index*39;DrawSeparator(g,card,y-4);
                int labelW=Math.Min(104,Math.Max(76,card.Width/3));
                Label(g,name,16,new Rectangle(card.X+16,y,Math.Max(1,labelW),31),false,Color.FromArgb(226,240,244),false);
                Field(g,value,16,new Rectangle(card.X+18+labelW,y,Math.Max(1,card.Width-labelW-34),31),true,Color.White);
            }
            private void FlagRow(Graphics g,Rectangle card,string name,bool? value,int index,ThemeLook theme,bool riskFlag)
            {
                int y=card.Y+43+index*39;DrawSeparator(g,card,y-4);
                Label(g,name,16,new Rectangle(card.X+16,y,Math.Max(1,card.Width-132),31),false,Color.FromArgb(231,243,246),false);
                string text=Flag(value);int pillW=text=="未检测到"?88:58;
                Rectangle pill=new Rectangle(card.Right-pillW-14,y+3,pillW,25);
                Color semantic=!value.HasValue?Color.FromArgb(116,133,148):riskFlag?(value.Value?Color.FromArgb(235,151,72):Color.FromArgb(47,190,130)):(value.Value?ThemeVisual.Blend(theme.Accent,Color.FromArgb(45,191,135),.55f):ThemeVisual.Blend(theme.Accent,Color.FromArgb(115,139,151),.48f));
                Color fill=ThemeVisual.Blend(theme.PanelBottom,semantic,.52f);
                Glass(g,pill,13,Color.FromArgb(232,fill),Color.FromArgb(220,ThemeVisual.Blend(theme.Rim,semantic,.35f)));
                Label(g,text,16,pill,true,Color.White,true);AddHit(new Hit {Kind="tip",Box=pill,Tip=name+" · "+text});
            }
            private static void DrawSeparator(Graphics g,Rectangle card,int y)
            {
                using(var line=new Pen(Color.FromArgb(56,Color.White),1f))g.DrawLine(line,card.X+14,y,card.Right-14,y);
            }
            private void DrawIpAddress(Graphics g,string address,Rectangle box,bool narrow,ThemeLook theme)
            {
                string value=Unknown(address);int size=narrow?21:28;AddHit(new Hit {Kind="tip",Box=box,Tip=value});
                using(var probe=new Font("Times New Roman",size,FontStyle.Bold,GraphicsUnit.Pixel))
                {
                    if(g.MeasureString(value,probe).Width<=box.Width){Label(g,value,size,box,true,theme.Accent,false);return;}
                }
                int middle=value.Length/2,split=value.LastIndexOf(':',middle);
                if(split<2)split=value.IndexOf(':',middle);
                if(split<2){Label(g,value,16,box,true,theme.Accent,false);return;}
                Label(g,value.Substring(0,split+1),Math.Max(16,size-2),new Rectangle(box.X,box.Y,box.Width,box.Height/2),true,theme.Accent,false);
                Label(g,value.Substring(split+1),Math.Max(16,size-2),new Rectangle(box.X,box.Y+box.Height/2,box.Width,box.Height-box.Height/2),true,theme.Accent,false);
            }
            private void DrawForecastPanel(Graphics g,Rectangle view,ThemeLook theme,bool narrow)
            {
                Glass(g,view,22,Color.FromArgb(150,theme.PanelBottom),theme.Rim);
                Color muted=ThemeVisual.Blend(Color.White,theme.PanelBottom,.18f);
                int innerX=view.X+10,innerW=view.Width-20;
                bool unread=!Forecast.Ready;
                int currentHeight=unread?(narrow?184:170):(narrow?174:158);
                int timeHeight=unread?0:(narrow?150:138);
                int sourceHeight=unread?0:(narrow?184:164);
                int explainHeight=narrow?112:94;
                int contentHeight=currentHeight+12+timeHeight+(timeHeight>0?12:0)+sourceHeight+(sourceHeight>0?12:0)+explainHeight+10;
                forecastScrollMax=Math.Max(0,contentHeight-view.Height+12);
                forecastScroll=Math.Max(0,Math.Min(forecastScrollMax,forecastScroll));
                int y=view.Y+10-forecastScroll;
                GraphicsState saved=g.Save();g.SetClip(Rectangle.Inflate(view,-2,-2),CombineMode.Intersect);hitClip=Rectangle.Inflate(view,-4,-4);

                Rectangle current=new Rectangle(innerX,y,innerW,currentHeight);
                Glass(g,current,18,Color.FromArgb(172,theme.PanelTop),theme.Rim);
                Label(g,"当前预告",narrow?20:23,new Rectangle(current.X+16,current.Y+11,current.Width-144,30),true,Color.White,false);
                string state=DisplayForecastState();
                Color stateColor=ForecastStateColor(Forecast,theme);
                Rectangle stateBox=new Rectangle(current.Right-(narrow?112:126),current.Y+11,narrow?96:110,28);
                Glass(g,stateBox,14,Color.FromArgb(226,ThemeVisual.Blend(theme.PanelBottom,stateColor,.56f)),Color.FromArgb(210,stateColor));
                string stateLabel=narrow?(state=="有待核实预告"?"待核实":state=="旧数据待更新"?"旧数据":state=="预告已过期"?"已过期":state=="暂无新预告"?"暂无预告":state):state;
                Label(g,stateLabel,16,stateBox,true,Color.White,true);AddHit(new Hit {Kind="tip",Box=stateBox,Tip=state});
                if(unread)
                {
                    string unreadTitle=state=="正在查询"?"正在查询预告":state=="查询失败"?"尚无查询结果":state=="已取消"?"已取消，尚无结果":"尚未查询";
                    Label(g,unreadTitle,narrow?21:25,new Rectangle(current.X+16,current.Y+53,current.Width-32,34),true,theme.Accent,false);
                    WrappedField(g,"查询第三方发布的额度重置消息。",16,new Rectangle(current.X+16,current.Y+89,current.Width-32,40),false,Color.White);
                    string unreadAction=ForecastBusy?"取消查询":state=="尚未查询"?"查询最新预告":"重新查询预告";
                    Button(g,new Rectangle(current.X+16,current.Bottom-43,Math.Min(174,current.Width-32),32),unreadAction,"refresh",theme);
                }
                else
                {
                    string title=Forecast.HasAnnouncement?FirstNonEmpty(Forecast.EventTitle,Forecast.Summary,"第三方额度重置消息"):"当前没有待核实的重置预告";
                    WrappedField(g,title,narrow?17:19,new Rectangle(current.X+16,current.Y+50,current.Width-32,narrow?62:52),true,Color.White);
                    string qualifier=Forecast.WindowExpired?"该时间窗口已经结束。":Forecast.Stale?"这是旧数据，请刷新后再判断。":Forecast.HasAnnouncement?(Forecast.IsInferred?"时间由公开信息推算，仍需核实。":"这是公开消息，仍需核实。"):"查询成功，目前没有新的待核实预告。";
                    WrappedField(g,qualifier,16,new Rectangle(current.X+16,current.Bottom-(narrow?55:48),current.Width-32,narrow?44:36),false,stateColor);
                }
                y=current.Bottom+12;

                if(!unread)
                {
                    Rectangle timing=new Rectangle(innerX,y,innerW,timeHeight);Glass(g,timing,18,Color.FromArgb(150,theme.PanelTop),theme.Rim);
                    Label(g,"预计时间",20,new Rectangle(timing.X+16,timing.Y+10,timing.Width-32,29),true,Color.White,false);
                    string window=Forecast.HasAnnouncement?FirstNonEmpty(Forecast.WindowLabel,Forecast.Summary,"待核实"):"—";
                    WrappedField(g,window,narrow?18:21,new Rectangle(timing.X+16,timing.Y+45,timing.Width-32,narrow?50:40),true,theme.Accent);
                    string windowMeta=Forecast.WindowEndsAt.Length>0?"窗口结束 · "+Forecast.WindowEndsAt:Forecast.HasAnnouncement?(Forecast.IsInferred?"推算时间 · 待核实":"公开时间 · 待核实"):"当前没有可显示的时间窗口";
                    Field(g,windowMeta,16,new Rectangle(timing.X+16,timing.Bottom-39,timing.Width-32,27),false,muted);
                    y=timing.Bottom+12;

                    Rectangle source=new Rectangle(innerX,y,innerW,sourceHeight);Glass(g,source,18,Color.FromArgb(138,theme.PanelTop),theme.Rim);
                    Label(g,"消息来源",20,new Rectangle(source.X+16,source.Y+10,source.Width-32,28),true,Color.White,false);
                    bool safe=ResetFeed.IsSafeSource(Forecast.SourceUrl);
                    string host=safe?SourceHost(Forecast.SourceUrl):"未提供可打开的来源";
                    Field(g,host,17,new Rectangle(source.X+16,source.Y+43,source.Width-(safe?154:32),30),true,Color.White);
                    if(safe)Button(g,new Rectangle(source.Right-138,source.Y+42,122,31),"查看来源 ↗","source",theme);
                    DrawSeparator(g,source,source.Y+83);
                    Field(g,"最近查询 · "+FirstNonEmpty(Forecast.FetchedAt,"未知"),16,new Rectangle(source.X+16,source.Y+91,source.Width-32,28),false,muted);
                    string sourceTime=FirstNonEmpty(Forecast.EventUpdatedAt,Forecast.CheckedAt);
                    Field(g,"源数据时间 · "+FirstNonEmpty(sourceTime,"未提供"),16,new Rectangle(source.X+16,source.Y+122,source.Width-32,28),false,muted);
                    y=source.Bottom+12;
                }

                Rectangle explain=new Rectangle(innerX,y,innerW,explainHeight);Glass(g,explain,18,Color.FromArgb(108,theme.PanelTop),theme.Rim);
                Label(g,"说明",18,new Rectangle(explain.X+16,explain.Y+9,explain.Width-32,27),true,Color.White,false);
                WrappedField(g,"这里显示公开消息，不是你的账号额度。个人额度请在主界面刷新额度。",16,new Rectangle(explain.X+16,explain.Y+38,explain.Width-32,explain.Height-47),false,Color.White);
                hitClip=null;g.Restore(saved);
                if(forecastScrollMax>0)DrawScrollAffordance(g,view,contentHeight,forecastScroll,forecastScrollMax,theme);
            }
            private void DrawScrollAffordance(Graphics g,Rectangle view,int contentHeight,int scroll,int maximum,ThemeLook theme)
            {
                int trackH=Math.Max(20,view.Height-24);Rectangle track=new Rectangle(view.Right-7,view.Y+12,3,trackH);
                using(var rail=new SolidBrush(Color.FromArgb(80,Color.White)))g.FillRectangle(rail,track);
                int thumbH=Math.Max(20,(int)Math.Round(trackH*(double)view.Height/contentHeight));
                int thumbY=track.Y+(int)Math.Round((trackH-thumbH)*(double)scroll/maximum);
                using(var thumb=new SolidBrush(Color.FromArgb(210,theme.Accent)))g.FillRectangle(thumb,new Rectangle(track.X,thumbY,track.Width,thumbH));
                string more=scroll>=maximum?"顶部":"更多";
                Rectangle moreBox=LayoutWidth<480?new Rectangle(20,60,52,27):new Rectangle(LayoutWidth-240,22,52,36);
                Button(g,moreBox,more,"more",theme);
            }
            internal static string ForecastState(ResetForecast value)
            {
                if(value==null || !value.Ready)return "尚未查询";
                if(value.WindowExpired)return "预告已过期";
                if(value.Stale)return "旧数据待更新";
                if(value.HasAnnouncement)return "有待核实预告";
                return "暂无新预告";
            }
            internal string DisplayForecastState()
            {
                if(Forecast!=null && Forecast.Ready)return ForecastState(Forecast);
                if(owner.ForecastRequests<=0)return "尚未查询";
                if(ForecastBusy)return "正在查询";
                if(ForecastStatus.Contains("取消"))return "已取消";
                return "查询失败";
            }
            private static Color ForecastStateColor(ResetForecast value,ThemeLook theme)
            {
                return ThemeVisual.Blend(theme.Accent,Color.White,.12f);
            }
            private static string FirstNonEmpty(params string[] values)
            {
                foreach(string value in values??new string[0])if(!string.IsNullOrWhiteSpace(value))return value.Trim();
                return "";
            }
            private static string SourceHost(string value)
            {
                Uri uri;return Uri.TryCreate(value,UriKind.Absolute,out uri)?uri.Host:value;
            }
            private void WrappedField(Graphics g,string value,int size,Rectangle box,bool bold,Color color)
            {
                if(box.Width<=0 || box.Height<=0)return;
                string text=(value??"").Replace('\r',' ').Replace('\n',' ').Trim();size=Math.Max(16,size);
                int lineHeight=Math.Max(22,size+7),maxLines=Math.Max(1,box.Height/lineHeight),offset=0;
                for(int lineIndex=0;lineIndex<maxLines && offset<text.Length;lineIndex++)
                {
                    int take=0;string line="";
                    while(offset+take<text.Length)
                    {
                        string candidate=text.Substring(offset,take+1);
                        if(MixedWidth(g,candidate,size,bold)>box.Width && take>0)break;
                        take++;line=candidate;
                    }
                    if(take==0){take=1;line=text.Substring(offset,1);}
                    bool last=lineIndex==maxLines-1 && offset+take<text.Length;
                    if(last)
                    {
                        line=line.TrimEnd();
                        while(line.Length>0 && MixedWidth(g,line+"…",size,bold)>box.Width)line=line.Substring(0,line.Length-1);
                        line+="…";
                    }
                    Label(g,line,size,new Rectangle(box.X,box.Y+lineIndex*lineHeight,box.Width,lineHeight),bold,color,false);
                    offset+=take;while(offset<text.Length && text[offset]==' ')offset++;
                }
                AddHit(new Hit {Kind="tip",Box=box,Tip=text});
            }
            private static float MixedWidth(Graphics g,string text,int size,bool bold)
            {
                FontStyle style=bold?FontStyle.Bold:FontStyle.Bold;
                using(var cn=new Font("楷体",size,style,GraphicsUnit.Pixel))
                using(var latin=new Font("Times New Roman",size,style,GraphicsUnit.Pixel))
                using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())
                {
                    format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces;
                    return Runs(text??"").Sum(run=>g.MeasureString(run.Value,run.Key?cn:latin,2000,format).Width);
                }
            }
            private static void Clock(Graphics g,Rectangle box,ThemeLook theme)
            {
                using(var brush=new SolidBrush(Color.FromArgb(145,theme.Accent)))g.FillEllipse(brush,box);
                int cx=box.X+box.Width/2,cy=box.Y+box.Height/2,r=box.Width/4;
                using(var pen=new Pen(Color.White,2.5f)){g.DrawEllipse(pen,cx-r,cy-r,2*r,2*r);g.DrawLine(pen,cx,cy,cx,cy-r+5);g.DrawLine(pen,cx,cy,cx+r-4,cy+5);}
            }
            private void Tab(Graphics g,Rectangle box,string text,bool active,string key,ThemeLook theme)
            {
                Glass(g,box,16,Color.FromArgb(active?248:175,active?theme.PanelBottom:theme.ButtonTop),theme.Rim);
                Label(g,text,LayoutWidth<480?19:22,box,true,Color.White,true);AddHit(new Hit {Kind=key,Box=box});
            }
            private void Button(Graphics g,Rectangle box,string text,string key,ThemeLook theme)
            {
                Glass(g,box,16,Color.FromArgb(210,theme.ButtonBottom),theme.Rim);Label(g,text,text=="×"?28:17,box,true,Color.White,true);AddHit(new Hit {Kind=key,Box=box});
            }
            private void AddHit(Hit hit)
            {
                Rectangle clip=hitClip.HasValue?hitClip.Value:LayoutRectangle;
                Rectangle visible=Rectangle.Intersect(hit.Box,clip);
                if(visible.Width>0 && visible.Height>0){hit.Box=visible;hits.Add(hit);}
            }
            private static void Glass(Graphics g,Rectangle box,int radius,Color color,Color rim)
            {
                if(box.Width<1 || box.Height<1)return;
                ThemeLook look=ThemeVisual.Current;
                if(ThemeVisual.IsReferenceCustom && (color.ToArgb() & 0xffffff)==(look.PanelTop.ToArgb() & 0xffffff)){
                    ReferenceGlass.Draw(g,box,radius,true,false);return;
                }
                using(var path=GraphicsExtensions.CreateRoundedPath(box,Math.Min(radius,box.Height/2)))using(var brush=new SolidBrush(color))using(var pen=new Pen(rim,1.2f)){g.FillPath(brush,path);g.DrawPath(pen,path);}
            }
            private static void Label(Graphics g,string value,int size,Rectangle box,bool bold,Color color,bool center)
            {
                if(box.Width<=0 || box.Height<=0)return;
                size=Math.Max(16,size);
                using(var cn=new Font("楷体",size,bold?FontStyle.Bold:FontStyle.Bold,GraphicsUnit.Pixel))
                using(var latin=new Font("Times New Roman",size,bold?FontStyle.Bold:FontStyle.Bold,GraphicsUnit.Pixel))
                using(var ink=new SolidBrush(color))
                using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())
                {
                    format.FormatFlags|=StringFormatFlags.NoWrap|StringFormatFlags.MeasureTrailingSpaces;
                    Func<string,float> width=fragment=>Runs(fragment).Sum(run=>g.MeasureString(run.Value,run.Key?cn:latin,2000,format).Width);
                    string text=(value??"").Replace('\n',' ');if(width(text)>box.Width){while(text.Length>0 && width(text+"…")>box.Width)text=text.Substring(0,text.Length-1);text+="…";}
                    float ascent=Math.Max(cn.Size*cn.FontFamily.GetCellAscent(cn.Style)/cn.FontFamily.GetEmHeight(cn.Style),latin.Size*latin.FontFamily.GetCellAscent(latin.Style)/latin.FontFamily.GetEmHeight(latin.Style));
                    float descent=Math.Max(cn.Size*cn.FontFamily.GetCellDescent(cn.Style)/cn.FontFamily.GetEmHeight(cn.Style),latin.Size*latin.FontFamily.GetCellDescent(latin.Style)/latin.FontFamily.GetEmHeight(latin.Style));
                    float x=center?box.X+(box.Width-width(text))/2:box.X,baseline=box.Y+(box.Height-ascent-descent)/2+ascent;
                    GraphicsState saved=g.Save();g.SetClip(box,CombineMode.Intersect);
                    foreach(var run in Runs(text)){Font f=run.Key?cn:latin;float a=f.Size*f.FontFamily.GetCellAscent(f.Style)/f.FontFamily.GetEmHeight(f.Style);g.DrawString(run.Value,f,ink,x,baseline-a,format);x+=g.MeasureString(run.Value,f,2000,format).Width;}
                    g.Restore(saved);
                }
            }
            private static List<KeyValuePair<bool,string>> Runs(string text)
            {
                var result=new List<KeyValuePair<bool,string>>();
                foreach(char c in text){bool cn=c>=0x2e80;if(result.Count==0 || result[result.Count-1].Key!=cn)result.Add(new KeyValuePair<bool,string>(cn,c.ToString()));else{var old=result[result.Count-1];result[result.Count-1]=new KeyValuePair<bool,string>(cn,old.Value+c);}}return result;
            }
            internal bool HitBounds(){return hits.All(hit=>LayoutRectangle.Contains(hit.Box));}
            internal bool ActionHitsDoNotOverlap()
            {
                string[] actions={"close","refresh","ip","forecast","more","ipSource","source"};
                List<Hit> active=hits.Where(hit=>actions.Contains(hit.Kind)).ToList();
                for(int i=0;i<active.Count;i++)for(int j=i+1;j<active.Count;j++)if(Rectangle.Intersect(active[i].Box,active[j].Box).Width>0 && Rectangle.Intersect(active[i].Box,active[j].Box).Height>0)return false;
                return true;
            }
            internal void SetTab(bool value){owner.SwitchTab(value);}
            internal void ApplyTab(bool value){forecastTab=value;Invalidate();}
            internal bool HasHit(string kind){return hits.Any(hit=>hit.Kind==kind);}
            internal bool ForecastTabIsFirst()
            {
                Hit forecast=hits.FirstOrDefault(hit=>hit.Kind=="forecast"),ip=hits.FirstOrDefault(hit=>hit.Kind=="ip");
                return forecast!=null && ip!=null && forecast.Box.X<ip.Box.X;
            }
            internal int IpScroll {get{return ipScroll;}}
            internal int IpScrollMaximum {get{return ipScrollMax;}}
            internal int ForecastScroll {get{return forecastScroll;}}
            internal int ForecastScrollMaximum {get{return forecastScrollMax;}}
            internal void ScrollTop(){ipScroll=0;Invalidate();}
            internal void ScrollBottom(){ipScroll=ipScrollMax;Invalidate();}
            internal void DiagnosticKey(Keys key){HandleScrollKey(key);}
            private void ScrollBy(int amount){ipScroll=Math.Max(0,Math.Min(ipScrollMax,ipScroll+amount));Invalidate();}
            private void ForecastScrollBy(int amount){forecastScroll=Math.Max(0,Math.Min(forecastScrollMax,forecastScroll+amount));Invalidate();}
            private void HandleScrollKey(Keys key)
            {
                int page=Math.Max(60,(forecastTab?forecastViewport:ipViewport).Height-24);
                if(key==Keys.Up){if(forecastTab)ForecastScrollBy(-36);else ScrollBy(-36);}
                else if(key==Keys.Down){if(forecastTab)ForecastScrollBy(36);else ScrollBy(36);}
                else if(key==Keys.PageUp){if(forecastTab)ForecastScrollBy(-page);else ScrollBy(-page);}
                else if(key==Keys.PageDown){if(forecastTab)ForecastScrollBy(page);else ScrollBy(page);}
                else if(key==Keys.Home){if(forecastTab)forecastScroll=0;else ipScroll=0;Invalidate();}
                else if(key==Keys.End){if(forecastTab)forecastScroll=forecastScrollMax;else ipScroll=ipScrollMax;Invalidate();}
            }
            protected override bool IsInputKey(Keys keyData)
            {
                Keys key=keyData&Keys.KeyCode;
                if(key==Keys.Up || key==Keys.Down || key==Keys.PageUp || key==Keys.PageDown || key==Keys.Home || key==Keys.End)return true;
                return base.IsInputKey(keyData);
            }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);int before=ipScroll;HandleScrollKey(e.KeyCode);if(ipScroll!=before)e.Handled=true;
            }
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                if(forecastTab)ForecastScrollBy(e.Delta>0?-72:72);else ScrollBy(e.Delta>0?-72:72);
            }
            protected override async void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);e=LogicalMouse(e);if(e.Button!=MouseButtons.Left)return;Focus();
                foreach(Hit hit in hits.AsEnumerable().Reverse())if(hit.Box.Contains(e.Location))
                {
                    if(hit.Kind=="close")owner.Close();
                    else if(hit.Kind=="refresh")await owner.RefreshNow();
                    else if(hit.Kind=="ip" || hit.Kind=="forecast")SetTab(hit.Kind=="forecast");
                    else if(hit.Kind=="more")
                    {
                        if(forecastTab){if(forecastScroll>=forecastScrollMax){forecastScroll=0;Invalidate();}else ForecastScrollBy(Math.Max(60,forecastViewport.Height-24));}
                        else{if(ipScroll>=ipScrollMax)ScrollTop();else ScrollBy(Math.Max(60,ipViewport.Height-24));}
                    }
                    else if(hit.Kind=="ipSource" || hit.Kind=="ipPure" || hit.Kind=="cleanIp"){
                        if(IpBusy)return;IpProvider=hit.Kind=="ipPure"?1:hit.Kind=="cleanIp"?2:0;ipScroll=0;IpStatus="已选择 "+(IpProvider==0?"ip.net.coffee":IpProvider==1?"IPPure":"CleanIP")+" · 点击查询刷新";Invalidate();
                    }
                    else if(hit.Kind=="providerWeb"){try{Process.Start(new ProcessStartInfo(IpProvider==1?"https://ippure.com/":IpProvider==2?"https://cleanip.io/":"https://ip.net.coffee/gpt/"){UseShellExecute=true});}catch{}}
                    else if(hit.Kind=="source" && ResetFeed.IsSafeSource(Forecast.SourceUrl)){try{Process.Start(new ProcessStartInfo(Forecast.SourceUrl) {UseShellExecute=true});}catch{}}
                    else if(!string.IsNullOrEmpty(hit.Tip))tooltip.Show(hit.Tip,this,e.Location,12000);
                    return;
                }
                if(e.Y<95)Native.BeginWindowDrag(owner.Handle);
            }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);e=LogicalMouse(e);Hit hit=hits.LastOrDefault(value=>value.Box.Contains(e.Location));Cursor=hit==null?Cursors.Default:Cursors.Hand;
                string text=hit==null?"":hit.Tip??"";if(text!=lastTip){lastTip=text;tooltip.SetToolTip(this,text);}
            }
            protected override void Dispose(bool disposing){if(disposing)tooltip.Dispose();base.Dispose(disposing);}
        }

        internal static int RunDiagnostic()
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);int code=1,ipCalls=0,forecastCalls=0;
            string originalTheme=ThemeSelection.Current;
            var store=new AccountStore(true);
            var full=new IpQueryResult
            {
                Ip="2001:0db8:85a3:0000:0000:8a2e:0370:7334",CountryCode="TW",Country="中国台湾",
                Region="Taipei City With A Deliberately Long Offline Diagnostic Name",City="Taipei",Isp="Offline Example Carrier With Long Display Name",
                Asn="AS64500",NetworkType="residential",Route="system-proxy",FetchedAt="2026-10-27 09:30:00",
                TrustScore=0,RiskCidr="2001:db8:85a3::/48",IsVpn=null,IsProxy=false,IsTor=false,IsCrawler=true,IsAbuser=null,IsDatacenter=false,IsResidential=true,
                GeoAvailable=true,RiskAvailable=true
            };
            var partial=new IpQueryResult
            {
                Ip="198.51.100.24",CountryCode="JP",Country="日本",Route="direct",FetchedAt="2026-10-27 09:31:00",
                GeoAvailable=true,RiskAvailable=false,Warning="风险服务暂时不可用；已保留可核实的 IP 与属地信息"
            };
            Func<CancellationToken,Task<IpQueryResult>> ipFake=delegate(CancellationToken token)
            {
                ipCalls++;
                if(ipCalls==1)return Task.FromResult(full);
                if(ipCalls==2 || ipCalls==5 || ipCalls==6)return Cancellable<IpQueryResult>(token);
                if(ipCalls==3){var failed=new TaskCompletionSource<IpQueryResult>();failed.SetException(new TimeoutException());return failed.Task;}
                return Task.FromResult(partial);
            };
            Func<CancellationToken,Task<ResetForecast>> forecastFake=delegate(CancellationToken token)
            {
                forecastCalls++;
                if(forecastCalls==2)return Cancellable<ResetForecast>(token);
                if(forecastCalls==3){var failed=new TaskCompletionSource<ResetForecast>();failed.SetException(new TimeoutException());return failed.Task;}
                if(forecastCalls==4)return Task.FromResult(new ResetForecast {Ready=true,Summary="暂无待确认的第三方预告",HasAnnouncement=false,FetchedAt="2026-10-27 10:04 +08:00",CheckedAt="2026-10-27 09:58 +08:00"});
                if(forecastCalls==5)return Task.FromResult(new ResetForecast {Ready=true,Summary="已结束的示例消息",EventTitle="额度重置维护窗口",WindowLabel="今日 08:00—09:00",WindowEndsAt="2026-10-27 09:00 +08:00",HasAnnouncement=true,WindowExpired=true,FetchedAt="2026-10-27 10:05 +08:00",CheckedAt="2026-10-27 08:55 +08:00",SourceUrl="http://status.example.com/not-safe"});
                if(forecastCalls==6)return Task.FromResult(new ResetForecast {Ready=true,Summary="旧的示例消息",EventTitle="历史维护安排",WindowLabel="昨日 20:00—22:00",HasAnnouncement=true,Stale=true,FetchedAt="2026-10-27 10:06 +08:00",CheckedAt="2026-10-26 18:00 +08:00",SourceUrl="https://help.openai.com/"});
                if(forecastCalls==7)return Task.FromResult(new ResetForecast {Ready=true,Summary="明确时间示例",EventTitle="第三方发布的额度重置消息",WindowLabel="今晚 22:00—23:00",WindowEndsAt="2026-10-27 23:00 +08:00",EventUpdatedAt="2026-10-27 09:30 +08:00",HasAnnouncement=true,FetchedAt="2026-10-27 10:07 +08:00",CheckedAt="2026-10-27 09:32 +08:00",SourceUrl="https://help.openai.com/"});
                return Task.FromResult(new ResetForecast {Ready=true,Summary="示例预告 · 时间窗口待核实",EventTitle="第三方发布的额度重置消息（很长的离线诊断标题，用于验证窄窗口换行和提示）",WindowLabel="明日预计窗口 08:00—10:00（原始窗口标签）",WindowEndsAt="2026-10-28 10:00 +08:00",EventUpdatedAt="2026-10-27 09:40 +08:00",IsInferred=true,HasAnnouncement=true,FetchedAt="2026-10-27 10:01 +08:00",CheckedAt="2026-10-27 09:41 +08:00",SourceUrl="https://help.openai.com/"});
            };
            using(var test=new ResetWindow(store,forecastFake,ipFake))
            {
                test.Shown+=async delegate
                {
                    try
                    {
                        if(ipCalls!=0 || forecastCalls!=0 || !test.TimerRunning)throw new Exception("Opening window must not fetch");
                        if((store.Settings.Accounts??new List<AccountMeta>()).Count!=0)throw new Exception("Diagnostic store must stay empty");
                        if(!test.Canvas.ForecastTab || test.Canvas.Forecast.Ready)throw new Exception("Forecast must be default unread tab");
                        test.Size=new Size(320,300);test.Update();Application.DoEvents();
                        if(ipCalls!=0 || forecastCalls!=0 || !test.Canvas.HitBounds() || !test.Canvas.ActionHitsDoNotOverlap() || !test.Canvas.HasHit("more"))throw new Exception("Initial manual-only forecast state");
                        Shot(test,"reset-forecast-unread-320x300-top.png");
                        test.Canvas.DiagnosticKey(Keys.End);test.Update();Application.DoEvents();
                        if(test.Canvas.ForecastScroll!=test.Canvas.ForecastScrollMaximum || !test.Canvas.HasHit("refresh") || !test.Canvas.HitBounds())throw new Exception("Unread forecast CTA scroll");
                        Shot(test,"reset-forecast-unread-320x300-bottom.png");
                        test.Canvas.DiagnosticKey(Keys.Home);test.Size=new Size(720,860);test.Update();Application.DoEvents();
                        if(!test.Canvas.HitBounds() || !test.Canvas.ActionHitsDoNotOverlap() || !test.Canvas.ForecastTabIsFirst())throw new Exception("Unread forecast large bounds / tab order");
                        Shot(test,"reset-forecast-unread-720x860.png");

                        int pendingProbeCalls=0;
                        Func<CancellationToken,Task<ResetForecast>> pendingProbeFetch=delegate(CancellationToken token){pendingProbeCalls++;return Cancellable<ResetForecast>(token);};
                        using(var pendingProbe=new ResetWindow(store,pendingProbeFetch,ipFake))
                        {
                            pendingProbe.Show();Task pendingProbeTask=pendingProbe.RefreshNow();await Task.Delay(30);pendingProbe.Update();Application.DoEvents();
                            if(pendingProbeCalls!=1 || pendingProbe.Canvas.DisplayForecastState()!="正在查询" || !pendingProbe.Canvas.ForecastBusy)throw new Exception("Initial pending forecast state");
                            pendingProbe.Canvas.SetTab(false);
                            if(await Task.WhenAny(pendingProbeTask,Task.Delay(1000))!=pendingProbeTask || pendingProbe.Canvas.Forecast.Ready)throw new Exception("Initial pending forecast cancellation");
                            pendingProbe.Close();
                        }

                        test.ForecastRequests=1;test.Canvas.ForecastStatus="网络读取超时 · 尚无查询结果，可重试";test.Size=new Size(384,520);test.Update();Application.DoEvents();
                        if(test.Canvas.DisplayForecastState()!="查询失败" || !test.Canvas.HasHit("refresh"))throw new Exception("Initial forecast failure retry state");
                        Shot(test,"reset-forecast-failed-no-result-384x520.png");
                        test.Canvas.ForecastStatus="刷新已取消 · 尚无查询结果，可重试";test.Update();Application.DoEvents();
                        if(test.Canvas.DisplayForecastState()!="已取消")throw new Exception("Initial forecast cancelled state");
                        test.ForecastRequests=0;test.Canvas.ForecastStatus="尚未查询 · 点击“刷新预告”读取公开消息";

                        await test.RefreshNow();
                        ResetForecast inferredForecast=test.Canvas.Forecast;
                        if(!inferredForecast.Ready || !inferredForecast.HasAnnouncement || !inferredForecast.IsInferred || forecastCalls!=1 || ResetCanvas.ForecastState(inferredForecast)!="有待核实预告")throw new Exception("Inferred forecast state");
                        foreach(Size size in new[]{new Size(320,300),new Size(384,520),new Size(720,860)})
                        {
                            test.Size=size;test.Canvas.DiagnosticKey(Keys.Home);test.Update();Application.DoEvents();
                            if(!test.Canvas.HitBounds() || !test.Canvas.ActionHitsDoNotOverlap())throw new Exception("Forecast hit bounds or overlap "+size);
                            Shot(test,"reset-forecast-inferred-"+size.Width+"x"+size.Height+"-top.png");
                            if(size.Width<=384)
                            {
                                test.Canvas.DiagnosticKey(Keys.End);test.Update();Application.DoEvents();
                                if(test.Canvas.ForecastScroll!=test.Canvas.ForecastScrollMaximum || !test.Canvas.HitBounds() || !test.Canvas.ActionHitsDoNotOverlap())throw new Exception("Forecast keyboard scroll bounds");
                                if(size.Width==384 && !test.Canvas.HasHit("source"))throw new Exception("Forecast source entry");
                                Shot(test,"reset-forecast-inferred-"+size.Width+"x"+size.Height+"-bottom.png");
                            }
                        }
                        if(ipCalls!=0 || forecastCalls!=1)throw new Exception("Rendering must not fetch");

                        test.Size=new Size(720,860);Task pending=test.RefreshNow();await Task.Delay(40);test.Canvas.SetTab(false);
                        if(await Task.WhenAny(pending,Task.Delay(1000))!=pending || !test.Canvas.ForecastStatus.Contains("取消") || !object.ReferenceEquals(test.Canvas.Forecast,inferredForecast))throw new Exception("Forecast tab-switch cancellation");
                        string cancelledForecastStatus=test.Canvas.ForecastStatus;

                        await test.RefreshNow();
                        if(!object.ReferenceEquals(test.Canvas.IpResult,full) || ipCalls!=1 || full.TrustScore!=0 || full.IsVpn.HasValue || full.IsProxy!=false || full.IsCrawler!=true)throw new Exception("Full IP result / all flag states / zero score");
                        foreach(Size size in new[]{new Size(320,300),new Size(384,520),new Size(720,860)})
                        {
                            test.Size=size;test.Canvas.SetTab(false);test.Canvas.ScrollTop();test.Update();Application.DoEvents();
                            if(!test.Canvas.HitBounds() || !test.Canvas.ActionHitsDoNotOverlap())throw new Exception("IP hit bounds or overlap "+size);
                            if(size.Width==320 && (!test.Canvas.HasHit("more") || test.Canvas.IpScrollMaximum<=0))throw new Exception("Small viewport more button");
                            Shot(test,"reset-ip-"+size.Width+"x"+size.Height+(size.Width==320?"-top":"")+".png");
                            if(size.Width<=384)
                            {
                                test.Canvas.DiagnosticKey(Keys.End);test.Update();Application.DoEvents();
                                if(test.Canvas.IpScroll!=test.Canvas.IpScrollMaximum || !test.Canvas.HasHit("ipSource") || !test.Canvas.HitBounds() || !test.Canvas.ActionHitsDoNotOverlap())throw new Exception("Keyboard scroll / full page entry");
                                Shot(test,"reset-ip-"+size.Width+"x"+size.Height+"-bottom.png");
                            }
                        }
                        full.TrustScore=100;ThemeSelection.Select("summer",false);test.Size=new Size(720,860);test.Canvas.ScrollTop();test.Update();Application.DoEvents();
                        if(!test.Canvas.HitBounds() || !test.Canvas.ActionHitsDoNotOverlap())throw new Exception("High score summer theme bounds");Shot(test,"reset-ip-score100-summer-720x860.png");
                        full.TrustScore=64;ThemeSelection.Select("autumn",false);test.Size=new Size(384,520);test.Canvas.ScrollTop();test.Update();Application.DoEvents();
                        if(!test.Canvas.HitBounds() || !test.Canvas.ActionHitsDoNotOverlap())throw new Exception("Intermediate score autumn theme bounds");Shot(test,"reset-ip-score64-autumn-384x520.png");
                        full.TrustScore=null;ThemeSelection.Select("winter",false);test.Size=new Size(720,860);test.Canvas.ScrollTop();test.Update();Application.DoEvents();
                        if(!test.Canvas.HitBounds() || !test.Canvas.ActionHitsDoNotOverlap())throw new Exception("Unknown score winter theme bounds");Shot(test,"reset-ip-score-unknown-winter-720x860.png");
                        full.TrustScore=0;ThemeSelection.Select(originalTheme,false);
                        if(ipCalls!=1 || forecastCalls!=2)throw new Exception("Rendering must not fetch");

                        test.Size=new Size(720,860);test.Canvas.SetTab(false);pending=test.RefreshNow();await Task.Delay(40);test.Canvas.SetTab(true);
                        if(await Task.WhenAny(pending,Task.Delay(1000))!=pending || !test.Canvas.IpStatus.Contains("取消") || !object.ReferenceEquals(test.Canvas.IpResult,full))throw new Exception("IP tab-switch cancellation");
                        string cancelledIpStatus=test.Canvas.IpStatus;

                        string ipStatusBeforeForecastFailure=test.Canvas.IpStatus;await test.RefreshNow();
                        if(!object.ReferenceEquals(test.Canvas.Forecast,inferredForecast) || !test.Canvas.ForecastStatus.Contains("超时") || test.Canvas.IpStatus!=ipStatusBeforeForecastFailure)throw new Exception("Forecast failure separation / preservation");

                        await test.RefreshNow();
                        if(!test.Canvas.Forecast.Ready || test.Canvas.Forecast.HasAnnouncement || ResetCanvas.ForecastState(test.Canvas.Forecast)!="暂无新预告")throw new Exception("Empty forecast state");
                        test.Size=new Size(720,860);test.Canvas.DiagnosticKey(Keys.Home);test.Update();Application.DoEvents();
                        if(!test.Canvas.HitBounds() || !test.Canvas.ActionHitsDoNotOverlap() || test.Canvas.HasHit("source"))throw new Exception("Empty forecast source/time state");
                        Shot(test,"reset-forecast-empty-720x860.png");

                        await test.RefreshNow();
                        if(ResetCanvas.ForecastState(test.Canvas.Forecast)!="预告已过期" || !test.Canvas.Forecast.WindowExpired)throw new Exception("Expired forecast state");
                        test.Update();Application.DoEvents();if(test.Canvas.HasHit("source"))throw new Exception("Unsafe forecast source button");
                        Shot(test,"reset-forecast-expired-720x860.png");

                        await test.RefreshNow();
                        if(ResetCanvas.ForecastState(test.Canvas.Forecast)!="旧数据待更新" || !test.Canvas.ForecastStatus.Contains("旧数据"))throw new Exception("Stale forecast state");
                        test.Update();Application.DoEvents();if(!test.Canvas.HasHit("source"))throw new Exception("Safe stale source button");
                        Shot(test,"reset-forecast-stale-720x860.png");

                        await test.RefreshNow();
                        if(ResetCanvas.ForecastState(test.Canvas.Forecast)!="有待核实预告" || test.Canvas.Forecast.IsInferred || test.Canvas.Forecast.WindowLabel.Length==0)throw new Exception("Announced forecast state");
                        test.Size=new Size(384,520);test.Canvas.DiagnosticKey(Keys.Home);test.Update();Application.DoEvents();
                        if(!test.Canvas.HitBounds() || !test.Canvas.ActionHitsDoNotOverlap() || test.Canvas.ForecastScrollMaximum<=0)throw new Exception("Announced forecast narrow bounds");
                        Shot(test,"reset-forecast-announced-384x520-top.png");
                        test.Canvas.DiagnosticKey(Keys.End);test.Update();Application.DoEvents();Shot(test,"reset-forecast-announced-384x520-bottom.png");

                        foreach(string forecastTheme in new[]{"heritage","summer","autumn"})
                        {
                            ThemeSelection.Select(forecastTheme,false);test.Size=new Size(720,860);test.Canvas.DiagnosticKey(Keys.Home);test.Update();Application.DoEvents();
                            if(!test.Canvas.HitBounds() || !test.Canvas.ActionHitsDoNotOverlap())throw new Exception("Forecast unified theme bounds "+forecastTheme);
                            Shot(test,"reset-forecast-announced-"+forecastTheme+"-720x860.png");
                        }
                        ThemeSelection.Select(originalTheme,false);

                        test.Canvas.SetTab(false);string forecastStatusBeforeIpFailure=test.Canvas.ForecastStatus;IpQueryResult retainedIp=test.Canvas.IpResult;await test.RefreshNow();
                        if(!object.ReferenceEquals(test.Canvas.IpResult,retainedIp) || !test.Canvas.IpStatus.Contains("超时") || test.Canvas.ForecastStatus!=forecastStatusBeforeIpFailure)throw new Exception("IP failure separation / preservation");
                        await test.RefreshNow();
                        if(!object.ReferenceEquals(test.Canvas.IpResult,partial) || !test.Canvas.IpStatus.Contains("部分") || partial.TrustScore.HasValue || partial.RiskAvailable)throw new Exception("Partial IP result");
                        test.Size=new Size(720,860);test.Canvas.ScrollTop();test.Update();Application.DoEvents();
                        if(!test.Canvas.HitBounds() || !test.Canvas.ActionHitsDoNotOverlap())throw new Exception("Partial result bounds");Shot(test,"reset-ip-partial-720x860.png");

                        pending=test.RefreshNow();await Task.Delay(40);test.Hide();
                        if(await Task.WhenAny(pending,Task.Delay(1000))!=pending || test.TimerRunning || test.Canvas.IpBusy || !object.ReferenceEquals(test.Canvas.IpResult,partial))throw new Exception("Hide cancellation / result preservation");
                        if(test.Canvas.ForecastStatus!=forecastStatusBeforeIpFailure)throw new Exception("Hide changed idle forecast status");
                        test.Show();if(!test.TimerRunning)throw new Exception("Visible timer");

                        pending=test.RefreshNow();await Task.Delay(40);test.Close();
                        if(await Task.WhenAny(pending,Task.Delay(1000))!=pending)throw new Exception("Close cancellation");
                        if((store.Settings.Accounts??new List<AccountMeta>()).Count!=0)throw new Exception("IP UI mutated accounts");
                        Console.WriteLine("RESET_REMINDER_OK forecast_first unified_theme_colors clear_states=unread,empty,announced,inferred,expired,stale,failed,cancelled source_and_query_times responsive_scroll ip_native purity_cards manual_only preserve_failure cancel_tab hide_show close keyboard_scroll more_button hit_bounds no_action_overlap widths=320x300,384x520,720x860 forecast_themes=heritage,summer,autumn ip_themes=summer,autumn,winter screenshots=26 offline_fakes no_account_mutation no_network");code=0;
                    }
                    catch(Exception ex){Console.WriteLine("RESET_REMINDER_FAILED "+ex);}
                    finally{try{ThemeSelection.Select(originalTheme,false);}catch{}test.Close();}
                };
                Application.Run(test);
            }
            return code;
        }
        private static Task<T> Cancellable<T>(CancellationToken token)
        {
            var result=new TaskCompletionSource<T>();
            token.Register(delegate{result.TrySetCanceled();});
            return result.Task;
        }
        private static void Shot(Form form,string name)
        {
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(folder);
            using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(folder,name));}
        }
    }
}
