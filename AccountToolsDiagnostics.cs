using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace CodexAccountDock
{
    internal static class AccountToolsDiagnostics
    {
        internal static int Run(string folder)
        {
            Directory.CreateDirectory(folder);
            var checks = new List<string>();
            Action<bool,string> check = delegate(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); };
            try {
                string json = "{\"accounts\":{\"acct-one\":{\"account\":{\"id\":\"acct-one\"},\"entitlement\":{\"expires_at\":\"2026-12-01T12:00:00Z\",\"renews_at\":\"2026-12-01T12:00:00Z\",\"has_active_subscription\":true},\"last_active_subscription\":{\"will_renew\":true}}}}";
                SubscriptionSnapshot s = AccountOnlineClient.ParseSubscription(json, "acct-one");
                check(s.ExpiresAtUtc.Length > 0 && s.RenewsAtUtc.Length > 0 && s.WillRenew == true, "subscription_dates_and_renewal_are_separate");
                bool rejected = false; try { AccountOnlineClient.ParseSubscription(json, "acct-other"); } catch (OnlineToolException) { rejected = true; }
                check(rejected, "other_account_and_default_workspace_are_not_used");
                rejected = false; try { AccountOnlineClient.ParseSubscription(json.Replace("\"id\":\"acct-one\"", "\"id\":\"acct-wrong\""), "acct-one"); } catch (OnlineToolException) { rejected = true; }
                check(rejected, "conflicting_response_identity_is_rejected");
                check(AccountOnlineClient.ReadDate("2026-12-01").Length == 0 && AccountOnlineClient.ReadDate("2026-12-01T12:00:00").Length == 0 && AccountOnlineClient.ReadDate(true).Length == 0 && AccountOnlineClient.ReadDate(99999999999999L).Length == 0, "ambiguous_invalid_dates_and_token_expiry_are_not_inferred");
                var missing = AccountOnlineClient.ParseSubscription("{\"accounts\":{\"acct-one\":{\"entitlement\":{}}}}", "acct-one");
                check(missing.HasActiveSubscription == null && missing.ExpiresAtUtc == "", "missing_subscription_fields_stay_unknown");
                var account = new AccountMeta { Id = "synthetic", Email = "demo@example.com", SubscriptionExpiresOn = "2028-03-01", SubscriptionRead = s };
                check(SubscriptionDisplay.Compact(account).StartsWith("当前周期结束") && account.SubscriptionExpiresOn == "2028-03-01", "automatic_read_preserves_manual_record_and_labels_cycle");
                account.SubscriptionRead = new SubscriptionSnapshot { RenewsAtUtc = s.RenewsAtUtc, HasActiveSubscription = true };
                check(SubscriptionDisplay.Compact(account).StartsWith("下次续费"), "renewal_is_not_labeled_subscription_expiry");
                account.SubscriptionRead.HasActiveSubscription = false;
                check(SubscriptionDisplay.Compact(account) == "", "inactive_subscription_does_not_show_active_countdown");
                var quota = AccountOnlineClient.ParseEligibility("{\"should_show\":true,\"remaining_send_capacity\":2,\"remaining_reward_capacity\":0}", AccountOnlineClient.ConsumerProgram);
                check(quota.CanSend && quota.RewardCapacity == 0, "send_capacity_and_reward_capacity_are_separate");
                check(!AccountOnlineClient.ParseEligibility("{}", AccountOnlineClient.ConsumerProgram).CanSend && !AccountOnlineClient.ParseEligibility("{\"should_show\":true,\"remaining_send_capacity\":\"3\"}", AccountOnlineClient.ConsumerProgram).CanSend, "missing_or_wrong_type_invite_capacity_is_not_zero_or_permission");
                check(!AccountOnlineClient.ParseEligibility("{\"should_show\":false,\"remaining_send_capacity\":3}", AccountOnlineClient.ConsumerProgram).CanSend, "ineligible_account_cannot_send");
                AccountOnlineClient.ConfirmInviteResponse("{\"invites\":[{\"email\":\"friend@example.com\",\"id\":\"fixture\"}]}", "friend@example.com");
                check(true, "invitation_requires_matching_recipient_and_server_record");
                rejected = false; try { AccountOnlineClient.ConfirmInviteResponse("{\"invites\":[{\"email\":\"other@example.com\",\"id\":\"fixture\"}]}", "friend@example.com"); } catch (OnlineToolException e) { rejected = e.OutcomeUnknown; }
                check(rejected, "unconfirmed_invitation_is_unknown_not_success");
                check(AccountOnlineClient.Fingerprint("p", "friend@example.com") == AccountOnlineClient.Fingerprint("p", "FRIEND@example.com"), "invite_duplicate_key_is_case_insensitive_without_storing_recipient");
                rejected = false; try { AccountOnlineClient.ValidateEmail("a@example.com\r\nother@example.com"); } catch { rejected = true; }
                check(rejected, "multiple_recipients_and_control_characters_rejected");
                check(ModelEvidence.Status("auto", "mini").StartsWith("自动") && ModelEvidence.Status("gpt-test", "").StartsWith("无法") && ModelEvidence.Status("gpt-test", "gpt-test-2026-01-01").StartsWith("同系列") && ModelEvidence.Status("gpt-test", "mini").StartsWith("标识不同"), "model_status_does_not_invent_quality_ranking");
                string report = Path.Combine(folder, "synthetic-model-report.json");
                File.WriteAllText(report, "{\"format\":\"codex-account-dock-models-v1\",\"source\":\"chatgpt-web\",\"observations\":[{\"requested\":\"test\",\"resolved\":\"test\",\"at\":\"2026-11-04T00:00:00Z\"}]}");
                check(ModelEvidence.ReadReport(report).Contains("标识一致"), "bounded_model_report_import");
                File.WriteAllText(report, File.ReadAllText(report).Replace("\"source\"", "\"access_token\":\"not-a-real-token\",\"source\""));
                rejected = false; try { ModelEvidence.ReadReport(report); } catch { rejected = true; }
                check(rejected, "model_report_with_extra_credentials_field_rejected");
                string log = Path.Combine(folder, "synthetic-session.jsonl");
                File.WriteAllText(log, "{\"type\":\"turn_context\",\"payload\":{\"model\":\"test\",\"turn_id\":\"t1\"}}\n{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"assistant\",\"content\":[]}}\n");
                bool partial; check(string.Join(" ", ModelEvidence.ReadCodexFile(log, out partial)).Contains("无法核对"), "codex_requested_model_is_not_treated_as_actual");
                string summary = ModelEvidence.DescribeCodexFiles(new [] {log});
                check(summary.Contains("当前无法核对实际模型") && !summary.Contains("未取得") && !summary.Contains(Path.GetFileName(log)), "missing_model_summary_explains_limit_without_raw_file_dump");
                File.AppendAllText(log, "{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"assistant\",\"turn_id\":\"other\",\"metadata\":{\"model_slug\":\"wrong\"}}}\n");
                check(!string.Join(" ", ModelEvidence.ReadCodexFile(log, out partial)).Contains("wrong"), "different_turn_response_is_not_compared");
                File.AppendAllText(log, "{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"assistant\",\"turn_id\":\"t1\",\"metadata\":{\"resolved_model_slug\":\"test\"}}}\n");
                check(string.Join(" ", ModelEvidence.ReadCodexFile(log, out partial)).Contains("标识一致"), "codex_explicit_response_metadata_can_be_compared");
                check(ModelEvidence.DescribeCodexFiles(new [] {log}).Contains("已找到可核对的模型记录"), "comparable_model_summary_reports_available_evidence");
                var restored = JsonUtil.Serializer.Deserialize<AccountMeta>(JsonUtil.Serializer.Serialize(account));
                check(restored.SubscriptionExpiresOn == "2028-03-01" && restored.SubscriptionRead != null, "old_manual_and_new_subscription_fields_roundtrip");
                Application.EnableVisualStyles();
                account.SubscriptionRead = s;
                foreach (string theme in new [] {"custom", "heritage", "spring", "summer", "autumn", "winter"})
                foreach (int width in new [] {384, 620, 1240}) {
                    ThemeSelection.Select(theme, false);
                    var store = new AccountStore(true); store.Settings.Accounts.Add(account); store.PreviewActiveId = account.Id;
                    using (var window = new AccountPopup(null, store)) {
                        window.ShowInTaskbar = false; window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-20000, -20000); window.ClientSize = new Size(width, width==1240?1520:760); window.Show();
                        window.ReferenceCommand("tools", account);
                        var view = window.Controls.OfType<AccountToolsView>().Single();
                        var canvas = view.Controls.OfType<AccountToolsCanvas>().Single();
                        check(view.TopLevelControl == window && view.Dock == DockStyle.Fill && canvas.Dock == DockStyle.Fill && !canvas.Controls.OfType<Panel>().Any(), "embedded_theme_" + theme + "_" + width);
                        for (int i = 0; i < 4; i++) {
                            view.SelectedTab = i; window.PerformLayout(); Application.DoEvents();
                            using (var bmp = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size)); bmp.Save(Path.Combine(folder, "account-tools-" + theme + "-" + width + "-" + i + ".png")); }
                            if (i == 0) {
                                store.Settings.SubscriptionReadConsent=true; canvas.Invalidate(); Application.DoEvents();
                                using(var bmp=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(folder,"account-tools-"+theme+"-"+width+"-checked.png"));}
                                store.Settings.SubscriptionReadConsent=false;
                            }
                            if (i == 1) {
                                view.RecipientEditor.Text = "friend@example.com";
                                view.RecipientEditor.Focus();
                                Application.DoEvents();
                                using (var bmp = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(bmp, new Rectangle(Point.Empty,bmp.Size)); bmp.Save(Path.Combine(folder,"account-tools-"+theme+"-"+width+"-email.png")); }
                                view.RecipientEditor.Clear();
                            }
                        }
                        window.AllowExit = true; window.Close();
                    }
                }
                checks.Add("four_tabs_embedded_six_themes_three_sizes_including_double_resolution_no_real_accounts_or_web");
                File.WriteAllText(Path.Combine(folder, "verification.json"), JsonUtil.Serializer.Serialize(new { ok = true, count = checks.Count, checks = checks, live_network = false, real_invites = 0, real_account_mutations = 0 }));
                return 0;
            } catch (Exception ex) {
                File.WriteAllText(Path.Combine(folder, "verification.json"), JsonUtil.Serializer.Serialize(new { ok = false, completed = checks, error = ex.ToString() })); return 1;
            }
        }

        // Explicit read-only acceptance probe; no account store creation, token
        // refresh, invitation send, or raw response/credential persistence.
        internal static int ReadCurrent(string path, bool invitationsOnly = false)
        {
            var result = new Dictionary<string, object> { {"invites_sent", 0}, {"account_mutations", 0} };
            byte[] raw = null;
            try {
                raw = File.ReadAllBytes(PortablePaths.LiveAuth);
                string id = AuthCodec.Parse(raw).StableId;
                if (!invitationsOnly) try {
                    var s = AccountOnlineClient.ReadSubscription((byte[])raw.Clone(), id, CancellationToken.None).GetAwaiter().GetResult();
                    result["subscription"] = "read"; result["has_expiry"] = s.ExpiresAtUtc.Length > 0; result["has_renewal"] = s.RenewsAtUtc.Length > 0;
                } catch (Exception ex) { result["subscription"] = ex is OnlineToolException ? ex.Message : "unavailable"; }
                try {
                    var q = AccountOnlineClient.ReadEligibility((byte[])raw.Clone(), id, AccountOnlineClient.ConsumerProgram, CancellationToken.None).GetAwaiter().GetResult();
                    result["eligibility"] = "read"; result["eligible"] = q.ShouldShow; result["remaining_send_capacity"] = q.SendCapacity;
                } catch (Exception ex) { result["eligibility"] = ex is OnlineToolException ? ex.Message : "unavailable"; }
            } catch { result["current_account"] = "unavailable"; }
            finally { if (raw != null) Array.Clear(raw, 0, raw.Length); }
            File.WriteAllText(path, JsonUtil.Serializer.Serialize(result)); return 0;
        }

    }
}
