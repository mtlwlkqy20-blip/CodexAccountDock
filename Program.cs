using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyVersion("3.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("3.0.0.0")]

namespace CodexAccountDock
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            DpiSupport.Initialize();
            if(args.Length==2 && args[0]=="--repair-local-projects"){
                try { var plan=DockContext.CreateCodexRestartPlan();System.Threading.Thread.Sleep(8000);DockContext.StopCodexAsync().GetAwaiter().GetResult();
                LocalProjectContinuity.Preserve(AuthCodec.Parse(File.ReadAllBytes(PortablePaths.LiveAuth)));
                File.WriteAllText(args[1],"PROJECT_STATE_REPAIRED");DockContext.LaunchCodexAsync(plan).GetAwaiter().GetResult(); }
                catch(Exception ex){File.WriteAllText(args[1],"FAILED "+ex.GetType().Name+": "+ex.Message);}return;
            }

            if(args.Length==2 && args[0]=="--account-tools-test"){Environment.ExitCode=AccountToolsDiagnostics.Run(args[1]);return;}
            if(args.Length==2 && args[0]=="--account-tools-current-read"){Environment.ExitCode=AccountToolsDiagnostics.ReadCurrent(args[1]);return;}
            if(args.Length==2 && args[0]=="--account-tools-invite-read"){Environment.ExitCode=AccountToolsDiagnostics.ReadCurrent(args[1],true);return;}
            if(args.Length==2 && args[0]=="--model-metadata-read"){File.WriteAllText(args[1],ModelEvidence.ReadRecentCodex(),new UTF8Encoding(false));return;}

#if !NO_DESKTOP_PET
            if(args.Contains("--desktop-pet-test")){Environment.ExitCode=DesktopPet.Diagnostic();return;}
#endif
            if(args.Contains("--dpi-test")){Environment.ExitCode=DpiSupport.RunDiagnostic();return;}
            if(args.Length==2 && args[0]=="--background-test"){Environment.ExitCode=BackgroundDiagnostics.Run(args[1]);return;}
            string previewTheme=args.FirstOrDefault(delegate(string arg) { return arg.StartsWith("--preview-theme="); });
            if(previewTheme!=null) ThemeSelection.Select(previewTheme.Substring("--preview-theme=".Length),false);
#if !NO_DESKTOP_PET
            if(args.Contains("--desktop-pet-guide-test")){Application.EnableVisualStyles();DesktopPet.Guide(null,true);Console.WriteLine("PET_GUIDE_UI_OK no_install no_account_access");return;}
#endif
            if(args.Contains("--cli-setup-test")){Environment.ExitCode=CliSetup.SelfTest()?0:1;return;}
            if(args.Contains("--cli-setup-preview")){Environment.ExitCode=CliSetup.RunVisualDiagnostic();return;}
            if(args.Contains("--shop-test")) { Environment.ExitCode=AiShop.SelfTest(); return; }
            if(args.Contains("--shop-ui-test")) { Environment.ExitCode=AiShop.RunUiDiagnostic(); return; }
            if(args.Contains("--shop-live-test")) { Environment.ExitCode=AiShop.RunLiveDiagnostic(); return; }
            if(args.Contains("--ip-query-test")){Environment.ExitCode=IpQueryClient.RunDiagnostic();return;}
            if(args.Contains("--ip-query-live-test")){Environment.ExitCode=IpQueryClient.RunLiveDiagnostic();return;}
            if(args.Contains("--stagger-test")) { Environment.ExitCode=StaggerPlan.SelfTest(); return; }
            if(args.Length==2 && (args[0]=="--stagger-authorized-probe" || args[0]=="--stagger-authorized-check"))
            {Environment.ExitCode=StaggerPlan.RunAuthorizedProbe(args[1],args[0]=="--stagger-authorized-probe");return;}
            if(args.Contains("--stagger-ui-test")) { Environment.ExitCode=StaggerPlan.RunUiDiagnostic(); return; }
            if(args.Contains("--plan-input-test")) { Environment.ExitCode=PlanInputDiagnostics.Run(); return; }
            if(args.Contains("--local-token-read")){LocalTokenTotals.Read();Console.WriteLine(LocalTokenTotals.Description);return;}
            if(args.Contains("--account-usage-test")){bool ok=AccountUsageClient.SelfTest();Console.WriteLine(ok?"ACCOUNT_USAGE_OK null zero large_integer negative type safety no_network":"ACCOUNT_USAGE_FAILED");Environment.ExitCode=ok?0:1;return;}
            if(args.Length==1 && (args[0]=="--account-usage-probe" || args[0]=="--account-usage-sync-confirmed"))
            {
                bool sync=args[0]=="--account-usage-sync-confirmed";
                AccountStore usageStore=new AccountStore();int failures=0;
                foreach(AccountMeta account in usageStore.Settings.Accounts)
                {
                    byte[] raw=null;
                    try{raw=usageStore.LoadRaw(account);AccountUsageResult usage=AccountUsageClient.FetchAsync(raw,account.Id,CancellationToken.None).GetAwaiter().GetResult();if(sync && usage.LifetimeTokens.HasValue){account.AccountLifetimeTokens=usage.LifetimeTokens.Value;account.AccountTokenUpdatedAt=usage.FetchedAtUtc;account.AccountTokenError="";}Console.WriteLine("ACCOUNT_USAGE_RESULT "+account.Email+" lifetime_tokens="+(usage.LifetimeTokens.HasValue?usage.LifetimeTokens.Value.ToString(CultureInfo.InvariantCulture):"unknown"));}
                    catch(Exception ex){failures++;if(sync)account.AccountTokenError="账号累计Token暂未读取成功，保留上次记录";Console.WriteLine("ACCOUNT_USAGE_RESULT "+account.Email+" failed="+ex.GetType().Name+" "+ex.Message);}
                    finally{if(raw!=null)Array.Clear(raw,0,raw.Length);}
                }
                if(sync){usageStore.Settings.AccountUsageConsent=true;usageStore.Save();}
                Console.WriteLine("ACCOUNT_USAGE_PROBE_DONE no_inference saved="+sync+" failures="+failures);Environment.ExitCode=failures==0?0:1;return;
            }
            if(args.Contains("--pro-plan-test")) { Environment.ExitCode=SelfTest.ProPlan(); return; }
            if(args.Contains("--refresh-feedback-test")) { Environment.ExitCode=SelfTest.RefreshFeedback(); return; }
            if(args.Contains("--news-feed-test")) { Environment.ExitCode=NewsFeed.SelfTest(); return; }
            if(args.Contains("--reset-feed-test")) { Environment.ExitCode=ResetFeed.SelfTest(); return; }
            if(args.Contains("--reset-feed-live-test")) { Environment.ExitCode=ResetFeed.LiveTest(); return; }
            if(args.Contains("--appearance-preview")) { Environment.ExitCode=AppearanceOptions.RenderPreview(); return; }
            if(args.Contains("--daily-test")) { Environment.ExitCode=DailyNews.RunDiagnostic(); return; }
            if(args.Contains("--daily-offline-test")) { Environment.ExitCode=DailyNews.RunOfflineDiagnostic(); return; }
            if(args.Contains("--daily-responsive-test")) { Environment.ExitCode=DailyNews.RunResponsiveDiagnostic(); return; }
            if(args.Contains("--daily-dock-test")) { Environment.ExitCode=DailyNews.RunDockDiagnostic(); return; }
            if(args.Contains("--reset-reminder-test")) { Environment.ExitCode=ResetPanel.RunDiagnostic(); return; }
            if(args.Contains("--expiry-datetime-test"))
            {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(folder);string details;
                bool ok=AccountPopup.RunExpiryDateTimeDiagnostic(Path.Combine(folder,"expiry-datetime.png"),out details);
                Console.WriteLine((ok?"EXPIRY_DATETIME_OK ":"EXPIRY_DATETIME_FAILED ")+details);Environment.ExitCode=ok?0:1;return;
            }
            if(args.Contains("--outer-border-test"))
            {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(folder);string details;
                bool ok=SidePanels.RunOuterBorderDiagnostic(Path.Combine(folder,"outer-border.png"),out details);
                Console.WriteLine((ok?"OUTER_BORDER_OK ":"OUTER_BORDER_FAILED ")+details);Environment.ExitCode=ok?0:1;return;
            }
            if (args.Length == 2 && args[0] == "--independent-probe")
            {
                IndependentProcess.Start("--helper-probe \"" + args[1] + "\"");
                Environment.ExitCode = DockContext.WaitUntil(delegate { return File.Exists(args[1]); }, 10000, 100) ? 0 : 1;
                return;
            }
            if (args.Length == 2 && args[0] == "--helper-probe") { File.WriteAllText(args[1], "ready"); return; }
            if (args.Length == 5 && args[0] == "--argument-probe")
            {
                if (args[1] == "" && args[2] == "" && args[3] == "fixture") File.WriteAllText(args[4], "quoted-empty-ok");
                return;
            }
            if (args.Length == 2 && args[0] == "--independent-survival-parent")
            {
                int parentPid = Process.GetCurrentProcess().Id;
                IndependentProcess.Start("--helper-survival-probe " + IndependentProcess.Quote(args[1]) + " " + parentPid.ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (args.Length == 3 && args[0] == "--helper-survival-probe")
            {
                int parentPid;
                if (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out parentPid)) return;
                if (!DockContext.WaitUntil(delegate { return !DockContext.IsProcessInstanceActive(parentPid, 0); }, 10000, 50)) return;
                File.WriteAllText(args[1], "survived");
                return;
            }
            if (args.Length == 5 && args[0] == "--restart-helper")
            {
                string markerDirectory = args[4];
                try
                {
                    if (!DockContext.IsSafeSwitchMarkerDirectory(markerDirectory)) throw new InvalidDataException("切换状态目录无效。");
                    Directory.CreateDirectory(markerDirectory);
                    DockContext.WriteProcessIdentity(Path.Combine(markerDirectory, "helper"));
                    File.WriteAllText(Path.Combine(markerDirectory, "ready"), "ready");
                    if (!DockContext.WaitUntil(delegate { return File.Exists(Path.Combine(markerDirectory, "go")) || File.Exists(Path.Combine(markerDirectory, "cancel")); }, 15000, 100)) return;
                    if (File.Exists(Path.Combine(markerDirectory, "cancel"))) return;
                    CodexRestartPlan plan = new CodexRestartPlan { AppUserModelId = Encoding.UTF8.GetString(Convert.FromBase64String(args[1])), ExecutablePath = Encoding.UTF8.GetString(Convert.FromBase64String(args[2])) };
                    string targetId = Encoding.UTF8.GetString(Convert.FromBase64String(args[3]));
                    DockContext.ExecuteRestartTransaction(
                        delegate { DockContext.StopCodexAsync().GetAwaiter().GetResult(); File.WriteAllText(Path.Combine(markerDirectory, "stopped"), "stopped"); },
                        delegate { new AccountStore().CommitPreparedSwitch(targetId); File.WriteAllText(Path.Combine(markerDirectory, "written"), "written"); },
                        delegate { DockContext.LaunchCodexAsync(plan).GetAwaiter().GetResult(); });
                    File.WriteAllText(Path.Combine(markerDirectory, "done"), "done");
                }
                catch (Exception ex)
                {
                    try { if (DockContext.IsSafeSwitchMarkerDirectory(markerDirectory)) File.WriteAllText(Path.Combine(markerDirectory, "error"), ex.GetType().Name + ": " + ex.Message); }
                    catch { }
                    if (!DockContext.IsDockInstanceRunning()) MessageBox.Show("Codex 自动重开失败：" + ex.Message, "账号切换");
                }
                finally
                {
                    try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--ensure-tray --show") { UseShellExecute = true }); }
                    catch { }
                    if (File.Exists(Path.Combine(markerDirectory, "cancel")) && !File.Exists(Path.Combine(markerDirectory, "done")) && !File.Exists(Path.Combine(markerDirectory, "error"))) DockContext.CleanupSwitchMarkerDirectory(markerDirectory);
                }
                return;
            }
            if (args.Any(delegate(string a) { return string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase); }))
            {
                Environment.ExitCode = SelfTest.Run();
                return;
            }

            if (args.Any(delegate(string a) { return string.Equals(a, "--audit", StringComparison.OrdinalIgnoreCase); }))
            {
                Environment.ExitCode = SelfTest.AuditInstalled();
                return;
            }

            bool created;
            bool diagnostic = args.Any(delegate(string a) { return a == "--render-test" || a == "--perf-test" || a == "--hotkey-test"; });
            using (Mutex mutex = new Mutex(true, diagnostic ? "Local\\CodexAccountDock.V3.Diagnostics" : "Local\\CodexAccountDock.V3.SingleInstance", out created))
            {
                if (!created)
                {
                    if (!args.Contains("--ensure-tray")) DashboardNative.OpenExisting();
                    return;
                }

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool renderTest = args.Any(delegate(string a) { return string.Equals(a, "--render-test", StringComparison.OrdinalIgnoreCase); });
                bool idleTest = args.Any(delegate(string a) { return string.Equals(a, "--perf-test", StringComparison.OrdinalIgnoreCase); });
                bool hotkeyTest = args.Any(delegate(string a) { return string.Equals(a, "--hotkey-test", StringComparison.OrdinalIgnoreCase); });
                UiDiagnostics.Enabled = args.Any(delegate(string a) { return string.Equals(a, "--paint-test", StringComparison.OrdinalIgnoreCase); });
                if (UiDiagnostics.Enabled) Application.AddMessageFilter(new DiagnosticMessageFilter());
                bool showOnStart = renderTest || args.Contains("--show");
                if (args.Contains("--enable-startup"))
                {
                    using (RegistryKey run = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) run.SetValue("CodexAccountDock", "\"" + Application.ExecutablePath + "\" --tray");
                    AccountStore startupStore = new AccountStore();
                    startupStore.Settings.StartWithWindows = true;
                    startupStore.Save();
                }
                Application.Run(new DockContext(showOnStart, renderTest, idleTest, hotkeyTest));
            }
        }
    }

    internal static class IndependentProcess
    {
        public static int Start(string arguments)
        {
            using (ManagementClass process = new ManagementClass("Win32_Process"))
            using (ManagementBaseObject input = process.GetMethodParameters("Create"))
            {
                input["CommandLine"] = "\"" + Application.ExecutablePath + "\" " + arguments;
                input["CurrentDirectory"] = AppDomain.CurrentDomain.BaseDirectory;
                using (ManagementBaseObject result = process.InvokeMethod("Create", input, null))
                {
                    if (Convert.ToUInt32(result["ReturnValue"]) != 0) throw new InvalidOperationException("无法启动独立切换助手，尚未关闭 Codex。");
                    return Convert.ToInt32(result["ProcessId"], CultureInfo.InvariantCulture);
                }
            }
        }

        public static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }
    }


    internal sealed class AppSettings
    {
        public int Version = 1;
        public bool NetworkConsent = false;
        public bool NetworkConsentAsked = false;
        public bool AccountUsageConsent = false;
        public bool SubscriptionReadConsent = false;
        public bool StartWithWindows = false;
        public List<AccountMeta> Accounts = new List<AccountMeta>();
    }

    internal sealed class AccountMeta
    {
        public string Id = "";
        public string Email = "";
        public string Plan = "";
        public string OfficialPlan = "";
        public string AccountId = "";
        public string UserId = "";
        public string AddedAt = "";
        public string SubscriptionExpiresOn = "";
        public SubscriptionSnapshot SubscriptionRead;
        public string SubscriptionReadError = "";
        public List<InviteAttempt> InviteAttempts = new List<InviteAttempt>();
        public double PrimaryRemaining = -1;
        public double SecondaryRemaining = -1;
        public double MonthlyRemaining = -1;
        public long PrimaryResetAt = 0;
        public long SecondaryResetAt = 0;
        public long MonthlyResetAt = 0;
        public bool HasMonthlyLimit = false;
        public int ResetCredits = -1;
        public List<ResetCreditInfo> ResetCreditDetails = new List<ResetCreditInfo>();
        public List<string> ResetCreditNotices = new List<string>();
        public string LastQuotaAt = "";
        public string LastError = "";
        public long AccountLifetimeTokens = -1;
        public string AccountTokenUpdatedAt = "";
        public string AccountTokenError = "";
    }

    internal sealed class AuthIdentity
    {
        public string Email;
        public string Plan;
        public string AccountId;
        public string UserId;
        public string AccessToken;
        public long ExpiresAt;
        public string StableId;
    }

    internal sealed class CodexRestartPlan
    {
        public string AppUserModelId = "";
        public string ExecutablePath = "";
        public int ProcessCount;
    }

    internal static class PortablePaths
    {
        public static readonly string Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        public static readonly string Data = Path.Combine(Root, "data");
        public static readonly string Accounts = Path.Combine(Data, "accounts");
        public static readonly string Backups = Path.Combine(Data, "backups");
        public static readonly string LoginStaging = Path.Combine(Data, "login-staging");
        public static readonly string SwitchHelpers = Path.Combine(Data, "switch-helper");
        public static readonly string Settings = Path.Combine(Data, "metadata.json");
        public static readonly string LiveAuth = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "auth.json");

        public static void Ensure()
        {
            Directory.CreateDirectory(Data);
            Directory.CreateDirectory(Accounts);
            Directory.CreateDirectory(Backups);
            Directory.CreateDirectory(LoginStaging);
            Directory.CreateDirectory(SwitchHelpers);
        }
    }

    internal static class JsonUtil
    {
        public static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static Dictionary<string, object> Dict(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static object Value(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? value : null;
        }

        public static string Text(Dictionary<string, object> dict, string key)
        {
            object value = Value(dict, key);
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static double Number(Dictionary<string, object> dict, string key, double fallback)
        {
            object value = Value(dict, key);
            if (value == null) return fallback;
            double number;
            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out number) ? number : fallback;
        }

        public static long Long(Dictionary<string, object> dict, string key, long fallback)
        {
            object value = Value(dict, key);
            if (value == null) return fallback;
            long number;
            if (long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out number)) return number;
            double d;
            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? Convert.ToInt64(d) : fallback;
        }
    }

    internal static class AuthCodec
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexAccountDock|DPAPI|v1");

        public static AuthIdentity Parse(byte[] raw)
        {
            Dictionary<string, object> root = JsonUtil.Serializer.DeserializeObject(Encoding.UTF8.GetString(raw)) as Dictionary<string, object>;
            Dictionary<string, object> tokens = JsonUtil.Dict(JsonUtil.Value(root, "tokens"));
            if (tokens == null) throw new InvalidDataException("未找到 tokens 对象。");

            string idToken = JsonUtil.Text(tokens, "id_token");
            string accessToken = JsonUtil.Text(tokens, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidDataException("未找到 access_token。");

            Dictionary<string, object> idClaims = DecodeJwt(idToken);
            Dictionary<string, object> accessClaims = DecodeJwt(accessToken);
            Dictionary<string, object> idAuth = JsonUtil.Dict(JsonUtil.Value(idClaims, "https://api.openai.com/auth"));
            Dictionary<string, object> accessAuth = JsonUtil.Dict(JsonUtil.Value(accessClaims, "https://api.openai.com/auth"));
            Dictionary<string, object> profile = JsonUtil.Dict(JsonUtil.Value(accessClaims, "https://api.openai.com/profile"));

            AuthIdentity identity = new AuthIdentity();
            identity.Email = First(JsonUtil.Text(idClaims, "email"), JsonUtil.Text(profile, "email"), "未知账号");
            identity.Plan = First(JsonUtil.Text(idAuth, "chatgpt_plan_type"), JsonUtil.Text(accessAuth, "chatgpt_plan_type"), "plus");
            identity.AccountId = First(JsonUtil.Text(tokens, "account_id"), JsonUtil.Text(idAuth, "chatgpt_account_id"), JsonUtil.Text(accessAuth, "chatgpt_account_id"));
            identity.UserId = First(JsonUtil.Text(idAuth, "chatgpt_user_id"), JsonUtil.Text(accessAuth, "chatgpt_user_id"), JsonUtil.Text(accessAuth, "chatgpt_account_user_id"));
            identity.AccessToken = accessToken;
            identity.ExpiresAt = JsonUtil.Long(accessClaims, "exp", 0);
            identity.StableId = HashId(First(identity.AccountId, identity.UserId, identity.Email) + "|" + identity.UserId + "|" + identity.Email.ToLowerInvariant());
            return identity;
        }

        private static string First(params string[] values)
        {
            foreach (string value in values) if (!string.IsNullOrWhiteSpace(value)) return value;
            return "";
        }

        private static Dictionary<string, object> DecodeJwt(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return new Dictionary<string, object>();
            string[] parts = token.Split('.');
            if (parts.Length < 2) return new Dictionary<string, object>();
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            while (payload.Length % 4 != 0) payload += "=";
            try
            {
                byte[] bytes = Convert.FromBase64String(payload);
                return JsonUtil.Serializer.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object> ?? new Dictionary<string, object>();
            }
            catch { return new Dictionary<string, object>(); }
        }

        private static string HashId(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder text = new StringBuilder();
                for (int i = 0; i < 12; i++) text.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }

        public static byte[] Protect(byte[] raw)
        {
            return ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
        }

        public static byte[] Unprotect(byte[] encrypted)
        {
            return ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        }
    }

    internal sealed class AccountStore
    {
        public AppSettings Settings;
        public string PreviewActiveId = "";
        private readonly bool transient;
        public bool IsPreview { get { return transient; } }

        public AccountStore() : this(false) { }

        public AccountStore(bool transientMode)
        {
            transient = transientMode;
            if (transient)
            {
                Settings = new AppSettings();
            }
            else
            {
                PortablePaths.Ensure();
                Load();
            }
        }

        public void Load()
        {
            try
            {
                Settings = File.Exists(PortablePaths.Settings)
                    ? JsonUtil.Serializer.Deserialize<AppSettings>(File.ReadAllText(PortablePaths.Settings, Encoding.UTF8))
                    : new AppSettings();
                if (Settings == null) Settings = new AppSettings();
                if (Settings.Accounts == null) Settings.Accounts = new List<AccountMeta>();
            }
            catch
            {
                Settings = new AppSettings();
            }
        }

        public void Save()
        {
            if (transient) return;
            PortablePaths.Ensure();
            string temp = PortablePaths.Settings + ".tmp";
            File.WriteAllText(temp, JsonUtil.Serializer.Serialize(Settings), new UTF8Encoding(false));
            AtomicReplace(temp, PortablePaths.Settings);
        }

        public AccountMeta ImportRaw(byte[] raw)
        {
            AuthIdentity identity = AuthCodec.Parse(raw);
            AccountMeta meta = Settings.Accounts.FirstOrDefault(delegate(AccountMeta a) { return a.Id == identity.StableId; });
            if (meta == null)
            {
                meta = new AccountMeta();
                meta.Id = identity.StableId;
                meta.AddedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                Settings.Accounts.Add(meta);
            }
            ApplyImportedIdentity(meta, identity);
            File.WriteAllBytes(AccountFile(meta.Id), AuthCodec.Protect(raw));
            Save();
            return meta;
        }

        internal static void ApplyImportedIdentity(AccountMeta meta, AuthIdentity identity)
        {
            meta.Email = identity.Email;
            // JWT claims can lag behind the usage endpoint after a subscription
            // changes. Once the official quota response has confirmed a plan,
            // importing the current login must not overwrite that newer value.
            if(!string.IsNullOrWhiteSpace(meta.OfficialPlan))meta.Plan=meta.OfficialPlan;
            else if(string.IsNullOrWhiteSpace(meta.Plan) || string.IsNullOrWhiteSpace(meta.LastQuotaAt))meta.Plan=identity.Plan;
            meta.AccountId = identity.AccountId;
            meta.UserId = identity.UserId;
        }

        public AccountMeta ImportFile(string path)
        {
            return ImportRaw(File.ReadAllBytes(path));
        }

        public byte[] LoadRaw(AccountMeta meta)
        {
            return AuthCodec.Unprotect(File.ReadAllBytes(AccountFile(meta.Id)));
        }

        public string ActiveId()
        {
            if (transient) return PreviewActiveId;
            try { return File.Exists(PortablePaths.LiveAuth) ? AuthCodec.Parse(File.ReadAllBytes(PortablePaths.LiveAuth)).StableId : ""; }
            catch { return ""; }
        }

        public void CaptureLive()
        {
            if (File.Exists(PortablePaths.LiveAuth)) ImportFile(PortablePaths.LiveAuth);
        }

        public void SwitchTo(AccountMeta target)
        {
            PrepareSwitch(target);
            CommitPreparedSwitch(target.Id);
        }

        public void PrepareSwitch(AccountMeta target)
        {
            CaptureLive();
            byte[] raw = LoadRaw(target);
            try
            {
                AuthIdentity check = AuthCodec.Parse(raw);
                if (check.StableId != target.Id) throw new InvalidDataException("密文账号与元数据不匹配。");
            }
            finally { Array.Clear(raw, 0, raw.Length); }

            Directory.CreateDirectory(Path.GetDirectoryName(PortablePaths.LiveAuth));
            if (File.Exists(PortablePaths.LiveAuth))
            {
                string backupName = "auth-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".bin";
                File.WriteAllBytes(Path.Combine(PortablePaths.Backups, backupName), AuthCodec.Protect(File.ReadAllBytes(PortablePaths.LiveAuth)));
            }
        }

        public void CommitPreparedSwitch(string targetId)
        {
            AccountMeta target = Settings.Accounts.FirstOrDefault(delegate(AccountMeta a) { return a.Id == targetId; });
            if (target == null) throw new InvalidDataException("目标账号已不存在，未修改登录。");
            byte[] raw = LoadRaw(target);
            try
            {
                AuthIdentity check = AuthCodec.Parse(raw);
                if (check.StableId != target.Id) throw new InvalidDataException("密文账号与元数据不匹配。");
                Directory.CreateDirectory(Path.GetDirectoryName(PortablePaths.LiveAuth));
                LocalProjectContinuity.Preserve(check);
                string temp = PortablePaths.LiveAuth + ".account-dock.tmp";
                try
                {
                    File.WriteAllBytes(temp, raw);
                    AtomicReplace(temp, PortablePaths.LiveAuth);
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); }
                    catch { }
                }
            }
            finally { Array.Clear(raw, 0, raw.Length); }
        }

        public void Delete(AccountMeta meta)
        {
            string file = AccountFile(meta.Id);
            if (File.Exists(file)) File.Delete(file);
            Settings.Accounts.Remove(meta);
            Save();
        }

        private static string AccountFile(string id)
        {
            return Path.Combine(PortablePaths.Accounts, id + ".bin");
        }

        internal static void AtomicReplace(string temp, string destination)
        {
            if (File.Exists(destination))
            {
                try { File.Replace(temp, destination, null, true); return; }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(temp, destination, true);
                    File.Delete(temp);
                    return;
                }
            }
            File.Move(temp, destination);
        }
    }

    internal sealed class QuotaResult
    {
        public string Plan = "";
        public double PrimaryRemaining = -1;
        public double SecondaryRemaining = -1;
        public double MonthlyRemaining = -1;
        public long PrimaryResetAt;
        public long SecondaryResetAt;
        public long MonthlyResetAt;
        public bool HasMonthlyLimit = false;
        public int ResetCredits = -1;
        public List<ResetCreditInfo> ResetCreditDetails = new List<ResetCreditInfo>();
    }

    internal static class QuotaClient
    {
        private const int MaxResponseBytes = 2 * 1024 * 1024;

        public static async Task<QuotaResult> FetchAsync(byte[] raw)
        {
            AuthIdentity identity = AuthCodec.Parse(raw);
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + identity.AccessToken);
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "codex-cli");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
                if (!string.IsNullOrWhiteSpace(identity.AccountId)) client.DefaultRequestHeaders.TryAddWithoutValidation("ChatGPT-Account-Id", identity.AccountId);

                string json = await GetLimitedStringAsync(client, "https://chatgpt.com/backend-api/wham/usage").ConfigureAwait(false);
                QuotaResult result = ParseResponse(json);
                try
                {
                    string creditsJson = await GetLimitedStringAsync(client, "https://chatgpt.com/backend-api/wham/rate-limit-reset-credits").ConfigureAwait(false);
                    result.ResetCredits = ParseResetCredits(creditsJson);
                    result.ResetCreditDetails = ResetCreditInfo.Parse(creditsJson);
                }
                catch { result.ResetCredits = -1; }
                return result;
            }
        }

        private static async Task<string> GetLimitedStringAsync(HttpClient client, string url)
        {
            return await RunWithHardDeadlineAsync(async delegate(CancellationToken token)
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                using (CancellationTokenRegistration closeResponse = token.Register(delegate { try { response.Dispose(); } catch { } }))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > MaxResponseBytes)
                        throw new InvalidDataException("额度响应过大");
                    using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        return await ReadWithCancellationDisposalAsync(stream, token).ConfigureAwait(false);
                }
            }, 20000).ConfigureAwait(false);
        }

        private static async Task<T> RunWithHardDeadlineAsync<T>(Func<CancellationToken, Task<T>> action, int milliseconds)
        {
            CancellationTokenSource deadline = new CancellationTokenSource();
            Task<T> work;
            try { work = action(deadline.Token); }
            catch { deadline.Dispose(); throw; }
            Task timer = Task.Delay(milliseconds, deadline.Token);
            if (await Task.WhenAny(work, timer).ConfigureAwait(false) != work)
            {
                deadline.Cancel();
                work.ContinueWith(delegate(Task<T> finished)
                {
                    if (finished.IsFaulted) { var ignored = finished.Exception; }
                    deadline.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw new TimeoutException("额度查询超时");
            }
            try { return await work.ConfigureAwait(false); }
            finally { deadline.Cancel(); deadline.Dispose(); }
        }

        private static async Task<string> ReadWithCancellationDisposalAsync(Stream stream, CancellationToken token)
        {
            using (CancellationTokenRegistration closeStream = token.Register(delegate { try { stream.Dispose(); } catch { } }))
                return await ReadUtf8LimitedAsync(stream, token).ConfigureAwait(false);
        }

        internal static async Task<string> ReadUtf8LimitedAsync(Stream stream, CancellationToken token)
        {
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[16384];
                int read;
                while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, token).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > MaxResponseBytes) throw new InvalidDataException("额度响应过大");
                    buffer.Write(chunk, 0, read);
                }
                token.ThrowIfCancellationRequested();
                return Encoding.UTF8.GetString(buffer.ToArray());
            }
        }

        internal static bool HardTimeoutSelfTest()
        {
            using (HangingReadStream stream = new HangingReadStream())
            {
                try { RunWithHardDeadlineAsync(delegate(CancellationToken token) { return ReadWithCancellationDisposalAsync(stream, token); }, 25).GetAwaiter().GetResult(); }
                catch (TimeoutException) { return stream.WasDisposed; }
                return false;
            }
        }

        private sealed class HangingReadStream : Stream
        {
            private readonly TaskCompletionSource<int> pending = new TaskCompletionSource<int>();
            internal bool WasDisposed;
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) { return pending.Task; }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
                pending.TrySetException(new ObjectDisposedException("stream"));
                base.Dispose(disposing);
            }
        }

        internal static QuotaResult ParseResponse(string json)
        {
            Dictionary<string, object> root = JsonUtil.Serializer.DeserializeObject(json) as Dictionary<string, object>;
            Dictionary<string, object> rate = JsonUtil.Dict(JsonUtil.Value(root, "rate_limit"));
            Dictionary<string, object> primary = JsonUtil.Dict(JsonUtil.Value(rate, "primary_window"));
            Dictionary<string, object> secondary = JsonUtil.Dict(JsonUtil.Value(rate, "secondary_window"));
            QuotaResult result = new QuotaResult();
            result.Plan = JsonUtil.Text(root, "plan_type");
            ApplyRateWindow(result,primary,0);
            ApplyRateWindow(result,secondary,1);

            Dictionary<string, object> spendControl=JsonUtil.Dict(JsonUtil.Value(root,"spend_control"));
            Dictionary<string, object> individualLimit=JsonUtil.Dict(JsonUtil.Value(spendControl,"individual_limit"));
            double spendRemaining=RemainingPercent(individualLimit);
            if(ValidPercent(spendRemaining))
            {
                // The official usage response exposes the monthly personal cap
                // here. Prefer it over a long rate-limit window when both exist.
                result.MonthlyRemaining=spendRemaining;
                result.MonthlyResetAt=ResetAt(individualLimit);
                result.HasMonthlyLimit=true;
            }
            result.ResetCredits = -1;
            return result;
        }

        private static void ApplyRateWindow(QuotaResult result,Dictionary<string, object> window,int legacyPosition)
        {
            double remaining=Remaining(window);
            if(!ValidPercent(remaining))return;
            long duration=JsonUtil.Long(window,"limit_window_seconds",0);
            long resetAt=ResetAt(window);
            if(duration==7L*24L*60L*60L)
            {
                result.SecondaryRemaining=remaining;
                result.SecondaryResetAt=resetAt;
            }
            else if(duration>=28L*24L*60L*60L && duration<=31L*24L*60L*60L)
            {
                result.MonthlyRemaining=remaining;
                result.MonthlyResetAt=resetAt;
                result.HasMonthlyLimit=true;
            }
            else if(duration<=0 && legacyPosition==1)
            {
                result.SecondaryRemaining=remaining;
                result.SecondaryResetAt=resetAt;
            }
            else
            {
                result.PrimaryRemaining=remaining;
                result.PrimaryResetAt=resetAt;
            }
        }

        internal static int ParseResetCredits(string json)
        {
            Dictionary<string, object> root = JsonUtil.Serializer.DeserializeObject(json) as Dictionary<string, object>;
            long direct = JsonUtil.Long(root, "available_count", -1);
            if (direct >= 0) return (int)Math.Min(int.MaxValue, direct);
            Dictionary<string, object> nested = JsonUtil.Dict(JsonUtil.Value(root, "rate_limit_reset_credits"));
            long nestedCount = JsonUtil.Long(nested, "available_count", -1);
            return nestedCount < 0 ? -1 : (int)Math.Min(int.MaxValue, nestedCount);
        }

        private static double Remaining(Dictionary<string, object> window)
        {
            if (window == null) return -1;
            double used = JsonUtil.Number(window, "used_percent", -1);
            return !ValidPercent(used) ? -1 : 100-used;
        }

        private static double RemainingPercent(Dictionary<string, object> limit)
        {
            if(limit==null)return -1;
            double remaining=JsonUtil.Number(limit,"remaining_percent",-1);
            return ValidPercent(remaining)?remaining:-1;
        }

        private static bool ValidPercent(double value)
        {return !double.IsNaN(value) && !double.IsInfinity(value) && value>=0 && value<=100;}

        private static long ResetAt(Dictionary<string, object> window)
        {
            if (window == null) return 0;
            long absolute = JsonUtil.Long(window, "reset_at", 0);
            if (absolute > 0) return absolute;
            long after = JsonUtil.Long(window, "reset_after_seconds", 0);
            return after > 0 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + after : 0;
        }
    }

    internal sealed class DockContext : ApplicationContext
    {
        internal const string PlanUpdatedQuotaUnavailable = "官方方案已更新，额度暂不可用";
        private readonly AccountStore store;
        private readonly NotifyIcon tray;
        private readonly AccountPopup popup;
        private readonly Icon appIcon;
        private readonly bool ownsAppIcon;
        private System.Windows.Forms.Timer creditTimer;
        private System.Windows.Forms.Timer loginTimer;
        private System.Windows.Forms.Timer renderTimer;
        private System.Windows.Forms.Timer perfTimer;
        private System.Windows.Forms.Timer hotkeyTestTimer;
        private DateTime loginStartedAt;
        private string loginHome;
        private Process loginProcess;
        private TimeSpan perfCpuStart;
        private bool refreshing;
        private bool switching;

        public DockContext(bool showOnStart, bool renderTest, bool idleTest, bool hotkeyTest)
        {
            bool transient = renderTest || idleTest || hotkeyTest;
            store = new AccountStore(transient);
            if (!transient) CleanupCompletedSwitchMarkers();
            if (renderTest || idleTest || hotkeyTest)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                store.Settings.Accounts = new List<AccountMeta>
                {
                    new AccountMeta { Id = "preview-one", Email = "work.account@example.com", Plan = "plus", PrimaryRemaining = 70, SecondaryRemaining = 37, PrimaryResetAt = now + 3 * 3600 + 21 * 60, SecondaryResetAt = now + 5 * 86400 + 10 * 3600, LastQuotaAt = DateTime.UtcNow.ToString("o"), ResetCredits = 0 },
                    new AccountMeta { Id = "preview-two", Email = "research@example.com", Plan = "plus", PrimaryRemaining = 0, SecondaryRemaining = 82, PrimaryResetAt = now + 4 * 3600 + 15 * 60, SecondaryResetAt = now + 2 * 86400, LastQuotaAt = DateTime.UtcNow.ToString("o"), ResetCredits = 1 },
                    new AccountMeta { Id = "preview-three", Email = "studio@example.com", Plan = "pro", PrimaryRemaining = 94, SecondaryRemaining = 99, PrimaryResetAt = now + 4 * 3600 + 40 * 60, SecondaryResetAt = now + 6 * 86400, LastQuotaAt = DateTime.UtcNow.ToString("o"), ResetCredits = 2 },
                    new AccountMeta { Id = "preview-four", Email = "personal@example.com", Plan = "plus", PrimaryRemaining = 12, SecondaryRemaining = 48, PrimaryResetAt = now + 3600, SecondaryResetAt = now + 3 * 86400, LastQuotaAt = DateTime.UtcNow.ToString("o"), ResetCredits = 0 },
                    new AccountMeta { Id = "preview-five", Email = "long.email.for.layout.check@example.com", Plan = "plus", PrimaryRemaining = -1, SecondaryRemaining = -1, LastError = "登录已过期，请重新登录" }
                };
                store.PreviewActiveId = "preview-one";
                if(Environment.GetCommandLineArgs().Contains("--mockup-preview"))
                {
                    store.Settings.Accounts=store.Settings.Accounts.Take(3).ToList();
                    store.Settings.Accounts[0].ResetCredits=2;
                    if(Environment.GetCommandLineArgs().Any(t=>t.StartsWith("--font-preview=")))store.Settings.Accounts[0].ResetCredits=1;
                    store.Settings.Accounts[0].SubscriptionExpiresOn=DateTime.Today.AddDays(180).ToString("yyyy-MM-dd");
                    store.Settings.Accounts[1].PrimaryRemaining=42;
                    store.Settings.Accounts[2].PrimaryRemaining=85;
                    store.Settings.Accounts[2].SecondaryRemaining=61;
                    store.Settings.Accounts[2].Plan="plus";
                }
                if(Environment.GetCommandLineArgs().Contains("--two-card-preview"))
                {
                    store.Settings.Accounts=store.Settings.Accounts.Take(4).ToList();
                    store.PreviewActiveId="preview-four";
                    store.Settings.Accounts[0].PrimaryRemaining=0;
                    store.Settings.Accounts[0].SecondaryRemaining=67;
                    store.Settings.Accounts[0].PrimaryResetAt=now+2*3600+42*60;
                    store.Settings.Accounts[0].ResetCredits=2;
                    store.Settings.Accounts[0].SubscriptionExpiresOn=DateTime.Today.AddDays(16).ToString("yyyy-MM-dd");
                    store.Settings.Accounts[1].PrimaryRemaining=10;
                    store.Settings.Accounts[1].SecondaryRemaining=70;
                    store.Settings.Accounts[1].PrimaryResetAt=now+2*3600+47*60;
                    store.Settings.Accounts[1].ResetCredits=1;
                    store.Settings.Accounts[1].SubscriptionExpiresOn=DateTime.Today.AddDays(150).ToString("yyyy-MM-dd");
                }
                if(Environment.GetCommandLineArgs().Contains("--pro-plan-preview"))
                {
                    store.Settings.Accounts=new List<AccountMeta> {
                        new AccountMeta {Id="preview-pro",Email="pro.account@example.com",Plan="pro",OfficialPlan="pro",PrimaryRemaining=0,PrimaryResetAt=now+3600,SecondaryRemaining=64,SecondaryResetAt=now+4*86400+3*3600,ResetCredits=2,LastQuotaAt=DateTime.UtcNow.ToString("o"),AccountLifetimeTokens=1280000},
                        new AccountMeta {Id="preview-pro-unknown",Email="pro.unknown@example.com",Plan="pro",OfficialPlan="pro",PrimaryRemaining=90,PrimaryResetAt=now+3600,SecondaryRemaining=-1,ResetCredits=-1}
                    };
                    store.PreviewActiveId="preview-pro";
                }
                if(Environment.GetCommandLineArgs().Contains("--free-plan-preview"))
                {
                    store.Settings.Accounts=new List<AccountMeta>
                    {
                        new AccountMeta {Id="preview-free",Email="free.account@example.com",Plan="free",OfficialPlan="free",PrimaryRemaining=-1,SecondaryRemaining=-1,PrimaryResetAt=0,SecondaryResetAt=0,ResetCredits=-1,LastQuotaAt="",LastError=PlanUpdatedQuotaUnavailable}
                    };
                    store.PreviewActiveId="preview-free";
                }
                if(Environment.GetCommandLineArgs().Contains("--monthly-plan-preview"))
                {
                    store.Settings.Accounts=new List<AccountMeta>
                    {
                        new AccountMeta {Id="preview-monthly",Email="free.monthly@example.com",Plan="free",OfficialPlan="free",PrimaryRemaining=-1,SecondaryRemaining=-1,MonthlyRemaining=100,PrimaryResetAt=0,SecondaryResetAt=0,MonthlyResetAt=now+30L*86400L,HasMonthlyLimit=true,ResetCredits=-1,LastQuotaAt=DateTime.UtcNow.ToString("o")}
                    };
                    store.PreviewActiveId="preview-monthly";
                }
                if(Environment.GetCommandLineArgs().Contains("--monthly-zero-preview"))
                {
                    store.Settings.Accounts=new List<AccountMeta>
                    {
                        new AccountMeta {Id="preview-monthly-zero",Email="free.used@example.com",Plan="free",OfficialPlan="free",PrimaryRemaining=-1,SecondaryRemaining=-1,MonthlyRemaining=0,PrimaryResetAt=0,SecondaryResetAt=0,MonthlyResetAt=now+12L*86400L,HasMonthlyLimit=true,ResetCredits=-1,LastQuotaAt=DateTime.UtcNow.ToString("o")}
                    };
                    store.PreviewActiveId="preview-monthly-zero";
                }
                if(Environment.GetCommandLineArgs().Contains("--monthly-unknown-preview"))
                {
                    store.Settings.Accounts=new List<AccountMeta>
                    {
                        new AccountMeta {Id="preview-monthly-unknown",Email="free.unknown@example.com",Plan="free",OfficialPlan="free",PrimaryRemaining=-1,SecondaryRemaining=-1,MonthlyRemaining=-1,PrimaryResetAt=0,SecondaryResetAt=0,MonthlyResetAt=0,HasMonthlyLimit=false,ResetCredits=-1,LastQuotaAt="",LastError=PlanUpdatedQuotaUnavailable}
                    };
                    store.PreviewActiveId="preview-monthly-unknown";
                }
                if(Environment.GetCommandLineArgs().Contains("--monthly-all-preview"))
                {
                    store.Settings.Accounts=new List<AccountMeta>
                    {
                        new AccountMeta {Id="preview-all-meters",Email="free.all.meters@example.com",Plan="free",OfficialPlan="free",PrimaryRemaining=72,SecondaryRemaining=48,MonthlyRemaining=83,PrimaryResetAt=now+3L*3600L,SecondaryResetAt=now+5L*86400L,MonthlyResetAt=now+29L*86400L,HasMonthlyLimit=true,ResetCredits=-1,LastQuotaAt=DateTime.UtcNow.ToString("o")}
                    };
                    store.PreviewActiveId="preview-all-meters";
                }
            }
            if(store.IsPreview && Environment.GetCommandLineArgs().Contains("--match-preview")){
                store.Settings.Accounts=store.Settings.Accounts.Take(2).ToList();var a=store.Settings.Accounts[0];a.Email="work@example.com";a.PrimaryRemaining=9;a.SecondaryRemaining=40;a.AccountLifetimeTokens=1280000;a.ResetCredits=2;a.PrimaryResetAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+2*3600+14*60+30;a.SecondaryResetAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+6*86400+3600+30;
                var b=store.Settings.Accounts[1];b.Email="study@example.com";b.Plan="pro";b.SecondaryRemaining=72;b.AccountLifetimeTokens=960000;b.ResetCredits=2;
                foreach(var account in store.Settings.Accounts)account.SubscriptionRead=new SubscriptionSnapshot{ExpiresAtUtc="2026-11-21T11:41:00Z",HasActiveSubscription=true};
            }
            popup = new AccountPopup(this, store);
            if(!transient) StaggerPlan.Initialize(store);

            Icon extracted = null;
            try { extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
            if (extracted != null)
            {
                appIcon = extracted;
                ownsAppIcon = true;
            }
            else
            {
                appIcon = SystemIcons.Application;
                ownsAppIcon = false;
            }

            tray = new NotifyIcon();
            tray.Icon = appIcon;
            tray.Text = "Codex 账号切换";
            tray.Visible = true;
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) TogglePopup(Cursor.Position); };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("打开账号列表", null, delegate { TogglePopup(Cursor.Position); });
            ToolStripMenuItem startup = new ToolStripMenuItem("开机自启");
            startup.Checked = store.Settings.StartWithWindows;
            startup.CheckOnClick = true;
            startup.CheckedChanged += delegate { SetStartup(startup.Checked); };
            menu.Items.Add(startup);
            menu.Items.Add("打开本地数据目录", null, delegate { Process.Start("explorer.exe", PortablePaths.Data); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Exit(); });
            tray.ContextMenuStrip = menu;
            if(!store.IsPreview){creditTimer=new System.Windows.Forms.Timer {Interval=30000};creditTimer.Tick+=delegate {CheckCreditReminders();};creditTimer.Start();}

            if(store.IsPreview && Environment.GetCommandLineArgs().Contains("--third-account-preview")) store.Settings.Accounts=store.Settings.Accounts.Take(3).ToList();
            if(store.IsPreview && Environment.GetCommandLineArgs().Contains("--single-preview"))store.Settings.Accounts=store.Settings.Accounts.Take(1).ToList();
            popup.Rebuild();
            if (showOnStart)
            {
                popup.ShowNear(Cursor.Position);
            }
            if (renderTest)
            {
                renderTimer = new System.Windows.Forms.Timer();
                renderTimer.Interval = 700;
                renderTimer.Tick += delegate
                {
                    renderTimer.Stop();
                    string outputDirectory = Path.Combine(PortablePaths.Root, "test-output");
                    Directory.CreateDirectory(outputDirectory);
                    bool detailedPreview=Environment.GetCommandLineArgs().Contains("--detailed-preview");
                    string outputName=Environment.GetCommandLineArgs().Contains("--monthly-zero-preview")
                        ? (detailedPreview?"dashboard-monthly-zero-detailed.png":"dashboard-monthly-zero-compact.png")
                        : Environment.GetCommandLineArgs().Contains("--monthly-unknown-preview")
                            ? (detailedPreview?"dashboard-monthly-unknown-detailed.png":"dashboard-monthly-unknown-compact.png")
                        : Environment.GetCommandLineArgs().Contains("--monthly-all-preview")
                        ? (detailedPreview?"dashboard-monthly-all-detailed.png":"dashboard-monthly-all-compact.png")
                        : Environment.GetCommandLineArgs().Contains("--monthly-plan-preview")
                            ? (detailedPreview?"dashboard-monthly-only-detailed.png":"dashboard-monthly-only-compact.png")
                            : Environment.GetCommandLineArgs().Contains("--free-plan-preview")
                                ? (detailedPreview?"dashboard-free-plan-detailed.png":"dashboard-free-plan-compact.png")
                                : "dashboard-v3.png";
                    string output = Path.Combine(outputDirectory, outputName);
                    if(Environment.GetCommandLineArgs().Contains("--feedback-preview")) popup.PreviewQuotaFeedback();
                    popup.SavePreview(output);
                    if(Environment.GetCommandLineArgs().Contains("--delete-preview-test")){
                        while(store.Settings.Accounts.Count>0){
                            int before=store.Settings.Accounts.Count;
                            popup.ReferenceCommand("delete",store.Settings.Accounts.Last());
                            if(store.Settings.Accounts.Count!=before-1)throw new InvalidOperationException("Delete preview failed");
                            popup.SavePreview(Path.Combine(outputDirectory,"after-delete-"+store.Settings.Accounts.Count+".png"));
                            if(!popup.VerifyResponsiveLayout())throw new InvalidOperationException("Delete layout failed");
                        }
                        File.WriteAllText(Path.Combine(outputDirectory,"delete-check.txt"),"PASS: card delete command, 3 to 2 to 1 to 0, page clamp, layout bounds; synthetic memory only");
                    }
                    if(!popup.VerifyResponsiveLayout())throw new InvalidOperationException("V3 layout hit bounds failed");
                    Console.WriteLine("UI_PREVIEW_OK mode="+(popup.ReferenceDetailed?"detailed":"compact")+" " + output);
                    Exit();
                };
                renderTimer.Start();
            }
            if (idleTest)
            {
                Process current = Process.GetCurrentProcess();
                perfCpuStart = current.TotalProcessorTime;
                perfTimer = new System.Windows.Forms.Timer();
                perfTimer.Interval = 10000;
                perfTimer.Tick += delegate
                {
                    perfTimer.Stop();
                    current.Refresh();
                    double cpuMs = (current.TotalProcessorTime - perfCpuStart).TotalMilliseconds;
                    Console.WriteLine("PERF_TEST_OK duration_ms=10000 cpu_ms=" + cpuMs.ToString("0.0", CultureInfo.InvariantCulture) + " working_set_mb=" + (current.WorkingSet64 / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " private_mb=" + (current.PrivateMemorySize64 / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " handles=" + current.HandleCount.ToString(CultureInfo.InvariantCulture) + UiDiagnostics.Report());
                    Exit();
                };
                perfTimer.Start();
            }
            if (hotkeyTest)
            {
                hotkeyTestTimer = new System.Windows.Forms.Timer();
                hotkeyTestTimer.Interval = 300;
                hotkeyTestTimer.Tick += delegate
                {
                    hotkeyTestTimer.Stop();
                    bool passed = popup.RunHotkeyMessageTest();
                    Environment.ExitCode = passed ? 0 : 1;
                    Console.WriteLine(passed ? "HOTKEY_TEST_OK" : "HOTKEY_TEST_FAILED");
                    Exit();
                };
                hotkeyTestTimer.Start();
            }
        }

        public void TogglePopup(Point anchor)
        {
            popup.Rebuild();
            popup.ShowNear(anchor);
        }

        public void TryImportCurrent(bool notify)
        {
            if (IsAccountMutationBusy(notify)) return;
            try
            {
                if (!File.Exists(PortablePaths.LiveAuth))
                {
                    if (notify) MessageBox.Show("尚未找到 Codex 登录文件。", "Codex 账号", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                AccountMeta meta = store.ImportFile(PortablePaths.LiveAuth);
                popup.Rebuild();
                if (notify) MessageBox.Show("已安全登记：" + meta.Email + "\n\n凭据已使用 Windows DPAPI 加密保存。", "登记完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (notify) MessageBox.Show("登记失败：" + ex.Message, "Codex 账号", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void BeginNewLogin()
        {
            if (IsAccountMutationBusy(true)) return;
            if (loginTimer != null || (loginProcess != null && !loginProcess.HasExited))
            {
                MessageBox.Show("已有一个账号登录窗口正在等待完成，请先完成或关闭它。", "登录新账号", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                string codex = CliSetup.EnsureAvailable(popup);
                if(string.IsNullOrEmpty(codex))return;
                PortablePaths.Ensure();
                loginHome = Path.Combine(PortablePaths.LoginStaging, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(loginHome);
                if (string.IsNullOrWhiteSpace(codex)) throw new FileNotFoundException("未找到 codex 命令。");

                string command = "$env:CODEX_HOME='" + loginHome.Replace("'", "''") + "'; & '" + codex.Replace("'", "''") + "' login -c cli_auth_credentials_store=file; Write-Host ''; Write-Host '登录完成后可关闭此窗口。'; Read-Host";
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoLogo -NoProfile -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"");
                psi.UseShellExecute = true;
                loginProcess = Process.Start(psi);
                if (loginProcess == null) throw new InvalidOperationException("登录窗口未能启动。");
                loginStartedAt = DateTime.Now;
                if (loginTimer == null)
                {
                    loginTimer = new System.Windows.Forms.Timer();
                    loginTimer.Interval = 2000;
                    loginTimer.Tick += delegate { CheckLoginStaging(); };
                }
                loginTimer.Start();
                MessageBox.Show("已打开独立的 Codex 登录窗口。\n\n请在浏览器中登录要新增的 ChatGPT Plus 账号。该流程不会覆盖当前 Codex 账号。", "登录新账号", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                EndLoginSession(true);
                MessageBox.Show("无法启动登录：" + ex.Message, "Codex 账号", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CheckLoginStaging()
        {
            string stagingAuth = string.IsNullOrWhiteSpace(loginHome) ? "" : Path.Combine(loginHome, "auth.json");
            if (File.Exists(stagingAuth))
            {
                try
                {
                    AccountMeta meta = store.ImportFile(stagingAuth);
                    EndLoginSession(true);
                    popup.Rebuild();
                    MessageBox.Show("新账号已登记：" + meta.Email + "\n\n当前 Codex 账号未改变。", "登记完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                catch { }
            }
            bool loginExited = false;
            try { loginExited = loginProcess != null && loginProcess.HasExited; }
            catch { loginExited = true; }
            if (loginExited)
            {
                EndLoginSession(false);
                MessageBox.Show("登录窗口已关闭，但没有读取到有效账号。本次临时登录已清理。", "登录未完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (LoginAttemptExpired(loginStartedAt, DateTime.Now))
            {
                EndLoginSession(true);
                MessageBox.Show("登录等待已超过 15 分钟，本次临时登录已清理，请重新尝试。", "登录超时", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void EndLoginSession(bool stopProcess)
        {
            if (loginTimer != null) { loginTimer.Stop(); loginTimer.Dispose(); loginTimer = null; }
            Process process = loginProcess;
            loginProcess = null;
            if (process != null)
            {
                try
                {
                    if (stopProcess && !process.HasExited)
                    {
                        ProcessStartInfo stop = new ProcessStartInfo("taskkill.exe", "/PID " + process.Id.ToString(CultureInfo.InvariantCulture) + " /T /F");
                        stop.UseShellExecute = false;
                        stop.CreateNoWindow = true;
                        using (Process killer = Process.Start(stop)) { if (killer != null) killer.WaitForExit(5000); }
                    }
                }
                catch { try { if (!process.HasExited) process.Kill(); } catch { } }
                finally { process.Dispose(); }
            }
            string home = loginHome;
            loginHome = null;
            loginStartedAt = DateTime.MinValue;
            CleanupLoginHome(home);
        }

        internal static bool LoginAttemptExpired(DateTime startedAt, DateTime now)
        {
            return startedAt != DateTime.MinValue && (now - startedAt).TotalMinutes > 15;
        }

        internal static bool IsSafeLoginHome(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return false;
            try
            {
                string root = Path.GetFullPath(PortablePaths.LoginStaging).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
                Guid id;
                return string.Equals(Path.GetDirectoryName(full).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase)
                    && Guid.TryParseExact(Path.GetFileName(full), "N", out id);
            }
            catch { return false; }
        }

        internal static void CleanupLoginHome(string directory)
        {
            if (!IsSafeLoginHome(directory)) return;
            try { Directory.Delete(directory, true); }
            catch { }
        }

        private static string FindCodexCommand()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] candidates = { Path.Combine(appData, "npm", "codex.ps1"), Path.Combine(appData, "npm", "codex.cmd") };
            foreach (string candidate in candidates) if (File.Exists(candidate)) return candidate;
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    foreach (string file in new[] { "codex.exe", "codex.cmd", "codex.ps1" })
                    {
                        string path = Path.Combine(directory.Trim().Trim('"'), file);
                        if (File.Exists(path)) return path;
                    }
                }
                catch { }
            }
            throw new FileNotFoundException("本机未找到 Codex CLI。请先安装官方 Codex CLI，或先在 Codex 桌面端完成登录。");
        }

        public async Task RefreshAllAsync(bool userInitiated)
        {await RefreshTargetsAsync(null,userInitiated);}

        public async Task RefreshOneAsync(AccountMeta account)
        {
            if(account==null || !store.Settings.Accounts.Any(a=>a.Id==account.Id))return;
            await RefreshTargetsAsync(account.Id,true);
        }

        internal static AccountMeta[] SelectRefreshTargets(IEnumerable<AccountMeta> accounts,string targetId)
        {return accounts.Where(a=>targetId==null || a.Id==targetId).ToArray();}

        private async Task RefreshTargetsAsync(string targetId,bool userInitiated)
        {
            if (IsAccountMutationBusy(userInitiated)) return;
            if (!EnsureNetworkConsent()) return;
            if(!store.Settings.AccountUsageConsent && userInitiated)
            {
                store.Settings.AccountUsageConsent=MessageBox.Show("是否同时读取各账号云端累计 Token？\n\n仅通过官方 Codex CLI 向 OpenAI 的 account/usage/read 接口提交该账号已有访问凭据，不发送第三方、不创建对话。失败保留旧值；只在你手动刷新时查询。", "授权账号 Token 查询",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
                store.Save();
            }
            if(targetId==null)TryImportCurrent(false);
            refreshing = true;
            popup.SetBusy(true);
            AccountMeta[] snapshot=SelectRefreshTargets(store.Settings.Accounts,targetId);
            popup.BeginQuotaRefresh(snapshot);
            bool saved=false;
            try
            {
            foreach (AccountMeta meta in snapshot)
            {
                await RefreshAccountAsync(meta,delegate { return QuotaClient.FetchAsync(store.LoadRaw(meta)); },popup.SetQuotaRefreshState);
                if(store.Settings.AccountUsageConsent)await RefreshAccountTokensAsync(meta);
                if(store.Settings.SubscriptionReadConsent) { try { await AccountOnlineClient.RefreshSubscription(store,meta,CancellationToken.None); } catch { } }
            }
            store.Save();
            saved=true;
            if (userInitiated && store.Settings.Accounts.Count == 0) MessageBox.Show("请先登记账号。", "Codex 账号", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception)
            {
                MessageBox.Show("无法保存刷新结果。请检查安装目录是否可写。", "Codex 账号", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                refreshing = false;
                popup.SetBusy(false);
                popup.FinishQuotaRefresh(saved);
                popup.Rebuild();
            }
        }

        internal static async Task RefreshAccountAsync(AccountMeta meta,Func<Task<QuotaResult>> fetch,Action<string,string> report)
        {
            report(meta.Id,"updating");
            bool success=false;
            try
            {
                QuotaResult result=await fetch();
                if(result==null)throw new FormatException("额度数据不完整");

                string officialPlan=NormalizeOfficialPlan(result.Plan);
                bool officialPlanChanged=false;
                if(!string.IsNullOrEmpty(officialPlan))
                {
                    officialPlanChanged=!string.Equals(NormalizeOfficialPlan(meta.Plan),officialPlan,StringComparison.OrdinalIgnoreCase);
                    meta.OfficialPlan=officialPlan;
                    meta.Plan=officialPlan;
                }

                string effectiveOfficialPlan=!string.IsNullOrEmpty(officialPlan)?officialPlan:NormalizeOfficialPlan(!string.IsNullOrWhiteSpace(meta.OfficialPlan)?meta.OfficialPlan:meta.Plan);
                bool freePlan=string.Equals(effectiveOfficialPlan,"free",StringComparison.OrdinalIgnoreCase);
                bool proPlan=string.Equals(effectiveOfficialPlan,"pro",StringComparison.OrdinalIgnoreCase);
                bool primaryValid=!proPlan && ValidRemaining(result.PrimaryRemaining);
                bool secondaryValid=ValidRemaining(result.SecondaryRemaining);
                bool monthlyValid=freePlan && result.HasMonthlyLimit && ValidRemaining(result.MonthlyRemaining);
                bool anyQuotaValid=freePlan?monthlyValid:(primaryValid || secondaryValid);
                // The monthly personal allowance belongs to the official Free
                // plan. A successful non-Free response must not retain an old
                // Free monthly meter, even when its other quota fields are absent.
                if(!freePlan)ClearMonthlyCache(meta);
                if(anyQuotaValid)
                {
                    // A successful official snapshot is complete for the meters it
                    // advertises. Clear absent old meters so a monthly-only response
                    // cannot retain stale Plus short/weekly values (or vice versa).
                    ClearQuotaCache(meta);
                    if(primaryValid && !freePlan)
                    {
                        meta.PrimaryRemaining=result.PrimaryRemaining;
                        meta.PrimaryResetAt=result.PrimaryResetAt;
                    }
                    if(secondaryValid && !freePlan)
                    {
                        meta.SecondaryRemaining=result.SecondaryRemaining;
                        meta.SecondaryResetAt=result.SecondaryResetAt;
                    }
                    if(monthlyValid)
                    {
                        meta.MonthlyRemaining=result.MonthlyRemaining;
                        meta.MonthlyResetAt=result.MonthlyResetAt;
                        meta.HasMonthlyLimit=true;
                    }
                    meta.ResetCredits=freePlan?-1:result.ResetCredits;
                    if(freePlan)meta.ResetCreditDetails=new List<ResetCreditInfo>();
                    else if(result.ResetCredits>=0)meta.ResetCreditDetails=result.ResetCreditDetails;
                    meta.LastQuotaAt=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);
                    meta.LastError="";
                    success=true;
                }
                else
                {
                    if(freePlan || officialPlanChanged)
                    {
                        ClearQuotaCache(meta);
                        meta.LastError=PlanUpdatedQuotaUnavailable;
                        report(meta.Id,"partial");
                        return;
                    }
                    throw new FormatException("额度数据不完整");
                }
            }
            catch(Exception ex) { meta.LastError=FriendlyNetworkError(ex); }
            report(meta.Id,success?"success":"failed");
        }

        private static string NormalizeOfficialPlan(string plan)
        {return string.IsNullOrWhiteSpace(plan)?"":plan.Trim().ToLowerInvariant();}

        private static bool ValidRemaining(double value)
        {return !double.IsNaN(value) && value>=0 && value<=100;}

        private static void ClearQuotaCache(AccountMeta meta)
        {
            meta.PrimaryRemaining=-1;meta.SecondaryRemaining=-1;
            meta.PrimaryResetAt=0;meta.SecondaryResetAt=0;
            ClearMonthlyCache(meta);
            meta.ResetCredits=-1;meta.ResetCreditDetails=new List<ResetCreditInfo>();meta.LastQuotaAt="";
        }

        private static void ClearMonthlyCache(AccountMeta meta)
        {meta.MonthlyRemaining=-1;meta.MonthlyResetAt=0;meta.HasMonthlyLimit=false;}

        private async Task RefreshAccountTokensAsync(AccountMeta meta)
        {
            byte[] raw=null;
            try
            {
                raw=store.LoadRaw(meta);
                AccountUsageResult result=await AccountUsageClient.FetchAsync(raw,meta.Id,CancellationToken.None);
                if(!result.LifetimeTokens.HasValue)throw new InvalidDataException("账号尚未提供累计Token");
                meta.AccountLifetimeTokens=result.LifetimeTokens.Value;
                meta.AccountTokenUpdatedAt=result.FetchedAtUtc;meta.AccountTokenError="";
            }
            catch(Exception){meta.AccountTokenError="账号累计Token暂未读取成功，保留上次记录";}
            finally{if(raw!=null)Array.Clear(raw,0,raw.Length);}
        }

        private bool EnsureNetworkConsent()
        {
            if (store.Settings.NetworkConsent) return true;
            DialogResult answer = MessageBox.Show("只有你主动点击「刷新额度」时，程序才会把该账号的 access token 作为 Bearer 凭据，仅发送到：\n\nhttps://chatgpt.com/backend-api/wham/usage\nhttps://chatgpt.com/backend-api/wham/rate-limit-reset-credits\n\n凭据不会发往第三方，不会写入日志；程序也不会在后台自动刷新。\n\n是否允许？", "授权额度查询", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            store.Settings.NetworkConsentAsked = true;
            store.Settings.NetworkConsent = answer == DialogResult.Yes;
            store.Save();
            return store.Settings.NetworkConsent;
        }

        private static string FriendlyNetworkError(Exception ex)
        {
            if(ex is FormatException) return "返回数据不完整，请稍后重试";
            if(ex is TaskCanceledException) return "请求超时，请稍后重试";
            HttpRequestException http = ex as HttpRequestException;
            if (http != null && http.Message.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0) return "登录已过期，请重新登录";
            if (http != null && http.Message.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0) return "账号无权限或登录已失效";
            return "额度暂时无法刷新";
        }

        public async void SwitchAccount(AccountMeta target)
        {
            if (IsAccountMutationBusy(true)) return;
            string active = store.ActiveId();
            if (active == target.Id)
            {
                MessageBox.Show("这已经是当前 Codex 账号。", "Codex 账号", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult answer = MessageBox.Show("将切换到：\n" + target.Email + "\n\n程序会先加密备份当前登录，然后重启 Codex。正在运行的任务会被中断。\n\n确定继续？", "确认切换", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            if (switching) return;
            switching = true;
            bool credentialsWritten = false;
            string restartMarker = Path.Combine(PortablePaths.SwitchHelpers, Guid.NewGuid().ToString("N"));
            try
            {
                await Task.Run(delegate
                {
                    Directory.CreateDirectory(restartMarker);
                    WriteProcessIdentity(Path.Combine(restartMarker, "owner"));
                    ExecuteSwitchPreparation(
                        CreateCodexRestartPlan,
                        delegate { store.PrepareSwitch(target); },
                        delegate(CodexRestartPlan plan)
                        {
                            string appId = Convert.ToBase64String(Encoding.UTF8.GetBytes(plan.AppUserModelId ?? ""));
                            string executable = Convert.ToBase64String(Encoding.UTF8.GetBytes(plan.ExecutablePath ?? ""));
                            string targetId = Convert.ToBase64String(Encoding.UTF8.GetBytes(target.Id));
                            IndependentProcess.Start("--restart-helper " + IndependentProcess.Quote(appId) + " " + IndependentProcess.Quote(executable) + " " + IndependentProcess.Quote(targetId) + " " + IndependentProcess.Quote(restartMarker));
                        },
                        delegate { return WaitUntil(delegate { return File.Exists(Path.Combine(restartMarker, "ready")); }, 10000, 100); },
                        delegate { File.WriteAllText(Path.Combine(restartMarker, "go"), "go"); },
                        delegate { File.WriteAllText(Path.Combine(restartMarker, "cancel"), "cancel"); });

                    bool finished = WaitUntil(delegate
                    {
                        return File.Exists(Path.Combine(restartMarker, "done")) || File.Exists(Path.Combine(restartMarker, "error"));
                    }, 30000, 150);
                    credentialsWritten = File.Exists(Path.Combine(restartMarker, "written"));
                    if (!finished) throw new TimeoutException("切换助手超过 30 秒仍未返回结果。程序不会重复写入账号，请稍后查看 Codex 是否已经重开。");
                    string errorFile = Path.Combine(restartMarker, "error");
                    if (File.Exists(errorFile)) throw new InvalidOperationException(File.ReadAllText(errorFile, Encoding.UTF8));
                    File.WriteAllText(Path.Combine(restartMarker, "ack"), "ack");
                });
                store.Load();
                popup.Rebuild();
            }
            catch (Exception ex)
            {
                popup.Rebuild();
                popup.ShowNear(Cursor.Position);
                string prefix = credentialsWritten ? "账号登录已切换为 " + target.Email + "，但 Codex 自动重启失败：" : "切换失败：";
                MessageBox.Show(prefix + ex.Message + "\n\n原登录备份目录：" + PortablePaths.Backups, "Codex 账号", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (File.Exists(Path.Combine(restartMarker, "done")) || File.Exists(Path.Combine(restartMarker, "error")))
                {
                    try { File.WriteAllText(Path.Combine(restartMarker, "ack"), "ack"); }
                    catch { }
                    CleanupSwitchMarkerDirectory(restartMarker);
                }
                switching = false;
            }
        }

        internal static void ExecuteSwitchPreparation(
            Func<CodexRestartPlan> createPlan,
            Action prepareCredentials,
            Action<CodexRestartPlan> startHelper,
            Func<bool> helperReady,
            Action signalGo,
            Action signalCancel)
        {
            CodexRestartPlan plan = createPlan();
            prepareCredentials();
            bool helperStarted = false;
            bool committed = false;
            try
            {
                startHelper(plan);
                helperStarted = true;
                if (!helperReady()) throw new TimeoutException("账号切换助手未能在 10 秒内启动，尚未修改账号登录。");
                signalGo();
                committed = true;
            }
            finally
            {
                if (helperStarted && !committed)
                {
                    try { signalCancel(); }
                    catch { }
                }
            }
        }

        internal static void ExecuteRestartTransaction(Action stopCodex, Action writeCredentials, Action launchCodex)
        {
            stopCodex();
            try { writeCredentials(); }
            catch (Exception writeError)
            {
                try { launchCodex(); }
                catch (Exception launchError) { throw new AggregateException("账号写入失败，恢复启动 Codex 也失败。", writeError, launchError); }
                throw;
            }
            launchCodex();
        }

        internal static bool IsSafeSwitchMarkerDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return false;
            try
            {
                string root = Path.GetFullPath(PortablePaths.SwitchHelpers).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
                string parent = Path.GetDirectoryName(full);
                Guid id;
                return parent != null
                    && string.Equals(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase)
                    && Guid.TryParseExact(Path.GetFileName(full), "N", out id);
            }
            catch { return false; }
        }

        internal static void CleanupSwitchMarkerDirectory(string directory)
        {
            if (!IsSafeSwitchMarkerDirectory(directory)) return;
            foreach (string name in new[] { "owner", "helper", "ready", "go", "cancel", "stopped", "written", "done", "error", "ack" })
            {
                try { File.Delete(Path.Combine(directory, name)); }
                catch { }
            }
            try { Directory.Delete(directory, false); }
            catch { }
        }

        internal static void CleanupCompletedSwitchMarkers()
        {
            try
            {
                if (!Directory.Exists(PortablePaths.SwitchHelpers)) return;
                foreach (string directory in Directory.GetDirectories(PortablePaths.SwitchHelpers))
                {
                    if (!IsSafeSwitchMarkerDirectory(directory)) continue;
                    bool terminal = File.Exists(Path.Combine(directory, "done")) || File.Exists(Path.Combine(directory, "error")) || File.Exists(Path.Combine(directory, "cancel"));
                    if (terminal)
                    {
                        if (File.Exists(Path.Combine(directory, "ack")) || !IsProcessIdentityActive(Path.Combine(directory, "owner"))) CleanupSwitchMarkerDirectory(directory);
                    }
                    else if (!IsProcessIdentityActive(Path.Combine(directory, "helper")) && !IsSwitchMarkerFresh(directory, TimeSpan.FromMinutes(10))) CleanupSwitchMarkerDirectory(directory);
                }
            }
            catch { }
        }

        internal static void WriteProcessIdentity(string path)
        {
            using (Process process = Process.GetCurrentProcess())
                File.WriteAllText(path, process.Id.ToString(CultureInfo.InvariantCulture) + "|" + process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), Encoding.UTF8);
        }

        internal static bool IsProcessInstanceActive(int processId, long startTicks)
        {
            if (processId <= 0) return false;
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    if (process.HasExited) return false;
                    return startTicks <= 0 || process.StartTime.ToUniversalTime().Ticks == startTicks;
                }
            }
            catch { return false; }
        }

        internal static bool IsProcessIdentityActive(string path)
        {
            try
            {
                string[] parts = File.ReadAllText(path, Encoding.UTF8).Split('|');
                int processId;
                long startTicks;
                return parts.Length == 2
                    && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out processId)
                    && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out startTicks)
                    && IsProcessInstanceActive(processId, startTicks);
            }
            catch { return false; }
        }

        internal static bool IsSwitchMarkerFresh(string directory, TimeSpan maximumAge)
        {
            try { return DateTime.UtcNow - Directory.GetLastWriteTimeUtc(directory) <= maximumAge; }
            catch { return false; }
        }

        public void DeleteAccount(AccountMeta meta)
        {
            if (store.IsPreview) { store.Settings.Accounts.Remove(meta); popup.Rebuild(); return; }
            if (IsAccountMutationBusy(true)) return;
            if (store.ActiveId() == meta.Id)
            {
                MessageBox.Show("不能删除当前正在使用的账号。请先切换到另一个账号。", "Codex 账号", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show("删除已登记账号：\n" + meta.Email + "\n\n这会删除该账号在本机中的加密副本。", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                StaggerPlan.RemoveAccountArtifacts(meta.Id);
                store.Delete(meta);
                popup.Rebuild();
            }
            catch (Exception ex)
            {
                popup.Rebuild();
                MessageBox.Show("删除账号未完成：" + ex.Message + "\n\n账号仍显示时，请关闭占用相关文件的程序后重试。", "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool IsAccountMutationBusy(bool notify)
        {
            bool loginPending = loginTimer != null || (loginProcess != null && !loginProcess.HasExited);
            bool busy = switching || refreshing || loginPending || IsSwitchHelperBusy();
            if (busy && notify) MessageBox.Show("账号操作正在进行，请等待当前的登录、刷新或切换完成后再试。", "账号操作", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return busy;
        }

        internal static bool IsSwitchHelperBusy()
        {
            try
            {
                if (!Directory.Exists(PortablePaths.SwitchHelpers)) return false;
                foreach (string directory in Directory.GetDirectories(PortablePaths.SwitchHelpers))
                {
                    if (!IsSafeSwitchMarkerDirectory(directory)) continue;
                    if (File.Exists(Path.Combine(directory, "done")) || File.Exists(Path.Combine(directory, "error")) || File.Exists(Path.Combine(directory, "cancel"))) continue;
                    if (IsProcessIdentityActive(Path.Combine(directory, "helper"))) return true;
                    bool hasProgress = File.Exists(Path.Combine(directory, "ready")) || File.Exists(Path.Combine(directory, "go")) || File.Exists(Path.Combine(directory, "stopped")) || File.Exists(Path.Combine(directory, "written"));
                    if (hasProgress && IsSwitchMarkerFresh(directory, TimeSpan.FromMinutes(2))) return true;
                }
            }
            catch { }
            return false;
        }

        internal static bool IsDockInstanceRunning()
        {
            try
            {
                using (Mutex existing = Mutex.OpenExisting("Local\\CodexAccountDock.V3.SingleInstance")) return existing != null;
            }
            catch { return false; }
        }

        internal static CodexRestartPlan CreateCodexRestartPlan()
        {
            List<Process> initial = FindCodexProcessesForRestart();
            if (initial.Count == 0) throw new InvalidOperationException("没有找到正在运行的 Codex 桌面端，尚未修改账号登录。请先打开 Codex 后再试。");
            CodexRestartPlan plan = new CodexRestartPlan();
            plan.ProcessCount = initial.Count;
            foreach (Process process in initial)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(plan.AppUserModelId)) plan.AppUserModelId = Native.GetApplicationUserModelId(process);
                    if (string.IsNullOrWhiteSpace(plan.ExecutablePath) && process.MainModule != null) plan.ExecutablePath = process.MainModule.FileName;
                }
                catch { }
                finally { process.Dispose(); }
            }
            if (string.IsNullOrWhiteSpace(plan.AppUserModelId) && string.IsNullOrWhiteSpace(plan.ExecutablePath)) throw new InvalidOperationException("无法解析当前 Codex 的启动入口，尚未修改账号登录。请先从开始菜单重新打开 Codex 后再试。");
            return plan;
        }

        internal static async Task RestartCodexAsync(CodexRestartPlan plan)
        {
            await StopCodexAsync();
            await LaunchCodexAsync(plan);
        }

        internal static async Task StopCodexAsync()
        {
            await Task.Run(delegate
            {
                bool stopped = WaitUntil(delegate
                {
                    bool found = false;
                    foreach (Process process in FindCodexProcessesForRestart())
                    {
                        try { found = true; process.Kill(); }
                        catch { }
                        finally { process.Dispose(); }
                    }
                    return !found;
                }, 10000, 160);
                if (!stopped) throw new TimeoutException("Codex 在 10 秒内未完全退出。为避免误伤其他进程，已停止自动重试。");
            });
        }

        internal static async Task LaunchCodexAsync(CodexRestartPlan plan)
        {
            await Task.Delay(900); // Original version's shutdown settling time.
            bool launched = !string.IsNullOrWhiteSpace(plan.AppUserModelId) && Native.LaunchPackagedApp(plan.AppUserModelId);
            if (!launched && !string.IsNullOrWhiteSpace(plan.ExecutablePath) && File.Exists(plan.ExecutablePath))
            {
                try { Process.Start(new ProcessStartInfo(plan.ExecutablePath) { UseShellExecute = true }); launched = true; }
                catch { }
            }
            if (!launched) throw new InvalidOperationException("已关闭旧 Codex，但无法解析它的启动入口。请从开始菜单手动打开 Codex。");

            bool started = await Task.Run(delegate
            {
                return WaitUntil(delegate
                {
                    List<Process> running = FindCodexProcessesForRestart();
                    bool any = running.Count > 0;
                    foreach (Process process in running) process.Dispose();
                    return any;
                }, 12000, 200);
            });
            if (!started) throw new TimeoutException("已发出启动请求，但 12 秒内没有检测到新的 Codex 进程。请从开始菜单手动打开 Codex。");
        }

        internal static List<Process> FindCodexProcessesForRestart()
        {
            int ownPid = Process.GetCurrentProcess().Id;
            List<Process> result = new List<Process>();
            foreach (Process process in Process.GetProcessesByName("ChatGPT"))
            {
                bool keep = false;
                try { keep = ShouldRestartProcess(ownPid, process.Id, process.ProcessName, Native.IsCodexProcess(process)); }
                catch { }
                if (keep) result.Add(process); else process.Dispose();
            }
            return result;
        }

        internal static bool ShouldRestartProcess(int ownPid, int candidatePid, string processName, bool hasCodexIdentity)
        {
            return candidatePid != ownPid
                && string.Equals(processName, "ChatGPT", StringComparison.OrdinalIgnoreCase)
                && hasCodexIdentity;
        }

        internal static bool WaitUntil(Func<bool> condition, int timeoutMilliseconds, int pollMilliseconds)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            do
            {
                if (condition()) return true;
                if (elapsed.ElapsedMilliseconds >= timeoutMilliseconds) return false;
                Thread.Sleep(Math.Max(1, pollMilliseconds));
            }
            while (true);
        }

        private void SetStartup(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (enabled) key.SetValue("CodexAccountDock", "\"" + Application.ExecutablePath + "\" --tray");
                    else key.DeleteValue("CodexAccountDock", false);
                }
                store.Settings.StartWithWindows = enabled;
                store.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法更新开机自启：" + ex.Message, "Codex 账号", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CheckCreditReminders() {
            foreach(var a in store.Settings.Accounts) {
                if(a.ResetCredits<=0 || !string.IsNullOrEmpty(a.LastError))continue;
                foreach(var c in a.ResetCreditDetails??new List<ResetCreditInfo>()) {
                    int stage=c.Stage(DateTimeOffset.UtcNow);if(stage==0)continue;
                    string key=c.Id+"|"+c.ExpiresAt+"|"+stage;
                    if(a.ResetCreditNotices==null)a.ResetCreditNotices=new List<string>();
                    if(a.ResetCreditNotices.Contains(key))continue;
                    a.ResetCreditNotices.Add(key);
                    try {store.Save();}catch {a.ResetCreditNotices.Remove(key);return;}
                    tray.ShowBalloonTip(12000,"重置卡即将到期",a.Email+"\n到期："+c.LocalExpiry()+"\n依据上次查询，请刷新额度确认仍可用。",ToolTipIcon.Info);return;
                }
            }
        }
        private void Exit()
        {
            if(creditTimer!=null){creditTimer.Stop();creditTimer.Dispose();}
            DynamicBackground.Shutdown();
            StaggerPlan.Shutdown();
            EndLoginSession(true);
            if (renderTimer != null) { renderTimer.Stop(); renderTimer.Dispose(); renderTimer = null; }
            if (perfTimer != null) { perfTimer.Stop(); perfTimer.Dispose(); perfTimer = null; }
            if (hotkeyTestTimer != null) { hotkeyTestTimer.Stop(); hotkeyTestTimer.Dispose(); hotkeyTestTimer = null; }
            popup.AllowExit = true;
            popup.Close();
            tray.Visible = false;
            tray.Dispose();
            if (ownsAppIcon) appIcon.Dispose();
            ExitThread();
        }
    }

    internal sealed class LegacyAccountPopup : Form
    {
        private readonly DockContext context;
        private readonly AccountStore store;
        private readonly SmoothFlowPanel list;
        private readonly Label busy;
        private readonly Image headerIcon;
        private const int HotkeyId = 0xCA11;
        private bool hotkeyRegistered;
        private IntPtr hotkeyHandle;
        private Size preferredClientSize = new Size(520, 650);

        public LegacyAccountPopup(DockContext context, AccountStore store)
        {
            this.context = context;
            this.store = store;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            FormBorderStyle = FormBorderStyle.None;
            Text = "Codex Account Dock";
            AccessibleName = "Codex Account Dock";
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(242, 244, 249);
            ClientSize = preferredClientSize;
            Padding = new Padding(16);
            // Keep the flyout visible until the user closes it. This makes the
            // borderless window reliably draggable and avoids disappearing
            // when a notification or another desktop window briefly receives focus.

            Panel header = new Panel();
            header.Location = new Point(16, 12);
            header.Size = new Size(488, 64);
            // Keep the header opaque. Transparent child controls can expose the
            // uninitialized back buffer of a borderless window as black pixels.
            header.BackColor = BackColor;
            Controls.Add(header);

            Icon extracted = null;
            try { extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
            headerIcon = (extracted ?? SystemIcons.Application).ToBitmap();
            if (extracted != null) extracted.Dispose();
            PictureBox icon = new PictureBox();
            icon.Image = headerIcon;
            icon.SizeMode = PictureBoxSizeMode.Zoom;
            icon.Location = new Point(0, 2);
            icon.Size = new Size(52, 52);
            header.Controls.Add(icon);

            Label title = MakeLabel("全部账户", 21, FontStyle.Bold, Color.FromArgb(30, 34, 45));
            title.Location = new Point(66, 0);
            title.AutoSize = true;
            header.Controls.Add(title);
            Label sub = MakeLabel("自动保存每个登录账户的最后状态", 9, FontStyle.Bold, Color.FromArgb(100, 105, 118));
            sub.Location = new Point(68, 38);
            sub.AutoSize = true;
            header.Controls.Add(sub);
            RoundButton close = RoundButton.Create("×", Color.FromArgb(232, 235, 242), Color.FromArgb(58, 63, 75), 12);
            close.Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold);
            close.Size = new Size(44, 42);
            close.Location = new Point(440, 5);
            close.Click += delegate { Hide(); };
            header.Controls.Add(close);

            RoundButton add = RoundButton.Create("⊕  登录并登记新账户", Color.FromArgb(20, 88, 232), Color.White, 14);
            add.Location = new Point(18, 84);
            add.Size = new Size(484, 54);
            add.Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold);
            add.Click += delegate { context.BeginNewLogin(); };
            Controls.Add(add);

            RoundButton import = RoundButton.Create("登记当前账号", Color.FromArgb(225, 231, 244), Color.FromArgb(46, 64, 104), 9);
            import.Location = new Point(18, 148);
            import.Size = new Size(136, 32);
            import.Click += delegate { context.TryImportCurrent(true); };
            Controls.Add(import);
            RoundButton refresh = RoundButton.Create("↻  刷新额度", Color.FromArgb(225, 231, 244), Color.FromArgb(46, 64, 104), 9);
            refresh.Location = new Point(162, 148);
            refresh.Size = new Size(126, 32);
            refresh.Click += async delegate { await context.RefreshAllAsync(true); };
            Controls.Add(refresh);
            busy = MakeLabel("", 9, FontStyle.Bold, Color.FromArgb(20, 91, 230));
            busy.Location = new Point(302, 155);
            busy.AutoSize = true;
            Controls.Add(busy);

            list = new SmoothFlowPanel();
            list.Location = new Point(16, 190);
            list.Size = new Size(488, 440);
            list.FlowDirection = FlowDirection.TopDown;
            list.WrapContents = false;
            list.AutoScroll = true;
            list.BackColor = BackColor;
            list.Padding = new Padding(0, 0, 0, 4);
            Controls.Add(list);
            EnableDrag(header);
            // The popup starts hidden, so force creation of its native handle.
            // OnHandleCreated owns global hotkey registration.
            IntPtr startupHandle = Handle;
            if (!hotkeyRegistered)
            {
                hotkeyHandle = startupHandle;
                hotkeyRegistered = Native.RegisterHotKey(hotkeyHandle, HotkeyId, 0x0001 | 0x0002, (int)Keys.D);
            }
            FormClosed += delegate
            {
                headerIcon.Dispose();
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (UiDiagnostics.Enabled) UiDiagnostics.PopupPaint++;
            base.OnPaint(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HotkeyId) context.TogglePopup(Cursor.Position);
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.EnableRoundedCorners(Handle);
            hotkeyHandle = Handle;
            hotkeyRegistered = Native.RegisterHotKey(hotkeyHandle, HotkeyId, 0x0001 | 0x0002, (int)Keys.D);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (hotkeyRegistered)
            {
                Native.UnregisterHotKey(hotkeyHandle, HotkeyId);
                hotkeyRegistered = false;
            }
            hotkeyHandle = IntPtr.Zero;
            base.OnHandleDestroyed(e);
        }

        public void ShowNear(Point anchor)
        {
            Screen screen = Screen.FromPoint(anchor);
            Rectangle area = screen.WorkingArea;
            int margin = 8;
            FitToWorkingArea(area, margin);
            int x = anchor.X - Width;
            int y = anchor.Y + 4;
            if (y + Height + margin > area.Bottom) y = anchor.Y - Height - 4;
            SetLocationClamped(new Point(x, y), area, margin);
            WindowState = FormWindowState.Normal;
            Show();
            BringToFront();
            Activate();
            Native.ShowAndActivate(Handle);
        }

        public bool RunHotkeyMessageTest()
        {
            if (!hotkeyRegistered) return false;
            Native.SendHotkeyMessage(Handle, HotkeyId);
            return Visible && Screen.FromControl(this).WorkingArea.IntersectsWith(Bounds);
        }

        private void EnableDrag(Control control)
        {
            if (control == null || control is Button) return;
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    // WM_NCLBUTTONDOWN blocks until the native move loop ends.
                    // Do not touch Location while that loop is active; doing so
                    // makes every mouse move re-enter layout and causes a
                    // visible one-beat drag delay. Clamp only once afterwards.
                    Native.BeginWindowDrag(Handle);
                    ClampCurrentLocation();
                }
            };
            foreach (Control child in control.Controls) EnableDrag(child);
        }

        private void SetLocationClamped(Point desired, Rectangle area, int margin)
        {
            int minX = area.Left + margin;
            int minY = area.Top + margin;
            int maxX = area.Right - Width - margin;
            int maxY = area.Bottom - Height - margin;
            // A popup can be wider/taller than a very small working area. In
            // that case use the area itself as the valid range so no negative
            // upper bound can push it back outside the monitor.
            if (maxX < minX) { minX = area.Left; maxX = area.Right - Width; }
            if (maxY < minY) { minY = area.Top; maxY = area.Bottom - Height; }
            int x = Math.Max(minX, Math.Min(desired.X, maxX));
            int y = Math.Max(minY, Math.Min(desired.Y, maxY));
            Point next = new Point(x, y);
            if (Location == next) return;
            Location = next;
        }

        private void ClampCurrentLocation()
        {
            if (!IsHandleCreated || !Visible) return;
            Rectangle bounds = Bounds;
            Screen screen = Screen.FromRectangle(bounds);
            FitToWorkingArea(screen.WorkingArea, 8);
            SetLocationClamped(Location, screen.WorkingArea, 8);
        }

        private void FitToWorkingArea(Rectangle area, int margin)
        {
            int maxWidth = Math.Max(1, area.Width - margin * 2);
            int maxHeight = Math.Max(1, area.Height - margin * 2);
            int targetWidth = Math.Min(preferredClientSize.Width, maxWidth);
            int targetHeight = Math.Min(preferredClientSize.Height, maxHeight);
            if (ClientSize.Width == targetWidth && ClientSize.Height == targetHeight) return;
            ClientSize = new Size(targetWidth, targetHeight);
            int listWidth = Math.Max(1, ClientSize.Width - 32);
            int listHeight = Math.Max(1, ClientSize.Height - 206);
            list.Size = new Size(listWidth, listHeight);
        }

        public void SetBusy(bool value) { busy.Text = value ? "正在刷新…" : ""; }

        public void SavePreview(string path)
        {
            using (Bitmap bitmap = new Bitmap(Width, Height))
            {
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        public void Rebuild()
        {
            if (InvokeRequired) { BeginInvoke(new Action(Rebuild)); return; }
            list.SuspendLayout();
            while (list.Controls.Count > 0)
            {
                Control old = list.Controls[0];
                list.Controls.RemoveAt(0);
                old.Dispose();
            }
            string active = store.ActiveId();
            if (store.Settings.Accounts.Count == 0)
            {
                Label empty = MakeLabel("还没有登记账号\n\n点击「登记当前账号」，\n或登录另一个 ChatGPT Plus 账号。", 10, FontStyle.Bold, Color.FromArgb(105, 110, 123));
                empty.TextAlign = ContentAlignment.MiddleCenter;
                empty.Size = new Size(464, 160);
                list.Controls.Add(empty);
            }
            foreach (AccountMeta meta in store.Settings.Accounts) list.Controls.Add(BuildCard(meta, meta.Id == active));
            int desired = Math.Min(730, 206 + Math.Max(170, store.Settings.Accounts.Count * 152));
            preferredClientSize = new Size(520, desired);
            ClientSize = preferredClientSize;
            list.Size = new Size(488, desired - 206);
            // Do not assign a rounded Region here. Region + transparent
            // owner-drawn children produces black strips at the clipped edges
            // on some Windows/DPI combinations. The opaque form background and
            // DWM corner preference provide the rounded visual treatment.
            Region oldRegion = Region;
            Region = null;
            if (oldRegion != null) oldRegion.Dispose();
            list.ResumeLayout();
        }

        private Control BuildCard(AccountMeta meta, bool active)
        {
            RoundedPanel card = new RoundedPanel();
            card.Size = new Size(464, 142);
            card.Margin = new Padding(3, 4, 3, 7);
            card.FillColor = active ? Color.FromArgb(250, 255, 251) : Color.FromArgb(252, 252, 254);
            card.BorderColor = active ? Color.FromArgb(174, 225, 188) : Color.FromArgb(218, 221, 229);
            card.Radius = 16;
            card.Cursor = Cursors.Hand;

            AvatarControl avatar = new AvatarControl();
            avatar.Active = active;
            avatar.Location = new Point(14, 25);
            avatar.Size = new Size(48, 48);
            card.Controls.Add(avatar);
            Label email = MakeLabel(meta.Email, 10, FontStyle.Bold, Color.FromArgb(34, 38, 48));
            email.Location = new Point(74, 13); email.Size = new Size(240, 24); email.AutoEllipsis = true;
            card.Controls.Add(email);
            Label secondaryEmail = MakeLabel(meta.Email, 8, FontStyle.Bold, Color.FromArgb(102, 106, 118));
            secondaryEmail.Location = new Point(74, 42); secondaryEmail.Size = new Size(240, 22); secondaryEmail.AutoEllipsis = true;
            card.Controls.Add(secondaryEmail);
            TagPill plan = new TagPill();
            bool isFree=Palette.IsFree(meta);
            plan.Text = string.IsNullOrWhiteSpace(meta.Plan) ? "未知" : meta.Plan.ToUpperInvariant();
            plan.FillColor = isFree?Color.FromArgb(226,245,235):Color.FromArgb(249, 229, 249);
            plan.TextColor = isFree?Color.FromArgb(42,124,84):Color.FromArgb(151, 51, 151);
            plan.Location = new Point(264, 13); plan.Size = new Size(48, 21);
            card.Controls.Add(plan);
            bool hasMonthly=Palette.HasMonthly(meta);
            double headlineRemaining=Palette.MainRemaining(meta);
            string primaryText = Percent(headlineRemaining);
            Label big = MakeLabel(primaryText, 19, FontStyle.Bold, RemainingColor(headlineRemaining));
            big.Location = new Point(330, 10); big.Size = new Size(105, 38); big.TextAlign = ContentAlignment.MiddleRight;
            card.Controls.Add(big);
            Label status = MakeLabel(active ? "当前" : "历史", 8, FontStyle.Bold, active ? Color.FromArgb(37, 150, 78) : Color.FromArgb(103, 107, 118));
            status.Location = new Point(388, 50); status.Size = new Size(46, 20); status.TextAlign = ContentAlignment.MiddleRight;
            card.Controls.Add(status);
            string firstMeter=isFree?"月额度 "+Percent(hasMonthly?meta.MonthlyRemaining:-1):"↻ 距重置 " + ResetText(meta.PrimaryResetAt);
            PillControl five = Pill(firstMeter, Color.FromArgb(227, 237, 255), Color.FromArgb(42, 86, 177));
            five.Location = new Point(72, 84); five.Size = new Size(142, 32); card.Controls.Add(five);
            string weeklyText=isFree?"月重置 "+ResetText(hasMonthly?meta.MonthlyResetAt:0):(meta.SecondaryRemaining < 0 ? "周额度 --" : "周额度 " + Math.Round(meta.SecondaryRemaining).ToString("0", CultureInfo.InvariantCulture) + "%");
            if (!isFree && meta.SecondaryResetAt > 0) weeklyText += " · " + ResetText(meta.SecondaryResetAt);
            PillControl weekly = Pill((isFree?"":"♛ ") + weeklyText, Color.FromArgb(248, 226, 248), Color.FromArgb(149, 43, 151));
            weekly.Location = new Point(220, 84); weekly.Size = new Size(146, 32); card.Controls.Add(weekly);
            string trailing=isFree?(hasMonthly?"官方月额":"待刷新"):(meta.ResetCredits < 0 ? "可重置 --" : "可重置 " + meta.ResetCredits.ToString(CultureInfo.InvariantCulture) + "次");
            Label credits = MakeLabel(trailing, 7, FontStyle.Bold, Color.FromArgb(83, 86, 96));
            credits.Location = new Point(368, 88); credits.Size = new Size(66, 24); credits.TextAlign = ContentAlignment.MiddleCenter; card.Controls.Add(credits);
            RoundButton remove = RoundButton.Create("×", Color.FromArgb(255, 237, 237), Color.FromArgb(188, 65, 65), 8);
            remove.Location = new Point(434, 89); remove.Size = new Size(24, 26); remove.Click += delegate(object sender, EventArgs e) { context.DeleteAccount(meta); };
            card.Controls.Add(remove);
            if (!string.IsNullOrWhiteSpace(meta.LastError))
            {
                Label err = MakeLabel(meta.LastError, 8, FontStyle.Bold, Color.FromArgb(190, 64, 64));
                err.Location = new Point(74, 119); err.Size = new Size(340, 18); card.Controls.Add(err);
            }
            EventHandler switchHandler = delegate(object sender, EventArgs e) { if (!(sender is Button)) context.SwitchAccount(meta); };
            card.Click += switchHandler; avatar.Click += switchHandler; email.Click += switchHandler; secondaryEmail.Click += switchHandler; plan.Click += switchHandler; big.Click += switchHandler; status.Click += switchHandler; five.Click += switchHandler; weekly.Click += switchHandler; credits.Click += switchHandler;
            return card;
        }

        private static Label MakeLabel(string text, float size, FontStyle style, Color color)
        {
            return new Label { Text = text, Font = new Font("Microsoft YaHei UI", size, style), ForeColor = color, BackColor = Color.Transparent };
        }

        private static PillControl Pill(string text, Color back, Color fore)
        {
            PillControl label = new PillControl();
            label.Text = text; label.Font = new Font("Microsoft YaHei UI", 8, FontStyle.Bold); label.FillColor = back; label.TextColor = fore;
            return label;
        }

        private static string Percent(double value) { return value < 0 ? "--" : Math.Round(value).ToString("0", CultureInfo.InvariantCulture) + "%"; }
        private static Color RemainingColor(double value) { return value < 0 ? Color.Gray : value <= 10 ? Color.FromArgb(205, 55, 55) : value <= 35 ? Color.FromArgb(224, 137, 35) : Color.FromArgb(39, 157, 82); }
        private static string ResetText(long unix)
        {
            if (unix <= 0) return "--";
            TimeSpan span = DateTimeOffset.FromUnixTimeSeconds(unix) - DateTimeOffset.Now;
            if (span.TotalSeconds <= 0) return "已到";
            if (span.TotalDays >= 1) return ((int)span.TotalDays).ToString(CultureInfo.InvariantCulture) + "天" + span.Hours.ToString(CultureInfo.InvariantCulture) + "时";
            return ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture) + "时" + span.Minutes.ToString(CultureInfo.InvariantCulture) + "分";
        }
    }

    internal sealed class SmoothFlowPanel : FlowLayoutPanel
    {
        public SmoothFlowPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            UpdateStyles();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (UiDiagnostics.Enabled) UiDiagnostics.FlowPaint++;
            // FlowLayoutPanel is intentionally opaque: transparent flow
            // surfaces can leave black bands behind rounded child cards.
            e.Graphics.Clear(BackColor);
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        public Color FillColor = Color.White;
        public Color BorderColor = Color.FromArgb(218, 221, 229);
        public int Radius = 16;

        public RoundedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(242, 244, 249);
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (Parent != null) BackColor = Parent.BackColor;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (UiDiagnostics.Enabled) UiDiagnostics.CardBackground++;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (SolidBrush brush = new SolidBrush(FillColor))
            {
                e.Graphics.FillPath(brush, path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (UiDiagnostics.Enabled) UiDiagnostics.CardPaint++;
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (Pen pen = new Pen(BorderColor)) e.Graphics.DrawPath(pen, path);
        }
    }

    internal sealed class RoundButton : Button
    {
        private bool hovered;
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        public Color FillColor = Color.FromArgb(20, 88, 232);
        public int Radius = 12;

        public static RoundButton Create(string text, Color fill, Color fore, int radius)
        {
            RoundButton button = new RoundButton();
            button.Text = text;
            button.FillColor = fill;
            button.ForeColor = fore;
            button.Radius = radius;
            return button;
        }

        public RoundButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Opaque, true);
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (Parent == null) return;
            RoundedPanel rounded = Parent as RoundedPanel;
            BackColor = rounded == null ? Parent.BackColor : rounded.FillColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (UiDiagnostics.Enabled) UiDiagnostics.ButtonPaint++;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (FindForm() is AccountPopup || DailyNews.IsGlassWindow(FindForm()))
            {
                GlassBackdrop.Draw(e.Graphics, this);
                AccountRow row = Parent as AccountRow;
                if (row != null) row.DrawGlass(e.Graphics,this);
            }
            else e.Graphics.Clear(BackColor);
            Color fill = Enabled ? (hovered ? ControlPaint.Light(FillColor, 0.12f) : FillColor) : Color.FromArgb(220, 222, 228);
            bool liquidButton = (FindForm() is AccountPopup || DailyNews.IsGlassWindow(FindForm())) && !Palette.Solid;
            bool primaryButton = FillColor.R < 120 && FillColor.G < 150 && FillColor.B > 160;
            Color top = Color.FromArgb(liquidButton ? (primaryButton ? 239 : 142) : 255, ControlPaint.Light(fill, 0.10f));
            Color bottom = Color.FromArgb(liquidButton ? (primaryButton ? 225 : 92) : 255, fill);
            if (liquidButton)
            {
                top = Color.FromArgb(hovered ? 190 : 155, Palette.ButtonTop);
                bottom = Color.FromArgb(190, Palette.ButtonBottom);
            }
            using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (LinearGradientBrush brush = new LinearGradientBrush(ClientRectangle, top, bottom, 90f))
            {
                e.Graphics.FillPath(brush, path);
                using (Pen edge = new Pen(Color.FromArgb(157, 250, 253, 255))) e.Graphics.DrawPath(edge, path);
            }
            if (liquidButton && (Text == "−" || Text == "×"))
            {
                float x = (Width - 1) / 2f, y = (Height - 1) / 2f;
                using (Pen symbol = new Pen(ForeColor, 1.8f))
                {
                    if (Text == "−") e.Graphics.DrawLine(symbol, x-6, y, x+6, y);
                    else { e.Graphics.DrawLine(symbol, x-5, y-5, x+5, y+5); e.Graphics.DrawLine(symbol, x+5, y-5, x-5, y+5); }
                }
            }
            else
            {
                string icon=Text.Contains("添加账户")?"person":Text=="登记当前"?"document":Text=="刷新额度"?"refresh":Text=="AI 日报"?"news":Text=="重置提醒"?"bell":Text=="错峰计划"?"calendar":null;
                Rectangle textBox=ClientRectangle;
                if(icon!=null && Width>130)
                {
                    string caption=Text.Replace("＋ ","");
                    int tw=TextRenderer.MeasureText(caption,Font).Width;
                    int x=Math.Max(10,(Width-tw-32)/2);
                    Palette.Icon(e.Graphics,icon,new Rectangle(x,(Height-22)/2,22,22));
                    textBox=new Rectangle(x+28,0,tw+4,Height);
                    TextRenderer.DrawText(e.Graphics,caption,Font,textBox,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
                }
                else TextRenderer.DrawText(e.Graphics,Text,Font,textBox,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            }
        }
    }

    internal class PillControl : Label
    {
        public Color FillColor = Color.FromArgb(227, 237, 255);
        public Color TextColor = Color.FromArgb(42, 86, 177);

        public PillControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            BackColor = Color.White;
            TextAlign = ContentAlignment.MiddleCenter;
            Cursor = Cursors.Hand;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (Parent == null) return;
            RoundedPanel rounded = Parent as RoundedPanel;
            BackColor = rounded == null ? Parent.BackColor : rounded.FillColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (UiDiagnostics.Enabled) UiDiagnostics.PillPaint++;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
            using (SolidBrush brush = new SolidBrush(FillColor)) e.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, TextColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class TagPill : PillControl { }

    internal sealed class AvatarControl : Control
    {
        public bool Active;

        public AvatarControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            BackColor = Color.White;
            Cursor = Cursors.Hand;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (Parent == null) return;
            RoundedPanel rounded = Parent as RoundedPanel;
            BackColor = rounded == null ? Parent.BackColor : rounded.FillColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (UiDiagnostics.Enabled) UiDiagnostics.AvatarPaint++;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            Color fill = Active ? Color.FromArgb(42, 181, 87) : Color.FromArgb(43, 99, 210);
            using (SolidBrush outer = new SolidBrush(fill)) e.Graphics.FillEllipse(outer, 1, 1, Width - 3, Height - 3);
            using (SolidBrush white = new SolidBrush(Color.White))
            {
                e.Graphics.FillEllipse(white, Width * 0.34f, Height * 0.20f, Width * 0.32f, Height * 0.32f);
                e.Graphics.FillEllipse(white, Width * 0.21f, Height * 0.50f, Width * 0.58f, Height * 0.33f);
            }
            if (Active)
            {
                using (Pen pen = new Pen(Color.White, 2.2f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(pen, new[] { new PointF(6, 33), new PointF(11, 38), new PointF(20, 27) });
                }
            }
        }
    }

    internal static class UiDiagnostics
    {
        public static bool Enabled;
        public static long PopupPaint;
        public static long FlowPaint;
        public static long CardBackground;
        public static long CardPaint;
        public static long ButtonPaint;
        public static long PillPaint;
        public static long AvatarPaint;
        private static readonly Dictionary<int, long> Messages = new Dictionary<int, long>();

        public static void RecordMessage(int message)
        {
            long count;
            Messages.TryGetValue(message, out count);
            Messages[message] = count + 1;
        }

        public static string Report()
        {
            if (!Enabled) return "";
            return " popup_paint=" + PopupPaint.ToString(CultureInfo.InvariantCulture)
                + " flow_paint=" + FlowPaint.ToString(CultureInfo.InvariantCulture)
                + " card_background=" + CardBackground.ToString(CultureInfo.InvariantCulture)
                + " card_paint=" + CardPaint.ToString(CultureInfo.InvariantCulture)
                + " button_paint=" + ButtonPaint.ToString(CultureInfo.InvariantCulture)
                + " pill_paint=" + PillPaint.ToString(CultureInfo.InvariantCulture)
                + " avatar_paint=" + AvatarPaint.ToString(CultureInfo.InvariantCulture)
                + " messages=" + string.Join(",", Messages.OrderByDescending(delegate(KeyValuePair<int, long> pair) { return pair.Value; }).Take(8).Select(delegate(KeyValuePair<int, long> pair) { return "0x" + pair.Key.ToString("X", CultureInfo.InvariantCulture) + ":" + pair.Value.ToString(CultureInfo.InvariantCulture); }).ToArray());
        }
    }

    internal sealed class DiagnosticMessageFilter : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            UiDiagnostics.RecordMessage(m.Msg);
            return false;
        }
    }

    internal static class Native
    {
        private const int WmNcLButtonDown = 0x00A1;
        private const int HtCaption = 2;
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, int modifiers, int key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int valueSize);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFullName(IntPtr process, ref int packageFullNameLength, StringBuilder packageFullName);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(IntPtr process, ref int applicationUserModelIdLength, StringBuilder applicationUserModelId);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr ShellExecute(IntPtr window, string operation, string file, string parameters, string directory, int showCommand);

        public static void BeginWindowDrag(IntPtr window)
        {
            if (window == IntPtr.Zero) return;
            ReleaseCapture();
            SendMessage(window, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
        }

        public static bool IsForegroundWindow(IntPtr window)
        {
            return window != IntPtr.Zero && GetForegroundWindow() == window;
        }

        public static void ShowAndActivate(IntPtr window)
        {
            if (window == IntPtr.Zero) return;
            const int SwRestore = 9;
            const uint SwpNoSize = 0x0001;
            const uint SwpNoMove = 0x0002;
            const uint SwpShowWindow = 0x0040;
            ShowWindow(window, SwRestore);
            SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpShowWindow);
            SetForegroundWindow(window);
        }

        public static void SendHotkeyMessage(IntPtr window, int hotkeyId)
        {
            if (window == IntPtr.Zero) return;
            SendMessage(window, 0x0312, new IntPtr(hotkeyId), IntPtr.Zero);
        }

        public static void EnableRoundedCorners(IntPtr window)
        {
            if (window == IntPtr.Zero) return;
            try
            {
                const int DwmWindowCornerPreference = 33;
                const int DwmWindowBorderColor = 34;
                const int Round = 2;
                int preference = Round;
                DwmSetWindowAttribute(window, DwmWindowCornerPreference, ref preference, sizeof(int));
                // DWMWA_COLOR_NONE (0xFFFFFFFE) lets the borderless form's
                // own background reach the rounded edge without a light DWM
                // outline showing around it.
                int colorNone = -2;
                DwmSetWindowAttribute(window, DwmWindowBorderColor, ref colorNone, sizeof(int));
            }
            catch { }
        }

        public static bool IsCodexProcess(Process process)
        {
            const uint QueryLimitedInformation = 0x1000;
            const int InsufficientBuffer = 122;
            IntPtr handle = OpenProcess(QueryLimitedInformation, false, process.Id);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    int length = 0;
                    int first = GetPackageFullName(handle, ref length, null);
                    if (first == InsufficientBuffer && length > 0)
                    {
                        StringBuilder package = new StringBuilder(length);
                        int second = GetPackageFullName(handle, ref length, package);
                        if (second == 0 && package.ToString().StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
                finally { CloseHandle(handle); }
            }
            try
            {
                string path = process.MainModule == null ? "" : process.MainModule.FileName;
                return path.IndexOf("\\WindowsApps\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 && path.EndsWith("\\app\\ChatGPT.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static string GetApplicationUserModelId(Process process)
        {
            const uint QueryLimitedInformation = 0x1000;
            const int InsufficientBuffer = 122;
            IntPtr handle = OpenProcess(QueryLimitedInformation, false, process.Id);
            if (handle == IntPtr.Zero) return "";
            try
            {
                int length = 0;
                int first = GetApplicationUserModelId(handle, ref length, null);
                if (first != InsufficientBuffer || length <= 0) return "";
                StringBuilder value = new StringBuilder(length);
                return GetApplicationUserModelId(handle, ref length, value) == 0 ? value.ToString() : "";
            }
            finally { CloseHandle(handle); }
        }

        public static bool LaunchPackagedApp(string appUserModelId)
        {
            if (string.IsNullOrWhiteSpace(appUserModelId)) return false;
            IntPtr result = ShellExecute(IntPtr.Zero, "open", "shell:AppsFolder\\" + appUserModelId, null, null, 1);
            return result.ToInt64() > 32;
        }
    }

    internal static class GraphicsExtensions
    {
        public static Color GetBackdropColor(Control control, Color fallback)
        {
            Control parent = control == null ? null : control.Parent;
            while (parent != null)
            {
                RoundedPanel rounded = parent as RoundedPanel;
                if (rounded != null) return rounded.FillColor;
                if (parent.BackColor != Color.Transparent) return parent.BackColor;
                parent = parent.Parent;
            }
            return fallback == Color.Transparent ? Color.White : fallback;
        }

        public static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            int diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle bounds, int radius)
        {
            using (GraphicsPath path = CreateRoundedPath(bounds, radius)) graphics.DrawPath(pen, path);
        }
    }

    internal static class SelfTest
    {
        public static int ProPlan()
        {
            try
            {
                string header=Base64Url("{\"alg\":\"none\"}");
                string claims=Base64Url("{\"email\":\"pro.fixture@example.com\",\"https://api.openai.com/auth\":{\"chatgpt_plan_type\":\"pro\",\"chatgpt_account_id\":\"pro-fixture\"}}");
                foreach(bool inAccess in new[]{false,true})
                {
                    string empty=Base64Url("{\"email\":\"pro.fixture@example.com\"}");
                    string auth="{\"tokens\":{\"id_token\":\""+header+"."+(inAccess?empty:claims)+".x\",\"access_token\":\""+header+"."+(inAccess?claims:empty)+".x\"}}";
                    AuthIdentity identity=AuthCodec.Parse(Encoding.UTF8.GetBytes(auth));
                    var registered=new AccountMeta();AccountStore.ApplyImportedIdentity(registered,identity);
                    if(registered.Email!="pro.fixture@example.com" || !Palette.IsPro(registered))throw new Exception("Pro auth registration");
                }
                foreach(string position in new[]{"primary_window","secondary_window"})
                foreach(int used in new[]{0,29,100})
                {
                    string body="{\"plan_type\":\"pro\",\"rate_limit\":{\""+position+"\":{\"used_percent\":"+used+",\"limit_window_seconds\":604800,\"reset_at\":4102444800}}}";
                    var meta=new AccountMeta{Plan="plus",PrimaryRemaining=0,SecondaryRemaining=10};
                    DockContext.RefreshAccountAsync(meta,delegate{return Task.FromResult(QuotaClient.ParseResponse(body));},delegate{}).GetAwaiter().GetResult();
                    if(!Palette.IsPro(meta) || meta.PrimaryRemaining!=-1 || Palette.HasPrimary(meta) || Palette.MainRemaining(meta)!=100-used || meta.SecondaryResetAt!=4102444800 || Palette.MainQuotaLabel(meta)!="周额度剩余" || !string.IsNullOrEmpty(meta.LastError))throw new Exception("Pro weekly refresh "+position);
                    foreach(bool detailed in new[]{false,true})if(Palette.VisibleResetAt(meta,detailed)!=4102444800 || Palette.VisibleResetLabel(meta,detailed)!="周")throw new Exception("Pro reset clock");
                    if(Palette.AllPresentQuotaAvailable(meta)!=(used<100))throw new Exception("Pro availability");
                }
                var unknown=new AccountMeta{Plan="pro",PrimaryRemaining=90,PrimaryResetAt=123};
                if(Palette.MainRemaining(unknown)!=-1 || Palette.HasAnyQuota(unknown) || Palette.VisibleResetAt(unknown,false)!=0)throw new Exception("Pro missing weekly incorrectly inferred");
                var cached=new AccountMeta{Plan="pro",PrimaryRemaining=0,SecondaryRemaining=64,SecondaryResetAt=4102444800};
                if(!Palette.AllPresentQuotaAvailable(cached))throw new Exception("Pro stale short cache blocks availability");
                DockContext.RefreshAccountAsync(cached,delegate{return Task.FromException<QuotaResult>(new InvalidOperationException("fixture network failure"));},delegate{}).GetAwaiter().GetResult();
                if(Palette.MainRemaining(cached)!=64 || string.IsNullOrEmpty(cached.LastError))throw new Exception("Pro failed refresh cache");
                var plus=new AccountMeta{Plan="plus",PrimaryRemaining=70,SecondaryRemaining=37};
                var free=new AccountMeta{Plan="free",HasMonthlyLimit=true,MonthlyRemaining=83};
                if(Palette.MainRemaining(plus)!=70 || !Palette.HasPrimary(plus) || Palette.MainRemaining(free)!=83 || Palette.HasPrimary(free))throw new Exception("Plus/Free regression");
                Console.WriteLine("PRO_PLAN_OK id_and_access_claim_registration weekly_in_either_window zero_full missing cached_failure compact_and_detailed plus_free no_network no_real_credentials");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine("PRO_PLAN_FAILED "+ex.Message);return 1;}
        }
        public static int RefreshFeedback()
        {
            try
            {
                QuotaResult monthly100=QuotaClient.ParseResponse("{\"plan_type\":\"free\",\"spend_control\":{\"individual_limit\":{\"remaining_percent\":100,\"reset_at\":4102444800}}}");
                if(!monthly100.HasMonthlyLimit || monthly100.MonthlyRemaining!=100 || monthly100.MonthlyResetAt!=4102444800 || monthly100.PrimaryRemaining!=-1 || monthly100.SecondaryRemaining!=-1)throw new Exception("spend monthly 100 parser");
                QuotaResult monthly0=QuotaClient.ParseResponse("{\"plan_type\":\"free\",\"spend_control\":{\"individual_limit\":{\"remaining_percent\":0,\"reset_after_seconds\":3600}}}");
                if(!monthly0.HasMonthlyLimit || monthly0.MonthlyRemaining!=0 || monthly0.MonthlyResetAt<=DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new Exception("spend monthly zero parser");
                foreach(string body in new[]{
                    "{\"spend_control\":{\"individual_limit\":{\"reset_at\":4102444800}}}",
                    "{\"spend_control\":{\"individual_limit\":{\"remaining_percent\":null}}}",
                    "{\"spend_control\":{\"individual_limit\":{\"remaining_percent\":\"bad\"}}}",
                    "{\"spend_control\":{\"individual_limit\":{\"remaining_percent\":101}}}"
                })
                {
                    QuotaResult unknownMonthly=QuotaClient.ParseResponse(body);
                    if(unknownMonthly.HasMonthlyLimit || unknownMonthly.MonthlyRemaining!=-1 || unknownMonthly.MonthlyResetAt!=0)throw new Exception("unknown spend monthly became a meter");
                }
                QuotaResult durationWindows=QuotaClient.ParseResponse("{\"rate_limit\":{\"primary_window\":{\"used_percent\":20,\"limit_window_seconds\":18000,\"reset_at\":101},\"secondary_window\":{\"used_percent\":30,\"limit_window_seconds\":604800,\"reset_at\":202}}}");
                if(durationWindows.PrimaryRemaining!=80 || durationWindows.PrimaryResetAt!=101 || durationWindows.SecondaryRemaining!=70 || durationWindows.SecondaryResetAt!=202 || durationWindows.HasMonthlyLimit)throw new Exception("duration window classification");
                QuotaResult longPrimary=QuotaClient.ParseResponse("{\"rate_limit\":{\"primary_window\":{\"used_percent\":25,\"limit_window_seconds\":2592000,\"reset_at\":303},\"secondary_window\":{\"used_percent\":40,\"limit_window_seconds\":604800,\"reset_at\":404}}}");
                if(longPrimary.PrimaryRemaining!=-1 || longPrimary.SecondaryRemaining!=60 || !longPrimary.HasMonthlyLimit || longPrimary.MonthlyRemaining!=75 || longPrimary.MonthlyResetAt!=303)throw new Exception("monthly primary misclassified");
                QuotaResult spendPreferred=QuotaClient.ParseResponse("{\"rate_limit\":{\"primary_window\":{\"used_percent\":50,\"limit_window_seconds\":2419200,\"reset_at\":505}},\"spend_control\":{\"individual_limit\":{\"remaining_percent\":83,\"reset_at\":606}}}");
                if(!spendPreferred.HasMonthlyLimit || spendPreferred.MonthlyRemaining!=83 || spendPreferred.MonthlyResetAt!=606)throw new Exception("spend monthly not preferred");
                QuotaResult threeMeters=QuotaClient.ParseResponse("{\"plan_type\":\"free\",\"rate_limit\":{\"primary_window\":{\"used_percent\":10,\"limit_window_seconds\":18000,\"reset_at\":701},\"secondary_window\":{\"used_percent\":40,\"limit_window_seconds\":604800,\"reset_at\":702}},\"spend_control\":{\"individual_limit\":{\"remaining_percent\":77,\"reset_at\":703}}}");
                if(threeMeters.PrimaryRemaining!=90 || threeMeters.SecondaryRemaining!=60 || !threeMeters.HasMonthlyLimit || threeMeters.MonthlyRemaining!=77)throw new Exception("three meter parser");
                QuotaResult legacyWindows=QuotaClient.ParseResponse("{\"rate_limit\":{\"primary_window\":{\"used_percent\":12},\"secondary_window\":{\"used_percent\":34}}}");
                if(legacyWindows.PrimaryRemaining!=88 || legacyWindows.SecondaryRemaining!=66 || legacyWindows.HasMonthlyLimit)throw new Exception("legacy positional fallback");

                var meta=new AccountMeta { Id="fixture", Plan="plus", OfficialPlan="plus", PrimaryRemaining=70, SecondaryRemaining=37, MonthlyRemaining=64, PrimaryResetAt=111, SecondaryResetAt=222, MonthlyResetAt=333, HasMonthlyLimit=true, LastQuotaAt="cached", ResetCredits=2, SubscriptionExpiresOn="2001-01-01" };
                var events=new List<string>();
                Action<string,string> report=delegate(string id,string state) { events.Add(id+":"+state); };
                Func<Task<QuotaResult>> denied=delegate { throw new HttpRequestException("401"); };
                DockContext.RefreshAccountAsync(meta,denied,report).GetAwaiter().GetResult();
                if(meta.Plan!="plus" || meta.PrimaryRemaining!=70 || meta.SecondaryRemaining!=37 || meta.MonthlyRemaining!=64 || !meta.HasMonthlyLimit || meta.LastQuotaAt!="cached" || meta.ResetCredits!=2 || meta.PrimaryResetAt!=111 || meta.LastError!="登录已过期，请重新登录") throw new Exception("failed query changed plan or cache");

                DockContext.RefreshAccountAsync(meta,delegate { return Task.FromResult(new QuotaResult { Plan="plus" }); },report).GetAwaiter().GetResult();
                if(meta.PrimaryRemaining!=70 || meta.SecondaryRemaining!=37 || meta.MonthlyRemaining!=-1 || meta.HasMonthlyLimit || meta.MonthlyResetAt!=0 || meta.LastQuotaAt!="cached" || meta.SecondaryResetAt!=222 || meta.OfficialPlan!="plus") throw new Exception("same-plan Plus response retained Free-only monthly meter");

                DockContext.RefreshAccountAsync(meta,delegate { return Task.FromResult(new QuotaResult { Plan="plus",MonthlyRemaining=91,MonthlyResetAt=444,HasMonthlyLimit=true }); },report).GetAwaiter().GetResult();
                if(meta.PrimaryRemaining!=70 || meta.SecondaryRemaining!=37 || meta.PrimaryResetAt!=111 || meta.SecondaryResetAt!=222 || meta.HasMonthlyLimit || meta.MonthlyRemaining!=-1 || meta.MonthlyResetAt!=0 || meta.ResetCredits!=2 || meta.LastQuotaAt!="cached")throw new Exception("Plus monthly-only response replaced short and weekly cache");

                DockContext.RefreshAccountAsync(meta,delegate { return Task.FromResult(new QuotaResult { Plan="free" }); },report).GetAwaiter().GetResult();
                if(meta.Plan!="free" || meta.OfficialPlan!="free" || meta.PrimaryRemaining!=-1 || meta.SecondaryRemaining!=-1 || meta.MonthlyRemaining!=-1 || meta.HasMonthlyLimit || meta.PrimaryResetAt!=0 || meta.SecondaryResetAt!=0 || meta.MonthlyResetAt!=0 || meta.ResetCredits!=-1 || meta.LastQuotaAt!="" || meta.LastError!=DockContext.PlanUpdatedQuotaUnavailable) throw new Exception("free downgrade did not clear Plus quota cache");

                AccountStore.ApplyImportedIdentity(meta,new AuthIdentity {Email="fixture@example.com",Plan="plus",AccountId="acct",UserId="user"});
                if(meta.Plan!="free" || meta.OfficialPlan!="free")throw new Exception("stale auth claim overwrote authoritative Free plan");

                DockContext.RefreshAccountAsync(meta,delegate { return Task.FromResult(new QuotaResult { Plan="free",PrimaryRemaining=61,PrimaryResetAt=555,ResetCredits=9 }); },report).GetAwaiter().GetResult();
                if(meta.Plan!="free" || meta.PrimaryRemaining!=-1 || meta.SecondaryRemaining!=-1 || meta.MonthlyRemaining!=-1 || meta.HasMonthlyLimit || meta.PrimaryResetAt!=0 || meta.SecondaryResetAt!=0 || meta.ResetCredits!=-1 || meta.LastQuotaAt!="" || meta.LastError!=DockContext.PlanUpdatedQuotaUnavailable) throw new Exception("Free short window was mistaken for monthly quota");

                DockContext.RefreshAccountAsync(meta,delegate { return Task.FromResult(new QuotaResult { Plan="free",PrimaryRemaining=61,PrimaryResetAt=555,MonthlyRemaining=100,MonthlyResetAt=777,HasMonthlyLimit=true,ResetCredits=4 }); },report).GetAwaiter().GetResult();
                if(meta.PrimaryRemaining!=-1 || meta.SecondaryRemaining!=-1 || meta.PrimaryResetAt!=0 || !meta.HasMonthlyLimit || meta.MonthlyRemaining!=100 || meta.MonthlyResetAt!=777 || meta.ResetCredits!=-1 || meta.LastError!="")throw new Exception("Free monthly snapshot was not isolated from short-week meters");

                DockContext.RefreshAccountAsync(meta,delegate { return Task.FromResult(new QuotaResult { Plan="free" }); },report).GetAwaiter().GetResult();
                if(meta.PrimaryRemaining!=-1 || meta.SecondaryRemaining!=-1 || meta.MonthlyRemaining!=-1 || meta.HasMonthlyLimit || meta.PrimaryResetAt!=0 || meta.SecondaryResetAt!=0 || meta.MonthlyResetAt!=0 || meta.ResetCredits!=-1 || meta.LastQuotaAt!="" || meta.LastError!=DockContext.PlanUpdatedQuotaUnavailable)throw new Exception("later missing Free windows left stale Free quota");

                DockContext.RefreshAccountAsync(meta,delegate { return Task.FromResult(new QuotaResult { Plan="plus",PrimaryRemaining=0,SecondaryRemaining=88,MonthlyRemaining=79,PrimaryResetAt=333,SecondaryResetAt=444,MonthlyResetAt=888,HasMonthlyLimit=true,ResetCredits=1 }); },report).GetAwaiter().GetResult();
                if(meta.PrimaryRemaining!=0 || meta.SecondaryRemaining!=88 || meta.HasMonthlyLimit || meta.MonthlyRemaining!=-1 || meta.MonthlyResetAt!=0 || meta.Plan!="plus" || meta.OfficialPlan!="plus" || meta.LastError!="" || meta.PrimaryResetAt!=333 || meta.ResetCredits!=1 || meta.SubscriptionExpiresOn!="2001-01-01") throw new Exception("renewed Plus did not keep short-week-only view independently of manual expiry");

                DockContext.RefreshAccountAsync(meta,delegate { return Task.FromResult(new QuotaResult { Plan="plus",PrimaryRemaining=42,SecondaryRemaining=52,PrimaryResetAt=901,SecondaryResetAt=902,ResetCredits=0 }); },report).GetAwaiter().GetResult();
                if(meta.PrimaryRemaining!=42 || meta.SecondaryRemaining!=52 || meta.HasMonthlyLimit || meta.MonthlyRemaining!=-1 || meta.MonthlyResetAt!=0)throw new Exception("fresh response missing monthly retained stale monthly meter");

                var noAuthority=new AccountMeta {Id="no-authority",Plan="plus",PrimaryRemaining=44,SecondaryRemaining=55,PrimaryResetAt=11,SecondaryResetAt=22,ResetCredits=3,LastQuotaAt="cached"};
                DockContext.RefreshAccountAsync(noAuthority,delegate{return Task.FromResult(new QuotaResult());},report).GetAwaiter().GetResult();
                if(noAuthority.Plan!="plus" || noAuthority.PrimaryRemaining!=44 || noAuthority.SecondaryRemaining!=55 || noAuthority.ResetCredits!=3)throw new Exception("missing official plan inferred downgrade or changed partial cache");

                var firstAuthority=new AccountMeta {Id="first-authority",Plan="plus",PrimaryRemaining=31,SecondaryRemaining=41,MonthlyRemaining=51,HasMonthlyLimit=true,PrimaryResetAt=51,SecondaryResetAt=61,MonthlyResetAt=71,ResetCredits=2,LastQuotaAt="cached"};
                DockContext.RefreshAccountAsync(firstAuthority,delegate{return Task.FromResult(new QuotaResult {Plan="plus"});},report).GetAwaiter().GetResult();
                if(firstAuthority.OfficialPlan!="plus" || firstAuthority.PrimaryRemaining!=31 || firstAuthority.SecondaryRemaining!=41 || firstAuthority.MonthlyRemaining!=-1 || firstAuthority.HasMonthlyLimit || firstAuthority.MonthlyResetAt!=0 || firstAuthority.ResetCredits!=2)throw new Exception("first Plus authority establishment retained Free-only monthly meter");

                var crossPlan=new AccountMeta {Id="cross-plan",Plan="free",OfficialPlan="free",PrimaryRemaining=73,MonthlyRemaining=63,HasMonthlyLimit=true,PrimaryResetAt=77,MonthlyResetAt=87,LastQuotaAt="free-cache",ResetCredits=-1};
                DockContext.RefreshAccountAsync(crossPlan,delegate{return Task.FromResult(new QuotaResult {Plan="plus"});},report).GetAwaiter().GetResult();
                if(crossPlan.Plan!="plus" || crossPlan.OfficialPlan!="plus" || crossPlan.PrimaryRemaining!=-1 || crossPlan.SecondaryRemaining!=-1 || crossPlan.MonthlyRemaining!=-1 || crossPlan.HasMonthlyLimit || crossPlan.PrimaryResetAt!=0 || crossPlan.SecondaryResetAt!=0 || crossPlan.MonthlyResetAt!=0 || crossPlan.ResetCredits!=-1 || crossPlan.LastQuotaAt!="" || crossPlan.LastError!=DockContext.PlanUpdatedQuotaUnavailable)throw new Exception("cross-plan partial response retained old-plan quota");

                var monthlyMetadataSource=new AccountMeta {Id="monthly-roundtrip",Plan="free",OfficialPlan="free",MonthlyRemaining=27,MonthlyResetAt=123456,HasMonthlyLimit=true};
                string metadataJson=JsonUtil.Serializer.Serialize(monthlyMetadataSource);
                AccountMeta metadataRoundTrip=JsonUtil.Serializer.Deserialize<AccountMeta>(metadataJson);
                if(metadataRoundTrip.MonthlyRemaining!=27 || metadataRoundTrip.MonthlyResetAt!=123456 || !metadataRoundTrip.HasMonthlyLimit || metadataRoundTrip.OfficialPlan!="free")throw new Exception("monthly metadata round-trip");

                if(!events.Contains("fixture:partial") || !events.Contains("fixture:success") || !events.Contains("fixture:failed"))throw new Exception("refresh progress states incomplete");
                var untouched=new AccountMeta {Id="other",PrimaryRemaining=12,LastQuotaAt="unchanged",AccountLifetimeTokens=123};
                var all=new[]{meta,untouched};int requests=0;
                foreach(AccountMeta target in DockContext.SelectRefreshTargets(all,"fixture"))
                    DockContext.RefreshAccountAsync(target,delegate{requests++;return Task.FromResult(new QuotaResult {PrimaryRemaining=80,SecondaryRemaining=70});},report).GetAwaiter().GetResult();
                if(requests!=1 || untouched.PrimaryRemaining!=12 || untouched.LastQuotaAt!="unchanged" || untouched.AccountLifetimeTokens!=123)throw new Exception("single refresh touched another account");
                if(DockContext.SelectRefreshTargets(all,"deleted").Length!=0 || DockContext.SelectRefreshTargets(all,null).Length!=2)throw new Exception("single target selection");
                Console.WriteLine("REFRESH_FEEDBACK_OK free_spend_monthly_100_zero_unknown duration_classification spend_preferred free_three_meters legacy_window_fallback plus_short_week_only free_monthly_only missing_monthly_cleared official_free_downgrade clears_cross_plan_cache stale_auth_guard network_failure_preserves_cache renewed_plus manual_expiry_ignored metadata_roundtrip wholly_incomplete_same_plan_preserves_short_week single_target_only no_network no_real_credentials");
                return 0;
            }
            catch(Exception ex) { Console.Error.WriteLine("REFRESH_FEEDBACK_FAILED "+ex.Message); return 1; }
        }

        public static int Run()
        {
            try
            {
                string header = Base64Url("{\"alg\":\"none\"}");
                string payload = Base64Url("{\"email\":\"test@example.com\",\"exp\":4102444800,\"https://api.openai.com/auth\":{\"chatgpt_plan_type\":\"plus\",\"chatgpt_account_id\":\"acct-test\",\"chatgpt_user_id\":\"user-test\"}}");
                string accessPayload = Base64Url("{\"exp\":4102444800,\"https://api.openai.com/auth\":{\"chatgpt_account_id\":\"acct-test\"}}");
                string json = "{\"auth_mode\":\"chatgpt\",\"tokens\":{\"id_token\":\"" + header + "." + payload + ".x\",\"access_token\":\"" + header + "." + accessPayload + ".x\",\"refresh_token\":\"fixture\",\"account_id\":\"acct-test\"}}";
                byte[] raw = Encoding.UTF8.GetBytes(json);
                AuthIdentity identity = AuthCodec.Parse(raw);
                if (identity.Email != "test@example.com" || identity.Plan != "plus" || identity.AccountId != "acct-test") throw new Exception("auth parser assertion failed");
                byte[] encrypted = AuthCodec.Protect(raw);
                byte[] roundTrip = AuthCodec.Unprotect(encrypted);
                if (!raw.SequenceEqual(roundTrip)) throw new Exception("DPAPI round-trip assertion failed");
                QuotaResult quota = QuotaClient.ParseResponse("{\"plan_type\":\"plus\",\"rate_limit\":{\"primary_window\":{\"used_percent\":30,\"reset_at\":4102444800},\"secondary_window\":{\"used_percent\":63,\"reset_after_seconds\":3600}}}");
                if (quota.PrimaryRemaining != 70 || quota.SecondaryRemaining != 37 || quota.PrimaryResetAt != 4102444800) throw new Exception("quota parser assertion failed");
                if (QuotaClient.ParseResetCredits("{\"available_count\":2}") != 2 || QuotaClient.ParseResetCredits("{\"rate_limit_reset_credits\":{\"available_count\":3}}") != 3) throw new Exception("reset credits parser assertion failed");
                string boundedQuotaBody = QuotaClient.ReadUtf8LimitedAsync(new MemoryStream(Encoding.UTF8.GetBytes("{\"ok\":true}")), CancellationToken.None).GetAwaiter().GetResult();
                if (boundedQuotaBody != "{\"ok\":true}") throw new Exception("quota bounded body assertion failed");
                bool quotaOversizeRejected = false;
                try { QuotaClient.ReadUtf8LimitedAsync(new MemoryStream(new byte[2 * 1024 * 1024 + 1]), CancellationToken.None).GetAwaiter().GetResult(); }
                catch (InvalidDataException) { quotaOversizeRejected = true; }
                if (!quotaOversizeRejected) throw new Exception("quota response limit assertion failed");
                if (!QuotaClient.HardTimeoutSelfTest()) throw new Exception("quota hard timeout assertion failed");
                int waitCalls = 0;
                bool waitSuccess = DockContext.WaitUntil(delegate { waitCalls++; return waitCalls >= 3; }, 100, 1);
                bool waitTimeout = !DockContext.WaitUntil(delegate { return false; }, 12, 2);
                if (!waitSuccess || !waitTimeout) throw new Exception("bounded restart wait assertion failed");
                if (DockContext.ShouldRestartProcess(25, 25, "ChatGPT", true)
                    || DockContext.ShouldRestartProcess(25, 26, "CodexAccountDock", true)
                    || DockContext.ShouldRestartProcess(25, 26, "ChatGPT", false)
                    || !DockContext.ShouldRestartProcess(25, 26, "ChatGPT", true))
                    throw new Exception("restart selection safety assertion failed");
                string safeMarker = Path.Combine(PortablePaths.SwitchHelpers, Guid.NewGuid().ToString("N"));
                if (!DockContext.IsSafeSwitchMarkerDirectory(safeMarker)
                    || DockContext.IsSafeSwitchMarkerDirectory(PortablePaths.SwitchHelpers)
                    || DockContext.IsSafeSwitchMarkerDirectory(Path.Combine(PortablePaths.Data, Guid.NewGuid().ToString("N"))))
                    throw new Exception("switch marker boundary assertion failed");
                string safeLoginHome = Path.Combine(PortablePaths.LoginStaging, Guid.NewGuid().ToString("N"));
                if (!DockContext.IsSafeLoginHome(safeLoginHome)
                    || DockContext.IsSafeLoginHome(PortablePaths.LoginStaging)
                    || DockContext.IsSafeLoginHome(Path.Combine(PortablePaths.Data, Guid.NewGuid().ToString("N"))))
                    throw new Exception("login home boundary assertion failed");
                Directory.CreateDirectory(safeLoginHome);
                File.WriteAllText(Path.Combine(safeLoginHome, "fixture.tmp"), "fixture");
                DockContext.CleanupLoginHome(safeLoginHome);
                if (Directory.Exists(safeLoginHome)) throw new Exception("login home cleanup assertion failed");
                Directory.CreateDirectory(safeMarker);
                DockContext.WriteProcessIdentity(Path.Combine(safeMarker, "owner"));
                File.WriteAllText(Path.Combine(safeMarker, "ready"), "ready");
                if (!DockContext.IsSwitchHelperBusy()) throw new Exception("switch helper busy assertion failed");
                File.WriteAllText(Path.Combine(safeMarker, "done"), "done");
                if (DockContext.IsSwitchHelperBusy()) throw new Exception("completed switch helper busy assertion failed");
                DockContext.CleanupCompletedSwitchMarkers();
                if (!Directory.Exists(safeMarker)) throw new Exception("active switch owner marker was cleaned early");
                File.WriteAllText(Path.Combine(safeMarker, "ack"), "ack");
                DockContext.CleanupCompletedSwitchMarkers();
                if (Directory.Exists(safeMarker)) throw new Exception("switch marker cleanup assertion failed");
                if (IndependentProcess.Quote("") != "\"\"" || IndependentProcess.Quote("C:\\fixture path\\item") != "\"C:\\fixture path\\item\"") throw new Exception("restart helper argument quoting assertion failed");
                string argumentProbeFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-output");
                Directory.CreateDirectory(argumentProbeFolder);
                string argumentProbe = Path.Combine(argumentProbeFolder, "argument-probe-" + Guid.NewGuid().ToString("N") + ".txt");
                IndependentProcess.Start("--argument-probe " + IndependentProcess.Quote("") + " " + IndependentProcess.Quote("") + " fixture " + IndependentProcess.Quote(argumentProbe));
                if (!DockContext.WaitUntil(delegate { return File.Exists(argumentProbe); }, 10000, 50) || File.ReadAllText(argumentProbe, Encoding.UTF8) != "quoted-empty-ok") throw new Exception("restart helper empty argument round-trip failed");
                try { File.Delete(argumentProbe); } catch { }
                string atomicFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-output", "atomic-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(atomicFolder);
                try
                {
                    string atomicDestination = Path.Combine(atomicFolder, "auth.json");
                    string atomicTemp = Path.Combine(atomicFolder, "auth.tmp");
                    File.WriteAllText(atomicDestination, "old", Encoding.UTF8);
                    File.WriteAllText(atomicTemp, "new", Encoding.UTF8);
                    AccountStore.AtomicReplace(atomicTemp, atomicDestination);
                    if (File.Exists(atomicTemp) || File.ReadAllText(atomicDestination, Encoding.UTF8) != "new") throw new Exception("atomic credential replacement assertion failed");
                    bool missingTempRejected = false;
                    try { AccountStore.AtomicReplace(Path.Combine(atomicFolder, "missing.tmp"), atomicDestination); }
                    catch (IOException) { missingTempRejected = true; }
                    if (!missingTempRejected || File.ReadAllText(atomicDestination, Encoding.UTF8) != "new") throw new Exception("failed credential replacement did not preserve destination");
                }
                finally { try { Directory.Delete(atomicFolder, true); } catch { } }
                DateTime loginStarted = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
                if (DockContext.LoginAttemptExpired(loginStarted, loginStarted.AddMinutes(14))
                    || !DockContext.LoginAttemptExpired(loginStarted, loginStarted.AddMinutes(16)))
                    throw new Exception("login timeout assertion failed");
                List<string> switchOrder = new List<string>();
                DockContext.ExecuteSwitchPreparation(
                    delegate { switchOrder.Add("plan"); return new CodexRestartPlan(); },
                    delegate { switchOrder.Add("prepare"); },
                    delegate { switchOrder.Add("helper"); },
                    delegate { switchOrder.Add("ready"); return true; },
                    delegate { switchOrder.Add("go"); },
                    delegate { switchOrder.Add("cancel"); });
                if (string.Join(",", switchOrder.ToArray()) != "plan,prepare,helper,ready,go") throw new Exception("switch preparation order assertion failed");
                switchOrder.Clear();
                bool readinessRejected = false;
                try
                {
                    DockContext.ExecuteSwitchPreparation(
                        delegate { switchOrder.Add("plan"); return new CodexRestartPlan(); },
                        delegate { switchOrder.Add("prepare"); },
                        delegate { switchOrder.Add("helper"); },
                        delegate { switchOrder.Add("ready"); return false; },
                        delegate { switchOrder.Add("go"); },
                        delegate { switchOrder.Add("cancel"); });
                }
                catch (TimeoutException) { readinessRejected = true; }
                if (!readinessRejected || string.Join(",", switchOrder.ToArray()) != "plan,prepare,helper,ready,cancel") throw new Exception("switch readiness guard assertion failed");
                switchOrder.Clear();
                bool prepareRejected = false;
                try
                {
                    DockContext.ExecuteSwitchPreparation(
                        delegate { switchOrder.Add("plan"); return new CodexRestartPlan(); },
                        delegate { switchOrder.Add("prepare"); throw new IOException("fixture"); },
                        delegate { switchOrder.Add("helper"); },
                        delegate { switchOrder.Add("ready"); return true; },
                        delegate { switchOrder.Add("go"); },
                        delegate { switchOrder.Add("cancel"); });
                }
                catch (IOException) { prepareRejected = true; }
                if (!prepareRejected || string.Join(",", switchOrder.ToArray()) != "plan,prepare") throw new Exception("switch preflight failure assertion failed");
                switchOrder.Clear();
                DockContext.ExecuteRestartTransaction(
                    delegate { switchOrder.Add("stop"); },
                    delegate { switchOrder.Add("write"); },
                    delegate { switchOrder.Add("launch"); });
                if (string.Join(",", switchOrder.ToArray()) != "stop,write,launch") throw new Exception("restart transaction order assertion failed");
                switchOrder.Clear();
                bool stopRejected = false;
                try
                {
                    DockContext.ExecuteRestartTransaction(
                        delegate { switchOrder.Add("stop"); throw new IOException("fixture"); },
                        delegate { switchOrder.Add("write"); },
                        delegate { switchOrder.Add("launch"); });
                }
                catch (IOException) { stopRejected = true; }
                if (!stopRejected || string.Join(",", switchOrder.ToArray()) != "stop") throw new Exception("restart stop-before-write assertion failed");
                switchOrder.Clear();
                bool commitRejected = false;
                try
                {
                    DockContext.ExecuteRestartTransaction(
                        delegate { switchOrder.Add("stop"); },
                        delegate { switchOrder.Add("write"); throw new IOException("fixture"); },
                        delegate { switchOrder.Add("relaunch-old"); });
                }
                catch (IOException) { commitRejected = true; }
                if (!commitRejected || string.Join(",", switchOrder.ToArray()) != "stop,write,relaunch-old") throw new Exception("restart write failure recovery assertion failed");
                int ownPid = Process.GetCurrentProcess().Id;
                List<Process> restartPlan = DockContext.FindCodexProcessesForRestart();
                bool launchIdResolved = restartPlan.Count == 0;
                bool executableResolved = restartPlan.Count == 0;
                foreach (Process process in restartPlan)
                {
                    if (process.Id == ownPid || !Native.IsCodexProcess(process)) throw new Exception("restart process filter assertion failed");
                    if (!string.IsNullOrWhiteSpace(Native.GetApplicationUserModelId(process))) launchIdResolved = true;
                    try { if (process.MainModule != null && !string.IsNullOrWhiteSpace(process.MainModule.FileName)) executableResolved = true; }
                    catch { }
                    process.Dispose();
                }
                if (!launchIdResolved && !executableResolved) throw new Exception("restart launch target assertion failed");
                string survivalFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-output");
                Directory.CreateDirectory(survivalFolder);
                string survivalMarker = Path.Combine(survivalFolder, "independent-survival-" + Guid.NewGuid().ToString("N") + ".txt");
                IndependentProcess.Start("--independent-survival-parent " + IndependentProcess.Quote(survivalMarker));
                if (!DockContext.WaitUntil(delegate { return File.Exists(survivalMarker); }, 12000, 50)) throw new Exception("independent helper did not survive parent exit");
                if (File.ReadAllText(survivalMarker, Encoding.UTF8) != "survived") throw new Exception("independent helper survival marker invalid");
                try { File.Delete(survivalMarker); } catch { }
                Color redAccent, blueAccent;
                using(Bitmap theme=new Bitmap(48,48))
                using(Graphics paint=Graphics.FromImage(theme))
                {
                    paint.Clear(Color.Red); Palette.FromBackground(theme); redAccent=Palette.Accents[0];
                    paint.Clear(Color.Blue); Palette.FromBackground(theme); blueAccent=Palette.Accents[0];
                    if(redAccent==blueAccent || Palette.GlassBottom.GetBrightness()>.35) throw new Exception("theme hue/contrast assertion failed");
                    paint.Clear(Color.White); Palette.FromBackground(theme);
                    if(Palette.Accents[0].GetBrightness()<.5 || Palette.GlassBottom.GetBrightness()>.35) throw new Exception("neutral theme assertion failed");
                }
                var news=DailyNews.Parse("{\"data\":{\"items\":[{\"title\":\"fixture\",\"selected\":true},{\"selected\":false}]}}");
                if(news.Count!=1 || JsonUtil.Text(news[0],"title")!="fixture") throw new Exception("daily parser assertion failed");
                bool rejectsMalformed=false;
                try { DailyNews.Parse("{\"data\":{}}"); } catch(FormatException) { rejectsMalformed=true; }
                if(!rejectsMalformed) throw new Exception("daily malformed response assertion failed");
                Console.WriteLine("SELF_TEST_OK auth_parser quota_parser reset_credits_parser quota_response_limit quota_hard_timeout dpapi theme_hue theme_contrast theme_neutral daily_parser daily_malformed restart_wait_success restart_wait_timeout restart_process_filter restart_selection_safety restart_argument_quoting restart_empty_argument_roundtrip atomic_credential_replace failed_replace_preserves_live_auth switch_marker_boundary switch_marker_cleanup active_owner_marker_preserved switch_busy_guard switch_preflight_order switch_ready_before_stop switch_preflight_failure_no_helper restart_stop_before_write restart_write_failure_relaunches_old independent_helper_survives_parent_exit login_home_boundary login_home_cleanup login_timeout restart_targets=" + restartPlan.Count.ToString(CultureInfo.InvariantCulture) + " dynamic_launch_id=" + launchIdResolved.ToString().ToLowerInvariant() + " executable_fallback=" + executableResolved.ToString().ToLowerInvariant() + " no_network no_real_credentials no_process_termination");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SELF_TEST_FAILED " + ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }

        public static int AuditInstalled()
        {
            try
            {
                AccountStore store = new AccountStore();
                int valid = 0;
                foreach (AccountMeta meta in store.Settings.Accounts)
                {
                    AuthIdentity identity = AuthCodec.Parse(store.LoadRaw(meta));
                    if (identity.StableId != meta.Id) throw new Exception("account identity mismatch");
                    valid++;
                }
                string metadata = File.Exists(PortablePaths.Settings) ? File.ReadAllText(PortablePaths.Settings, Encoding.UTF8) : "";
                bool tokenFree = metadata.IndexOf("access_token", StringComparison.OrdinalIgnoreCase) < 0 && metadata.IndexOf("refresh_token", StringComparison.OrdinalIgnoreCase) < 0 && metadata.IndexOf("id_token", StringComparison.OrdinalIgnoreCase) < 0;
                bool activeRegistered = string.IsNullOrWhiteSpace(store.ActiveId()) || store.Settings.Accounts.Any(delegate(AccountMeta a) { return a.Id == store.ActiveId(); });
                if (!tokenFree || !activeRegistered) throw new Exception("metadata or active-account audit failed");
                Console.WriteLine("INSTALL_AUDIT_OK accounts=" + valid.ToString(CultureInfo.InvariantCulture) + " active_registered=" + activeRegistered.ToString().ToLowerInvariant() + " metadata_token_free=" + tokenFree.ToString().ToLowerInvariant());
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("INSTALL_AUDIT_FAILED " + ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }

        private static string Base64Url(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}

namespace CodexAccountDock {
internal static class LocalProjectContinuity {
 static readonly string[] Maps={"local-projects","thread-project-assignments","sidebar-project-thread-orders","thread-workspace-root-hints","thread-projectless-output-directories"};
 static Dictionary<string,object> Dict(object x){return x as Dictionary<string,object>??new Dictionary<string,object>();}
 static object Get(Dictionary<string,object>d,string k){object v;return d.TryGetValue(k,out v)?v:null;}
 internal static string Owner(AuthIdentity id){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new[]{id.UserId,id.AccountId})))).Replace("-","").ToLowerInvariant();}
 internal static string Merge(string json,string target){
 if(string.IsNullOrWhiteSpace(target))throw new InvalidDataException("目标项目配置标识为空。");
 var j=new JavaScriptSerializer{MaxJsonLength=int.MaxValue,RecursionLimit=256};var root=Dict(j.DeserializeObject(json));
 var scope=Dict(Get(root,"electron-account-renderer-state-v1"));var inactive=Dict(Get(scope,"inactive"));
 var sources=new List<Dictionary<string,object>>();foreach(var v in inactive.Values)sources.Add(Dict(Get(Dict(v),"globals")));sources.Add(root);
 var merged=new Dictionary<string,Dictionary<string,object>>();foreach(var key in Maps){var m=new Dictionary<string,object>();foreach(var s in sources)foreach(var kv in Dict(Get(s,key)))m[kv.Key]=kv.Value;merged[key]=m;}
 var dest=Dict(Get(inactive,target));var globals=Dict(Get(dest,"globals"));
 foreach(var key in Maps){var m=new Dictionary<string,object>(merged[key]);foreach(var kv in Dict(Get(globals,key)))m[kv.Key]=kv.Value;globals[key]=m;root[key]=m;}
 foreach(string listKey in new[]{"project-order","projectless-thread-ids"}){var order=new List<object>();var ordered=new List<Dictionary<string,object>>(sources);ordered.Add(globals);foreach(var source in ordered){var seq=Get(source,listKey) as IEnumerable;if(seq!=null && !(seq is string))foreach(var x in seq)if(x is string&&!order.Contains(x))order.Add(x);}globals[listKey]=order;root[listKey]=order;}
 dest["globals"]=globals;if(!dest.ContainsKey("atoms"))dest["atoms"]=new Dictionary<string,object>();inactive[target]=dest;scope["inactive"]=inactive;root["electron-account-renderer-state-v1"]=scope;
 return j.Serialize(root);
 }
 internal static void Preserve(AuthIdentity target){
 if(string.IsNullOrWhiteSpace(target.UserId)||string.IsNullOrWhiteSpace(target.AccountId))throw new InvalidDataException("无法确认目标账号的项目配置标识，未切换登录。");
 string path=Path.Combine(Path.GetDirectoryName(PortablePaths.LiveAuth),".codex-global-state.json");if(!File.Exists(path))return;
 string before=File.ReadAllText(path,Encoding.UTF8),after=Merge(before,Owner(target));
 string folder=Path.Combine(PortablePaths.Backups,"local-projects");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".json"),before,new UTF8Encoding(false));
 string temp=path+".project-continuity.tmp";File.WriteAllText(temp,after,new UTF8Encoding(false));AccountStore.AtomicReplace(temp,path);File.WriteAllText(path+".bak",after,new UTF8Encoding(false));
 }
}
}

namespace CodexAccountDock {
 internal sealed class ResetCreditInfo {
  public string Id="",ExpiresAt="",GrantedAt="";
  internal static List<ResetCreditInfo> Parse(string json) {
   var root=JsonUtil.Serializer.DeserializeObject(json) as Dictionary<string,object>;var result=new List<ResetCreditInfo>();
   var items=JsonUtil.Value(root,"credits") as System.Collections.IEnumerable;if(items==null)return result;
   foreach(object item in items){var d=JsonUtil.Dict(item);if(d==null)continue;
    if(Convert.ToString(JsonUtil.Value(d,"status"))!="available" || !object.Equals(JsonUtil.Value(d,"is_supported_by_plan"),true))continue;
    var c=new ResetCreditInfo {Id=Convert.ToString(JsonUtil.Value(d,"id")),ExpiresAt=Convert.ToString(JsonUtil.Value(d,"expires_at")),GrantedAt=Convert.ToString(JsonUtil.Value(d,"granted_at"))};
    if(!string.IsNullOrEmpty(c.Id))result.Add(c);
   }return result;
  }
  internal bool Date(out DateTimeOffset date){date=default(DateTimeOffset);string s=ExpiresAt??"";if(!(s.EndsWith("Z",StringComparison.OrdinalIgnoreCase)||System.Text.RegularExpressions.Regex.IsMatch(s,@"[+-]\d{2}:\d{2}$")))return false;return DateTimeOffset.TryParse(s,CultureInfo.InvariantCulture,DateTimeStyles.None,out date);}
  internal int Stage(DateTimeOffset now){DateTimeOffset d;if(!Date(out d))return 0;double h=(d-now).TotalHours;return h<=0||h>72?0:h<=1?1:h<=24?24:72;}
  internal string LocalExpiry(){DateTimeOffset d;return Date(out d)?d.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):"官方未提供有效期";}
  internal string Summary(){DateTimeOffset d;if(!Date(out d))return "有效期未知";double h=(d-DateTimeOffset.UtcNow).TotalHours;return h<=0?"已到期":h<1?"不足 1 小时":h<24?Math.Ceiling(h)+" 小时后到期":Math.Ceiling(h/24)+" 天后到期";}
 }
}
