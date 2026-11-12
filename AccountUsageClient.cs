using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountDock
{
    internal sealed class AccountUsageResult
    {
        internal long? LifetimeTokens;
        internal string FetchedAtUtc;
    }

    /// <summary>
    /// Reads the authenticated account's aggregate token total through the
    /// official Codex app-server protocol.  Credentials are supplied only on
    /// the child process' standard input and the process runs in a private,
    /// throw-away CODEX_HOME.
    /// </summary>
    internal static class AccountUsageClient
    {
        private const int HardTimeoutMilliseconds = 25000;
        private const int MaximumLineCharacters = 65536;
        private const int MaximumResponseCharacters = 262144;
        private const int MaximumStderrCharacters = 65536;
        private const string RuntimePrefix = "CodexAccountDock-usage-";
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static async Task<AccountUsageResult> FetchAsync(byte[] raw, string expectedStableId, CancellationToken cancellationToken)
        {
            if (raw == null || raw.Length == 0) throw new ArgumentException("账号凭据为空。", "raw");
            if (string.IsNullOrWhiteSpace(expectedStableId)) throw new ArgumentException("账号标识为空。", "expectedStableId");

            AuthIdentity identity = AuthCodec.Parse(raw);
            if (!string.Equals(identity.StableId, expectedStableId, StringComparison.Ordinal))
                throw new InvalidDataException("账号凭据与所选账号不一致。");
            if (string.IsNullOrWhiteSpace(identity.AccessToken) || string.IsNullOrWhiteSpace(identity.AccountId))
                throw new InvalidDataException("账号授权信息不完整，请重新登记该账号。");

            string executable = CliSetup.Find();
            if (string.IsNullOrWhiteSpace(executable) ||
                !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(executable))
                throw new FileNotFoundException("未找到官方 Codex CLI 原生程序，请先完成 CLI 安装。");

            string runtimeHome = CreatePrivateRuntimeHome();
            Process process = null;
            JobScope job = null;
            Task<long> stderrDrain = null;
            CancellationTokenSource timeout = new CancellationTokenSource();
            CancellationTokenSource linked = null;
            try
            {
                timeout.CancelAfter(HardTimeoutMilliseconds);
                linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = executable;
                start.Arguments = "app-server -c cli_auth_credentials_store=file -c analytics.enabled=false";
                start.WorkingDirectory = runtimeHome;
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.RedirectStandardInput = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.StandardOutputEncoding = StrictUtf8;
                start.StandardErrorEncoding = StrictUtf8;
                start.EnvironmentVariables["CODEX_HOME"] = runtimeHome;
                start.EnvironmentVariables.Remove("OPENAI_API_KEY");
                start.EnvironmentVariables.Remove("CODEX_API_KEY");
                ApplyExistingSystemProxy(start);

                process = new Process();
                process.StartInfo = start;
                process.EnableRaisingEvents = true;
                if (!process.Start()) throw new InvalidOperationException("无法启动官方 Codex CLI。");

                job = JobScope.Attach(process);
                BoundedLineReader output = new BoundedLineReader(process.StandardOutput, MaximumLineCharacters, MaximumResponseCharacters);
                stderrDrain = DrainStderrAsync(process.StandardError, linked.Token);

                await WriteJsonLineAsync(process.StandardInput.BaseStream, JsonUtil.Serializer.Serialize(new Dictionary<string, object>
                {
                    { "method", "initialize" },
                    { "id", 1 },
                    { "params", new Dictionary<string, object>
                        {
                            { "clientInfo", new Dictionary<string, object>
                                {
                                    { "name", "CodexAccountDock" },
                                    { "title", "Codex Account Dock" },
                                    { "version", "2.0.0" }
                                }
                            },
                            { "capabilities", new Dictionary<string, object> { { "experimentalApi", true } } }
                        }
                    }
                }), linked.Token).ConfigureAwait(false);
                await ReadResponseAsync(output, 1, "初始化", linked.Token).ConfigureAwait(false);

                await WriteJsonLineAsync(process.StandardInput.BaseStream, JsonUtil.Serializer.Serialize(new Dictionary<string, object>
                {
                    { "method", "initialized" },
                    { "params", new Dictionary<string, object>() }
                }), linked.Token).ConfigureAwait(false);

                await WriteJsonLineAsync(process.StandardInput.BaseStream, JsonUtil.Serializer.Serialize(new Dictionary<string, object>
                {
                    { "method", "account/login/start" },
                    { "id", 2 },
                    { "params", new Dictionary<string, object>
                        {
                            { "type", "chatgptAuthTokens" },
                            { "accessToken", identity.AccessToken },
                            { "chatgptAccountId", identity.AccountId },
                            { "chatgptPlanType", string.IsNullOrWhiteSpace(identity.Plan) ? "plus" : identity.Plan }
                        }
                    }
                }), linked.Token).ConfigureAwait(false);
                await ReadResponseAsync(output, 2, "账号授权", linked.Token).ConfigureAwait(false);

                await WriteJsonLineAsync(process.StandardInput.BaseStream, JsonUtil.Serializer.Serialize(new Dictionary<string, object>
                {
                    { "method", "account/usage/read" },
                    { "id", 3 },
                    { "params", new Dictionary<string, object>() }
                }), linked.Token).ConfigureAwait(false);
                Dictionary<string, object> usageResponse = await ReadResponseAsync(output, 3, "Token 统计读取", linked.Token).ConfigureAwait(false);

                AccountUsageResult result = ParseResponse(usageResponse);
                result.FetchedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                return result;
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                throw new TimeoutException("读取账号 Token 统计超时，请稍后重试。");
            }
            finally
            {
                try { if (process != null) process.StandardInput.Close(); } catch { }
                if (job != null) job.Dispose();
                else
                {
                    try { if (process != null && !process.HasExited) process.Kill(); } catch { }
                }
                if (process != null) { try { process.WaitForExit(3000); } catch { } }
                if (stderrDrain != null) { try { stderrDrain.Wait(3000); } catch { } }
                if (process != null) process.Dispose();
                if (linked != null) linked.Dispose();
                timeout.Dispose();
                DeletePrivateRuntimeHome(runtimeHome);
            }
        }

        internal static bool SelfTest()
        {
            try
            {
                AccountUsageResult zero = ParseResponse("{\"id\":3,\"result\":{\"summary\":{\"lifetimeTokens\":0}}}");
                AccountUsageResult none = ParseResponse("{\"id\":3,\"result\":{\"summary\":{\"lifetimeTokens\":null}}}");
                AccountUsageResult large = ParseResponse("{\"id\":3,\"result\":{\"summary\":{\"lifetimeTokens\":9007199254740993}}}");
                bool negativeRejected = Rejects("{\"id\":3,\"result\":{\"summary\":{\"lifetimeTokens\":-1}}}");
                bool typeRejected = Rejects("{\"id\":3,\"result\":{\"summary\":{\"lifetimeTokens\":\"12\"}}}");
                bool missingRejected = Rejects("{\"id\":3,\"result\":{\"summary\":{}}}");
                bool largeStderrDrained = DrainsLargeSyntheticStderr();
                return zero.LifetimeTokens == 0 && !none.LifetimeTokens.HasValue &&
                    large.LifetimeTokens == 9007199254740993L && negativeRejected && typeRejected && missingRejected && largeStderrDrained;
            }
            catch { return false; }
        }

        private static bool DrainsLargeSyntheticStderr()
        {
            byte[] payload = new byte[MaximumStderrCharacters * 3 + 17];
            for (int index = 0; index < payload.Length; index++) payload[index] = (byte)'x';
            try
            {
                using (MemoryStream stream = new MemoryStream(payload, false))
                using (StreamReader reader = new StreamReader(stream, StrictUtf8, false, 1024, true))
                {
                    long drained = DrainStderrAsync(reader, CancellationToken.None).GetAwaiter().GetResult();
                    return payload.Length > 192 * 1024 && drained == payload.Length && reader.Read() == -1;
                }
            }
            finally { Array.Clear(payload, 0, payload.Length); }
        }

        internal static AccountUsageResult ParseResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumLineCharacters)
                throw new InvalidDataException("Token 统计响应为空或过大。");
            Dictionary<string, object> response;
            try { response = JsonUtil.Serializer.DeserializeObject(json) as Dictionary<string, object>; }
            catch (Exception) { throw new InvalidDataException("Token 统计响应格式无效。"); }
            return ParseResponse(response);
        }

        private static AccountUsageResult ParseResponse(Dictionary<string, object> response)
        {
            Dictionary<string, object> result = JsonUtil.Dict(JsonUtil.Value(response, "result"));
            Dictionary<string, object> summary = JsonUtil.Dict(JsonUtil.Value(result, "summary"));
            if (summary == null || !summary.ContainsKey("lifetimeTokens"))
                throw new InvalidDataException("当前 Codex CLI 未返回账号 Token 统计。");

            object value = summary["lifetimeTokens"];
            if (value == null) return new AccountUsageResult { LifetimeTokens = null };
            long parsed;
            if (!TryReadNonNegativeInteger(value, out parsed))
                throw new InvalidDataException("Codex CLI 返回的 Token 统计值无效。");
            return new AccountUsageResult { LifetimeTokens = parsed };
        }

        private static bool TryReadNonNegativeInteger(object value, out long parsed)
        {
            parsed = 0;
            if (value is int) parsed = (int)value;
            else if (value is long) parsed = (long)value;
            else if (value is short) parsed = (short)value;
            else if (value is byte) parsed = (byte)value;
            else if (value is uint) parsed = (uint)value;
            else if (value is ushort) parsed = (ushort)value;
            else if (value is sbyte) parsed = (sbyte)value;
            else if (value is ulong)
            {
                ulong unsigned = (ulong)value;
                if (unsigned > long.MaxValue) return false;
                parsed = (long)unsigned;
            }
            else return false;
            return parsed >= 0;
        }

        private static bool Rejects(string json)
        {
            try { ParseResponse(json); return false; }
            catch (InvalidDataException) { return true; }
        }

        private static async Task<Dictionary<string, object>> ReadResponseAsync(BoundedLineReader reader, long expectedId, string operation, CancellationToken token)
        {
            while (true)
            {
                string line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line == null) throw new EndOfStreamException("Codex CLI 在" + operation + "完成前已退出。");
                if (string.IsNullOrWhiteSpace(line)) continue;

                Dictionary<string, object> message;
                try { message = JsonUtil.Serializer.DeserializeObject(line) as Dictionary<string, object>; }
                catch (Exception) { throw new InvalidDataException("Codex CLI 返回了无效响应。"); }
                if (message == null) throw new InvalidDataException("Codex CLI 返回了无效响应。");

                object idValue = JsonUtil.Value(message, "id");
                if (idValue == null) continue;
                long id;
                if (!TryReadId(idValue, out id)) throw new InvalidDataException("Codex CLI 返回了无效响应编号。");
                if (id != expectedId) throw new InvalidDataException("Codex CLI 响应顺序异常，请重试。");
                if (JsonUtil.Value(message, "error") != null)
                {
                    Dictionary<string,object> error=JsonUtil.Dict(JsonUtil.Value(message,"error"));
                    long code=JsonUtil.Long(error,"code",0);
                    throw new InvalidOperationException(code==-32601?"当前 Codex CLI 不支持此统计接口，请更新 CLI。":"官方账号统计查询未成功，请检查该账号登录及网络；已保留旧值。");
                }
                if (JsonUtil.Dict(JsonUtil.Value(message, "result")) == null)
                    throw new InvalidDataException("Codex CLI 未返回" + operation + "结果。");
                return message;
            }
        }

        private static bool TryReadId(object value, out long id)
        {
            id = 0;
            if (value is int) { id = (int)value; return true; }
            if (value is long) { id = (long)value; return true; }
            return false;
        }

        private static async Task WriteJsonLineAsync(Stream stream, string json, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            byte[] bytes = StrictUtf8.GetBytes(json + "\n");
            try
            {
                await stream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }

        private static async Task<long> DrainStderrAsync(StreamReader error, CancellationToken token)
        {
            char[] buffer = new char[1024];
            long total = 0;
            try
            {
                while (true)
                {
                    int read = await AwaitWithCancellation(error.ReadAsync(buffer, 0, buffer.Length), token).ConfigureAwait(false);
                    if (read <= 0) break;
                    total += read;
                    Array.Clear(buffer, 0, read);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally { Array.Clear(buffer, 0, buffer.Length); }
            return total;
        }

        private static async Task<T> AwaitWithCancellation<T>(Task<T> task, CancellationToken token)
        {
            var cancelled=new TaskCompletionSource<bool>();
            using(token.Register(delegate{cancelled.TrySetResult(true);}))
            {
                Task finished=await Task.WhenAny(task,cancelled.Task).ConfigureAwait(false);
                if(finished!=task)
                {
                    task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
                    token.ThrowIfCancellationRequested();
                }
                return await task.ConfigureAwait(false);
            }
        }

        private static string CreatePrivateRuntimeHome()
        {
            string path = Path.Combine(Path.GetTempPath(), RuntimePrefix + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(path);
                SecurityIdentifier sid = WindowsIdentity.GetCurrent().User;
                if (sid == null) throw new UnauthorizedAccessException("无法确定当前 Windows 用户。");
                DirectorySecurity security = new DirectorySecurity();
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(path).SetAccessControl(security);
                return path;
            }
            catch
            {
                DeletePrivateRuntimeHome(path);
                throw;
            }
        }

        private static void ApplyExistingSystemProxy(ProcessStartInfo start)
        {
            if (HasProxyEnvironment(start)) return;
            try
            {
                Uri destination = new Uri("https://chatgpt.com", UriKind.Absolute);
                IWebProxy system = WebRequest.GetSystemWebProxy();
                Uri proxy = system == null ? null : system.GetProxy(destination);
                if (!IsSafeSystemProxy(destination, proxy)) return;
                string value = proxy.AbsoluteUri;
                start.EnvironmentVariables["HTTPS_PROXY"] = value;
                start.EnvironmentVariables["HTTP_PROXY"] = value;
            }
            catch { }
        }

        private static bool HasProxyEnvironment(ProcessStartInfo start)
        {
            foreach (string name in new[] { "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY" })
            {
                string value = start.EnvironmentVariables[name];
                if (!string.IsNullOrWhiteSpace(value)) return true;
            }
            return false;
        }

        private static bool IsSafeSystemProxy(Uri destination, Uri proxy)
        {
            if (proxy == null || !proxy.IsAbsoluteUri ||
                Uri.Compare(destination, proxy, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0)
                return false;
            if (!string.Equals(proxy.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(proxy.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
            return string.IsNullOrEmpty(proxy.UserInfo) && string.IsNullOrEmpty(proxy.Query) &&
                string.IsNullOrEmpty(proxy.Fragment) && !string.IsNullOrWhiteSpace(proxy.Host) && proxy.Port > 0;
        }

        private static void DeletePrivateRuntimeHome(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string target = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                string leaf = Path.GetFileName(target);
                Guid id;
                if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
                    !leaf.StartsWith(RuntimePrefix, StringComparison.Ordinal) ||
                    !Guid.TryParseExact(leaf.Substring(RuntimePrefix.Length), "N", out id)) return;
                if (Directory.Exists(target)) Directory.Delete(target, true);
            }
            catch { }
        }

        private sealed class BoundedLineReader
        {
            private readonly StreamReader reader;
            private readonly int maximumLine;
            private readonly int maximumTotal;
            private int total;
            private readonly char[] buffer=new char[2048];
            private int position,count;

            internal BoundedLineReader(StreamReader reader, int maximumLine, int maximumTotal)
            {
                this.reader = reader;
                this.maximumLine = maximumLine;
                this.maximumTotal = maximumTotal;
            }

            internal async Task<string> ReadLineAsync(CancellationToken token)
            {
                StringBuilder line = new StringBuilder();
                while (true)
                {
                    if(position>=count)
                    {
                        count=await AwaitWithCancellation(reader.ReadAsync(buffer,0,buffer.Length),token).ConfigureAwait(false);position=0;
                        if(count==0)return line.Length==0?null:line.ToString();
                    }
                    total++;
                    if (total > maximumTotal) throw new InvalidDataException("Codex CLI 响应过大。");
                    char value = buffer[position++];
                    if (value == '\n') return line.ToString().TrimEnd('\r');
                    line.Append(value);
                    if (line.Length > maximumLine) throw new InvalidDataException("Codex CLI 单行响应过大。");
                }
            }
        }

        private sealed class JobScope : IDisposable
        {
            private IntPtr handle;

            private JobScope(IntPtr handle) { this.handle = handle; }

            internal static JobScope Attach(Process process)
            {
                IntPtr handle = CreateJobObject(IntPtr.Zero, null);
                if (handle == IntPtr.Zero) throw new InvalidOperationException("无法创建 Codex CLI 隔离作业。");
                JobScope scope = new JobScope(handle);
                try
                {
                    JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                    int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    IntPtr memory = Marshal.AllocHGlobal(length);
                    try
                    {
                        Marshal.StructureToPtr(info, memory, false);
                        if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, memory, (uint)length))
                            throw new InvalidOperationException("无法配置 Codex CLI 隔离作业。");
                    }
                    finally { Marshal.FreeHGlobal(memory); }
                    if (!AssignProcessToJobObject(handle, process.Handle))
                        throw new InvalidOperationException("无法绑定 Codex CLI 隔离作业。");
                    return scope;
                }
                catch { scope.Dispose(); throw; }
            }

            public void Dispose()
            {
                if (handle == IntPtr.Zero) return;
                CloseHandle(handle);
                handle = IntPtr.Zero;
            }

            private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
            private const int JobObjectExtendedLimitInformation = 9;

            [StructLayout(LayoutKind.Sequential)]
            private struct IO_COUNTERS
            {
                internal ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
                internal ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                internal long PerProcessUserTimeLimit;
                internal long PerJobUserTimeLimit;
                internal uint LimitFlags;
                internal UIntPtr MinimumWorkingSetSize;
                internal UIntPtr MaximumWorkingSetSize;
                internal uint ActiveProcessLimit;
                internal UIntPtr Affinity;
                internal uint PriorityClass;
                internal uint SchedulingClass;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                internal JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                internal IO_COUNTERS IoInfo;
                internal UIntPtr ProcessMemoryLimit;
                internal UIntPtr JobMemoryLimit;
                internal UIntPtr PeakProcessMemoryUsed;
                internal UIntPtr PeakJobMemoryUsed;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string name);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint length);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

            [DllImport("kernel32.dll")]
            private static extern bool CloseHandle(IntPtr handle);
        }
    }
}
