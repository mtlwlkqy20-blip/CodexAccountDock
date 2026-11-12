using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexAccountDock
{
    internal sealed class IpQueryResult
    {
        internal string Ip="";
        internal string CountryCode="";
        internal string Country="";
        internal string Region="";
        internal string City="";
        internal string Isp="";
        internal string Asn="";
        internal string NetworkType="";
        internal string Route="";
        internal string FetchedAt="";
        internal string Warning="";
        internal string RiskCidr="";
        internal int? TrustScore;
        internal bool? IsVpn;
        internal bool? IsProxy;
        internal bool? IsTor;
        internal bool? IsCrawler;
        internal bool? IsAbuser;
        internal bool? IsDatacenter;
        internal bool? IsResidential;
        internal bool GeoAvailable;
        internal bool RiskAvailable;
    }

    // Manual, stateless lookup for the route used to reach chatgpt.com.  It
    // does not receive account data, inspect browser state, or persist results.
    internal static class IpQueryClient
    {
        private const int MaximumResponseBytes=1024*1024;
        private static readonly TimeSpan OverallTimeout=TimeSpan.FromSeconds(28);
        private static readonly Uri TraceEndpoint=new Uri("https://chatgpt.com/cdn-cgi/trace");
        private static readonly Uri ApiRoot=new Uri("https://ip.net.coffee/");
        private static readonly UTF8Encoding StrictUtf8=new UTF8Encoding(false,true);

        private sealed class GeoData
        {
            internal string CountryCode="";
            internal string Country="";
            internal string Region="";
            internal string City="";
            internal string Isp="";
        }

        private sealed class RiskData
        {
            internal string CountryCode="";
            internal string Country="";
            internal string Region="";
            internal string City="";
            internal string Organization="";
            internal string Asn="";
            internal string Cidr="";
            internal int? TrustScore;
            internal bool? IsVpn;
            internal bool? IsProxy;
            internal bool? IsTor;
            internal bool? IsCrawler;
            internal bool? IsAbuser;
            internal bool? IsDatacenter;
            internal bool? IsResidential;
        }

        private sealed class DetailOutcome<T>
        {
            internal T Value;
            internal string Warning="";
        }

        internal static async Task<IpQueryResult> FetchAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string route;
            IWebProxy proxy=PublicFeedTransport.FindUsableProxy(TraceEndpoint,out route);
            if(string.IsNullOrEmpty(route))route="direct";

            using(var overall=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                overall.CancelAfter(OverallTimeout);
                using(var transport=new HttpTransport(proxy))
                {
                    try
                    {
                        return await FetchCoreAsync(transport.GetAsync,route,overall.Token).ConfigureAwait(false);
                    }
                    catch(OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        if(overall.IsCancellationRequested)throw new TimeoutException("IP 查询超时，请检查网络后重试");
                        throw;
                    }
                }
            }
        }

        private static async Task<IpQueryResult> FetchCoreAsync(Func<Uri,CancellationToken,Task<string>> getAsync,string route,CancellationToken token)
        {
            string trace=await getAsync(TraceEndpoint,token).ConfigureAwait(false);
            string loc;
            IPAddress address=ParseTrace(trace,out loc);
            string ip=address.ToString();
            string escaped=Uri.EscapeDataString(ip);
            Uri geoEndpoint=new Uri(ApiRoot,"api/geoip/"+escaped);
            Uri riskEndpoint=new Uri(ApiRoot,"api/iprisk/"+escaped);

            Task<DetailOutcome<GeoData>> geoTask=FetchGeoAsync(getAsync,geoEndpoint,token);
            Task<DetailOutcome<RiskData>> riskTask=FetchRiskAsync(getAsync,riskEndpoint,address,token);
            await Task.WhenAll(geoTask,riskTask).ConfigureAwait(false);

            DetailOutcome<GeoData> geo=geoTask.Result;
            DetailOutcome<RiskData> risk=riskTask.Result;
            var result=new IpQueryResult
            {
                Ip=ip,
                Route=route,
                FetchedAt=DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz",CultureInfo.InvariantCulture)
            };

            if(geo.Value!=null)
            {
                result.GeoAvailable=true;
                result.CountryCode=geo.Value.CountryCode;
                result.Country=geo.Value.Country;
                result.Region=geo.Value.Region;
                result.City=geo.Value.City;
                result.Isp=geo.Value.Isp;
            }
            else if(!string.IsNullOrEmpty(loc))result.CountryCode=loc;

            if(risk.Value!=null)
            {
                result.RiskAvailable=true;
                result.Asn=risk.Value.Asn;
                result.RiskCidr=risk.Value.Cidr;
                result.TrustScore=risk.Value.TrustScore;
                result.IsVpn=risk.Value.IsVpn;
                result.IsProxy=risk.Value.IsProxy;
                result.IsTor=risk.Value.IsTor;
                result.IsCrawler=risk.Value.IsCrawler;
                result.IsAbuser=risk.Value.IsAbuser;
                result.IsDatacenter=risk.Value.IsDatacenter;
                result.IsResidential=risk.Value.IsResidential;
                if(string.IsNullOrEmpty(result.CountryCode))result.CountryCode=risk.Value.CountryCode;
                if(string.IsNullOrEmpty(result.Country))result.Country=risk.Value.Country;
                if(string.IsNullOrEmpty(result.Region))result.Region=risk.Value.Region;
                if(string.IsNullOrEmpty(result.City))result.City=risk.Value.City;
                if(string.IsNullOrEmpty(result.Isp))result.Isp=risk.Value.Organization;
                if(risk.Value.IsResidential==true)result.NetworkType="住宅网络";
                else if(risk.Value.IsDatacenter==true)result.NetworkType="数据中心";
            }

            if(!string.IsNullOrEmpty(result.CountryCode))result.CountryCode=result.CountryCode.ToUpperInvariant();
            var warnings=new List<string>();
            if(!string.IsNullOrEmpty(geo.Warning))warnings.Add(geo.Warning);
            if(!string.IsNullOrEmpty(risk.Warning))warnings.Add(risk.Warning);
            result.Warning=string.Join("；",warnings.ToArray());
            return result;
        }

        private static async Task<DetailOutcome<GeoData>> FetchGeoAsync(Func<Uri,CancellationToken,Task<string>> getAsync,Uri endpoint,CancellationToken token)
        {
            try
            {
                string json=await getAsync(endpoint,token).ConfigureAwait(false);
                return new DetailOutcome<GeoData> {Value=ParseGeo(json)};
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex)
            {
                return new DetailOutcome<GeoData> {Warning="地区信息暂不可用（"+SafeKind(ex)+"）"};
            }
        }

        private static async Task<DetailOutcome<RiskData>> FetchRiskAsync(Func<Uri,CancellationToken,Task<string>> getAsync,Uri endpoint,IPAddress expectedIp,CancellationToken token)
        {
            try
            {
                string json=await getAsync(endpoint,token).ConfigureAwait(false);
                return new DetailOutcome<RiskData> {Value=ParseRisk(json,expectedIp)};
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex)
            {
                if(ex is FormatException && ex.Message=="风险信息 IP 不匹配")
                    return new DetailOutcome<RiskData> {Warning="风险服务返回的 IP 不一致，已忽略该风险结果"};
                return new DetailOutcome<RiskData> {Warning="风险信息暂不可用（"+SafeKind(ex)+"）"};
            }
        }

        private static string SafeKind(Exception ex)
        {
            if(ex is TimeoutException || ex is TaskCanceledException)return "超时";
            if(ex is HttpRequestException || ex is WebException || ex is IOException)return "网络错误";
            return "数据无效";
        }

        private sealed class HttpTransport:IDisposable
        {
            private readonly HttpClient client;
            private int requests;

            internal HttpTransport(IWebProxy proxy)
            {
                ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
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
                client=new HttpClient(handler) {Timeout=Timeout.InfiniteTimeSpan,MaxResponseContentBufferSize=MaximumResponseBytes};
            }

            internal async Task<string> GetAsync(Uri endpoint,CancellationToken token)
            {
                ValidateEndpoint(endpoint);
                if(Interlocked.Increment(ref requests)>3)throw new InvalidOperationException("IP 查询请求次数超出限制");
                token.ThrowIfCancellationRequested();
                // A server can stall after headers. Bound the complete request,
                // including stream reads, without changing network routes.
                using(var requestLife=CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    Task<string> pending=Task.Run(delegate{return SendAsync(endpoint,requestLife.Token);},requestLife.Token);
                    return await AwaitBounded(pending,requestLife,token,TimeSpan.FromSeconds(11)).ConfigureAwait(false);
                }
            }

            private async Task<string> SendAsync(Uri endpoint,CancellationToken token)
            {
                using(var request=new HttpRequestMessage(HttpMethod.Get,endpoint))
                {
                    request.Headers.Accept.ParseAdd(endpoint==TraceEndpoint?"text/plain":"application/json");
                    request.Headers.CacheControl=new System.Net.Http.Headers.CacheControlHeaderValue {NoCache=true,NoStore=true};
                    using(HttpResponseMessage response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
                    using(CancellationTokenRegistration responseAbort=token.Register(delegate { try{response.Dispose();}catch{} }))
                    {
                        int status=(int)response.StatusCode;
                        if(status<200 || status>299)throw new HttpRequestException("IP 查询服务返回 HTTP "+status.ToString(CultureInfo.InvariantCulture));
                        if(response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value>MaximumResponseBytes)
                            throw new InvalidDataException("IP 查询响应过大");

                        using(Stream stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using(CancellationTokenRegistration streamAbort=token.Register(delegate { try{stream.Dispose();}catch{} }))
                        using(var buffer=new MemoryStream())
                        {
                            byte[] chunk=new byte[8192];
                            int read;
                            while((read=await stream.ReadAsync(chunk,0,chunk.Length,token).ConfigureAwait(false))>0)
                            {
                                if(buffer.Length+read>MaximumResponseBytes)throw new InvalidDataException("IP 查询响应过大");
                                buffer.Write(chunk,0,read);
                            }
                            token.ThrowIfCancellationRequested();
                            return StrictUtf8.GetString(buffer.ToArray());
                        }
                    }
                }
            }

            public void Dispose(){client.Dispose();}
        }

        private static async Task<string> AwaitBounded(Task<string> pending,CancellationTokenSource requestLife,CancellationToken caller,TimeSpan timeout)
        {
            using(var delayLife=CancellationTokenSource.CreateLinkedTokenSource(caller))
            {
                Task deadline=Task.Delay(timeout,delayLife.Token);
                if(await Task.WhenAny(pending,deadline).ConfigureAwait(false)==pending)
                {
                    delayLife.Cancel();
                    return await pending.ConfigureAwait(false);
                }
                requestLife.Cancel();
                pending.ContinueWith(task=>{var ignored=task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
                caller.ThrowIfCancellationRequested();
                throw new TimeoutException("IP 查询响应超时");
            }
        }

        private static void ValidateEndpoint(Uri endpoint)
        {
            if(endpoint==null || endpoint.Scheme!=Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo) || endpoint.Port!=443)
                throw new InvalidOperationException("IP 查询地址无效");
            if(endpoint==TraceEndpoint)return;
            if(!endpoint.Host.Equals("ip.net.coffee",StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
                throw new InvalidOperationException("IP 查询地址不在允许列表");
            string path=Uri.UnescapeDataString(endpoint.AbsolutePath);
            string value;
            if(path.StartsWith("/api/geoip/",StringComparison.Ordinal))value=path.Substring(11);
            else if(path.StartsWith("/api/iprisk/",StringComparison.Ordinal))value=path.Substring(12);
            else throw new InvalidOperationException("IP 查询地址不在允许列表");
            IPAddress parsed;
            if(!IPAddress.TryParse(value,out parsed) || !IsPublicAddress(parsed))throw new InvalidOperationException("IP 查询地址包含无效 IP");
        }

        private static IPAddress ParseTrace(string text,out string location)
        {
            if(string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text)>MaximumResponseBytes)throw new FormatException("ChatGPT 路由响应无效");
            string ipValue="";
            string locValue="";
            foreach(string raw in text.Replace("\r\n","\n").Split('\n'))
            {
                if(raw.Length==0)continue;
                int split=raw.IndexOf('=');
                if(split<=0)continue;
                string key=raw.Substring(0,split).Trim();
                string value=raw.Substring(split+1).Trim();
                if(key.Equals("ip",StringComparison.OrdinalIgnoreCase))
                {
                    if(ipValue.Length>0 && !ipValue.Equals(value,StringComparison.OrdinalIgnoreCase))throw new FormatException("ChatGPT 路由返回多个 IP");
                    ipValue=value;
                }
                else if(key.Equals("loc",StringComparison.OrdinalIgnoreCase))locValue=value;
            }
            IPAddress address;
            if(!IPAddress.TryParse(ipValue,out address) || !IsPublicAddress(address))throw new FormatException("ChatGPT 路由 IP 无效");
            location=NormalizeCountryCode(locValue);
            return address;
        }

        private static bool IsPublicAddress(IPAddress address)
        {
            if(address==null || IPAddress.IsLoopback(address))return false;
            byte[] bytes=address.GetAddressBytes();
            if(address.AddressFamily==AddressFamily.InterNetwork)
            {
                int a=bytes[0],b=bytes[1],c=bytes[2];
                if(a==0 || a==10 || a==127 || a>=224)return false;
                if(a==100 && b>=64 && b<=127)return false;
                if(a==169 && b==254)return false;
                if(a==172 && b>=16 && b<=31)return false;
                if(a==192 && (b==168 || (b==0 && c<=2)))return false;
                if(a==198 && (b==18 || b==19 || (b==51 && c==100)))return false;
                if(a==203 && b==0 && c==113)return false;
                return true;
            }
            if(address.AddressFamily!=AddressFamily.InterNetworkV6)return false;
            if(address.IsIPv4MappedToIPv6)return IsPublicAddress(address.MapToIPv4());
            if(address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6Loopback))return false;
            if((bytes[0]&0xFE)==0xFC || (bytes[0]==0xFE && (bytes[1]&0xC0)==0x80) || bytes[0]==0xFF)return false;
            if(bytes[0]==0x20 && bytes[1]==0x01 && bytes[2]==0x0D && bytes[3]==0xB8)return false;
            return true;
        }

        private static GeoData ParseGeo(string json)
        {
            Dictionary<string,object> root=ParseObject(json);
            var value=new GeoData
            {
                Country=ReadString(root,"country"),
                Region=ReadString(root,"region"),
                City=ReadString(root,"city"),
                Isp=ReadString(root,"isp"),
                CountryCode=NormalizeCountryCode(ReadString(root,"country_code"))
            };
            if(value.Country.Length==0 && value.Region.Length==0 && value.City.Length==0 && value.Isp.Length==0 && value.CountryCode.Length==0)
                throw new FormatException("地区信息为空");
            return value;
        }

        private static RiskData ParseRisk(string json,IPAddress expectedIp)
        {
            Dictionary<string,object> root=ParseObject(json);
            bool recognized=false;
            string responseIp=ReadString(root,"ip",ref recognized);
            string cidr=ReadString(root,"cidr",ref recognized);
            if(responseIp.Length>0)
            {
                IPAddress parsed;
                if(!IPAddress.TryParse(responseIp,out parsed) || !IsPublicAddress(parsed))throw new FormatException("风险信息 IP 不匹配");
                if(!parsed.Equals(expectedIp) && !SameRiskSubnet(cidr,expectedIp,parsed))throw new FormatException("风险信息 IP 不匹配");
            }
            if(cidr.Length>0 && !SameRiskSubnet(cidr,expectedIp,expectedIp))throw new FormatException("风险信息 IP 不匹配");
            var value=new RiskData
            {
                Cidr=cidr,
                Asn=ReadNumberString(root,"asn",ref recognized),
                Organization=ReadString(root,"asOrganization",ref recognized),
                Country=ReadString(root,"country",ref recognized),
                CountryCode=NormalizeCountryCode(ReadString(root,"countryCode",ref recognized)),
                Region=ReadString(root,"region",ref recognized),
                City=ReadString(root,"city",ref recognized),
                TrustScore=ReadTrustScore(root,"trust_score",ref recognized),
                IsDatacenter=ReadNullableBool(root,"is_datacenter",ref recognized),
                IsResidential=ReadNullableBool(root,"isResidential",ref recognized),
                IsVpn=ReadNullableBool(root,"is_vpn",ref recognized),
                IsProxy=ReadNullableBool(root,"is_proxy",ref recognized),
                IsTor=ReadNullableBool(root,"is_tor",ref recognized),
                IsCrawler=ReadNullableBool(root,"is_crawler",ref recognized),
                IsAbuser=ReadNullableBool(root,"is_abuser",ref recognized)
            };
            if(!recognized)throw new FormatException("风险信息为空");
            return value;
        }

        // The service caches reputation by CIDR and may return another host in
        // that CIDR as its representative IP. Accept it only with a canonical,
        // public subnet proving that both IPs belong to that exact scope.
        private static bool SameRiskSubnet(string cidr,IPAddress expected,IPAddress reported)
        {
            string[] parts=(cidr??"").Split('/');IPAddress network;int prefix;
            if(parts.Length!=2 || !IPAddress.TryParse(parts[0],out network) || !IsPublicAddress(network) ||
                !int.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out prefix))return false;
            if(network.AddressFamily!=expected.AddressFamily || network.AddressFamily!=reported.AddressFamily)return false;
            byte[] net=network.GetAddressBytes(),a=expected.GetAddressBytes(),b=reported.GetAddressBytes();
            if(prefix<(net.Length==4?8:16) || prefix>net.Length*8)return false;
            for(int i=0;i<net.Length;i++)
            {
                int bits=Math.Max(0,Math.Min(8,prefix-i*8));int mask=bits==0?0:(255<<(8-bits))&255;
                if((net[i]&mask)!=net[i] || (a[i]&mask)!=net[i] || (b[i]&mask)!=net[i])return false;
            }
            return true;
        }

        private static Dictionary<string,object> ParseObject(string json)
        {
            if(string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json)>MaximumResponseBytes)throw new FormatException("IP 查询数据为空或过大");
            try
            {
                var serializer=new JavaScriptSerializer {MaxJsonLength=MaximumResponseBytes,RecursionLimit=20};
                Dictionary<string,object> root=serializer.Deserialize<Dictionary<string,object>>(json);
                if(root==null)throw new FormatException("IP 查询数据不是对象");
                return root;
            }
            catch(InvalidOperationException ex){throw new FormatException("IP 查询 JSON 无效",ex);}
            catch(ArgumentException ex){throw new FormatException("IP 查询 JSON 无效",ex);}
        }

        private static string ReadString(Dictionary<string,object> root,string name)
        {
            bool ignored=false;
            return ReadString(root,name,ref ignored);
        }

        private static string ReadString(Dictionary<string,object> root,string name,ref bool recognized)
        {
            object raw;
            if(!root.TryGetValue(name,out raw))return "";
            recognized=true;
            if(raw==null)return "";
            string value=raw as string;
            if(value==null)throw new FormatException(name+" 必须是字符串");
            value=value.Trim();
            if(value.Length>512)throw new FormatException(name+" 过长");
            return value;
        }

        private static string ReadNumberString(Dictionary<string,object> root,string name,ref bool recognized)
        {
            object raw;
            if(!root.TryGetValue(name,out raw))return "";
            recognized=true;
            if(raw==null)return "";
            double number;
            if(!TryJsonNumber(raw,out number) || number<0 || number>4294967295d || number!=Math.Truncate(number))throw new FormatException(name+" 必须是有效整数");
            return number.ToString("0",CultureInfo.InvariantCulture);
        }

        private static int? ReadTrustScore(Dictionary<string,object> root,string name,ref bool recognized)
        {
            object raw;
            if(!root.TryGetValue(name,out raw))return null;
            recognized=true;
            if(raw==null)return null;
            double number;
            if(!TryJsonNumber(raw,out number) || number<0 || number>100 || number!=Math.Truncate(number))throw new FormatException("trust_score 必须是 0 到 100 的整数");
            return (int)number;
        }

        private static bool? ReadNullableBool(Dictionary<string,object> root,string name,ref bool recognized)
        {
            object raw;
            if(!root.TryGetValue(name,out raw))return null;
            recognized=true;
            if(raw==null)return null;
            if(!(raw is bool))throw new FormatException(name+" 必须是布尔值");
            return (bool)raw;
        }

        private static bool TryJsonNumber(object raw,out double value)
        {
            value=0;
            if(raw is bool || raw is string)return false;
            if(!(raw is byte) && !(raw is sbyte) && !(raw is short) && !(raw is ushort) && !(raw is int) && !(raw is uint) && !(raw is long) && !(raw is ulong) && !(raw is float) && !(raw is double) && !(raw is decimal))return false;
            try{value=Convert.ToDouble(raw,CultureInfo.InvariantCulture);}
            catch(Exception ex){if(ex is InvalidCastException || ex is FormatException || ex is OverflowException)return false;throw;}
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string NormalizeCountryCode(string value)
        {
            if(string.IsNullOrWhiteSpace(value))return "";
            string code=value.Trim().ToUpperInvariant();
            if(code.Length!=2 || code[0]<'A' || code[0]>'Z' || code[1]<'A' || code[1]>'Z')throw new FormatException("国家代码无效");
            return code;
        }

        internal static int RunDiagnostic()
        {
            try
            {
                int requests=0;
                Func<Uri,CancellationToken,Task<string>> good=delegate(Uri uri,CancellationToken token)
                {
                    token.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref requests);
                    if(uri==TraceEndpoint)return Task.FromResult("fl=1\nip=8.8.8.8\nloc=US\n");
                    if(uri.AbsolutePath.StartsWith("/api/geoip/",StringComparison.Ordinal))return Task.FromResult("{\"country\":\"United States\",\"region\":\"California\",\"city\":\"Mountain View\",\"isp\":\"Example ISP\",\"country_code\":\"US\"}");
                    if(uri.AbsolutePath.StartsWith("/api/iprisk/",StringComparison.Ordinal))return Task.FromResult("{\"ip\":\"8.8.8.8\",\"asn\":15169,\"asOrganization\":\"Example Org\",\"country\":\"United States\",\"countryCode\":\"US\",\"region\":\"California\",\"city\":\"Mountain View\",\"trust_score\":0,\"is_datacenter\":false,\"isResidential\":null,\"is_vpn\":false,\"is_proxy\":false,\"is_tor\":false,\"is_abuser\":false}");
                    throw new InvalidOperationException("unexpected endpoint");
                };
                IpQueryResult result=FetchCoreAsync(good,"system",CancellationToken.None).GetAwaiter().GetResult();
                Require(requests==3,"request budget");
                Require(result.Ip=="8.8.8.8" && result.Route=="system","trace parse");
                Require(result.GeoAvailable && result.RiskAvailable,"availability");
                Require(result.TrustScore==0 && result.IsResidential==null && result.IsVpn==false,"unknown/false/zero distinction");
                Require(result.Asn=="15169" && result.CountryCode=="US","detail parse");

                Func<Uri,CancellationToken,Task<string>> partial=delegate(Uri uri,CancellationToken token)
                {
                    if(uri==TraceEndpoint)return Task.FromResult("ip=1.1.1.1\nloc=AU\n");
                    if(uri.AbsolutePath.StartsWith("/api/geoip/",StringComparison.Ordinal))throw new HttpRequestException("offline fake");
                    return Task.FromResult("{\"ip\":\"1.1.1.2\",\"asn\":13335,\"country\":\"Australia\",\"countryCode\":\"AU\",\"trust_score\":80,\"is_vpn\":false,\"cidr\":\"1.1.1.0/24\",\"is_crawler\":false}");
                };
                result=FetchCoreAsync(partial,"direct",CancellationToken.None).GetAwaiter().GetResult();
                Require(!result.GeoAvailable && result.RiskAvailable && result.Ip=="1.1.1.1","partial result");
                Require(result.Warning.IndexOf("地区信息",StringComparison.Ordinal)>=0,"partial warning");
                Require(result.RiskCidr=="1.1.1.0/24" && result.IsCrawler==false,"CIDR and crawler result mapping");

                ExpectFailure(delegate { ParseTrace("ip=127.0.0.1\nloc=US",out result.Warning); },"private trace");
                ExpectFailure(delegate { ParseTrace("ip=not-an-ip\nloc=US",out result.Warning); },"invalid trace");
                ExpectFailure(delegate { ParseRisk("{\"ip\":\"8.8.8.8\",\"trust_score\":101}",IPAddress.Parse("8.8.8.8")); },"trust range");
                ExpectFailure(delegate { ParseRisk("{\"ip\":\"8.8.8.8\",\"is_vpn\":\"false\"}",IPAddress.Parse("8.8.8.8")); },"boolean string");
                ExpectFailure(delegate { ParseGeo("not-json"); },"invalid json");
                string code;
                Require(ParseTrace("ip=2606:4700:4700::1111\nloc=US",out code).AddressFamily==AddressFamily.InterNetworkV6,"IPv6 trace");
                ExpectFailure(delegate { ParseRisk("{\"ip\":\"1.1.1.1\"}",IPAddress.Parse("8.8.8.8")); },"mismatched IP");
                RiskData networkRisk=ParseRisk("{\"ip\":\"1.1.1.2\",\"cidr\":\"1.1.1.0/24\",\"trust_score\":100,\"is_crawler\":false}",IPAddress.Parse("1.1.1.1"));
                Require(networkRisk.TrustScore==100 && networkRisk.Cidr=="1.1.1.0/24" && networkRisk.IsCrawler==false,"CIDR representative score");
                ExpectFailure(delegate { ParseRisk("{\"ip\":\"1.1.2.2\",\"cidr\":\"1.1.1.0/24\",\"trust_score\":100}",IPAddress.Parse("1.1.1.1")); },"representative outside CIDR");
                ExpectFailure(delegate { ParseRisk("{\"ip\":\"1.1.1.2\",\"cidr\":\"1.1.1.0/24\",\"trust_score\":100}",IPAddress.Parse("8.8.8.8")); },"query outside CIDR");
                Require(!SameRiskSubnet("0.0.0.0/0",IPAddress.Parse("1.1.1.1"),IPAddress.Parse("8.8.8.8")),"global range rejected");
                Require(!SameRiskSubnet("1.1.1.2/24",IPAddress.Parse("1.1.1.1"),IPAddress.Parse("1.1.1.2")),"noncanonical range rejected");
                Require(!SameRiskSubnet("1.1.1.0/33",IPAddress.Parse("1.1.1.1"),IPAddress.Parse("1.1.1.2")),"invalid prefix rejected");
                Require(SameRiskSubnet("2606:4700:4700::/48",IPAddress.Parse("2606:4700:4700::1111"),IPAddress.Parse("2606:4700:4700::1001")),"IPv6 CIDR scope");
                using(var requestLife=new CancellationTokenSource())
                {
                    bool timedOut=false;
                    try{AwaitBounded(new TaskCompletionSource<string>().Task,requestLife,CancellationToken.None,TimeSpan.FromMilliseconds(30)).GetAwaiter().GetResult();}
                    catch(TimeoutException){timedOut=true;}
                    Require(timedOut && requestLife.IsCancellationRequested,"stalled request hard deadline");
                }
                using(var caller=new CancellationTokenSource())
                using(var requestLife=CancellationTokenSource.CreateLinkedTokenSource(caller.Token))
                {
                    caller.Cancel();bool cancelled=false;
                    try{AwaitBounded(new TaskCompletionSource<string>().Task,requestLife,caller.Token,TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();}
                    catch(OperationCanceledException){cancelled=true;}
                    Require(cancelled,"caller cancellation");
                }
                Console.WriteLine("IP_QUERY_TEST PASS requests=3 partial=PASS validation=PASS ipv6 mismatch cidr_scope crawler hard_deadline cancellation no_network no_credentials");
                return 0;
            }
            catch(Exception ex)
            {
                Console.WriteLine("IP_QUERY_TEST FAIL kind="+ex.GetType().Name+" detail="+ex.Message);
                return 1;
            }
        }

        internal static int RunLiveDiagnostic()
        {
            try
            {
                IpQueryResult result=FetchAsync(CancellationToken.None).GetAwaiter().GetResult();
                IPAddress address;
                if(!IPAddress.TryParse(result.Ip,out address))throw new FormatException("live IP missing");
                int knownFlags=0;
                if(result.IsVpn.HasValue)knownFlags++;
                if(result.IsProxy.HasValue)knownFlags++;
                if(result.IsTor.HasValue)knownFlags++;
                if(result.IsCrawler.HasValue)knownFlags++;
                if(result.IsAbuser.HasValue)knownFlags++;
                if(result.IsDatacenter.HasValue)knownFlags++;
                if(result.IsResidential.HasValue)knownFlags++;
                int details=0;
                if(result.CountryCode.Length>0)details++;
                if(result.Country.Length>0)details++;
                if(result.Region.Length>0)details++;
                if(result.City.Length>0)details++;
                if(result.Isp.Length>0)details++;
                if(result.Asn.Length>0)details++;
                Console.WriteLine("IP_QUERY_LIVE family="+(address.AddressFamily==AddressFamily.InterNetworkV6?"IPv6":"IPv4")+" geo="+result.GeoAvailable+" risk="+result.RiskAvailable+" trust="+result.TrustScore.HasValue+" flags_known="+knownFlags+" details="+details+" warning="+(result.Warning.Length>0)+" route="+result.Route);
                Console.WriteLine("IP_QUERY_LIVE validated_cidr="+(result.RiskCidr.Length>0));
                if(result.Warning.Length>0)Console.WriteLine("IP_QUERY_LIVE partial_reason="+(result.Warning.IndexOf("IP 不一致",StringComparison.Ordinal)>=0?"risk_ip_mismatch":result.Warning.IndexOf("超时",StringComparison.Ordinal)>=0?"timeout":result.Warning.IndexOf("网络错误",StringComparison.Ordinal)>=0?"network_error":"invalid_data"));
                return 0;
            }
            catch(Exception ex)
            {
                Console.WriteLine("IP_QUERY_LIVE FAIL kind="+ex.GetType().Name);
                return 1;
            }
        }

        private static void Require(bool condition,string name)
        {
            if(!condition)throw new InvalidOperationException("diagnostic failed: "+name);
        }

        private static void ExpectFailure(Action action,string name)
        {
            bool failed=false;
            try{action();}
            catch(FormatException){failed=true;}
            if(!failed)throw new InvalidOperationException("diagnostic accepted "+name);
        }
    }
}
