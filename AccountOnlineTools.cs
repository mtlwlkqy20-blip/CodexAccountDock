using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAccountDock
{
    internal sealed class SubscriptionSnapshot
    {
        public string FetchedAtUtc = "";
        public string ExpiresAtUtc = "";
        public string RenewsAtUtc = "";
        public string CancelsAtUtc = "";
        public bool? HasActiveSubscription;
        public bool? WillRenew;
    }

    internal sealed class InviteAttempt
    {
        public string Fingerprint = "";
        public string AtUtc = "";
        public string State = "";
    }

    internal sealed class InviteEligibility
    {
        internal string Program = "";
        internal bool? ShouldShow;
        internal int? SendCapacity;
        internal int? RewardCapacity;
        internal string Description = "";
        internal bool CanSend { get { return ShouldShow == true && SendCapacity.HasValue && SendCapacity.Value > 0; } }
    }

    internal sealed class OnlineToolException : Exception
    {
        internal readonly bool OutcomeUnknown;
        internal OnlineToolException(string message, bool unknown = false) : base(message) { OutcomeUnknown = unknown; }
    }

    // The website endpoints are not a public, versioned API. No cookie import,
    // cross-account fallback, redirects, automatic login, or POST retries.
    internal static class AccountOnlineClient
    {
        internal const string ConsumerProgram = "codex_referral_consumer";
        internal const string WorkspaceProgram = "codex_referral_workspace";
        private const string AccountPath = "/backend-api/accounts/check/v4-2023-04-27";
        private const string EligibilityPath = "/backend-api/referrals/invite/eligibility";
        private const string InvitePath = "/backend-api/referrals/invite";

        internal static async Task<SubscriptionSnapshot> ReadSubscription(byte[] raw, string expectedId, CancellationToken token)
        {
            AuthIdentity identity = Identity(raw, expectedId);
            string json = await Request(identity, AccountPath, null, token).ConfigureAwait(false);
            return ParseSubscription(json, identity.AccountId);
        }

        internal static async Task<InviteEligibility> ReadEligibility(byte[] raw, string expectedId, string program, CancellationToken token)
        {
            CheckProgram(program);
            AuthIdentity identity = Identity(raw, expectedId);
            string path = EligibilityPath + "?program_id=" + Uri.EscapeDataString(program) + "&entrypoint=persistent";
            return ParseEligibility(await Request(identity, path, null, token).ConfigureAwait(false), program);
        }

        internal static async Task SendInvitation(byte[] raw, string expectedId, string program, string email, CancellationToken token)
        {
            CheckProgram(program);
            email = ValidateEmail(email);
            AuthIdentity identity = Identity(raw, expectedId);
            string body = JsonUtil.Serializer.Serialize(new Dictionary<string, object> {
                {"program_id", program}, {"entrypoint", "persistent"}, {"emails", new [] {email}}
            });
            string json = await Request(identity, InvitePath, body, token).ConfigureAwait(false);
            ConfirmInviteResponse(json, email);
        }

        private static AuthIdentity Identity(byte[] raw, string expectedId)
        {
            AuthIdentity identity;
            try { identity = AuthCodec.Parse(raw); }
            finally { if (raw != null) Array.Clear(raw, 0, raw.Length); }
            if (identity.StableId != expectedId || string.IsNullOrWhiteSpace(identity.AccountId))
                throw new OnlineToolException("凭据与所选账号不一致，已停止查询。");
            return identity;
        }

        internal static async Task RefreshSubscription(AccountStore store, AccountMeta account, CancellationToken token)
        {
            try {
                SubscriptionSnapshot value = await ReadSubscription(store.LoadRaw(account), account.Id, token).ConfigureAwait(false);
                account.SubscriptionRead = value; account.SubscriptionReadError = "";
            } catch (Exception ex) {
                account.SubscriptionReadError = ex is OnlineToolException ? ex.Message : "订阅读取未完成，保留已有记录。";
                throw new OnlineToolException(account.SubscriptionReadError);
            }
        }

        private static void CheckProgram(string value)
        {
            if (value != ConsumerProgram && value != WorkspaceProgram) throw new OnlineToolException("未知邀请类型。");
        }

        internal static string ValidateEmail(string value)
        {
            string email = (value ?? "").Trim();
            if (email.Length > 254 || email.IndexOfAny(new [] {'\r', '\n', ',', ';', '<', '>'}) >= 0)
                throw new OnlineToolException("请填写一个有效邮箱。");
            try {
                var parsed = new System.Net.Mail.MailAddress(email);
                if (parsed.Address != email || !email.Contains("@") || email.Any(char.IsWhiteSpace)) throw new FormatException();
            } catch { throw new OnlineToolException("请填写一个有效邮箱。"); }
            return email;
        }

        private static async Task<string> Request(AuthIdentity identity, string path, string body, CancellationToken token)
        {
            bool post = body != null;
            if ((!post && path != AccountPath && !path.StartsWith(EligibilityPath + "?", StringComparison.Ordinal)) ||
                (post && path != InvitePath)) throw new OnlineToolException("请求地址不在允许范围内。");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string route;
            IWebProxy proxy = PublicFeedTransport.FindUsableProxy(new Uri("https://chatgpt.com/"), out route);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, Proxy = proxy, UseProxy = proxy != null })
            using (var client = new HttpClient(handler))
            using (var request = new HttpRequestMessage(post ? HttpMethod.Post : HttpMethod.Get, "https://chatgpt.com" + path))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                client.Timeout = Timeout.InfiniteTimeSpan;
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + identity.AccessToken);
                request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", identity.AccountId);
                request.Headers.TryAddWithoutValidation("User-Agent", "CodexAccountDock/2.0");
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                if (path != AccountPath) {
                    request.Headers.TryAddWithoutValidation("originator", "CodexAccountDock");
                    request.Headers.TryAddWithoutValidation("openai-beta", "codex-1");
                }
                if (post) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                try {
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                    {
                        int status = (int)response.StatusCode;
                        if (!response.IsSuccessStatusCode) {
                            if (status == 401) throw new OnlineToolException("授权已失效，请为这个账号重新完成官方登录。");
                            if (status == 403) throw new OnlineToolException("当前账号授权不能访问此功能。请通过官方客户端核对该账号的资格。");
                            if (status == 404) throw new OnlineToolException("该接口或活动当前不可用，请使用官方客户端核对。");
                            if (status == 409) throw new OnlineToolException("服务端报告重复或已有邀请，请到官方客户端核对。");
                            if (status == 429) throw new OnlineToolException("请求频率受限，请稍后手动查询。", post);
                            if (status >= 300 && status < 400) throw new OnlineToolException("请求被重定向，已停止；请通过官方网页核对。");
                            throw new OnlineToolException("服务未能完成请求（HTTP " + status + "）。", post && status >= 500);
                        }
                        if (response.Content.Headers.ContentLength.GetValueOrDefault() > 262144) throw new OnlineToolException("响应超过大小限制。", post);
                        using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var close = deadline.Token.Register(delegate { try { stream.Dispose(); } catch { } }))
                        using (var data = new MemoryStream()) {
                            byte[] buffer = new byte[8192]; int count;
                            while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false)) > 0) {
                                if (data.Length + count > 262144) throw new OnlineToolException("响应超过大小限制。", post);
                                data.Write(buffer, 0, count);
                            }
                            return new UTF8Encoding(false, true).GetString(data.ToArray());
                        }
                    }
                }
                catch (OnlineToolException) { throw; }
                catch (OperationCanceledException) { throw new OnlineToolException(post ? "发送已取消或超时，结果未知；不会自动重发，请先在官方客户端核对。" : "查询已取消或超时，保留旧记录。", post); }
                catch { throw new OnlineToolException(post ? "发送期间连接异常，结果未知；不会自动重发，请先在官方客户端核对。" : "查询失败；当前授权可能不支持网页接口，保留旧记录。", post); }
            }
        }

        internal static SubscriptionSnapshot ParseSubscription(string json, string accountId)
        {
            var root = JsonUtil.Dict(JsonUtil.Serializer.DeserializeObject(json));
            var accounts = JsonUtil.Dict(JsonUtil.Value(root, "accounts"));
            if (accounts == null || string.IsNullOrEmpty(accountId)) throw new OnlineToolException("响应没有可核对的账号资料。");
            Dictionary<string, object> entry = null;
            foreach (var pair in accounts) {
                var candidate = JsonUtil.Dict(pair.Value);
                var account = JsonUtil.Dict(JsonUtil.Value(candidate, "account"));
                string nested = JsonUtil.Text(account, "account_id");
                if (string.IsNullOrEmpty(nested)) nested = JsonUtil.Text(account, "id");
                if (pair.Key == accountId || nested == accountId) {
                    if (!string.IsNullOrEmpty(nested) && nested != accountId) throw new OnlineToolException("响应账号不一致，未保存。");
                    if (entry != null && pair.Key != accountId) continue;
                    entry = candidate;
                }
            }
            if (entry == null) throw new OnlineToolException("未找到所选账号的订阅资料，未使用其他账号或默认工作区。");
            var entitlement = JsonUtil.Dict(JsonUtil.Value(entry, "entitlement"));
            if (entitlement == null) throw new OnlineToolException("官方响应未提供订阅字段。");
            var last = JsonUtil.Dict(JsonUtil.Value(entry, "last_active_subscription"));
            return new SubscriptionSnapshot {
                FetchedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                ExpiresAtUtc = ReadDate(JsonUtil.Value(entitlement, "expires_at")),
                RenewsAtUtc = ReadDate(JsonUtil.Value(entitlement, "renews_at")),
                CancelsAtUtc = ReadDate(JsonUtil.Value(entitlement, "cancels_at")),
                HasActiveSubscription = ReadBool(JsonUtil.Value(entitlement, "has_active_subscription")),
                WillRenew = ReadBool(JsonUtil.Value(last, "will_renew"))
            };
        }

        internal static string ReadDate(object value)
        {
            if (value == null || value is bool) return "";
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            DateTimeOffset date; long seconds;
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds)) {
                if (seconds < 1420070400L || seconds > 4102444800L) return "";
                date = DateTimeOffset.FromUnixTimeSeconds(seconds);
            } else {
                if (text.Length > 64 || !text.Contains("T") || !(text.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                    System.Text.RegularExpressions.Regex.IsMatch(text, @"[+-]\d{2}:?\d{2}$"))) return "";
                if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return "";
                if (date.Year < 2015 || date.Year > 2100) return "";
            }
            return date.ToUniversalTime().ToString("o");
        }

        private static bool? ReadBool(object value) { return value is bool ? (bool?)value : null; }
        private static int? Capacity(object value) {
            if (!(value is int) && !(value is long)) return null;
            long n = Convert.ToInt64(value); return n >= 0 && n <= 1000000 ? (int?)n : null;
        }
        internal static InviteEligibility ParseEligibility(string json, string program)
        {
            CheckProgram(program);
            var root = JsonUtil.Dict(JsonUtil.Serializer.DeserializeObject(json));
            if (root == null || JsonUtil.Value(root, "error") != null) throw new OnlineToolException("未取得有效邀请资格。");
            var text = new List<string>();
            foreach (string field in new [] {"title", "description"}) { string s = JsonUtil.Text(root, field); if (!string.IsNullOrWhiteSpace(s)) text.Add(s.Substring(0, Math.Min(1500, s.Length))); }
            var rules = JsonUtil.Value(root, "rules") as IEnumerable;
            if (rules != null && !(rules is string)) foreach (object rule in rules) {
                if (text.Count >= 10) break;
                string s = rule as string;
                if (s == null) s = JsonUtil.Text(JsonUtil.Dict(rule), "description");
                if (!string.IsNullOrWhiteSpace(s)) text.Add(s.Substring(0, Math.Min(1000, s.Length)));
            }
            return new InviteEligibility { Program = program, ShouldShow = ReadBool(JsonUtil.Value(root, "should_show")),
                SendCapacity = Capacity(JsonUtil.Value(root, "remaining_send_capacity")),
                RewardCapacity = Capacity(JsonUtil.Value(root, "remaining_reward_capacity")), Description = string.Join("\r\n", text) };
        }

        internal static void ConfirmInviteResponse(string json, string email)
        {
            var root = JsonUtil.Dict(JsonUtil.Serializer.DeserializeObject(json));
            var invites = JsonUtil.Value(root, "invites") as object[];
            if (JsonUtil.Value(root, "error") == null && invites != null) foreach (object item in invites) {
                var invite = JsonUtil.Dict(item);
                string recipient = JsonUtil.Text(invite, "email");
                if (string.IsNullOrEmpty(recipient)) recipient = JsonUtil.Text(invite, "recipient_email");
                string status = JsonUtil.Text(invite, "status").ToLowerInvariant();
                if (string.Equals(recipient, email, StringComparison.OrdinalIgnoreCase) && status != "failed" && status != "error" &&
                    (!string.IsNullOrWhiteSpace(JsonUtil.Text(invite, "id")) || status == "pending" || status == "sent")) return;
            }
            throw new OnlineToolException("服务端响应未能确认该邮箱的邀请记录；结果待核实，不会自动重发。", true);
        }

        internal static string Fingerprint(string program, string email) {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(program + "|" + email.Trim().ToLowerInvariant()))).Replace("-", "");
        }
    }

    internal static class SubscriptionDisplay
    {
        internal static string Compact(AccountMeta account)
        {
            SubscriptionSnapshot s = account.SubscriptionRead;
            if (s == null || s.HasActiveSubscription == false) return "";
            string stamp = !string.IsNullOrEmpty(s.CancelsAtUtc) ? s.CancelsAtUtc : !string.IsNullOrEmpty(s.ExpiresAtUtc) ? s.ExpiresAtUtc : s.RenewsAtUtc;
            if (string.IsNullOrEmpty(stamp)) return "";
            DateTimeOffset end;
            if (!DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out end)) return "";
            string label = !string.IsNullOrEmpty(s.CancelsAtUtc) ? "订阅结束" : !string.IsNullOrEmpty(s.ExpiresAtUtc) ? (s.WillRenew == true ? "当前周期结束" : s.WillRenew == false ? "订阅到期" : "当前权益截止") : "下次续费";
            double days = (end - DateTimeOffset.UtcNow).TotalDays;
            string relative = days <= 0 ? "时间已到，待刷新" : days >= 1 ? Math.Ceiling(days).ToString(CultureInfo.InvariantCulture) + " 天" : Math.Ceiling(days * 24).ToString(CultureInfo.InvariantCulture) + " 小时";
            return label + " · " + relative;
        }
        internal static string Describe(AccountMeta account)
        {
            var s = account.SubscriptionRead;
            if (s == null) return "尚未读取官方订阅日期。\r\n手填记录仍然保留；登录令牌有效期不会用作订阅到期时间。";
            var lines = new List<string> { "读取时间：" + Local(s.FetchedAtUtc), "订阅状态：" + (s.HasActiveSubscription == true ? "有效" : s.HasActiveSubscription == false ? "未确认有有效订阅" : "官方未提供"), "自动续费：" + (s.WillRenew == true ? "是" : s.WillRenew == false ? "否" : "官方未提供"),
                "当前权益截止：" + Local(s.ExpiresAtUtc), "下次续费：" + Local(s.RenewsAtUtc), "计划结束：" + Local(s.CancelsAtUtc) };
            string compact = Compact(account); if (compact.Length > 0) lines.Add("\r\n" + compact + "（以上次读取结果计算）");
            if (s.HasActiveSubscription == false) lines.Add("返回的日期可能属于上一订阅周期，请到官方账单核对。");
            lines.Add("\r\n下次续费时间不代表永久到期；缺少日期时保持未知。网页接口可能随时变化或不接受当前授权。");
            if (!string.IsNullOrEmpty(account.SubscriptionReadError)) lines.Add("\r\n最近读取失败：" + account.SubscriptionReadError + "\r\n以上为上次记录。");
            return string.Join("\r\n", lines);
        }
        private static string Local(string value) { DateTimeOffset d; return DateTimeOffset.TryParse(value, out d) ? d.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz") : "未提供"; }
    }

}
