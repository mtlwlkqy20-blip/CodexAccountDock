using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CodexAccountDock
{
    // This browser belongs to the existing app. Its profile is local, isolated
    // per account, excluded from public packages, and never populated from Edge.
    internal sealed class AccountWebTools : UserControl
    {
        private readonly AccountMeta account;
        private readonly bool preview;
        private WebView2 browser;
        private string statusText = "网页尚未打开 · 首次使用请自行登录所选账号";
        internal event Action StatusChanged;
        internal string StatusText { get { return statusText; } private set { statusText=value; if(StatusChanged!=null)StatusChanged(); } }
        internal bool IsOpen { get { return browser!=null; } }
        private readonly Panel viewport;
        private readonly Dictionary<string, TaskCompletionSource<Dictionary<string, object>>> pending = new Dictionary<string, TaskCompletionSource<Dictionary<string, object>>>();
        private readonly List<string> modelRows = new List<string>();
        private bool starting;
        private string verifiedStamp = "";
        private int generation;
        private DateTime lastModelAt;
        internal event Action<string> ModelsChanged;

        internal AccountWebTools(AccountMeta account, bool preview)
        {
            this.account = account; this.preview = preview; Dock = DockStyle.Fill;
            viewport = new Panel { Dock = DockStyle.Fill }; Controls.Add(viewport);
        }
        internal Task OpenAsync() { return Initialize(false, null); }
        internal async Task VerifyForUi() { await VerifyIdentity(); StatusText = "网页账号已核对：" + account.Email; }

        internal async Task Initialize(bool offlineDiagnostic, string diagnosticDirectory)
        {
            if (preview || starting || IsDisposed) return;
            if (browser != null) { browser.Focus(); return; }
            if (!offlineDiagnostic && !Regex.IsMatch(account.Id ?? "", "^[a-f0-9]{24}$")) throw new OnlineToolException("账号本地标识无效，未打开网页目录。");
            starting = true;
            try {
                string profile = offlineDiagnostic ? diagnosticDirectory : Path.Combine(PortablePaths.Data, "web-sessions", account.Id);
                if (string.IsNullOrWhiteSpace(profile)) throw new InvalidOperationException();
                Directory.CreateDirectory(profile);
                var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
                var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(profile).SetAccessControl(security);
                var view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = ThemeVisual.Current.PanelBottom };
                browser = view; viewport.Controls.Add(view); view.BringToFront();
                var environment = await CoreWebView2Environment.CreateAsync(null, profile);
                if (IsDisposed || browser != view) { view.Dispose(); return; }
                await view.EnsureCoreWebView2Async(environment);
                var core = view.CoreWebView2;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsWebMessageEnabled = true;
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e) {
                    Uri uri; if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) || (uri.Scheme != "https" && e.Uri != "about:blank")) { e.Cancel = true; return; }
                    generation++; verifiedStamp = ""; CancelPending(); StatusText = "当前网页：" + (uri.Scheme == "https" ? uri.GetLeftPart(UriPartial.Authority) : "本地诊断") + " · 需要时重新核对账号";
                };
                core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; Uri uri; if (Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) && uri.Scheme == "https") core.Navigate(e.Uri); };
                core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
                core.DownloadStarting += delegate(object sender, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; StatusText = "此工具页不下载文件，请在官方浏览器处理下载。"; };
                core.WebMessageReceived += OnWebMessage;
                string script;
                using (var stream = typeof(AccountWebTools).Assembly.GetManifestResourceStream("CodexAccountDock.WebTools.js"))
                using (var reader = new StreamReader(stream)) script = reader.ReadToEnd();
                await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
                if (offlineDiagnostic) { core.Navigate("about:blank"); StatusText = "内嵌网页运行时初始化通过 · 离线测试，没有访问账号"; }
                else { core.Navigate("https://chatgpt.com/"); StatusText = "请自行登录 " + account.Email + "，然后点击“核对当前账号”。"; }
            } catch { Disconnect(); throw; }
            finally { starting = false; }
        }

        private static bool OfficialSource(string value) { Uri u; return Uri.TryCreate(value, UriKind.Absolute, out u) && u.Scheme == "https" && u.Host == "chatgpt.com" && u.IsDefaultPort; }
        private async Task<Dictionary<string, object>> Send(string action, string program, string email)
        {
            if (browser == null || browser.CoreWebView2 == null || !OfficialSource(browser.CoreWebView2.Source)) throw new OnlineToolException("请先在“网页连接”页打开并自行登录官方 ChatGPT 网页。");
            string id = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<Dictionary<string, object>>(); pending.Add(id, completion);
            try {
                browser.CoreWebView2.PostWebMessageAsJson(JsonUtil.Serializer.Serialize(new { channel = "account-dock-tools-v1", id = id, action = action, program = program, email = email, expectedStamp = verifiedStamp, expectedEmail = account.Email }));
                if (await Task.WhenAny(completion.Task, Task.Delay(45000)) != completion.Task) throw new OnlineToolException(action == "send" ? "网页发送超时，结果未知，请通过官方页面核对，不会自动重发。" : "网页读取超时，请确认已回到 chatgpt.com 并完成登录。", action == "send");
                var reply = await completion.Task;
                if (!(JsonUtil.Value(reply, "ok") is bool) || !(bool)JsonUtil.Value(reply, "ok")) throw new OnlineToolException(SafeMessage(JsonUtil.Text(reply, "message")), JsonUtil.Value(reply, "unknown") is bool && (bool)JsonUtil.Value(reply, "unknown"));
                var value = JsonUtil.Dict(JsonUtil.Value(reply, "value"));
                if (value == null) throw new OnlineToolException("网页未返回可核对的数据。", action == "send");
                return value;
            } finally { pending.Remove(id); }
        }
        private static string SafeMessage(string value) { return string.IsNullOrWhiteSpace(value) || value.Length > 512 ? "网页操作未完成，请到官方客户端核对。" : value; }
        private async Task VerifyIdentity()
        {
            var identity = await Send("account", null, null);
            if (!string.Equals(JsonUtil.Text(identity, "email"), account.Email, StringComparison.OrdinalIgnoreCase) || JsonUtil.Text(identity, "accountId") != account.AccountId) {
                verifiedStamp = ""; throw new OnlineToolException("内置网页与所选账号不一致，请在网页中自行登录所选账号；没有读取其他账号的邀请或模型记录。");
            }
            string stamp = JsonUtil.Text(identity, "stamp"); if (stamp.Length == 0 || stamp.Length > 512) throw new OnlineToolException("网页账号标识不完整。");
            verifiedStamp = stamp;
        }
        internal async Task<Dictionary<string, object>> Call(string action, string program, string email)
        {
            if (action != "eligibility" && action != "send") throw new OnlineToolException("不支持的网页操作。");
            await VerifyIdentity(); return await Send(action, program, email);
        }
        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!OfficialSource(e.Source)) return;
            try {
                string json = e.WebMessageAsJson; if (json.Length > 20000) return;
                var row = JsonUtil.Dict(JsonUtil.Serializer.DeserializeObject(json));
                string channel = JsonUtil.Text(row, "channel");
                if (channel == "account-dock-tools-reply-v1") { TaskCompletionSource<Dictionary<string, object>> t; if (pending.TryGetValue(JsonUtil.Text(row, "id"), out t)) t.TrySetResult(row); }
                else if (channel == "account-dock-model-v1" && verifiedStamp.Length > 0 && (DateTime.UtcNow - lastModelAt).TotalMilliseconds > 750) {
                    lastModelAt = DateTime.UtcNow; ObserveModel(JsonUtil.Text(row, "requested"), JsonUtil.Text(row, "resolved"));
                }
            } catch { }
        }
        private async void ObserveModel(string requested, string resolved)
        {
            Func<string, bool> valid = s => s == "" || s.Length <= 128 && Regex.IsMatch(s, "^[A-Za-z0-9][A-Za-z0-9._:/-]*$");
            if (!valid(requested) || !valid(resolved)) return;
            int current = generation;
            try {
                await VerifyIdentity(); if (current != generation || IsDisposed) return;
                modelRows.Insert(0, DateTime.Now.ToString("MM-dd HH:mm:ss") + "  " + ModelEvidence.Status(requested, resolved) + "\r\n所选：" + (requested == "" ? "未取得" : requested) + "   返回：" + (resolved == "" ? "未取得" : resolved));
                if (modelRows.Count > 20) modelRows.RemoveAt(20);
                if (ModelsChanged != null) ModelsChanged("内置 ChatGPT 网页 · 已核对账号：" + account.Email + "\r\n只保留本次打开期间最近 20 条模型标识，不保存聊天正文。\r\n\r\n" + string.Join("\r\n\r\n", modelRows));
            } catch { if (!IsDisposed) StatusText = "模型记录未绑定：请核对网页当前账号。"; }
        }
        private void CancelPending() { foreach (var t in pending.Values.ToArray()) t.TrySetException(new OnlineToolException("网页连接已结束，结果待核实。", true)); pending.Clear(); }
        internal void Disconnect()
        {
            generation++; verifiedStamp = ""; CancelPending();
            WebView2 old = browser; browser = null; if (old != null) { viewport.Controls.Remove(old); old.Dispose(); }
            if (!IsDisposed) StatusText = "网页已断开；本机独立登录状态保留，未执行退出账号。";
        }
        protected override void Dispose(bool disposing) { if (disposing) Disconnect(); base.Dispose(disposing); }
    }
}
