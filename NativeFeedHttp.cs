using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDock
{
    internal class NativeFeedRejectedException:HttpRequestException
    {
        internal readonly int StatusCode;

        internal NativeFeedRejectedException(int statusCode)
            :base("公开数据请求未获允许（HTTP "+statusCode.ToString(CultureInfo.InvariantCulture)+"）")
        {
            StatusCode=statusCode;
        }
    }

    // Uses only the curl.exe shipped in the Windows system directory.  The
    // feed URL and proxy are supplied through stdin, never through the process
    // command line or a temporary file.
    internal static class NativeFeedHttp
    {
        private const int MaximumBodyBytes=2*1024*1024;
        private const int MaximumFooterBytes=128;
        private const string FooterMarker="\n__CODEX_FEED_HTTP_STATUS__:";
        private static readonly UTF8Encoding StrictUtf8=new UTF8Encoding(false,true);

        internal static bool IsAvailable
        {
            get
            {
                try
                {
                    string path=GetCurlPath();
                    return Path.IsPathRooted(path) && File.Exists(path);
                }
                catch{return false;}
            }
        }

        internal static async Task<string> FetchAsync(Uri endpoint,IWebProxy proxy,TimeSpan timeout,Action<int> accountBytes,CancellationToken token)
        {
            if(endpoint==null)throw new ArgumentNullException("endpoint");
            if(accountBytes==null)throw new ArgumentNullException("accountBytes");
            if(endpoint.Scheme!=Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo))
                throw new InvalidOperationException("公开数据地址无效");
            if(timeout<=TimeSpan.Zero)throw new ArgumentOutOfRangeException("timeout");
            token.ThrowIfCancellationRequested();

            string curlPath=GetCurlPath();
            if(!Path.IsPathRooted(curlPath) || !File.Exists(curlPath))
                throw new HttpRequestException("Windows 网络组件不可用");

            Uri proxyUri=null;
            if(proxy!=null)
            {
                if(proxy.Credentials!=null)throw new NotSupportedException("本机网络组件不接收带凭据的代理");
                try{proxyUri=proxy.GetProxy(endpoint);}
                catch(Exception ex)
                {
                    if(ex is WebException || ex is InvalidOperationException || ex is NotSupportedException || ex is UriFormatException)
                        throw new HttpRequestException("无法读取系统代理",ex);
                    throw;
                }
                if(proxyUri==null || !proxyUri.IsAbsoluteUri || !string.IsNullOrEmpty(proxyUri.UserInfo)
                    || (proxyUri.Scheme!=Uri.UriSchemeHttp && proxyUri.Scheme!=Uri.UriSchemeHttps))
                    throw new NotSupportedException("本机代理类型不受支持");
            }

            string config=BuildConfig(endpoint,proxyUri,timeout);
            var startInfo=new ProcessStartInfo
            {
                FileName=curlPath,
                Arguments="-q --config -",
                UseShellExecute=false,
                CreateNoWindow=true,
                RedirectStandardInput=true,
                RedirectStandardOutput=true,
                RedirectStandardError=true,
                WorkingDirectory=Path.GetDirectoryName(curlPath)
            };
            ClearProxyEnvironment(startInfo);

            using(var lifetime=CancellationTokenSource.CreateLinkedTokenSource(token))
            using(var process=new Process {StartInfo=startInfo,EnableRaisingEvents=true})
            {
                lifetime.CancelAfter(timeout);
                var exited=new TaskCompletionSource<bool>();
                var cancelled=new TaskCompletionSource<bool>();
                process.Exited+=delegate {exited.TrySetResult(true);};
                try
                {
                    try
                    {
                        if(!process.Start())throw new HttpRequestException("Windows 网络组件启动失败");
                    }
                    catch(Exception ex)
                    {
                        if(ex is HttpRequestException)throw;
                        if(ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
                            throw new HttpRequestException("Windows 网络组件启动失败",ex);
                        throw;
                    }

                    if(SafeHasExited(process))exited.TrySetResult(true);
                    var stdout=new AsyncStreamCollector(process.StandardOutput.BaseStream,MaximumBodyBytes+MaximumFooterBytes,delegate {KillOwnedProcess(process);},false);
                    var stderr=new AsyncStreamCollector(process.StandardError.BaseStream,0,delegate {KillOwnedProcess(process);},true);
                    stdout.Start();
                    stderr.Start();

                    using(CancellationTokenRegistration stop=lifetime.Token.Register(delegate
                    {
                        KillOwnedProcess(process);
                        cancelled.TrySetResult(true);
                    }))
                    {
                        try
                        {
                            process.StandardInput.Write(config);
                            process.StandardInput.Close();
                        }
                        catch(Exception ex)
                        {
                            if(lifetime.IsCancellationRequested)
                                return ThrowCancellation(token,ex);
                            if(ex is IOException || ex is InvalidOperationException || ex is ObjectDisposedException)
                                throw new HttpRequestException("Windows 网络组件通信失败",ex);
                            throw;
                        }

                        Task completed=Task.WhenAll(exited.Task,stdout.Completion,stderr.Completion);
                        Task winner=await Task.WhenAny(completed,cancelled.Task).ConfigureAwait(false);
                        if(winner!=completed)
                        {
                            KillOwnedProcess(process);
                            await Task.WhenAny(completed,Task.Delay(2000)).ConfigureAwait(false);
                            return ThrowCancellation(token,null);
                        }
                        await completed.ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if(lifetime.IsCancellationRequested)throw new TimeoutException("公开数据连接超时");
                        if(stdout.Error is IOException || stdout.Error is ObjectDisposedException)
                            throw new HttpRequestException("公开数据连接中断",stdout.Error);
                        if(stdout.Error!=null)throw stdout.Error;

                        int exitCode;
                        try{exitCode=process.ExitCode;}
                        catch(Exception ex){throw new HttpRequestException("Windows 网络组件未正常结束",ex);}
                        if(exitCode==28)throw new TimeoutException("公开数据连接超时");
                        if(exitCode!=0)throw new HttpRequestException("公开数据连接失败（网络状态 "+exitCode.ToString(CultureInfo.InvariantCulture)+"）");

                        return ParseResponse(stdout.GetBytes(),accountBytes);
                    }
                }
                finally
                {
                    KillOwnedProcess(process);
                }
            }
        }

        private static string ThrowCancellation(CancellationToken caller,Exception inner)
        {
            caller.ThrowIfCancellationRequested();
            if(inner==null)throw new TimeoutException("公开数据连接超时");
            throw new TimeoutException("公开数据连接超时",inner);
        }

        private static string ParseResponse(byte[] output,Action<int> accountBytes)
        {
            byte[] marker=Encoding.ASCII.GetBytes(FooterMarker);
            int markerAt=LastIndexOf(output,marker);
            if(markerAt<0)throw new InvalidDataException("公开数据响应缺少状态标记");
            int statusAt=markerAt+marker.Length;
            if(statusAt+3>output.Length)throw new InvalidDataException("公开数据状态标记不完整");
            for(int i=statusAt;i<statusAt+3;i++)if(output[i]<'0' || output[i]>'9')throw new InvalidDataException("公开数据状态标记无效");
            for(int i=statusAt+3;i<output.Length;i++)if(output[i]!='\r' && output[i]!='\n')throw new InvalidDataException("公开数据状态尾部无效");
            if(markerAt>MaximumBodyBytes)throw new InvalidDataException("公开数据响应过大");

            int status=(output[statusAt]-'0')*100+(output[statusAt+1]-'0')*10+(output[statusAt+2]-'0');
            accountBytes(markerAt);
            string body;
            try{body=StrictUtf8.GetString(output,0,markerAt);}
            catch(DecoderFallbackException ex){throw new InvalidDataException("公开数据不是有效的 UTF-8",ex);}
            if(status>=200 && status<=299)return body;
            if(status>=400 && status<=499)throw new NativeFeedRejectedException(status);
            if(status>=500 && status<=599)throw new HttpRequestException("公开数据服务暂时不可用（HTTP "+status.ToString(CultureInfo.InvariantCulture)+"）");
            throw new HttpRequestException("公开数据返回意外状态（HTTP "+status.ToString(CultureInfo.InvariantCulture)+"）");
        }

        private static string BuildConfig(Uri endpoint,Uri proxyUri,TimeSpan timeout)
        {
            int seconds=(int)Math.Ceiling(Math.Min(timeout.TotalSeconds,86400));
            if(seconds<1)seconds=1;
            var config=new StringBuilder(512);
            config.Append("silent\n");
            config.Append("show-error\n");
            config.Append("request = \"GET\"\n");
            config.Append("header = \"Accept: application/json\"\n");
            config.Append("proto = \"=https\"\n");
            config.Append("url = \"").Append(EscapeConfigValue(endpoint.AbsoluteUri)).Append("\"\n");
            if(proxyUri==null)
            {
                config.Append("proxy = \"\"\n");
                config.Append("noproxy = \"*\"\n");
            }
            else
            {
                config.Append("proxy = \"").Append(EscapeConfigValue(proxyUri.AbsoluteUri)).Append("\"\n");
                config.Append("noproxy = \"\"\n");
            }
            config.Append("max-time = ").Append(seconds.ToString(CultureInfo.InvariantCulture)).Append("\n");
            config.Append("write-out = \"\\n__CODEX_FEED_HTTP_STATUS__:%{http_code}\\n\"\n");
            return config.ToString();
        }

        private static string EscapeConfigValue(string value)
        {
            if(value==null)throw new ArgumentNullException("value");
            var escaped=new StringBuilder(value.Length+8);
            foreach(char ch in value)
            {
                if(ch=='\r' || ch=='\n' || ch=='\0')throw new InvalidDataException("网络配置包含无效字符");
                if(ch=='\\' || ch=='\"')escaped.Append('\\');
                escaped.Append(ch);
            }
            return escaped.ToString();
        }

        private static void ClearProxyEnvironment(ProcessStartInfo startInfo)
        {
            string[] names={"HTTP_PROXY","HTTPS_PROXY","ALL_PROXY","NO_PROXY","http_proxy","https_proxy","all_proxy","no_proxy"};
            foreach(string name in names)startInfo.EnvironmentVariables.Remove(name);
        }

        private static string GetCurlPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"curl.exe");
        }

        private static bool SafeHasExited(Process process)
        {
            try{return process.HasExited;}
            catch{return false;}
        }

        private static void KillOwnedProcess(Process process)
        {
            try{if(process!=null && !process.HasExited)process.Kill();}catch{}
        }

        private static int LastIndexOf(byte[] source,byte[] pattern)
        {
            if(source==null || pattern==null || pattern.Length==0 || pattern.Length>source.Length)return -1;
            for(int i=source.Length-pattern.Length;i>=0;i--)
            {
                int j=0;
                while(j<pattern.Length && source[i+j]==pattern[j])j++;
                if(j==pattern.Length)return i;
            }
            return -1;
        }

        private sealed class AsyncStreamCollector
        {
            private readonly Stream stream;
            private readonly int maximumBytes;
            private readonly Action abort;
            private readonly bool discard;
            private readonly byte[] chunk=new byte[16384];
            private readonly MemoryStream memory=new MemoryStream();
            private readonly TaskCompletionSource<bool> completion=new TaskCompletionSource<bool>();
            private int finished;

            internal AsyncStreamCollector(Stream stream,int maximumBytes,Action abort,bool discard)
            {
                this.stream=stream;
                this.maximumBytes=maximumBytes;
                this.abort=abort;
                this.discard=discard;
            }

            internal Task Completion {get{return completion.Task;}}
            internal Exception Error {get;private set;}

            internal void Start(){BeginRead();}

            internal byte[] GetBytes()
            {
                lock(memory)return memory.ToArray();
            }

            private void BeginRead()
            {
                if(Volatile.Read(ref finished)!=0)return;
                try{stream.BeginRead(chunk,0,chunk.Length,ReadCompleted,null);}
                catch(Exception ex){Finish(ex);}
            }

            private void ReadCompleted(IAsyncResult result)
            {
                int count;
                try{count=stream.EndRead(result);}
                catch(Exception ex){Finish(ex);return;}
                if(count<=0){Finish(null);return;}
                if(!discard)
                {
                    lock(memory)
                    {
                        if(memory.Length+count>maximumBytes)
                        {
                            Finish(new InvalidDataException("公开数据响应过大"));
                            try{abort();}catch{}
                            return;
                        }
                        memory.Write(chunk,0,count);
                    }
                }
                BeginRead();
            }

            private void Finish(Exception error)
            {
                if(Interlocked.Exchange(ref finished,1)!=0)return;
                Error=error;
                completion.TrySetResult(true);
            }
        }

        // Purely local checks: no process, registry, DNS or network access.
        internal static int SelfTest()
        {
            byte[] ok=Encoding.UTF8.GetBytes("{\"ok\":true}"+FooterMarker+"200\n");
            int counted=-1;
            string body=ParseResponse(ok,delegate(int value){counted=value;});
            if(body!="{\"ok\":true}" || counted!=Encoding.UTF8.GetByteCount(body))throw new Exception("Native feed footer parse");

            bool rejected=false;
            try{ParseResponse(Encoding.UTF8.GetBytes("x"+FooterMarker+"403\n"),delegate(int ignored){});}
            catch(NativeFeedRejectedException){rejected=true;}
            if(!rejected)throw new Exception("Native feed 4xx type");

            bool retryable=false;
            try{ParseResponse(Encoding.UTF8.GetBytes("x"+FooterMarker+"503\n"),delegate(int ignored){});}
            catch(HttpRequestException ex){retryable=!(ex is NativeFeedRejectedException);}
            if(!retryable)throw new Exception("Native feed 5xx type");

            bool bounded=false;
            try{ParseResponse(Encoding.UTF8.GetBytes(new string('x',MaximumBodyBytes+1)+FooterMarker+"200\n"),delegate(int ignored){});}
            catch(InvalidDataException){bounded=true;}
            if(!bounded)throw new Exception("Native feed body limit");

            string direct=BuildConfig(new Uri("https://example.invalid/a?b=1"),null,TimeSpan.FromSeconds(4.1));
            if(direct.IndexOf("proxy = \"\"",StringComparison.Ordinal)<0 || direct.IndexOf("noproxy = \"*\"",StringComparison.Ordinal)<0 || direct.IndexOf("max-time = 5",StringComparison.Ordinal)<0)
                throw new Exception("Native feed direct config");
            bool escaped=false;
            try{EscapeConfigValue("bad\nvalue");}catch(InvalidDataException){escaped=true;}
            if(!escaped)throw new Exception("Native feed config injection");

            Console.WriteLine("NATIVE_FEED_HTTP_OK footer status_types direct_config config_escape no_network");
            return 0;
        }
    }
}
