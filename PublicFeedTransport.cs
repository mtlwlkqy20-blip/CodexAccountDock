using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CodexAccountDock
{
    // Bounded transport shared by the two public, manually refreshed feeds.
    // It never receives account credentials and never writes a response to disk.
    internal static class PublicFeedTransport
    {
        private const int MaximumResponseBytes=2*1024*1024;
        private const int MaximumRoundBytes=6*1024*1024;
        private const int MaximumRequests=3;
        private static readonly TimeSpan OverallTimeout=TimeSpan.FromSeconds(95);

        private sealed class Attempt
        {
            internal IWebProxy Proxy;
            internal TimeSpan Timeout;
            internal string Route;
            internal bool UseNative;
        }

        private sealed class RoundBudget
        {
            internal int Requests;
            internal long Bytes;

            internal void BeginRequest()
            {
                if(++Requests>MaximumRequests)throw new InvalidOperationException("公开数据请求次数超出限制");
            }

            internal void AddBytes(int count)
            {
                Bytes+=count;
                if(Bytes>MaximumRoundBytes)throw new InvalidDataException("公开数据本轮响应过大");
            }
        }

        private sealed class RetryableFeedException:Exception
        {
            internal RetryableFeedException(string message):base(message){}
            internal RetryableFeedException(string message,Exception inner):base(message,inner){}
        }

        private sealed class FeedHttpException:Exception
        {
            internal FeedHttpException(string message):base(message){}
        }

        internal static async Task<string> FetchJsonAsync(string url,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Uri endpoint;
            if(!Uri.TryCreate(url,UriKind.Absolute,out endpoint) || endpoint.Scheme!=Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo))
                throw new InvalidOperationException("公开数据地址无效");

            string proxyRoute;
            IWebProxy proxy=FindUsableProxy(endpoint,out proxyRoute);
            bool nativeAvailable=NativeFeedHttp.IsAvailable;
            var attempts=new List<Attempt>
            {
                new Attempt {Proxy=null,Timeout=TimeSpan.FromSeconds(proxy==null?(nativeAvailable?45:80):8),Route="direct",UseNative=nativeAvailable}
            };
            if(proxy!=null)
            {
                attempts.Add(new Attempt {Proxy=proxy,Timeout=TimeSpan.FromSeconds(45),Route=proxyRoute,UseNative=nativeAvailable && proxy.Credentials==null});
                attempts.Add(new Attempt {Proxy=proxy,Timeout=TimeSpan.FromSeconds(35),Route=proxyRoute});
            }
            else if(nativeAvailable)attempts.Add(new Attempt {Proxy=null,Timeout=TimeSpan.FromSeconds(35),Route="direct"});

            using(var overall=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                overall.CancelAfter(OverallTimeout);
                return await ExecuteRoundAsync(endpoint,attempts,overall,token,SendAttemptAsync).ConfigureAwait(false);
            }
        }

        private static async Task<string> ExecuteRoundAsync(Uri endpoint,List<Attempt> attempts,CancellationTokenSource overall,CancellationToken caller,Func<Uri,Attempt,RoundBudget,CancellationToken,Task<string>> operation)
        {
            var budget=new RoundBudget();
            for(int index=0;index<attempts.Count;index++)
            {
                caller.ThrowIfCancellationRequested();
                if(overall.IsCancellationRequested)throw new TimeoutException("公开数据刷新总超时");
                try
                {
                    if(index>0)await Task.Delay(1000,overall.Token).ConfigureAwait(false);
                    budget.BeginRequest();
                    return await operation(endpoint,attempts[index],budget,overall.Token).ConfigureAwait(false);
                }
                catch(OperationCanceledException)
                {
                    caller.ThrowIfCancellationRequested();
                    if(overall.IsCancellationRequested)throw new TimeoutException("公开数据刷新总超时");
                    throw;
                }
                catch(Exception ex)
                {
                    Console.WriteLine("PUBLIC_FEED failed_route="+attempts[index].Route+" attempt="+budget.Requests+" kind="+ex.GetType().Name);
                    if(!IsRetryable(ex))throw;
                    if(index+1>=attempts.Count)throw new HttpRequestException("公开数据连接失败，请检查网络后重试",ex);
                }
            }
            throw new InvalidOperationException("公开数据刷新未完成");
        }

        private static async Task<string> SendAttemptAsync(Uri endpoint,Attempt attempt,RoundBudget budget,CancellationToken roundToken)
        {
            if(attempt.UseNative)
            {
                try
                {
                    string json=await NativeFeedHttp.FetchAsync(endpoint,attempt.Proxy,attempt.Timeout,budget.AddBytes,roundToken).ConfigureAwait(false);
                    Console.WriteLine("PUBLIC_FEED route="+attempt.Route+" attempt="+budget.Requests+" engine=windows");
                    return json;
                }
                catch(NativeFeedRejectedException ex){throw new FeedHttpException(ex.Message);}
                catch(HttpRequestException ex){throw new RetryableFeedException("公开数据连接失败",ex);}
                catch(TimeoutException ex){throw new RetryableFeedException("公开数据连接超时",ex);}
            }
            using(var attemptLifetime=CancellationTokenSource.CreateLinkedTokenSource(roundToken))
            {
                attemptLifetime.CancelAfter(attempt.Timeout);
                CancellationToken requestToken=attemptLifetime.Token;
                try
                {
                    ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
                    using(var handler=CreateHandler(attempt.Proxy))
                    using(var client=new HttpClient(handler) {Timeout=Timeout.InfiniteTimeSpan})
                    using(var request=new HttpRequestMessage(HttpMethod.Get,endpoint))
                    {
                        request.Headers.Accept.ParseAdd("application/json");
                        using(HttpResponseMessage response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,requestToken).ConfigureAwait(false))
                        using(CancellationTokenRegistration responseAbort=requestToken.Register(delegate { try{response.Dispose();}catch{} }))
                        {
                            int status=(int)response.StatusCode;
                            if(status>=500 && status<=599)throw new RetryableFeedException("公开数据服务暂时不可用");
                            if(status<200 || status>299)throw new FeedHttpException("公开数据请求未获允许（HTTP "+status+"）");
                            if(response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value>MaximumResponseBytes)
                                throw new InvalidDataException("公开数据响应过大");

                            using(Stream stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using(CancellationTokenRegistration streamAbort=requestToken.Register(delegate { try{stream.Dispose();}catch{} }))
                            using(var buffer=new MemoryStream())
                            {
                                byte[] chunk=new byte[16384];
                                int read;
                                while((read=await stream.ReadAsync(chunk,0,chunk.Length,requestToken).ConfigureAwait(false))>0)
                                {
                                    if(buffer.Length+read>MaximumResponseBytes)throw new InvalidDataException("公开数据响应过大");
                                    budget.AddBytes(read);
                                    buffer.Write(chunk,0,read);
                                }
                                requestToken.ThrowIfCancellationRequested();
                                string json=Encoding.UTF8.GetString(buffer.ToArray());
                                Console.WriteLine("PUBLIC_FEED route="+attempt.Route+" attempt="+budget.Requests);
                                return json;
                            }
                        }
                    }
                }
                catch(OperationCanceledException ex)
                {
                    roundToken.ThrowIfCancellationRequested();
                    throw new RetryableFeedException("公开数据连接超时",ex);
                }
                catch(HttpRequestException ex){throw new RetryableFeedException("公开数据连接失败",ex);}
                catch(WebException ex){throw new RetryableFeedException("公开数据连接失败",ex);}
                catch(IOException ex)
                {
                    if(requestToken.IsCancellationRequested)
                    {
                        roundToken.ThrowIfCancellationRequested();
                        throw new RetryableFeedException("公开数据连接超时",ex);
                    }
                    throw new RetryableFeedException("公开数据连接中断",ex);
                }
                catch(ObjectDisposedException ex)
                {
                    if(requestToken.IsCancellationRequested)
                    {
                        roundToken.ThrowIfCancellationRequested();
                        throw new RetryableFeedException("公开数据连接超时",ex);
                    }
                    throw new RetryableFeedException("公开数据连接已中断",ex);
                }
                // .NET Framework can report a remotely closed HTTP stream as
                // InvalidOperationException instead of IOException. Everything
                // inside this try owns its request/stream; retry it within the
                // same bounded network budget, never the parsing or UI work.
                catch(InvalidOperationException ex)
                {
                    roundToken.ThrowIfCancellationRequested();
                    throw new RetryableFeedException("公开数据连接已中断",ex);
                }
            }
        }

        private static HttpClientHandler CreateHandler(IWebProxy proxy)
        {
            var handler=new HttpClientHandler
            {
                UseProxy=proxy!=null,
                UseCookies=false,
                UseDefaultCredentials=false,
                Credentials=null,
                AllowAutoRedirect=false,
                AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate
            };
            if(proxy!=null)handler.Proxy=proxy;
            return handler;
        }

        private static bool IsRetryable(Exception error)
        {
            return error is RetryableFeedException;
        }

        internal static IWebProxy FindUsableProxy(Uri endpoint,out string routeName)
        {
            routeName="";
            IWebProxy proxy=ReadEnabledWinInetProxy();
            if(IsDistinctProxy(proxy,endpoint)){routeName="system";return proxy;}

            // Use explicit proxy settings only: synchronous PAC/WPAD discovery
            // can block the UI and has no reliable cancellation on Framework.
            proxy=ReadEnvironmentProxy();
            if(IsDistinctProxy(proxy,endpoint)){routeName="environment";return proxy;}
            return null;
        }

        private static IWebProxy ReadEnabledWinInetProxy()
        {
            try
            {
                using(RegistryKey key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings",false))
                {
                    if(key==null || Convert.ToInt32(key.GetValue("ProxyEnable",0))!=1)return null;
                    return CreateProxy(Convert.ToString(key.GetValue("ProxyServer","")));
                }
            }
            catch(Exception ex)
            {
                if(ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException || ex is FormatException || ex is OverflowException)return null;
                throw;
            }
        }

        private static IWebProxy ReadEnvironmentProxy()
        {
            string value=Environment.GetEnvironmentVariable("HTTPS_PROXY");
            if(string.IsNullOrWhiteSpace(value))value=Environment.GetEnvironmentVariable("https_proxy");
            if(string.IsNullOrWhiteSpace(value))value=Environment.GetEnvironmentVariable("HTTP_PROXY");
            if(string.IsNullOrWhiteSpace(value))value=Environment.GetEnvironmentVariable("http_proxy");
            return CreateProxy(value);
        }

        private static IWebProxy CreateProxy(string setting)
        {
            if(string.IsNullOrWhiteSpace(setting))return null;
            string selected=setting.Trim();
            if(selected.IndexOf(';')>=0 || selected.IndexOf('=')>=0)
            {
                string https="",http="";
                foreach(string raw in selected.Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries))
                {
                    int split=raw.IndexOf('=');
                    if(split<=0)continue;
                    string name=raw.Substring(0,split).Trim();
                    string value=raw.Substring(split+1).Trim();
                    if(name.Equals("https",StringComparison.OrdinalIgnoreCase))https=value;
                    else if(name.Equals("http",StringComparison.OrdinalIgnoreCase))http=value;
                }
                selected=https.Length>0?https:http;
            }
            if(string.IsNullOrWhiteSpace(selected))return null;
            if(selected.IndexOf("://",StringComparison.Ordinal)<0)selected="http://"+selected;
            Uri uri;
            if(!Uri.TryCreate(selected,UriKind.Absolute,out uri) || (uri.Scheme!=Uri.UriSchemeHttp && uri.Scheme!=Uri.UriSchemeHttps) || string.IsNullOrWhiteSpace(uri.Host) || uri.Port<=0)return null;

            NetworkCredential credentials=null;
            if(!string.IsNullOrEmpty(uri.UserInfo))
            {
                string[] parts=uri.UserInfo.Split(new[]{':'},2);
                credentials=new NetworkCredential(Uri.UnescapeDataString(parts[0]),parts.Length>1?Uri.UnescapeDataString(parts[1]):"");
            }
            var clean=new UriBuilder(uri) {UserName="",Password="",Path="",Query="",Fragment=""};
            var proxy=new WebProxy(clean.Uri,false);
            if(credentials!=null)proxy.Credentials=credentials;
            return proxy;
        }

        private static bool IsDistinctProxy(IWebProxy proxy,Uri endpoint)
        {
            if(proxy==null)return false;
            try
            {
                Uri route=proxy.GetProxy(endpoint);
                return route!=null && route.IsAbsoluteUri && !SameEndpoint(route,endpoint);
            }
            catch(Exception ex)
            {
                if(ex is InvalidOperationException || ex is NotSupportedException || ex is WebException || ex is UriFormatException)return false;
                throw;
            }
        }

        private static bool SameEndpoint(Uri left,Uri right)
        {
            return left.Scheme.Equals(right.Scheme,StringComparison.OrdinalIgnoreCase)
                && left.Host.Equals(right.Host,StringComparison.OrdinalIgnoreCase)
                && left.Port==right.Port
                && left.AbsolutePath.Equals(right.AbsolutePath,StringComparison.Ordinal);
        }

        // Offline deterministic checks. No registry, proxy, DNS or HTTP access occurs.
        internal static int SelfTest()
        {
            NativeFeedHttp.SelfTest();
            Uri endpoint=new Uri("https://example.invalid/feed");
            IWebProxy proxy=new WebProxy("http://127.0.0.1:1");
            var attempts=new List<Attempt>
            {
                new Attempt {Timeout=TimeSpan.FromSeconds(1),Route="direct"},
                new Attempt {Proxy=proxy,Timeout=TimeSpan.FromSeconds(1),Route="system"},
                new Attempt {Proxy=proxy,Timeout=TimeSpan.FromSeconds(1),Route="system"}
            };
            int calls=0;
            using(var overall=new CancellationTokenSource())
            {
                string value=ExecuteRoundAsync(endpoint,attempts,overall,CancellationToken.None,delegate(Uri ignored,Attempt route,RoundBudget budget,CancellationToken ct)
                {
                    calls++;
                    if(calls==1)throw new RetryableFeedException("injected");
                    return Task.FromResult("{}");
                }).GetAwaiter().GetResult();
                if(value!="{}" || calls!=2)throw new Exception("Public feed retry plan");
            }

            using(var cancelled=new CancellationTokenSource())
            using(var overall=CancellationTokenSource.CreateLinkedTokenSource(cancelled.Token))
            {
                cancelled.Cancel();
                bool stopped=false;
                try{ExecuteRoundAsync(endpoint,attempts,overall,cancelled.Token,delegate { throw new Exception("must not run"); }).GetAwaiter().GetResult();}
                catch(OperationCanceledException){stopped=true;}
                if(!stopped)throw new Exception("Public feed cancellation");
            }

            calls=0;
            using(var overall=new CancellationTokenSource())
            {
                bool exhausted=false;
                try{ExecuteRoundAsync(endpoint,attempts,overall,CancellationToken.None,delegate {calls++;throw new RetryableFeedException("injected");}).GetAwaiter().GetResult();}
                catch(HttpRequestException){exhausted=true;}
                if(!exhausted || calls!=3)throw new Exception("Public feed retry exhaustion");
            }
            calls=0;
            using(var overall=new CancellationTokenSource())
            {
                bool stopped=false;
                try{ExecuteRoundAsync(endpoint,attempts,overall,CancellationToken.None,delegate {calls++;throw new FeedHttpException("injected 403");}).GetAwaiter().GetResult();}
                catch(FeedHttpException){stopped=true;}
                if(!stopped || calls!=1)throw new Exception("Public feed HTTP rejection must not retry");
            }

            var sampleBudget=new RoundBudget();
            sampleBudget.BeginRequest();sampleBudget.BeginRequest();sampleBudget.BeginRequest();
            bool requestBounded=false;try{sampleBudget.BeginRequest();}catch(InvalidOperationException){requestBounded=true;}
            sampleBudget.AddBytes(MaximumRoundBytes);
            bool byteBounded=false;try{sampleBudget.AddBytes(1);}catch(InvalidDataException){byteBounded=true;}
            if(!requestBounded || !byteBounded)throw new Exception("Public feed budget");
            Console.WriteLine("PUBLIC_FEED_TRANSPORT_OK fail_first_success cancel bounded_exhaustion nonretryable_http request_budget byte_budget no_network");
            return 0;
        }
    }
}
