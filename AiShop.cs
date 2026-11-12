using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexAccountDock
{
    // Read-only native product cards backed by Catfk's public storefront APIs.
    // Data is fetched only on manual refresh and is never persisted locally.
    internal static class AiShop
    {
        private const string CategoriesUrl="https://catfk.com/shopApi/Shop/categoryList";
        private const string GoodsUrl="https://catfk.com/shopApi/Shop/goodsList";
        private const string StoreUrl="https://catfk.com/shop/UNIQUE";
        private const int MaxBytes=3*1024*1024;
        private const int MaxRefreshBytes=6*1024*1024;
        private const int MaxConcurrentRequests=3;
        private const int PageSize=50;
        private const int MaxProducts=500;
        private static ShopWindow window;

        internal sealed class ShopItem
        {
            internal string Id,Name,Summary,Category,Link,PriceText,StockText;
            internal int StockCount,ShowStockType;
        }
        private sealed class ShopCategory{internal int Id,GoodsCount;internal string Name;}

        internal static void Open(Form owner)
        {
            if(owner==null || owner.IsDisposed)return;
            if(window==null || window.IsDisposed){window=new ShopWindow(null);window.FormClosed+=delegate{window=null;};}
            SidePanels.Activate(owner,window);
        }

        internal static int SelfTest()
        {
            try
            {
                var categories=ParseCategories("{\"code\":1,\"data\":[{\"id\":8,\"name\":\"Codex\",\"goods_count\":2},{\"id\":9,\"name\":\"空分类\",\"goods_count\":0}]}");
                Assert(categories.Count==1 && categories[0].Id==8 && categories[0].Name=="Codex","category parser");
                string fixture="{\"code\":1,\"data\":{\"list\":[{\"goods_key\":\"abc123\",\"link\":\"https://catfk.com/item/abc123\",\"name\":\"Codex Plus\",\"price\":125,\"description\":\"<p>官方充值&nbsp;30 天</p>\",\"category\":{\"name\":\"Codex\"},\"extend\":{\"stock_count\":4,\"show_stock_type\":0}},{\"goods_key\":\"missing\",\"link\":\"https://catfk.com/item/missing\",\"name\":\"字段缺失\",\"extend\":{}},{\"goods_key\":\"bad\",\"link\":\"file:///c:/secret\",\"name\":\"bad\",\"price\":1}]}}";
                var items=ParseGoods(fixture,"Codex");
                Assert(items.Count==2,"unsafe product rejected");
                Assert(items[0].PriceText=="¥125" && items[0].StockText=="库存少量","price stock");
                Assert(items[1].PriceText=="价格待确认" && items[1].StockText=="库存待确认","missing fields");
                Assert(items[0].Summary.IndexOf('<')<0 && items[0].Summary.Contains("官方充值"),"plain summary");
                Assert(StockLabel(0,0)=="缺货" && StockLabel(21,0)=="库存充足" && StockLabel(7,1)=="剩余 7 件","stock policy");
                Assert(IsProductLink("https://catfk.com/item/abc123") && !IsProductLink("https://catfk.com.example.org/item/x"),"link policy");
                Assert(MaxRefreshBytes==6*1024*1024 && MaxConcurrentRequests==3,"resource policy");
                using(var cancel=new CancellationTokenSource())
                {
                    var budget=new DownloadBudget(6,cancel);
                    string first=ReadLimitedAsync(new MemoryStream(new byte[]{1,2,3,4}),10,budget,CancellationToken.None).GetAwaiter().GetResult();
                    Assert(first.Length==4 && budget.Used==4,"refresh budget counts bytes");
                    bool totalRejected=false;
                    try{ReadLimitedAsync(new MemoryStream(new byte[]{5,6,7}),10,budget,CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidDataException){totalRejected=true;}
                    Assert(totalRejected && budget.Exceeded && cancel.IsCancellationRequested,"refresh budget cancels round");
                }
                bool responseRejected=false;
                try{ReadLimitedAsync(new MemoryStream(new byte[]{1,2,3,4}),3,null,CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidDataException){responseRejected=true;}
                Assert(responseRejected,"response budget");
                Console.WriteLine("AI_SHOP_OK public_api parser price stock plain_text safe_links response_limit refresh_budget concurrent_limit manual_only no_cache no_webview no_purchase");return 0;
            }
            catch(Exception ex){Console.WriteLine("AI_SHOP_FAILED "+ex.Message);return 1;}
        }

        internal static int RunUiDiagnostic()
        {
            string old=ThemeSelection.Current;
            try
            {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                using(var test=new ShopWindow(delegate(CancellationToken token){return Task.FromResult(FakeItems(8));}))
                {
                    test.Show();test.Canvas.SetItems(FakeItems(8));test.Canvas.SetStatus("更新完成 · 8 件商品 · 14:30 · 仅手动刷新");Application.DoEvents();
                    foreach(int width in new[]{320,384,500,600,720})
                    {
                        test.Size=new Size(width,width==320?620:(width==384?700:860));test.Update();Application.DoEvents();
                        Assert(test.Canvas.AllHitsInside(),"hit bounds "+width);Assert(test.Canvas.HasHit("store"),"store button "+width);Assert(test.Canvas.ItemCount==8,"items "+width);
                        SaveShot(test,"ai-shop-width-"+width.ToString(CultureInfo.InvariantCulture)+".png");
                    }
                    test.Size=new Size(1080,1290);test.Update();Application.DoEvents();Assert(test.Canvas.AllHitsInside(),"150 percent hits");SaveShot(test,"ai-shop-dpi-150.png");
                    test.Size=new Size(720,860);
                    foreach(string theme in new[]{"heritage","spring","summer","autumn","winter","custom"})
                    {ThemeSelection.Select(theme,false);test.RefreshGlass();test.Update();Application.DoEvents();Assert(test.Canvas.AllHitsInside(),"theme "+theme);SaveShot(test,"ai-shop-theme-"+theme+".png");}
                    test.Close();
                }
                int calls=0;
                using(var requestTest=new ShopWindow(delegate(CancellationToken token)
                {
                    calls++;
                    if(calls==2 || calls==4)return new TaskCompletionSource<List<ShopItem>>().Task;
                    if(calls==3){var failed=new TaskCompletionSource<List<ShopItem>>();failed.SetException(new IOException("离线失败测试"));return failed.Task;}
                    return Task.FromResult(FakeItems(3));
                }))
                {
                    requestTest.Show();Pump(requestTest.RefreshNow(),2000);
                    Assert(requestTest.Canvas.ItemCount==3 && requestTest.LastRefreshSucceeded,"initial refresh");
                    Task pending=requestTest.RefreshNow();Pump(requestTest.RefreshNow(),1000);Pump(pending,1500);
                    Assert(requestTest.Canvas.ItemCount==3,"cancel preserves products");
                    Pump(requestTest.RefreshNow(),1500);
                    Assert(requestTest.Canvas.ItemCount==3 && requestTest.Canvas.StatusText.Contains("保留"),"failure preserves products");
                    pending=requestTest.RefreshNow();requestTest.Hide();Pump(pending,1500);
                    Assert(requestTest.Canvas.ItemCount==3,"hide cancels and preserves products");requestTest.Close();
                }
                Console.WriteLine("AI_SHOP_UI_OK widths=320,384,500,600,720 themes=6 native_cards failure_preserves cancel_preserves hide_cancels manual_only no_network no_webview no_purchase");return 0;
            }
            catch(Exception ex){Console.WriteLine("AI_SHOP_UI_FAILED "+ex);return 1;}
            finally{ThemeSelection.Select(old,false);}
        }

        internal static int RunLiveDiagnostic()
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);int code=1;
            using(var test=new ShopWindow(null))using(var stop=new System.Windows.Forms.Timer{Interval=22000})
            {
                stop.Tick+=delegate{Console.WriteLine("AI_SHOP_LIVE_FAILED deadline");test.Close();};
                test.Shown+=async delegate
                {
                    stop.Start();
                    try{await test.RefreshNow();if(!test.LastRefreshSucceeded || test.Canvas.ItemCount==0)throw new Exception("public product feed empty");if(!test.Canvas.ItemsValid())throw new Exception("invalid product values");test.Update();Application.DoEvents();SaveShot(test,"ai-shop-live.png");Console.WriteLine("AI_SHOP_LIVE_OK products="+test.Canvas.ItemCount+" public_api_only manual_refresh screenshot=ai-shop-live.png no_webview no_purchase");code=0;}
                    catch(Exception ex){Console.WriteLine("AI_SHOP_LIVE_FAILED "+ex.Message);}
                    finally{stop.Stop();test.Close();}
                };
                Application.Run(test);
            }
            return code;
        }

        internal static async Task<List<ShopItem>> FetchAsync(CancellationToken token)
        {
            using(var lifetime=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var budget=new DownloadBudget(MaxRefreshBytes,lifetime);
                Task<List<ShopItem>> work=Task.Run(async delegate
                {
                    ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
                    using(var handler=new HttpClientHandler{UseProxy=false,UseCookies=false,UseDefaultCredentials=false,Credentials=null,AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate})
                    using(var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(14)})
                    {
                        string categoryJson=await Post(client,CategoriesUrl,"{\"token\":\"UNIQUE\",\"goods_type\":\"card\",\"category_key\":\"\"}",budget,lifetime.Token).ConfigureAwait(false);
                        var categories=ParseCategories(categoryJson);if(categories.Count==0)return new List<ShopItem>();
                        int announced=categories.Sum(delegate(ShopCategory category){return category.GoodsCount;});
                        if(announced>MaxProducts)throw new InvalidDataException("店铺商品较多，请打开店铺网页查看");
                        using(var gate=new SemaphoreSlim(MaxConcurrentRequests,MaxConcurrentRequests))
                        {
                            var calls=new List<Task<List<ShopItem>>>();
                            foreach(var category in categories.Take(20))
                            {
                                int pages=Math.Max(1,(category.GoodsCount+PageSize-1)/PageSize);
                                for(int page=1;page<=pages;page++)
                                {
                                    string payload="{\"token\":\"UNIQUE\",\"keywords\":\"\",\"category_id\":"+category.Id.ToString(CultureInfo.InvariantCulture)+",\"goods_type\":\"card\",\"current\":"+page.ToString(CultureInfo.InvariantCulture)+",\"pageSize\":"+PageSize.ToString(CultureInfo.InvariantCulture)+"}";
                                    calls.Add(FetchCategory(client,payload,category.Name,gate,budget,lifetime.Token));
                                }
                            }
                            List<ShopItem>[] batches=await Task.WhenAll(calls.ToArray()).ConfigureAwait(false);
                            var result=new List<ShopItem>();var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach(var batch in batches)foreach(var item in batch)if(keys.Add(item.Id))result.Add(item);
                            if(result.Count<announced)throw new InvalidDataException("店铺商品读取不完整，请稍后手动重试");
                            return result;
                        }
                    }
                },lifetime.Token);
                Task deadline=Task.Delay(TimeSpan.FromSeconds(15),token);
                if(await Task.WhenAny(work,deadline).ConfigureAwait(false)!=work){lifetime.Cancel();Observe(work);token.ThrowIfCancellationRequested();throw new TimeoutException("店铺连接超时，请稍后手动重试。");}
                try{return await work.ConfigureAwait(false);}catch(OperationCanceledException){token.ThrowIfCancellationRequested();if(budget.Exceeded)throw new InvalidDataException("店铺本次刷新数据过大，请打开店铺网页查看");throw new TimeoutException("店铺连接超时，请稍后手动重试。");}
            }
        }
        private static async Task<List<ShopItem>> FetchCategory(HttpClient client,string payload,string category,SemaphoreSlim gate,DownloadBudget budget,CancellationToken token)
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try{return ParseGoods(await Post(client,GoodsUrl,payload,budget,token).ConfigureAwait(false),category);}
            finally{gate.Release();}
        }
        private static async Task<string> Post(HttpClient client,string url,string json,DownloadBudget budget,CancellationToken token)
        {
            using(var request=new HttpRequestMessage(HttpMethod.Post,url))
            {
                request.Headers.Accept.ParseAdd("application/json");request.Content=new StringContent(json,Encoding.UTF8,"application/json");
                using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value>MaxBytes)throw new InvalidDataException("店铺响应过大");
                    using(Stream stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))return await ReadLimitedAsync(stream,MaxBytes,budget,token).ConfigureAwait(false);
                }
            }
        }

        private sealed class DownloadBudget
        {
            private readonly int limit;private readonly CancellationTokenSource round;private int used,exceeded;
            internal DownloadBudget(int limit,CancellationTokenSource round){this.limit=limit;this.round=round;}
            internal int Used{get{return Volatile.Read(ref used);}}
            internal bool Exceeded{get{return Volatile.Read(ref exceeded)!=0;}}
            internal void Consume(int count)
            {
                if(count<=0)return;
                if(Interlocked.Add(ref used,count)<=limit)return;
                Interlocked.Exchange(ref exceeded,1);if(round!=null)round.Cancel();
                throw new InvalidDataException("店铺本次刷新数据过大，请打开店铺网页查看");
            }
        }
        private static async Task<string> ReadLimitedAsync(Stream stream,int responseLimit,DownloadBudget budget,CancellationToken token)
        {
            using(var buffer=new MemoryStream())
            {
                byte[] chunk=new byte[16384];int read;
                while((read=await stream.ReadAsync(chunk,0,chunk.Length,token).ConfigureAwait(false))>0)
                {
                    if(buffer.Length+read>responseLimit)throw new InvalidDataException("店铺响应过大");
                    if(budget!=null)budget.Consume(read);buffer.Write(chunk,0,read);
                }
                token.ThrowIfCancellationRequested();return Encoding.UTF8.GetString(buffer.ToArray());
            }
        }

        private static List<ShopCategory> ParseCategories(string json)
        {
            var root=Deserialize(json);if(Number(Value(root,"code"),0)!=1)throw new FormatException("店铺分类读取失败");
            IEnumerable rows=Value(root,"data") as IEnumerable;if(rows==null)throw new FormatException("店铺分类格式无法识别");
            var result=new List<ShopCategory>();
            foreach(object value in rows)
            {
                var row=Dict(value);if(row==null)continue;int id=Number(Value(row,"id"),0),count=Number(Value(row,"goods_count"),0);string name=Clean(Text(row,"name"),60);
                if(id>0 && count>0 && name.Length>0)result.Add(new ShopCategory{Id=id,GoodsCount=count,Name=name});if(result.Count>=20)break;
            }
            return result;
        }
        private static List<ShopItem> ParseGoods(string json,string fallback)
        {
            var root=Deserialize(json);if(Number(Value(root,"code"),0)!=1)throw new FormatException("商品列表读取失败");
            var data=Dict(Value(root,"data"));IEnumerable rows=data==null?null:Value(data,"list") as IEnumerable;if(rows==null)throw new FormatException("商品列表格式无法识别");
            var result=new List<ShopItem>();
            foreach(object value in rows)
            {
                var row=Dict(value);if(row==null)continue;string id=Clean(Text(row,"goods_key"),100),name=Clean(Text(row,"name"),240),link=Clean(Text(row,"link"),2048);
                if(id.Length==0 || name.Length==0 || !IsProductLink(link))continue;
                var category=Dict(Value(row,"category"));var extend=Dict(Value(row,"extend"));string categoryName=Clean(Text(category,"name"),60);if(categoryName.Length==0)categoryName=Clean(fallback,60);
                object priceRaw=Value(row,"price");decimal price;bool priceKnown=TryMoney(priceRaw,out price);object stockRaw=Value(extend,"stock_count");int stock;bool stockKnown=TryNumber(stockRaw,out stock);int show=Number(Value(extend,"show_stock_type"),0);
                result.Add(new ShopItem{Id=id,Name=name,Link=link,Category=categoryName.Length==0?"商品":categoryName,PriceText=priceKnown?PriceLabel(price):"价格待确认",StockCount=stockKnown?stock:-1,ShowStockType=show,StockText=stockKnown?StockLabel(stock,show):"库存待确认",Summary=PlainSummary(Text(row,"description"),260)});
                if(result.Count>=100)break;
            }
            return result;
        }
        private static Dictionary<string,object> Deserialize(string json)
        {if(string.IsNullOrEmpty(json) || json.Length>MaxBytes)throw new FormatException("店铺内容无效或过大");var root=new JavaScriptSerializer{MaxJsonLength=MaxBytes,RecursionLimit=40}.Deserialize<Dictionary<string,object>>(json);if(root==null)throw new FormatException("店铺内容格式无法识别");return root;}
        private static Dictionary<string,object> Dict(object value){return value as Dictionary<string,object>;}
        private static object Value(Dictionary<string,object> source,string key){object value;return source!=null && source.TryGetValue(key,out value)?value:null;}
        private static string Text(Dictionary<string,object> source,string key){object value=Value(source,key);return value==null?"":Convert.ToString(value,CultureInfo.InvariantCulture)??"";}
        private static bool TryNumber(object value,out int result){try{if(value==null){result=0;return false;}result=Convert.ToInt32(value,CultureInfo.InvariantCulture);return true;}catch{result=0;return false;}}
        private static int Number(object value,int fallback){int result;return TryNumber(value,out result)?result:fallback;}
        private static bool TryMoney(object value,out decimal result){try{if(value==null){result=0;return false;}result=Convert.ToDecimal(value,CultureInfo.InvariantCulture);if(result<0){result=0;return false;}return true;}catch{result=0;return false;}}
        private static string Clean(string value,int limit){value=value??"";if(value.Length>limit)value=value.Substring(0,limit);return new string(value.Where(delegate(char c){return !char.IsControl(c)||c=='\n'||c=='\t';}).ToArray()).Trim();}
        private static string PlainSummary(string html,int limit)
        {
            if(string.IsNullOrWhiteSpace(html))return "点击“查看商品”阅读完整说明与购买须知。";
            var plain=new StringBuilder();bool tag=false;
            foreach(char c in html){if(c=='<'){tag=true;if(plain.Length>0 && plain[plain.Length-1]!=' ')plain.Append(' ');continue;}if(c=='>'){tag=false;continue;}if(!tag)plain.Append(c);if(plain.Length>=limit*5)break;}
            string decoded=WebUtility.HtmlDecode(plain.ToString())??"";var normalized=new StringBuilder();bool space=false;
            foreach(char c in decoded){if(char.IsWhiteSpace(c)||char.IsControl(c)){space=normalized.Length>0;continue;}if(space){normalized.Append(' ');space=false;}normalized.Append(c);if(normalized.Length>=limit)break;}
            string text=normalized.ToString().Trim();return text.Length==0?"点击“查看商品”阅读完整说明与购买须知。":text+(decoded.Trim().Length>text.Length?"…":"");
        }
        private static string PriceLabel(decimal price){return price==0?"免费":"¥"+price.ToString(price==decimal.Truncate(price)?"0":"0.##",CultureInfo.InvariantCulture);}
        private static string StockLabel(int count,int showType){if(count<=0)return "缺货";if(showType!=0)return "剩余 "+count.ToString(CultureInfo.InvariantCulture)+" 件";if(count<=5)return "库存少量";if(count<20)return "库存一般";return "库存充足";}
        private static bool IsProductLink(string value){Uri uri;return Uri.TryCreate(value,UriKind.Absolute,out uri)&&uri.Scheme==Uri.UriSchemeHttps&&string.IsNullOrEmpty(uri.UserInfo)&&string.Equals(uri.DnsSafeHost,"catfk.com",StringComparison.OrdinalIgnoreCase)&&uri.AbsolutePath.StartsWith("/item/",StringComparison.OrdinalIgnoreCase);}
        private static void Observe(Task task){task.ContinueWith(delegate(Task t){var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
        private static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        private static void Pump(Task task,int milliseconds)
        {
            Stopwatch clock=Stopwatch.StartNew();
            while(!task.IsCompleted && clock.ElapsedMilliseconds<milliseconds){Application.DoEvents();Thread.Sleep(10);}
            if(!task.IsCompleted)throw new TimeoutException("diagnostic task timeout");
            task.GetAwaiter().GetResult();
        }
        private static void SaveShot(Form form,string name){string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(folder);using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(folder,name));}}
        private static List<ShopItem> FakeItems(int count)
        {
            string[] categories={"Codex","Gemini","Duolingo Super"};var result=new List<ShopItem>();
            for(int i=0;i<count;i++)result.Add(new ShopItem{Id="demo-"+i,Name=i%2==0?"Codex Plus 官方充值 CDK 质保订阅 30 天":"AI 工具会员与数字商品演示 "+(i+1),Category=categories[i%categories.Length],PriceText="¥"+(12+i*9),StockCount=i%4==0?0:i+1,StockText=i%4==0?"缺货":(i<3?"库存少量":"库存一般"),Summary="商品简介用于验证单列大卡、清晰字体、展开说明与小屏滚动。真实内容仅在点击手动刷新后读取。",Link="https://catfk.com/item/demo"+i});
            return result;
        }

        internal sealed class ShopWindow:DockedGlassWindow
        {
            private readonly Func<CancellationToken,Task<List<ShopItem>>> fetch;internal readonly ShopCanvas Canvas;private CancellationTokenSource operation;private bool busy,disposed;private int generation;internal bool LastRefreshSucceeded;
            internal ShopWindow(Func<CancellationToken,Task<List<ShopItem>>> source){Text="AI 小铺";Size=new Size(720,860);MinimumSize=new Size(320,520);fetch=source??FetchAsync;Canvas=new ShopCanvas(this){Dock=DockStyle.Fill};Controls.Add(Canvas);VisibleChanged+=delegate{if(!Visible&&busy)Cancel("刷新已取消；再次打开后可手动重试。");};}
            internal async Task RefreshNow()
            {
                if(busy){Cancel("已取消本次刷新；现有商品保持不变。");return;}busy=true;LastRefreshSucceeded=false;int current=++generation;var request=new CancellationTokenSource();operation=request;Canvas.SetBusy(true,"正在读取店铺商品… 再点一次可取消。");
                try
                {
                    Task<List<ShopItem>> pending=fetch(request.Token);if(await Task.WhenAny(pending,Task.Delay(16000,request.Token))!=pending){Observe(pending);request.Token.ThrowIfCancellationRequested();request.Cancel();throw new TimeoutException("店铺连接超时");}
                    List<ShopItem> received=await pending;if(request.IsCancellationRequested||disposed||current!=generation)return;Canvas.SetItems(received??new List<ShopItem>());LastRefreshSucceeded=true;Canvas.SetStatus(received==null||received.Count==0?"店铺暂时没有可显示的商品。":"更新完成 · "+received.Count+" 件商品 · "+DateTime.Now.ToString("HH:mm",CultureInfo.InvariantCulture)+" · 仅手动刷新");
                }
                catch(OperationCanceledException){if(!disposed&&current==generation)Canvas.SetStatus("刷新已取消；现有商品保持不变。");}
                catch(Exception ex){if(!disposed&&current==generation)Canvas.SetStatus("刷新失败："+Friendly(ex)+" · 已保留当前商品");}
                finally{if(object.ReferenceEquals(operation,request))operation=null;request.Dispose();if(!disposed&&current==generation){busy=false;Canvas.SetBusy(false,null);}}
            }
            private static string Friendly(Exception ex){if(ex is TimeoutException||ex is TaskCanceledException)return "请求超时，请稍后手动重试";string text=(ex.Message??"店铺接口暂时不可用").Replace('\r',' ').Replace('\n',' ').Trim();return text.Length>48?text.Substring(0,48)+"…":text;}
            private void Cancel(string message){generation++;busy=false;if(operation!=null&&!operation.IsCancellationRequested)operation.Cancel();if(!disposed)Canvas.SetBusy(false,message);}
            internal override void RefreshGlass(){base.RefreshGlass();Canvas.Invalidate();}
            protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;Cancel("已关闭");if(operation!=null)operation.Dispose();}base.Dispose(disposing);}
        }

        internal sealed class ShopCanvas:Control
        {
            private float ContentScale{get{return Math.Max(1f,owner.ClientSize.Height/860f);}}
            private int LayoutWidth{get{return Math.Max(1,(int)(base.Width/ContentScale));}}
            private int LayoutHeight{get{return Math.Max(1,(int)(base.Height/ContentScale));}}
            private Rectangle LayoutRectangle{get{return new Rectangle(0,0,LayoutWidth,LayoutHeight);}}
            private MouseEventArgs LogicalMouse(MouseEventArgs e){return new MouseEventArgs(e.Button,e.Clicks,(int)(e.X/ContentScale),(int)(e.Y/ContentScale),e.Delta);}
            private sealed class Hit{internal string Kind,Id;internal Rectangle Box;}
            private readonly ShopWindow owner;private readonly List<ShopItem> items=new List<ShopItem>();private readonly List<Hit> hits=new List<Hit>();private string expandedId,status="尚未加载 · 点击手动刷新读取实时商品";private bool busy;private int scroll,totalHeight;private Rectangle viewport;
            internal int ItemCount{get{return items.Count;}}internal string StatusText{get{return status;}}
            internal ShopCanvas(ShopWindow value){owner=value;DoubleBuffered=true;TabStop=true;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
            internal void SetItems(IEnumerable<ShopItem> source){items.Clear();if(source!=null)items.AddRange(source.Take(MaxProducts));scroll=0;expandedId=items.Count>0?items[0].Id:null;Invalidate();}
            internal void SetBusy(bool value,string message){busy=value;if(!string.IsNullOrEmpty(message))status=message;Invalidate();}internal void SetStatus(string value){status=value??"";Invalidate();}
            internal bool AllHitsInside(){return hits.All(delegate(Hit hit){return LayoutRectangle.Contains(hit.Box);});}internal bool ItemsValid(){return items.All(delegate(ShopItem item){return item!=null&&item.Name.Length>0&&item.PriceText.Length>0&&item.StockText.Length>0&&IsProductLink(item.Link);});}
            internal bool HasHit(string kind){return hits.Any(delegate(Hit hit){return hit.Kind==kind;});}
            protected override void OnPaintBackground(PaintEventArgs e){GlassBackdrop.Draw(e.Graphics,this);}protected override void OnResize(EventArgs e){base.OnResize(e);Clamp();Invalidate();}
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g=e.Graphics;g.ScaleTransform(ContentScale,ContentScale);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;hits.Clear();ThemeLook theme=ThemeVisual.Current;using(var shade=new SolidBrush(Color.FromArgb(ThemeVisual.IsReferenceCustom?0:76,theme.PanelBottom)))g.FillRectangle(shade,LayoutRectangle);int top=Header(g,theme);viewport=new Rectangle(12,top+9,Math.Max(1,LayoutWidth-24),Math.Max(1,LayoutHeight-top-61));Cards(g,theme);Status(g,theme);
            }
            private int Header(Graphics g,ThemeLook theme)
            {
                bool compact=LayoutWidth<600,narrow=LayoutWidth<480;
                Label(g,"AI 小铺",LayoutWidth<380?24:(narrow?28:38),new Rectangle(18,9,compact?Math.Max(90,LayoutWidth-120):230,48),true,ThemeVisual.HeaderInk,false,false);
                Label(g,"UNIQUE · 实时商品，手动刷新后同步",narrow?15:18,new Rectangle(20,compact?52:57,Math.Max(1,LayoutWidth-40),30),true,ThemeVisual.HeaderInk,false,false);
                int y=compact?13:18,x=LayoutWidth-48;
                Button(g,new Rectangle(x,y,36,36),"×","close",theme);x-=43;Button(g,new Rectangle(x,y,36,36),"−","hide",theme);
                int by;
                if(compact)
                {
                    int actionY=86,gap=7,available=Math.Max(1,LayoutWidth-28-gap),half=available/2;
                    Button(g,new Rectangle(14,actionY,half,36),"打开店铺 ↗","store",theme);
                    Button(g,new Rectangle(14+half+gap,actionY,available-half,36),busy?"取消":"手动刷新","refresh",theme);
                    by=130;
                }
                else
                {
                    int refreshWidth=110,storeWidth=126;
                    x-=refreshWidth+7;Button(g,new Rectangle(x,y,refreshWidth,36),busy?"取消":"手动刷新","refresh",theme);
                    x-=storeWidth+7;Button(g,new Rectangle(x,y,storeWidth,36),"打开店铺 ↗","store",theme);
                    by=101;
                }
                Rectangle bar=new Rectangle(14,by,Math.Max(1,LayoutWidth-28),47);Glass(g,bar,19,Color.FromArgb(158,theme.PanelBottom),theme.Rim);using(var pen=new Pen(Color.White,2)){g.DrawRectangle(pen,bar.X+15,bar.Y+14,18,17);g.DrawLine(pen,bar.X+18,bar.Y+14,bar.X+21,bar.Y+9);g.DrawLine(pen,bar.X+21,bar.Y+9,bar.X+29,bar.Y+9);g.DrawLine(pen,bar.X+29,bar.Y+9,bar.X+32,bar.Y+14);}string count=items.Count==0?"等待刷新":items.Count+" 件商品";Label(g,"商品、价格和库存以店铺实时数据为准",narrow?14:16,new Rectangle(bar.X+43,bar.Y,Math.Max(1,bar.Width-155),bar.Height),true,Color.White,false,false);Label(g,count,narrow?14:16,new Rectangle(bar.Right-112,bar.Y,96,bar.Height),true,ThemeVisual.Blend(theme.Accent,Color.White,.55f),true,false);return bar.Bottom;
            }
            private void Cards(Graphics g,ThemeLook theme)
            {
                GraphicsState saved=g.Save();g.SetClip(viewport);
                if(items.Count==0){Label(g,"还没有商品内容",26,new Rectangle(viewport.X+20,viewport.Y+viewport.Height/2-65,viewport.Width-40,42),true,Color.White,true,false);Label(g,"点击“手动刷新”，读取店铺最新商品、价格和库存。",17,new Rectangle(viewport.X+22,viewport.Y+viewport.Height/2-16,viewport.Width-44,68),false,Color.White,true,true);Label(g,"需要购买时会打开真实商品页",14,new Rectangle(viewport.X+20,viewport.Y+viewport.Height/2+55,viewport.Width-40,30),false,ThemeVisual.Blend(theme.Accent,Color.White,.55f),true,false);totalHeight=0;scroll=0;g.Restore(saved);return;}
                bool narrow=LayoutWidth<640;int collapsed=narrow?166:142,expanded=narrow?302:270,y=viewport.Y-scroll;
                foreach(var item in items){bool open=item.Id==expandedId;int height=open?expanded:collapsed;Rectangle card=new Rectangle(viewport.X,y,viewport.Width,height);if(card.Bottom>=viewport.Top&&card.Top<=viewport.Bottom)Card(g,card,item,open,theme,narrow);y+=height+10;}
                totalHeight=y-(viewport.Y-scroll)-10;g.Restore(saved);Clamp();ScrollBar(g,theme);
            }
            private void Card(Graphics g,Rectangle card,ShopItem item,bool open,ThemeLook theme,bool narrow)
            {
                using(var path=GraphicsExtensions.CreateRoundedPath(card,22))using(var fill=new LinearGradientBrush(card,Color.FromArgb(205,theme.PanelTop),Color.FromArgb(224,theme.PanelBottom),90f))using(var rim=new Pen(Color.FromArgb(225,theme.Rim),1.4f)){g.FillPath(fill,path);g.DrawPath(rim,path);}using(var dot=new SolidBrush(item.StockCount==0?Color.FromArgb(240,146,150):theme.Accent))g.FillEllipse(dot,card.X+17,card.Y+23,11,11);
                Rectangle toggle=new Rectangle(card.Right-53,card.Y+10,38,36);AddHit("toggle",item.Id,new Rectangle(card.X+30,card.Y+6,Math.Max(1,card.Width-84),narrow?82:70));Label(g,item.Name,narrow?18:21,new Rectangle(card.X+39,card.Y+9,Math.Max(1,card.Width-99),narrow?55:53),true,Color.White,false,true);Label(g,open?"⌃":"⌄",25,toggle,true,Color.White,true,false);AddHit("toggle",item.Id,toggle);
                if(narrow){Pill(g,new Rectangle(card.X+18,card.Y+70,106,28),item.Category,theme);Label(g,item.PriceText,22,new Rectangle(card.X+134,card.Y+67,Math.Max(1,card.Width-150),34),true,Color.White,false,false);Label(g,item.StockText,15,new Rectangle(card.X+19,card.Y+111,125,34),true,item.StockCount==0?Color.FromArgb(255,195,198):ThemeVisual.Blend(theme.Accent,Color.White,.5f),false,false);Rectangle buy=new Rectangle(card.Right-133,card.Y+107,116,39);Button(g,buy,"查看商品 ↗","",theme);AddHit("open",item.Id,buy);}
                else{Pill(g,new Rectangle(card.X+42,card.Y+72,150,30),item.Category,theme);Label(g,item.PriceText,24,new Rectangle(card.X+210,card.Y+66,120,42),true,Color.White,false,false);Label(g,item.StockText,16,new Rectangle(card.X+336,card.Y+69,Math.Max(80,card.Width-490),38),true,item.StockCount==0?Color.FromArgb(255,195,198):ThemeVisual.Blend(theme.Accent,Color.White,.5f),false,false);Rectangle buy=new Rectangle(card.Right-151,card.Y+65,132,42);Button(g,buy,"查看商品 ↗","",theme);AddHit("open",item.Id,buy);}
                if(!open)return;int divider=narrow?card.Y+159:card.Y+125;using(var pen=new Pen(Color.FromArgb(125,theme.Rim),1))g.DrawLine(pen,card.X+20,divider,card.Right-20,divider);Label(g,"商品简介",18,new Rectangle(card.X+24,divider+11,120,30),true,Color.White,false,false);Label(g,item.Summary,narrow?16:18,new Rectangle(card.X+25,divider+45,card.Width-50,card.Bottom-divider-58),false,Color.White,false,true);
            }
            private void Pill(Graphics g,Rectangle box,string text,ThemeLook theme){Glass(g,box,14,Color.FromArgb(190,theme.ButtonBottom),theme.Rim);Label(g,text,15,box,true,Color.White,true,false);}
            private void Status(Graphics g,ThemeLook theme){Rectangle bar=new Rectangle(12,LayoutHeight-48,Math.Max(1,LayoutWidth-24),36);Glass(g,bar,16,Color.FromArgb(192,theme.PanelBottom),theme.Rim);Label(g,status,LayoutWidth<380?14:16,new Rectangle(bar.X+13,bar.Y,Math.Max(1,bar.Width-26),bar.Height),true,Color.White,false,false);}
            private void ScrollBar(Graphics g,ThemeLook theme){int max=Math.Max(0,totalHeight-viewport.Height);if(max<=0)return;int h=Math.Max(30,viewport.Height*viewport.Height/Math.Max(viewport.Height,totalHeight)),y=viewport.Y+(viewport.Height-h)*scroll/max;using(var brush=new SolidBrush(Color.FromArgb(170,theme.Accent)))using(var path=GraphicsExtensions.CreateRoundedPath(new Rectangle(viewport.Right-6,y,4,h),2))g.FillPath(brush,path);}
            private void Clamp(){int max=Math.Max(0,totalHeight-Math.Max(1,viewport.Height));scroll=Math.Max(0,Math.Min(scroll,max));}private void SetScroll(int value){scroll=value;Clamp();Invalidate();}protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);SetScroll(scroll-(e.Delta>0?95:-95));}
            protected override bool IsInputKey(Keys keyData)
            {Keys key=keyData&Keys.KeyCode;return key==Keys.PageDown||key==Keys.PageUp||key==Keys.Home||key==Keys.End||base.IsInputKey(keyData);}
            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);int page=Math.Max(80,viewport.Height-80),maximum=Math.Max(0,totalHeight-Math.Max(1,viewport.Height));
                if(e.KeyCode==Keys.PageDown){SetScroll(scroll+page);e.Handled=true;}else if(e.KeyCode==Keys.PageUp){SetScroll(scroll-page);e.Handled=true;}else if(e.KeyCode==Keys.Home){SetScroll(0);e.Handled=true;}else if(e.KeyCode==Keys.End){SetScroll(maximum);e.Handled=true;}
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);e=LogicalMouse(e);if(e.Button!=MouseButtons.Left)return;Focus();for(int i=hits.Count-1;i>=0;i--)if(hits[i].Box.Contains(e.Location)){Hit hit=hits[i];if(hit.Kind=="refresh")RefreshClicked();else if(hit.Kind=="store")OpenStore();else if(hit.Kind=="hide")owner.Hide();else if(hit.Kind=="close")owner.Close();else if(hit.Kind=="toggle"){expandedId=expandedId==hit.Id?null:hit.Id;Clamp();Invalidate();}else if(hit.Kind=="open"){ShopItem item=items.FirstOrDefault(delegate(ShopItem candidate){return candidate.Id==hit.Id;});if(item!=null)OpenProduct(item.Link);}return;}if(e.Y<82)Native.BeginWindowDrag(owner.Handle);
            }
            private async void RefreshClicked(){await owner.RefreshNow();}protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);e=LogicalMouse(e);Cursor=hits.Any(delegate(Hit hit){return hit.Box.Contains(e.Location);})?Cursors.Hand:Cursors.Default;}
            private static void OpenStore(){try{Process.Start(new ProcessStartInfo(StoreUrl){UseShellExecute=true});}catch{}}
            private static void OpenProduct(string value){if(!IsProductLink(value))return;try{Process.Start(new ProcessStartInfo(value){UseShellExecute=true});}catch{}}
            private void AddHit(string kind,string id,Rectangle box){Rectangle clipped=(kind=="refresh"||kind=="store"||kind=="hide"||kind=="close")?Rectangle.Intersect(LayoutRectangle,box):Rectangle.Intersect(viewport,box);if(clipped.Width>0&&clipped.Height>0)hits.Add(new Hit{Kind=kind,Id=id,Box=clipped});}
            private void Button(Graphics g,Rectangle box,string text,string key,ThemeLook theme){Glass(g,box,17,Color.FromArgb(214,theme.ButtonBottom),theme.Rim);Label(g,text,text.Length<=2?22:17,box,true,Color.White,true,false);if(key.Length>0)AddHit(key,null,box);}
            private static void Glass(Graphics g,Rectangle box,int radius,Color fill,Color rim){if(box.Width<=0||box.Height<=0)return;using(var path=GraphicsExtensions.CreateRoundedPath(box,radius))using(var brush=new SolidBrush(fill))using(var pen=new Pen(Color.FromArgb(225,rim),1.25f)){g.FillPath(brush,path);g.DrawPath(pen,path);}}
            private static void Label(Graphics g,string value,int size,Rectangle box,bool bold,Color color,bool center,bool wrap)
            {
                if(box.Width<=0||box.Height<=0)return;size=Math.Max(16,size);using(var cn=new Font("楷体",size,bold?FontStyle.Bold:FontStyle.Bold,GraphicsUnit.Pixel))using(var latin=new Font("Times New Roman",size,bold?FontStyle.Bold:FontStyle.Bold,GraphicsUnit.Pixel))using(var ink=new SolidBrush(color))using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())
                {
                    format.FormatFlags|=StringFormatFlags.NoWrap|StringFormatFlags.MeasureTrailingSpaces;Func<string,float> width=delegate(string text){return Runs(text).Sum(delegate(KeyValuePair<bool,string> run){return g.MeasureString(run.Value,run.Key?cn:latin,2000,format).Width;});};float ascent=Math.Max(cn.Size*cn.FontFamily.GetCellAscent(cn.Style)/cn.FontFamily.GetEmHeight(cn.Style),latin.Size*latin.FontFamily.GetCellAscent(latin.Style)/latin.FontFamily.GetEmHeight(latin.Style)),descent=Math.Max(cn.Size*cn.FontFamily.GetCellDescent(cn.Style)/cn.FontFamily.GetEmHeight(cn.Style),latin.Size*latin.FontFamily.GetCellDescent(latin.Style)/latin.FontFamily.GetEmHeight(latin.Style)),lineHeight=Math.Max(cn.GetHeight(g),latin.GetHeight(g));int maximum=wrap?Math.Max(1,(int)(box.Height/lineHeight)):1;var lines=new List<string>();string line="";bool overflow=false;
                    foreach(char c in value??""){if(c=='\r')continue;if(c=='\n'&&!wrap){line+=" ";continue;}if(wrap&&(c=='\n'||(line.Length>0&&width(line+c)>box.Width))){lines.Add(line);line="";if(lines.Count>=maximum){overflow=true;break;}if(c=='\n')continue;}line+=c;}if(lines.Count<maximum)lines.Add(line);if(lines.Count==0)lines.Add("");
                    for(int index=0;index<lines.Count;index++){string text=lines[index];if(width(text)>box.Width||(overflow&&index==lines.Count-1)){while(text.Length>0&&width(text+"…")>box.Width)text=text.Substring(0,text.Length-1);text+="…";}float x=center?box.X+(box.Width-width(text))/2:box.X,baseline=(wrap?box.Y+index*lineHeight:box.Y+(box.Height-ascent-descent)/2)+ascent;GraphicsState saved=g.Save();g.SetClip(box,CombineMode.Intersect);foreach(var run in Runs(text)){Font font=run.Key?cn:latin;float cellAscent=font.Size*font.FontFamily.GetCellAscent(font.Style)/font.FontFamily.GetEmHeight(font.Style);g.DrawString(run.Value,font,ink,x,baseline-cellAscent,format);x+=g.MeasureString(run.Value,font,2000,format).Width;}g.Restore(saved);}
                }
            }
            private static List<KeyValuePair<bool,string>> Runs(string text){var result=new List<KeyValuePair<bool,string>>();foreach(char c in text??""){bool chinese=c>=0x2e80;if(result.Count==0||result[result.Count-1].Key!=chinese)result.Add(new KeyValuePair<bool,string>(chinese,c.ToString()));else{var previous=result[result.Count-1];result[result.Count-1]=new KeyValuePair<bool,string>(chinese,previous.Value+c);}}return result;}
        }
    }
}
