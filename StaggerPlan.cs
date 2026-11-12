using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAccountDock
{
    // A deliberately separate scheduler: it never changes LiveAuth, switches accounts,
    // restarts Codex Desktop, or borrows the foreground desktop session.
    internal static class StaggerPlan
    {
        private static readonly object Gate = new object();
        private static PlanEngine engine;
        private static PlanWindow window;

        public static void Initialize(AccountStore store)
        {
            if (store == null || store.IsPreview) return;
            lock (Gate)
            {
                if (engine != null) return;
                engine = new PlanEngine(store, new SystemClock(), new CodexCliSender());
                engine.Start();
            }
        }

        public static void Open(Form owner, AccountStore store)
        {
            Initialize(store);
            if (window == null || window.IsDisposed) window = new PlanWindow(store, engine);
            window.Reload();
            SidePanels.Activate(owner, window);
        }

        public static void Shutdown()
        {
            lock (Gate)
            {
                if (engine != null) engine.Dispose();
                engine = null;
            }
            if (window != null && !window.IsDisposed) window.Close();
            window = null;
        }

        // Called by the account store before it removes the primary encrypted
        // account copy.  Remove the account from future schedules first, then
        // remove its separate stagger-plan authorization.  Historical events
        // stay intact so the user can still audit earlier sends.
        internal static void RemoveAccountArtifacts(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId)) return;
            PlanStorage.Update(delegate(PlanSettings plan)
            {
                RemoveAccountReference(plan, accountId);
                return plan;
            });
            AuthVault.DeleteAccount(accountId);
            lock (Gate) { if (engine != null) engine.Reload(); }
        }

        private static void RemoveAccountReference(PlanSettings plan, string accountId)
        {
            if (plan == null) return;
            if (plan.Accounts == null) plan.Accounts = new List<PlanAccount>();
            plan.Accounts.RemoveAll(delegate(PlanAccount account)
            {
                return account != null && string.Equals(account.Id, accountId, StringComparison.Ordinal);
            });
            if (plan.Enabled && !plan.Accounts.Any(delegate(PlanAccount account) { return account != null && account.Selected; }))
                plan.Enabled = false;
        }

        internal static int SelfTest()
        {
            try
            {
                DateTimeOffset day = new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.FromHours(8));
                PlanSettings plan = PlanSettings.Default();
                plan.PlanDate = "2026-10-24";
                plan.StartMinutes = 9 * 60;
                plan.CutoffMinutes = 22 * 60 + 30;
                plan.IntervalMinutes = 100;
                plan.Accounts.Add(new PlanAccount { Id = "a", Selected = true, Message = "one" });
                plan.Accounts.Add(new PlanAccount { Id = "b", Selected = true, Message = "two" });
                plan.Accounts.Add(new PlanAccount { Id = "c", Selected = true, Message = "three" });
                List<PlanOccurrence> schedule = Planner.Build(plan, day);
                Assert(schedule.Count == 3, "three accounts");
                Assert(schedule[0].When.Hour == 9 && schedule[1].When.Hour == 10 && schedule[1].When.Minute == 40 && schedule[2].When.Hour == 12 && schedule[2].When.Minute == 20, "interval order");
                Assert(schedule.Select(delegate(PlanOccurrence x) { return x.AccountId; }).Distinct().Count() == 3, "one event per account");
                Assert(SameOccurrenceTime(schedule[0].When.ToString("o"),schedule[0].When),"exact execution time");
                Assert(!SameOccurrenceTime(schedule[0].When.AddHours(-1).ToString("o"),schedule[0].When),"old result must not represent edited time");

                PlanSettings cross=PlanSettings.Default();
                cross.PlanDate="2026-10-24";cross.CutoffDate="2026-10-25";cross.StartMinutes=23*60+50;cross.CutoffMinutes=30;cross.IntervalMinutes=20;
                cross.UniformMessage=true;cross.CommonMessage="cross-midnight";
                cross.Accounts.Add(new PlanAccount{Id="night-a",Selected=true});cross.Accounts.Add(new PlanAccount{Id="night-b",Selected=true});
                List<PlanOccurrence> crossSchedule=Planner.Build(cross,day);
                Assert(crossSchedule.Count==2 && crossSchedule[0].When.Day==24 && crossSchedule[1].When.Day==25 && crossSchedule[1].When.Hour==0 && crossSchedule[1].When.Minute==10,"cross midnight schedule");
                PlanSettings legacy=PlanSettings.Default();legacy.PlanDate="2026-10-24";legacy.CutoffDate="";legacy.StartMinutes=9*60;legacy.CutoffMinutes=10*60;legacy.CommonMessage="legacy";legacy.Accounts.Add(new PlanAccount{Id="legacy",Selected=true});
                List<PlanOccurrence> legacySchedule=Planner.Build(legacy,day);Assert(legacySchedule.Count==1 && legacySchedule[0].When.Day==24,"legacy same-day cutoff fallback");
                PlanSettings daily=PlanSettings.Default();daily.DailyRepeat=true;daily.PlanDate="invalid-ignored";daily.CutoffDate="invalid-ignored";daily.StartMinutes=9*60;daily.CutoffMinutes=10*60;daily.CommonMessage="daily";daily.Accounts.Add(new PlanAccount{Id="daily",Selected=true});
                List<PlanOccurrence> dailySchedule=Planner.Build(daily,day);Assert(dailySchedule.Count==1 && dailySchedule[0].When.Date==day.Date,"daily repeat uses current Taiwan day");
                Assert(Planner.Validate(daily,day.AddHours(5).ToUniversalTime()).Length==0,"daily repeat remains valid after today's time");
                DateTimeOffset beforeCross=new DateTimeOffset(2026,10,24,22,0,0,TimeSpan.FromHours(8));
                Assert(Planner.Validate(cross,beforeCross.ToUniversalTime()).Length==0,"future cross-midnight validation");
                PlanSettings invalidDate=PlanSettings.Default();invalidDate.PlanDate="not-a-date";invalidDate.CutoffDate="2026-10-25";invalidDate.CommonMessage="x";invalidDate.Accounts.Add(new PlanAccount{Id="x",Selected=true});
                Assert(Planner.Validate(invalidDate,beforeCross.ToUniversalTime()).Contains("开始日期无效"),"invalid date rejected");
                cross.PlanDate="2026-10-23";cross.CutoffDate="2026-10-24";
                Assert(Planner.Validate(cross,beforeCross.ToUniversalTime()).Contains("必须晚于"),"past start rejected");
                cross.PlanDate="2026-10-24";cross.CutoffDate="2026-10-25";

                plan.CutoffMinutes = 10 * 60;
                Assert(Planner.Build(plan, day).Count == 1, "strict cutoff");
                plan.Accounts.Clear();
                Assert(Planner.Build(plan, day).Count == 0, "no accounts");
                plan.Accounts.Add(new PlanAccount { Id = "a", Selected = true, Message = "" });
                Assert(Planner.Validate(plan).Contains("发送内容"), "empty content");
                plan.CommonMessage = "ok";
                plan.StartMinutes = 23 * 60;
                string cutoffError=Planner.Validate(plan,day.AddHours(-1).ToUniversalTime());
                Assert(cutoffError.Contains("停止"), "start after cutoff: "+cutoffError);
                string redacted=CleanExecutionError("\u001b[31mERROR: access_token=private_token https://example.test/?secret=hidden\u001b[0m");
                Assert(IsAuthorizationFailure("ERROR 401 Unauthorized token_revoked"),"revoked authorization classified");
                Assert(!IsAuthorizationFailure("network timeout") && !IsAuthorizationFailure("quota exceeded 429"),"network and quota not auth revocation");
                Assert(ExplainExecutionError("token_revoked").Contains("重登"),"actionable error explanation");
                var authEvents=new List<PlanEvent>{new PlanEvent{AccountId="a",Status="failed",Detail="token_revoked",FinishedAt=day.ToString("o")}};
                Assert(AuthorizationRejected(authEvents,"a",day.AddMinutes(-1).UtcDateTime),"old saved token rejected");
                Assert(!AuthorizationRejected(authEvents,"a",day.AddMinutes(1).UtcDateTime),"new login clears rejected status");
                Assert(!AuthorizationRejected(authEvents,"b",day.UtcDateTime),"rejection isolated per account");
                authEvents.Add(new PlanEvent{AccountId="a",Status="skipped"});
                Assert(AuthorizationRejected(authEvents,"a",day.AddMinutes(-1).UtcDateTime),"skipped round cannot clear revoked status");
                authEvents.Add(new PlanEvent{AccountId="a",Status="success"});
                Assert(!AuthorizationRejected(authEvents,"a",day.AddMinutes(-1).UtcDateTime),"later successful send clears rejected status");
                Assert(SendResult.Uncertain("timeout").Unknown && !SendResult.Fail("401").Unknown,"unknown distinguished from explicit failure");
                Assert(!redacted.Contains("private_token") && !redacted.Contains("secret=hidden") && !redacted.Contains("\u001b"),"error details redacted");

                plan = PlanSettings.Default();
                plan.Enabled = true;
                plan.PlanDate = "2026-10-24";
                plan.StartMinutes = 9 * 60;
                plan.CutoffMinutes = 22 * 60 + 30;
                plan.IntervalMinutes = 100;
                plan.UniformMessage = true;
                plan.CommonMessage = "health check";
                plan.Accounts.Add(new PlanAccount { Id = "a", Selected = true });
                FakeClock clock = new FakeClock(day.AddMinutes(3));
                FakeSender sender = new FakeSender();
                MemoryPlanStorage memory = new MemoryPlanStorage(plan);
                PlanEngine test = new PlanEngine(null, clock, sender, memory);
                test.EvaluateAsync().Wait();
                Assert(sender.Count == 0 && memory.Value.Events.Any(delegate(PlanEvent x) { return x.Status == "skipped"; }), "missed event skipped");
                test.EvaluateAsync().Wait();
                Assert(sender.Count == 0 && memory.Value.Events.Count == 1, "missed dedup");

                memory.Value.Events.Clear();
                clock.NowValue = day.AddSeconds(30);
                test.EvaluateAsync().Wait();
                Assert(sender.Count == 1 && memory.Value.Events.Count == 1 && memory.Value.Events[0].Status == "success", "fake sender success");
                test.EvaluateAsync().Wait();
                Assert(sender.Count == 1, "success dedup");

                PlanSettings rounds=PlanSettings.Default();rounds.Enabled=true;rounds.PlanDate="2026-10-24";rounds.StartMinutes=9*60;rounds.CutoffMinutes=22*60;rounds.UniformMessage=true;rounds.CommonMessage="round";rounds.Accounts.Add(new PlanAccount{Id="round-a",Selected=true});
                FakeClock roundsClock=new FakeClock(day.AddSeconds(30));FakeSender roundsSender=new FakeSender();MemoryPlanStorage roundsStorage=new MemoryPlanStorage(rounds);PlanEngine roundsEngine=new PlanEngine(null,roundsClock,roundsSender,roundsStorage);
                roundsEngine.EvaluateAsync().Wait();Assert(roundsSender.Count==1 && roundsStorage.Value.Events.Count==1,"morning round sends");
                PlanSettings afternoon=PlanSettings.Default();afternoon.Enabled=true;afternoon.PlanDate="2026-10-24";afternoon.StartMinutes=12*60+30;afternoon.CutoffMinutes=22*60;afternoon.UniformMessage=true;afternoon.CommonMessage="round";afternoon.Accounts.Add(new PlanAccount{Id="round-a",Selected=true});roundsStorage.SaveConfiguration(afternoon);roundsClock.NowValue=day.AddHours(3).AddMinutes(30).AddSeconds(30);
                roundsEngine.EvaluateAsync().Wait();Assert(roundsSender.Count==2 && roundsStorage.Value.Events.Count==2,"afternoon round sends after morning success");
                roundsEngine.EvaluateAsync().Wait();Assert(roundsSender.Count==2 && roundsStorage.Value.Events.Count==2,"same afternoon occurrence does not duplicate");roundsEngine.Dispose();

                PlanSettings legacyOccurrence=PlanSettings.Default();legacyOccurrence.Enabled=true;legacyOccurrence.PlanDate="2026-10-24";legacyOccurrence.StartMinutes=9*60;legacyOccurrence.CutoffMinutes=22*60;legacyOccurrence.UniformMessage=true;legacyOccurrence.CommonMessage="legacy occurrence";legacyOccurrence.Accounts.Add(new PlanAccount{Id="legacy-a",Selected=true});
                legacyOccurrence.Events.Add(new PlanEvent{Key="2026-10-24|legacy-a",AccountId="legacy-a",ScheduledAt=day.ToString("o",CultureInfo.InvariantCulture),Status="success"});
                FakeClock legacyClock=new FakeClock(day.AddSeconds(30));FakeSender legacySender=new FakeSender();MemoryPlanStorage legacyStorage=new MemoryPlanStorage(legacyOccurrence);PlanEngine legacyEngine=new PlanEngine(null,legacyClock,legacySender,legacyStorage);
                legacyEngine.EvaluateAsync().Wait();Assert(legacySender.Count==0 && legacyStorage.Value.Events.Count==1,"legacy exact occurrence remains deduplicated");
                PlanSettings legacyAfternoon=PlanSettings.Default();legacyAfternoon.Enabled=true;legacyAfternoon.PlanDate="2026-10-24";legacyAfternoon.StartMinutes=12*60+30;legacyAfternoon.CutoffMinutes=22*60;legacyAfternoon.UniformMessage=true;legacyAfternoon.CommonMessage="legacy occurrence";legacyAfternoon.Accounts.Add(new PlanAccount{Id="legacy-a",Selected=true});legacyStorage.SaveConfiguration(legacyAfternoon);legacyClock.NowValue=day.AddHours(3).AddMinutes(30).AddSeconds(30);
                legacyEngine.EvaluateAsync().Wait();Assert(legacySender.Count==1 && legacyStorage.Value.Events.Count==2,"legacy date key does not block different time");legacyEngine.Dispose();

                PlanSettings protectedOutcomes=PlanSettings.Default();protectedOutcomes.Enabled=true;protectedOutcomes.PlanDate="2026-10-24";protectedOutcomes.StartMinutes=9*60;protectedOutcomes.IntervalMinutes=10;protectedOutcomes.CutoffMinutes=22*60;protectedOutcomes.UniformMessage=true;protectedOutcomes.CommonMessage="protected";protectedOutcomes.Accounts.Add(new PlanAccount{Id="failed-a",Selected=true});protectedOutcomes.Accounts.Add(new PlanAccount{Id="unknown-a",Selected=true});protectedOutcomes.Accounts.Add(new PlanAccount{Id="sending-a",Selected=true});
                DateTimeOffset unknownAt=day.AddMinutes(10);DateTimeOffset sendingAt=day.AddMinutes(20);protectedOutcomes.Events.Add(new PlanEvent{Key=OccurrenceKey("failed-a",day),AccountId="failed-a",ScheduledAt=day.ToString("o",CultureInfo.InvariantCulture),Status="failed"});protectedOutcomes.Events.Add(new PlanEvent{Key="2026-10-24|unknown-a",AccountId="unknown-a",ScheduledAt=unknownAt.ToString("o",CultureInfo.InvariantCulture),Status="unknown"});protectedOutcomes.Events.Add(new PlanEvent{Key="2026-10-24|sending-a",AccountId="sending-a",ScheduledAt=sendingAt.ToString("o",CultureInfo.InvariantCulture),Status="sending"});
                FakeSender protectedSender=new FakeSender();MemoryPlanStorage protectedStorage=new MemoryPlanStorage(protectedOutcomes);PlanEngine protectedEngine=new PlanEngine(null,new FakeClock(sendingAt.AddSeconds(30)),protectedSender,protectedStorage);protectedEngine.EvaluateAsync().Wait();Assert(protectedSender.Count==0 && protectedStorage.Value.Events.Count==3,"failed, unknown, and sending outcomes protect the same occurrence");protectedEngine.Dispose();

                PlanSettings repeated=PlanSettings.Default();repeated.Enabled=true;repeated.DailyRepeat=true;repeated.StartMinutes=9*60;repeated.CutoffMinutes=10*60;repeated.UniformMessage=true;repeated.CommonMessage="daily round";repeated.Accounts.Add(new PlanAccount{Id="daily-a",Selected=true});
                FakeClock repeatedClock=new FakeClock(day.AddSeconds(30));FakeSender repeatedSender=new FakeSender();MemoryPlanStorage repeatedStorage=new MemoryPlanStorage(repeated);PlanEngine repeatedEngine=new PlanEngine(null,repeatedClock,repeatedSender,repeatedStorage);repeatedEngine.EvaluateAsync().Wait();repeatedClock.NowValue=day.AddDays(1).AddSeconds(30);repeatedEngine.EvaluateAsync().Wait();Assert(repeatedSender.Count==2 && repeatedStorage.Value.Events.Count==2,"daily repeat uses distinct occurrence dates");repeatedEngine.EvaluateAsync().Wait();Assert(repeatedSender.Count==2,"daily repeat same date deduplicates");repeatedEngine.Dispose();

                cross.Enabled=true;
                MemoryPlanStorage crossMemory=new MemoryPlanStorage(cross);
                FakeSender crossSender=new FakeSender();
                FakeClock crossClock=new FakeClock(new DateTimeOffset(2026,10,25,0,10,30,TimeSpan.FromHours(8)));
                PlanEngine crossEngine=new PlanEngine(null,crossClock,crossSender,crossMemory);
                crossEngine.EvaluateAsync().Wait();
                Assert(crossSender.Count==1 && crossMemory.Value.Events.Any(delegate(PlanEvent e){return e.AccountId=="night-a" && e.Status=="skipped";}) && crossMemory.Value.Events.Any(delegate(PlanEvent e){return e.AccountId=="night-b" && e.Status=="success";}),"next-day cross-midnight execution");
                crossEngine.EvaluateAsync().Wait();Assert(crossSender.Count==1,"cross-midnight dedup");crossEngine.Dispose();

                PlanSettings expired=PlanSettings.Default();expired.Enabled=true;expired.PlanDate="2026-01-01";expired.CutoffDate="";expired.StartMinutes=9*60;expired.CutoffMinutes=10*60;expired.CommonMessage="expired";expired.Accounts.Add(new PlanAccount{Id="expired",Selected=true});
                MemoryPlanStorage expiredMemory=new MemoryPlanStorage(expired);FakeSender expiredSender=new FakeSender();
                PlanEngine expiredEngine=new PlanEngine(null,new FakeClock(day),expiredSender,expiredMemory);expiredEngine.EvaluateAsync().Wait();
                Assert(expiredSender.Count==0 && expiredMemory.Value.Events.Count==0,"expired legacy one-shot remains inert");expiredEngine.Dispose();

                PlanSettings edited = PlanSettings.Default();
                edited.Enabled = true;
                edited.PlanDate = "2026-10-24";
                edited.StartMinutes = 9 * 60;
                edited.CutoffMinutes = 22 * 60 + 30;
                edited.IntervalMinutes = 100;
                edited.UniformMessage = true;
                edited.CommonMessage = "edited without replay";
                edited.Accounts.Add(new PlanAccount { Id = "a", Selected = true });
                memory.SaveConfiguration(edited);
                Assert(memory.Value.Events.Count == 1, "reenable preserves dedup events");
                test.EvaluateAsync().Wait();
                Assert(sender.Count == 1, "reenable does not resend same occurrence");

                PlanSettings racePlan = PlanSettings.Default();
                racePlan.Enabled = true;
                racePlan.PlanDate = "2026-10-24";
                racePlan.StartMinutes = 9 * 60;
                racePlan.CutoffMinutes = 22 * 60;
                racePlan.UniformMessage = true;
                racePlan.CommonMessage = "race";
                racePlan.Accounts.Add(new PlanAccount { Id = "race", Selected = true });
                MemoryPlanStorage raceStorage = new MemoryPlanStorage(racePlan);
                ControlledSender controlled = new ControlledSender();
                PlanEngine raceEngine = new PlanEngine(null, new FakeClock(day.AddSeconds(30)), controlled, raceStorage);
                Task pending = raceEngine.EvaluateAsync();
                Assert(controlled.Count == 1, "controlled send started");
                PlanSettings paused = PlanSettings.Default();
                paused.Enabled = false;
                paused.PlanDate = "2026-10-24";
                raceStorage.SaveConfiguration(paused);
                controlled.Complete();
                pending.Wait();
                Assert(!raceStorage.Value.Enabled, "send completion does not overwrite paused settings");
                raceEngine.Dispose();

                PlanSettings cancelPlan = PlanSettings.Default();
                cancelPlan.Enabled = true;
                cancelPlan.PlanDate = "2026-10-24";
                cancelPlan.StartMinutes = 9 * 60;
                cancelPlan.CutoffMinutes = 22 * 60;
                cancelPlan.UniformMessage = true;
                cancelPlan.CommonMessage = "cancel";
                cancelPlan.Accounts.Add(new PlanAccount { Id = "cancel", Selected = true });
                MemoryPlanStorage cancelStorage = new MemoryPlanStorage(cancelPlan);
                ControlledSender cancelSender = new ControlledSender();
                PlanEngine cancelEngine = new PlanEngine(null, new FakeClock(day.AddSeconds(30)), cancelSender, cancelStorage);
                Task cancelling = cancelEngine.EvaluateAsync();
                cancelEngine.Dispose();
                cancelling.Wait();
                Assert(cancelStorage.Value.Events.Any(delegate(PlanEvent e) { return e.Status == "unknown" && e.Detail.Contains("已取消"); }), "shutdown cancels in-flight send without claiming definite failure");

                PlanSettings trackedPlan = PlanSettings.Default();
                trackedPlan.Enabled = true;
                trackedPlan.PlanDate = "2026-10-24";
                trackedPlan.StartMinutes = 9 * 60;
                trackedPlan.CutoffMinutes = 22 * 60;
                trackedPlan.UniformMessage = true;
                trackedPlan.CommonMessage = "tracked shutdown";
                trackedPlan.Accounts.Add(new PlanAccount { Id = "tracked", Selected = true });
                MemoryPlanStorage trackedStorage = new MemoryPlanStorage(trackedPlan);
                ControlledSender trackedSender = new ControlledSender();
                PlanEngine trackedEngine = new PlanEngine(null, new FakeClock(day.AddSeconds(30)), trackedSender, trackedStorage);
                trackedEngine.Start();
                trackedEngine.Reload();
                Assert(trackedSender.WaitUntilStarted(3000), "tracked timer send started");
                trackedEngine.Dispose();
                Assert(trackedStorage.Value.Events.Any(delegate(PlanEvent e) { return e.AccountId == "tracked" && e.Status == "unknown" && e.Detail.Contains("已取消"); }), "tracked shutdown reaches terminal event");

                PlanSettings removed = PlanSettings.Default();
                removed.Enabled = true;
                removed.Accounts.Add(new PlanAccount { Id = "remove", Selected = true });
                removed.Events.Add(new PlanEvent { Key = "history|remove", AccountId = "remove", Status = "success" });
                RemoveAccountReference(removed, "remove");
                Assert(!removed.Enabled && removed.Accounts.Count == 0 && removed.Events.Count == 1, "account removal pauses empty plan and preserves history");

                PlanSettings partlyRemoved = PlanSettings.Default();
                partlyRemoved.Enabled = true;
                partlyRemoved.Accounts.Add(new PlanAccount { Id = "remove-one", Selected = true });
                partlyRemoved.Accounts.Add(new PlanAccount { Id = "keep-one", Selected = true });
                partlyRemoved.Events.Add(new PlanEvent { Key = "history|remove-one", AccountId = "remove-one", Status = "success" });
                RemoveAccountReference(partlyRemoved, "remove-one");
                Assert(partlyRemoved.Enabled && partlyRemoved.Accounts.Count == 1 && partlyRemoved.Accounts[0].Id == "keep-one" && partlyRemoved.Events.Count == 1, "account removal preserves other enabled account and history");
                VerifySyntheticAuthDeletion();

                PlanSettings interrupted = PlanSettings.Default();
                interrupted.Enabled = true;
                interrupted.Events.Add(new PlanEvent { Key = "interrupted|a", AccountId = "a", Status = "sending", Detail = "started" });
                MemoryPlanStorage interruptedStorage = new MemoryPlanStorage(interrupted);
                PlanEngine interruptedEngine = new PlanEngine(null, new FakeClock(day), new FakeSender(), interruptedStorage);
                interruptedEngine.RecoverInterruptedEvents();
                Assert(interruptedStorage.Value.Events[0].Status == "unknown" && interruptedStorage.Value.Events[0].Detail.Contains("结果未知"), "startup marks interrupted send unknown");
                Assert(interruptedStorage.UpdateCount == 1, "interrupted recovery writes once");
                interruptedEngine.Dispose();

                MemoryPlanStorage cleanStartupStorage = new MemoryPlanStorage(PlanSettings.Default());
                PlanEngine cleanStartupEngine = new PlanEngine(null, new FakeClock(day), new FakeSender(), cleanStartupStorage);
                cleanStartupEngine.RecoverInterruptedEvents();
                Assert(cleanStartupStorage.UpdateCount == 0, "clean startup does not rewrite plan storage");
                cleanStartupEngine.Dispose();

                PlanSettings throwingPlan = PlanSettings.Default();
                throwingPlan.Enabled = true;
                throwingPlan.PlanDate = "2026-10-24";
                throwingPlan.StartMinutes = 9 * 60;
                throwingPlan.CutoffMinutes = 22 * 60;
                throwingPlan.UniformMessage = true;
                throwingPlan.CommonMessage = "throw";
                throwingPlan.Accounts.Add(new PlanAccount { Id = "throw", Selected = true });
                MemoryPlanStorage throwingStorage = new MemoryPlanStorage(throwingPlan);
                PlanEngine throwingEngine = new PlanEngine(null, new FakeClock(day.AddSeconds(30)), new ThrowingSender(), throwingStorage);
                throwingEngine.EvaluateAsync().Wait();
                Assert(throwingStorage.Value.Events.Any(delegate(PlanEvent e) { return e.Status == "unknown"; }), "unexpected sender exception is finalized");
                throwingEngine.Dispose();
                throwingPlan.Events.Clear();
                MemoryPlanStorage timeoutStorage=new MemoryPlanStorage(throwingPlan);
                PlanEngine timeoutEngine=new PlanEngine(null,new FakeClock(day.AddSeconds(30)),new UncertainSender(),timeoutStorage);
                timeoutEngine.EvaluateAsync().Wait();timeoutEngine.EvaluateAsync().Wait();
                Assert(timeoutStorage.Value.Events.Count==1 && timeoutStorage.Value.Events[0].Status=="unknown","timeout remains unknown and is not retried");
                timeoutEngine.Dispose();

                DateTimeOffset utc = new DateTimeOffset(2026, 10, 24, 1, 0, 0, TimeSpan.Zero);
                Assert(Planner.Taiwan(utc).Hour == 9 && Planner.Taiwan(utc).Offset == TimeSpan.FromHours(8), "taiwan timezone");
                string isolation = CliProcess.BuildExecArguments();
                Assert(isolation.Contains("--disable shell_tool") && isolation.Contains("--disable computer_use") && isolation.Contains("web_search=disabled") && isolation.Contains("approval_policy=never"), "explicit tool isolation");
                VerifyStrictUtf8Pipe();
                Console.WriteLine("STAGGER_PLAN_OK interval noaccounts emptycontent cutoff explicit_dates invalid_date past_rejected cross_midnight_next_day legacy_expired_inert timezone dedup reenable stale_settings shutdown_cancel tracked_shutdown interrupted_unknown clean_startup_no_write sender_exception account_cleanup auth_delete_idempotent isolation_flags strict_utf8_pipe missed fake_sender no_network no_real_credentials");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("STAGGER_PLAN_FAILED " + ex.Message);
                return 1;
            }
        }

        // Explicit diagnostic only: never called by the scheduler or an offline test.
        // A run ID is claimed on disk before any request, so rerunning a command
        // after an interrupted terminal cannot silently duplicate paid sends.
        internal static int RunAuthorizedProbe(string runId, bool send)
        {
            Guid parsed;
            if(!Guid.TryParseExact(runId,"N",out parsed))throw new ArgumentException("Invalid diagnostic run ID");
            AccountStore store=new AccountStore();
            List<AccountMeta> accounts=store.Settings.Accounts.Where(a=>AuthVault.IsReady(a)).ToList();
            foreach(AccountMeta account in accounts)Console.WriteLine("AUTHORIZED_ACCOUNT "+account.Email);
            if(accounts.Count!=3){Console.WriteLine("AUTHORIZED_PROBE_REFUSED expected=3 ready="+accounts.Count);return 2;}
            if(!send){Console.WriteLine("AUTHORIZED_PROBE_READY count=3 no_send");return 0;}
            string folder=Path.Combine(PortablePaths.Data,"stagger-test-results");Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,runId+".log");
            using(FileStream claim=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
            using(StreamWriter record=new StreamWriter(claim,new UTF8Encoding(false)))
            {
                record.AutoFlush=true;record.WriteLine("User-authorized one-shot test; no retries; "+DateTime.UtcNow.ToString("o"));
                int failures=0;CodexCliSender sender=new CodexCliSender();
                foreach(AccountMeta account in accounts)
                {
                    record.WriteLine("START "+account.Email);Console.WriteLine("AUTHORIZED_SEND_START "+account.Email);
                    SendResult result;
                    using(CancellationTokenSource cancel=new CancellationTokenSource(TimeSpan.FromSeconds(130)))
                    {
                        try{result=sender.SendAsync(account,"这是账号错峰发送测试。请仅回复：OK",cancel.Token).GetAwaiter().GetResult();}
                        catch(Exception ex){result=SendResult.Fail(SafeError(ex));}
                    }
                    if(!result.Success)failures++;
                    string line=account.Email+" success="+result.Success+" "+result.Detail;
                    record.WriteLine(line);Console.WriteLine("AUTHORIZED_SEND_RESULT "+line);
                }
                Console.WriteLine("AUTHORIZED_PROBE_DONE tested=3 failures="+failures+" no_retry no_schedule_mutation");
                return failures==0?0:1;
            }
        }

        internal static int RunUiDiagnostic()
        {
            try
            {
                string priorError=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output","stagger-ui-error.txt");
                if(File.Exists(priorError))File.Delete(priorError);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                AccountStore store = new AccountStore(true);
                string[] emails = { "work.account@example.com", "research@example.com", "studio@example.com", "backup.account@example.com", "archive@example.com" };
                for (int i = 0; i < emails.Length; i++) store.Settings.Accounts.Add(new AccountMeta { Id = "ui-" + i.ToString(CultureInfo.InvariantCulture), Email = emails[i], Plan = "PLUS" });
                PlanSettings plan = PlanSettings.Default();
                plan.Enabled = false;
                plan.StartMinutes = 9 * 60;
                plan.CutoffMinutes = 22 * 60 + 30;
                plan.IntervalMinutes = 100;
                plan.UniformMessage = true;
                plan.CommonMessage = "请用三句话介绍一个时间管理方法。";
                for (int i = 0; i < store.Settings.Accounts.Count; i++) plan.Accounts.Add(new PlanAccount { Id = store.Settings.Accounts[i].Id, Selected = true, Message = i == 1 ? new string('旧', 520) : "第 " + (i + 1).ToString(CultureInfo.InvariantCulture) + " 个账号的离线示例内容" });
                MemoryPlanStorage memory = new MemoryPlanStorage(plan);
                string oldTheme = ThemeSelection.Current;
                using (PlanWindow test = new PlanWindow(store, null, memory))
                {
                    test.Reload();
                    test.Show(); Application.DoEvents();
                    Assert(test.DiagnosticEditorCount == 5 && test.DiagnosticVisiblePreviewRows == 3, "five accounts and first preview page");
                    test.DiagnosticPreviewPage(1);Application.DoEvents();Assert(test.DiagnosticVisiblePreviewRows==2,"fourth and fifth account preview reachable");
                    test.DiagnosticPreviewPage(0);
                    test.DiagnosticSetMode(false, 1);
                    Assert(test.DiagnosticIndividualLength == 520, "legacy message preserved");
                    test.DiagnosticEditIndividual("第二个账号的独立文本");
                    test.DiagnosticSetMode(false,2);test.DiagnosticEditIndividual("第三个账号的独立文本");
                    test.DiagnosticSetMode(true,0);test.DiagnosticSetMode(false,1);
                    Assert(test.DiagnosticIndividualText=="第二个账号的独立文本","individual text does not leak across accounts or uniform mode");
                    test.DiagnosticSetMode(false,2);Assert(test.DiagnosticIndividualText=="第三个账号的独立文本","third account text preserved");
                    test.DiagnosticSetMode(true, 0);
                    test.DiagnosticSetInterval(0, 0);
                    test.DiagnosticSaveDraft();
                    Assert(!test.DiagnosticStoredEnabled && test.DiagnosticStoredInterval == 1, "draft remains disabled and interval lower bound");
                    DateTime futureStart=Planner.Taiwan(DateTimeOffset.UtcNow).Date.AddDays(2);
                    DateTime futureCutoff=futureStart.AddDays(1);
                    test.DiagnosticSetDates(futureStart,futureCutoff);
                    test.DiagnosticSetTimes(23*60+50,30);
                    test.DiagnosticSetInterval(0,20);
                    test.DiagnosticSaveDraft();
                    Assert(test.DiagnosticStoredStartDate==futureStart.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture) && test.DiagnosticStoredCutoffDate==futureCutoff.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),"draft preserves explicit dates");
                    Assert(test.DiagnosticFirstPreviewTime.StartsWith(futureStart.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),StringComparison.Ordinal),"preview shows full date");
                    string calendarPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output","stagger-calendar.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(calendarPath));
                    test.DiagnosticSaveCalendar(calendarPath);Assert(File.Exists(calendarPath),"calendar screenshot");
                    memory.Value.PlanDate="invalid-date";memory.Value.CutoffDate="invalid-cutoff";test.Reload();test.DiagnosticSaveDraft();
                    Assert(test.DiagnosticStoredStartDate=="invalid-date" && test.DiagnosticStoredCutoffDate=="invalid-cutoff","invalid stored dates not silently normalized");
                    test.DiagnosticSetDates(futureStart,futureCutoff);
                    test.DiagnosticSetInterval(12, 59);
                    test.DiagnosticSaveDraft();
                    Assert(test.DiagnosticStoredInterval == 720, "interval upper bound");
                    test.DiagnosticSetInterval(1, 40);
                    test.DiagnosticSetTimes(9 * 60, 22 * 60 + 30);
                    foreach (Size size in new[] { new Size(720, 860), new Size(500, 620) })
                    {
                        test.Size = size; test.Update(); Application.DoEvents();
                        string boundsProblem = test.DiagnosticBoundsProblem();
                        Assert(boundsProblem.Length == 0, "controls inside " + size.Width.ToString(CultureInfo.InvariantCulture) + " " + boundsProblem);
                        SaveUiShot(test, "stagger-ui-" + size.Width.ToString(CultureInfo.InvariantCulture) + "x" + size.Height.ToString(CultureInfo.InvariantCulture) + ".png");
                    }
                    test.Size=new Size(720,860);test.Refresh();Application.DoEvents();
                    test.DiagnosticProgrammaticScroll(34);Application.DoEvents();Application.DoEvents();
                    Assert(test.DiagnosticScrollOffset>0 && test.DiagnosticLastRepaintScrollOffset==test.DiagnosticScrollOffset,"programmatic middle scroll repaints at final geometry");
                    Assert(test.DiagnosticAncestorRimReplayPixels()==0,"ancestor rounded rim is not replayed inside scrolled child pixels");
                    test.Refresh();Application.DoEvents();
                    SaveUiShot(test,"stagger-scrolled-middle-720x860.png");
                    foreach(string scrollPath in new[]{"wheel","line","thumb","programmatic"})
                    {
                        test.DiagnosticNativeScroll(true);Application.DoEvents();Application.DoEvents();
                        if(scrollPath=="thumb"){test.DiagnosticProgrammaticScroll(32);Application.DoEvents();Application.DoEvents();}
                        int beforePaints=test.DiagnosticScrollPaints;
                        if(scrollPath=="wheel")test.DiagnosticMouseWheelDown();
                        else if(scrollPath=="line")test.DiagnosticNativeLineDown();
                        else if(scrollPath=="thumb")test.DiagnosticNativeThumb(test.DiagnosticScrollOffset);
                        else test.DiagnosticProgrammaticScroll(32);
                        Application.DoEvents();Application.DoEvents();
                        Assert(test.DiagnosticScrollOffset>0,scrollPath+" scroll changes offset");
                        Assert(test.DiagnosticScrollPaints>beforePaints && test.DiagnosticLastRepaintScrollOffset==test.DiagnosticScrollOffset,scrollPath+" deferred repaint uses final geometry");
                    }
                    test.Size=new Size(500,620);test.Refresh();Application.DoEvents();
                    int scrollPaints=test.DiagnosticScrollPaints;
                    test.DiagnosticNativeScroll(false);Application.DoEvents();
                    Assert(test.DiagnosticScrollOffset>0 && test.DiagnosticScrollPaints>scrollPaints,"native scroll invalidates background and children");
                    SaveUiShot(test,"stagger-scrolled-bottom.png");
                    test.DiagnosticNativeScroll(true);Application.DoEvents();
                    Assert(test.DiagnosticScrollOffset==0,"native scroll top");
                    SaveUiShot(test,"stagger-scrolled-top.png");
                    test.Size = new Size(720, 860);
                    foreach (string theme in new[] { "heritage", "spring", "summer", "autumn", "winter", "custom" })
                    {
                        ThemeSelection.Select(theme, false); test.RefreshGlass(); test.Update(); Application.DoEvents();
                        Assert(test.DiagnosticBoundsInside(), "theme bounds " + theme);
                        Assert(test.DiagnosticInputsReadable,"visible input text after theme change "+theme);
                        SaveUiShot(test, "stagger-ui-theme-" + theme + ".png");
                    }
                    // Exercise repeated layout and invalidation without reading
                    // pixels from any other desktop window.
                    foreach (Size liveSize in new[] { new Size(500,620),new Size(720,860),new Size(540,700),new Size(720,860) })
                    {
                        test.Size=liveSize;test.DiagnosticToggleRepeat();
                        test.DiagnosticSetMode(!test.DiagnosticUniformMode,0);
                        test.Refresh();Application.DoEvents();
                    }
                    test.DiagnosticSetMode(true,0);test.DiagnosticSetRepeat(false);
                    test.Size=new Size(720,860);test.Refresh();Application.DoEvents();
                    SaveUiShot(test, "stagger-ui.png");
                    test.DiagnosticSetRepeat(true);test.Refresh();Application.DoEvents();SaveUiShot(test,"stagger-ui-daily-repeat.png");test.DiagnosticSetRepeat(false);
                    test.DiagnosticSetTimes(9*60,22*60+30);
                    DateTimeOffset diagnosticDate=Planner.Taiwan(DateTimeOffset.UtcNow);
                    string diagnosticDay=diagnosticDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                    test.DiagnosticSetDates(diagnosticDate.Date.AddDays(-1),diagnosticDate.Date.AddDays(-1));
                    Assert(test.DiagnosticFirstPreviewState=="已过期" && test.DiagnosticFirstPreviewDetail.Contains("不会自动补发"),"expired one-shot preview is not pending");
                    test.DiagnosticSetDates(diagnosticDate.Date,diagnosticDate.Date);
                    memory.Value.Events.Add(new PlanEvent {Key=diagnosticDay+"|ui-0",AccountId="ui-0",ScheduledAt=diagnosticDay+"T08:00:00+08:00",FinishedAt=diagnosticDay+"T08:00:01+08:00",Status="failed",Detail="离线历史错误示例"});
                    test.DiagnosticPreviewPage(0);test.Refresh();Application.DoEvents();
                    Assert(test.DiagnosticFirstPreviewState!="历史记录" && !test.DiagnosticFirstPreviewDetail.Contains("08:00:00") && !test.DiagnosticFirstPreviewDetail.Contains("离线历史错误示例"),"earlier same-day result must not replace current occurrence preview");
                    SaveUiShot(test,"stagger-current-round.png");
                    memory.Value.Events.Add(new PlanEvent{AccountId="ui-0",ScheduledAt=diagnosticDay+"T09:00:00+08:00",FinishedAt=diagnosticDay+"T09:00:01+08:00",Status="failed",Detail="ERROR 401 Unauthorized token_revoked"});
                    test.DiagnosticPreviewPage(0);test.Refresh();Application.DoEvents();
                    Assert(test.DiagnosticFirstPreviewState=="需重登" && test.DiagnosticFirstPreviewDetail.Contains("重登"),"revoked token has actionable preview status and details");
                    SaveUiShot(test,"stagger-auth-rejected.png");
                    test.Size=new Size(1080,1290);test.Refresh();Application.DoEvents();SaveUiShot(test,"stagger-dpi-150.png");
                    test.Size=new Size(720,860);test.Refresh();Application.DoEvents();
                    test.Close();
                }
                ThemeSelection.Select(oldTheme, false);
                Console.WriteLine("STAGGER_UI_OK accounts=5 preview_page=3+2 unified individual legacy520 interval1_720 explicit_dates cross_midnight full_date_preview expired_not_pending invalid_dates_preserved shared_calendar single_repeat_screens draft_disabled small500x620 middle720x860 wheel_line_thumb_programmatic_final_repaint ancestor_rim_clip themes no_enable no_send no_auth no_network");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("STAGGER_UI_FAILED " + ex.Message);
                string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"stagger-ui-error.txt"),ex.ToString(),new UTF8Encoding(false));
                return 1;
            }
        }

        private static void SaveUiShot(Form form, string name)
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-output");
            Directory.CreateDirectory(folder);
            using (Bitmap image = new Bitmap(Math.Max(1, form.ClientSize.Width), Math.Max(1, form.ClientSize.Height)))
            {
                form.DrawToBitmap(image, form.ClientRectangle);
                PlanWindow plan=form as PlanWindow;if(plan!=null)plan.CompositeDiagnosticNativeInput(image);
                image.Save(Path.Combine(folder, name), System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static bool SameOccurrenceTime(string recorded,DateTimeOffset scheduled)
        {
            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(recorded,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out parsed) && parsed==scheduled;
        }

        private static string OccurrenceKey(string accountId,DateTimeOffset scheduled)
        {
            return "occurrence|"+scheduled.ToString("o",CultureInfo.InvariantCulture)+"|"+(accountId??"");
        }

        private static bool EventMatchesOccurrence(PlanEvent item,PlanOccurrence occurrence)
        {
            if(item==null || occurrence==null || !string.Equals(item.AccountId,occurrence.AccountId,StringComparison.Ordinal))return false;
            if(string.Equals(item.Key,OccurrenceKey(occurrence.AccountId,occurrence.When),StringComparison.Ordinal))return true;
            // Older versions keyed one result per account and calendar day.  Use
            // its stored scheduled time so that only the same occurrence remains
            // protected; a later round on that day is allowed to run.
            return SameOccurrenceTime(item.ScheduledAt,occurrence.When);
        }

        private static void VerifyStrictUtf8Pipe()
        {
            string script = "$inputStream=[Console]::OpenStandardInput();"
                + "$buffer=New-Object System.IO.MemoryStream;"
                + "$inputStream.CopyTo($buffer);"
                + "$bytes=$buffer.ToArray();"
                + "$utf8=New-Object System.Text.UTF8Encoding($false,$true);"
                + "try{$text=$utf8.GetString($bytes)}catch{[Console]::Error.WriteLine($_.Exception.Message);exit 9};"
                + "[Console]::Out.WriteLine([Convert]::ToBase64String($bytes));"
                + "[Console]::Out.WriteLine([Convert]::ToBase64String($utf8.GetBytes($text)));";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powershell)) powershell = "powershell.exe";
            ProcessStartInfo psi = new ProcessStartInfo(powershell, "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            CliProcess.ConfigureStrictUtf8Input(psi);
            string sample = CliProcess.GuardedMessage("你好，你是什么模型\r\n中文 English 0123456789 " + char.ConvertFromUtf32(0x1F680));
            byte[] expected = new UTF8Encoding(false, true).GetBytes(sample);
            using (Process process = new Process { StartInfo = psi })
            {
                Assert(process.Start(), "utf8 probe started");
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                CliProcess.WriteStrictUtf8(process, sample);
                Assert(process.WaitForExit(8000), "utf8 pipe EOF");
                Task.WaitAll(new Task[] { stdout, stderr }, 2000);
                Assert(process.ExitCode == 0, "utf8 child strict decode " + (stderr.IsCompleted ? stderr.Result.Trim() : ""));
                string[] lines = (stdout.IsCompleted ? stdout.Result : "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                Assert(lines.Length == 2, "utf8 probe output");
                byte[] actual = Convert.FromBase64String(lines[0]);
                Assert(actual.SequenceEqual(expected), "utf8 exact bytes");
                Assert(!(actual.Length >= 3 && actual[0] == 0xEF && actual[1] == 0xBB && actual[2] == 0xBF), "utf8 no BOM");
                string decoded = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(lines[1]));
                Assert(decoded == sample, "utf8 child text");
            }
        }

        private static void VerifySyntheticAuthDeletion()
        {
            string root = Path.Combine(Path.GetTempPath(), "CodexAccountDock-auth-delete-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string accountId = "synthetic-account";
                string target = AuthVault.FileForRoot(root, accountId);
                string temp = target + ".tmp";
                string other = AuthVault.FileForRoot(root, "other-account");
                File.WriteAllBytes(target, new byte[] { 1 });
                File.WriteAllBytes(temp, new byte[] { 2 });
                File.WriteAllBytes(other, new byte[] { 3 });
                AuthVault.DeleteAccountFiles(root, accountId);
                AuthVault.DeleteAccountFiles(root, accountId);
                Assert(!File.Exists(target) && !File.Exists(temp) && File.Exists(other), "auth deletion is idempotent and account-scoped");
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
        }

        private sealed class PlanWindow : DockedGlassWindow
        {
            private readonly AccountStore store;
            private readonly PlanEngine planEngine;
            private readonly IPlanStorage storage;
            private readonly PlanDateInput startDate = DatePicker();
            private readonly PlanDateInput cutoffDate = DatePicker();
            private readonly PlanTimeInput start = TimePicker();
            private readonly PlanTimeInput cutoff = TimePicker();
            private readonly PlanNumberInput intervalHours = Number(0, 12, 1);
            private readonly PlanNumberInput intervalMinutes = Number(0, 59, 40);
            private readonly CheckBox repeat = Check("每天重复");
            private readonly PairedTextBox common = Editor();
            private readonly PairedTextBox individual = Editor();
            private readonly ComboBox accountChoice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            private readonly PlanGlassPanel body = new PlanGlassPanel { AutoScroll = true, TabStop=true };
            private readonly PlanGlassPanel content = new PlanGlassPanel { Surface=false,Border=false };
            private readonly PlanGlassPanel timeSection = new PlanGlassPanel { Surface=false,Border=false,Divider=true };
            private readonly PlanGlassPanel accountSection = new PlanGlassPanel { Surface=false,Border=false,Divider=true };
            private readonly PlanGlassPanel messageSection = new PlanGlassPanel { Surface=false,Border=false,Divider=true };
            private readonly PlanGlassPanel previewSection = new PlanGlassPanel { Surface=false,Border=false };
            private readonly PlanGlassPanel messageFrame = new PlanGlassPanel { Alpha=255, Flat=true };
            private readonly Label messageHint = KaiLabel("示例内容，可自由修改；启用前核对完整内容。",12,false);
            private readonly Label themeSubtitle = KaiLabel("",15,false);
            private readonly FlowLayoutPanel accountGrid = new PlanFlowPanel { WrapContents = true, AutoScroll = false, Margin = Padding.Empty, Padding = Padding.Empty };
            private readonly Panel previewRows = new PlanTimelinePanel();
            private readonly List<Label> previewEmails = new List<Label>();
            private readonly List<Label> previewTimes = new List<Label>();
            private readonly List<Label> previewStates = new List<Label>();
            private readonly List<PlanGlassPanel> previewCards = new List<PlanGlassPanel>();
            private readonly Label previewEmpty = KaiLabel("选择账号并填写内容后，这里会显示发送顺序。", 14, false);
            private readonly Label previewPage = TimesLabel("0 / 0", 13, true);
            private readonly Button previewPrevious = ActionButton("‹", 42);
            private readonly Button previewNext = ActionButton("›", 42);
            private readonly Label state = KaiLabel("未启用", 14, true);
            private readonly Label counter = KaiLabel("0/500", 11, false);
            private readonly Button uniformButton = ActionButton("统一内容", 150);
            private readonly Button individualButton = ActionButton("分别设置", 150);
            private readonly Button enable = ActionButton("启用计划", 150);
            private readonly Button pause = ActionButton("暂停计划", 112);
            private readonly List<AccountEditor> editors = new List<AccountEditor>();
            private PlanSettings settings;
            private bool loading;
            private bool uniformMode = true;
            private int individualIndex = -1;
            private int previewPageIndex;
            private bool normalizingInterval;
            private float PlanScale{get{return Math.Max(1f,ClientSize.Height/860f);}}
            private bool scalingPlan;private float appliedPlanScale=1;
            private readonly Dictionary<Control,Rectangle> basePlanBounds=new Dictionary<Control,Rectangle>();
            private readonly Dictionary<Control,Font> basePlanFonts=new Dictionary<Control,Font>();
            private IEnumerable<Control> PlanTree(Control root){yield return root;foreach(Control child in root.Controls)foreach(Control item in PlanTree(child))yield return item;}
            private void RestorePlanGeometry(){if(appliedPlanScale<=1.01f)return;foreach(Control item in PlanTree(content)){item.SuspendLayout();Rectangle box;if(basePlanBounds.TryGetValue(item,out box))item.Bounds=box;Font font;if(!(item is PairedTextBox)&&basePlanFonts.TryGetValue(item,out font))item.Font=font;}}
            private void ApplyPlanGeometry()
            {
                if(PlanScale<=1.01f){foreach(Control item in PlanTree(content).Reverse())item.ResumeLayout(false);accountGrid.PerformLayout();appliedPlanScale=1;return;}
                var controls=PlanTree(content).ToArray();
                foreach(Control item in controls){basePlanBounds[item]=item.Bounds;if(!basePlanFonts.ContainsKey(item))basePlanFonts[item]=item.Font;}
                foreach(Control item in controls){Rectangle r=basePlanBounds[item];float scale=PlanScale;item.SetBounds((int)Math.Round(r.X*scale),(int)Math.Round(r.Y*scale),(int)Math.Round(r.Width*scale),(int)Math.Round(r.Height*scale));Font f=basePlanFonts[item];if(!(item is PairedTextBox)&&scale>1.01f && Math.Abs(item.Font.Size-f.Size*scale)>.01f)item.Font=new Font(f.FontFamily,f.Size*scale,f.Style,f.Unit);PairedTextBox input=item as PairedTextBox;if(input!=null)input.SetPixelScale(scale);}
                foreach(Control item in controls.Reverse())item.ResumeLayout(false);accountGrid.PerformLayout();appliedPlanScale=PlanScale;
            }
            private void PlanBounds(Control control,int x,int y,int width,int height){float scale=PlanScale;control.SetBounds((int)(x*scale),(int)(y*scale),(int)(width*scale),(int)(height*scale));}
            private bool startDateDirty;
            private bool cutoffDateDirty;

            internal PlanWindow(AccountStore store, PlanEngine engine, IPlanStorage storageOverride = null)
            {
                this.store = store;
                planEngine = engine;
                storage = storageOverride ?? (store != null && store.IsPreview
                    ? (IPlanStorage)new MemoryPlanStorage(PlanSettings.Default())
                    : new FilePlanStorage());
                Text = "错峰计划";
                Size = new Size(720, 860);
                MinimumSize = new Size(500, 620);
                Font = Kai(11, false);Padding=new Padding(14);

                PlanGlassPanel header = new PlanGlassPanel { Name="planHeader", Dock = DockStyle.Top, Height = 90, Surface=false,Border=false };
                Label title = KaiLabel("错峰计划", 34, true); if(ThemeVisual.IsReferenceCustom)title.ForeColor=Color.FromArgb(63,45,99); title.SetBounds(20, 7, 260, 42);
                Label subtitle = themeSubtitle; subtitle.SetBounds(22, 47, 390, 26);
                Label zone = KaiLabel("台湾时间 · Asia/Taipei", 15, true); zone.TextAlign = ContentAlignment.MiddleRight; zone.SetBounds(330, 12, 230, 35); zone.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                state.SetBounds(568, 12, 92, 36); state.TextAlign = ContentAlignment.MiddleCenter; state.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                ((PlanLineLabel)state).Pill=true;
                Button close = ActionButton("×",42); close.SetBounds(668, 12, 38, 36); close.Anchor = AnchorStyles.Top | AnchorStyles.Right; close.Click += delegate { Close(); };
                header.Controls.AddRange(new Control[] { title, subtitle, zone, state, close });

                BuildTimeSection();
                BuildAccountSection();
                BuildMessageSection();
                BuildPreviewSection();

                PlanGlassPanel footer = new PlanGlassPanel { Name="planFooter", Dock = DockStyle.Bottom, Height = 100,Surface=false,Border=false };
                Label safe = KaiLabel("◇  不切换前台账号 · 不关闭工作窗口", 13, true); safe.SetBounds(20, 8, 300, 28);
                Label truth = KaiLabel("ⓘ  发送成功不代表额度已重置", 13, true); truth.SetBounds(326, 8, 280, 28);
                ((PlanLineLabel)safe).Pill=true;((PlanLineLabel)truth).Pill=true;safe.ForeColor=truth.ForeColor=Color.White;
                Button draft = ActionButton("保存草稿", 150); draft.Click += delegate { SaveDraft(); };
                draft.SetBounds(Width - 344, 43, 150, 40); draft.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                enable.SetBounds(Width - 184, 43, 164, 40); enable.Anchor = AnchorStyles.Top | AnchorStyles.Right; enable.Click += delegate { EnablePlan(); };
                pause.SetBounds(20, 43, 112, 40); pause.Click += delegate { PausePlan(); };
                footer.Controls.AddRange(new Control[] { safe, truth, draft, enable, pause });

                body.Dock = DockStyle.Fill;
                body.Controls.Add(content);
                // AutoScrollPosition can be changed in code without a native
                // scrollbar message. The content location is the final scroll
                // geometry, so use it as the common repaint trigger as well.
                content.LocationChanged += delegate { body.NotifyScrolledContentMoved(); };
                body.Resize += delegate { LayoutBody(); };
                content.Controls.AddRange(new Control[] { timeSection, accountSection, messageSection, previewSection });
                Controls.Add(body); Controls.Add(header); Controls.Add(footer);

                startDate.ValueChanged += delegate { if(!loading)startDateDirty=true;RefreshSchedulePreview(); };
                cutoffDate.ValueChanged += delegate { if(!loading)cutoffDateDirty=true;RefreshSchedulePreview(); };
                start.ValueChanged += delegate { RefreshSchedulePreview(); };
                cutoff.ValueChanged += delegate { RefreshSchedulePreview(); };
                intervalHours.ValueChanged += delegate { NormalizeInterval(); };
                intervalMinutes.ValueChanged += delegate { NormalizeInterval(); };
                repeat.CheckedChanged += delegate { UpdateDateMode();RefreshSchedulePreview(); };
                common.TextChanged += delegate { UpdateCounter(); RefreshSchedulePreview(); };
                individual.TextChanged += delegate { if (!loading && individualIndex >= 0 && individualIndex < editors.Count) editors[individualIndex].MessageText = individual.Text; UpdateCounter(); RefreshSchedulePreview(); };
                accountChoice.SelectedIndexChanged += delegate { SelectIndividual(accountChoice.SelectedIndex); };
                uniformButton.Click += delegate { SetMessageMode(true); };
                individualButton.Click += delegate { SetMessageMode(false); };
                Resize += delegate { LayoutChrome(title, subtitle, zone, state, close, safe, truth, draft, enable, pause); LayoutBody(); };
                FormClosed += delegate { if (window == this) window = null; };
                ApplyColors();((PlanGlassButton)enable).Emphasized=true;
                var resultPulse=new System.Windows.Forms.Timer {Interval=3000};
                DateTime observedWrite=DateTime.MinValue;
                resultPulse.Tick+=delegate{
                    if(!Visible || loading)return;
                    DateTime stamp=PlanStorage.LastWrite;
                    if(stamp==observedWrite)return;
                    observedWrite=stamp;RefreshSchedulePreview();
                };
                VisibleChanged+=delegate{resultPulse.Enabled=Visible;};
                Shown+=delegate{body.Focus();observedWrite=PlanStorage.LastWrite;resultPulse.Start();};
                FormClosed+=delegate{resultPulse.Stop();resultPulse.Dispose();};
                LayoutChrome(title, subtitle, zone, state, close, safe, truth, draft, enable, pause);
            }

            private void BuildTimeSection()
            {
                Label heading = KaiLabel("1. 时间安排", 20, true); heading.SetBounds(16, 8, 220, 30);
                Label fromDate = KaiLabel("开始日期", 15, true);
                Label toDate = KaiLabel("停止日期", 15, true);
                Label from = KaiLabel("开始时间", 15, true);
                Label to = KaiLabel("停止时间", 15, true);
                Label gap = KaiLabel("账号间隔", 15, true);
                Label hour = KaiLabel("小时", 14, false);
                Label minute = KaiLabel("分钟", 14, false);
                Label once = KaiLabel("每个账号每轮发送 1 条", 13, false); once.TextAlign = ContentAlignment.MiddleRight;
                timeSection.Controls.AddRange(new Control[] { heading, fromDate, startDate, toDate, cutoffDate, from, start, to, cutoff, gap, intervalHours, hour, intervalMinutes, minute, repeat, once });
            }

            private void BuildAccountSection()
            {
                Label heading = KaiLabel("2. 参与账号", 20, true); heading.SetBounds(16, 8, 220, 30);
                accountGrid.SetBounds(18, 44, 610, 58);
                accountSection.Controls.Add(heading); accountSection.Controls.Add(accountGrid);
            }

            private void BuildMessageSection()
            {
                Label heading=KaiLabel("3. 发送内容",20,true);heading.SetBounds(16,6,220,30);
                accountChoice.Font=Times(13,true);accountChoice.AutoSize=false;
                common.MaxLength=500;individual.MaxLength=500;
                messageFrame.Controls.AddRange(new Control[]{common,individual,counter});
                messageSection.Controls.AddRange(new Control[]{heading,uniformButton,individualButton,accountChoice,messageFrame,messageHint});
            }

            private void BuildPreviewSection()
            {
                Label heading = KaiLabel("4. 本轮发送预览", 20, true); heading.SetBounds(16, 8, 260, 30);
                previewPrevious.SetBounds(438, 8, 42, 30); previewPrevious.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                previewPage.SetBounds(484, 9, 58, 28); previewPage.TextAlign = ContentAlignment.MiddleCenter; previewPage.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                previewNext.SetBounds(546, 8, 42, 30); previewNext.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                previewPrevious.Click += delegate { if (previewPageIndex > 0) { previewPageIndex--; RefreshSchedulePreview(); } };
                previewNext.Click += delegate { previewPageIndex++; RefreshSchedulePreview(); };
                previewRows.SetBounds(18, 42, 590, 126);
                previewEmpty.TextAlign = ContentAlignment.MiddleCenter; previewEmpty.Dock = DockStyle.Fill; previewRows.Controls.Add(previewEmpty);
                for (int row = 0; row < 3; row++)
                {
                    PlanGlassPanel line = new PlanGlassPanel { Alpha=55,Border=false }; line.SetBounds(0, row * 42, previewRows.Width, 38); line.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
                    Control badge = new PlanBadge {Text=((char)('A'+row)).ToString(),Name="badge",Variant=row};badge.SetBounds(8,4,32,32);
                    Label email = TimesLabel("", 13, true); email.SetBounds(46, 5, 250, 27); email.AutoEllipsis = true;
                    Label when = TimesLabel("", 15, true); when.TextAlign = ContentAlignment.MiddleRight; when.SetBounds(390, 5, 80, 27); when.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    Label queued = KaiLabel("待启用", 12, true); queued.TextAlign = ContentAlignment.MiddleRight; queued.SetBounds(476, 5, 90, 27); queued.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    EventHandler showOutcome=delegate { if(queued.Tag!=null)MessageBox.Show(this,Convert.ToString(queued.Tag,CultureInfo.InvariantCulture),"发送结果",MessageBoxButtons.OK,MessageBoxIcon.Information); };
                    queued.Click+=showOutcome;line.Click+=showOutcome;email.Click+=showOutcome;
                    queued.Cursor=Cursors.Hand;
                    line.Controls.AddRange(new Control[] { badge, email, when, queued }); line.Visible = false;
                    previewRows.Controls.Add(line); previewCards.Add(line); previewEmails.Add(email); previewTimes.Add(when); previewStates.Add(queued);
                }
                previewSection.Controls.AddRange(new Control[] { heading, previewPrevious, previewPage, previewNext, previewRows });
            }

            private void LayoutChrome(Label title,Label subtitle,Label zone,Label status,Control close,Label safe,Label truth,Control draft,Control enableButton,Control pauseButton)
            {
                title.Parent.Height=(int)(90*PlanScale);safe.Parent.Height=(int)(100*PlanScale);
                foreach(Control chrome in new Control[]{title,subtitle,status,close,draft,enableButton,pauseButton}){if(!basePlanFonts.ContainsKey(chrome))basePlanFonts[chrome]=chrome.Font;Font original=basePlanFonts[chrome];if(Math.Abs(chrome.Font.Size-original.Size*PlanScale)>.01f)chrome.Font=new Font(original.FontFamily,original.Size*PlanScale,original.Style,original.Unit);}
                int width=(int)((ClientSize.Width-Padding.Horizontal)/PlanScale);bool narrow=width<600;
                PlanBounds(title,14,8,narrow?190:260,42);
                PlanBounds(subtitle,16,53,width-32,26);
                zone.Text=narrow?"UTC+8":"台湾时间 · Asia/Taipei";zone.Font=Times((narrow?12:13)*PlanScale,true);
                PlanBounds(zone,width-(narrow?218:348),12,narrow?72:198,32);
                PlanBounds(status,width-140,12,86,34);status.BorderStyle=BorderStyle.None;
                PlanBounds(close,width-48,10,40,38);
                int inner=width-24;
                safe.Text="不切换前台账号 · 不关闭工作窗口";
                truth.Text="发送成功不代表额度已重置";
                safe.TextAlign=truth.TextAlign=ContentAlignment.MiddleCenter;
                safe.Font=new Font(AppTypography.ChineseFamily,(narrow?13:15)*PlanScale,FontStyle.Bold,GraphicsUnit.Pixel);truth.Font=new Font(AppTypography.ChineseFamily,(narrow?13:15)*PlanScale,FontStyle.Bold,GraphicsUnit.Pixel);
                safe.AutoEllipsis=true;truth.AutoEllipsis=true;
                PlanBounds(safe,12,narrow?4:6,narrow?inner:(inner-10)/2,narrow?22:30);
                PlanBounds(truth,narrow?12:22+(inner-10)/2,narrow?28:6,narrow?inner:(inner-10)/2,narrow?22:30);
                int y=narrow?53:44;
                PlanBounds(pauseButton,12,y,96,40);
                PlanBounds(draft,width-(narrow?300:420),y,narrow?138:195,46);
                PlanBounds(enableButton,width-(narrow?154:215),y,narrow?142:203,46);
            }

            private void LayoutBody()
            {
                if(body.ClientSize.Width<50||scalingPlan)return;
                scalingPlan=true;try{RestorePlanGeometry();
                int width=Math.Max(430,(int)(body.ClientSize.Width/PlanScale)-18);
                bool narrow=width<580;
                // Two columns keep the account identifier readable at the
                // normal 720px window width and make four accounts a stable 2x2.
                int columns=2;
                int rows=Math.Max(1,(editors.Count+columns-1)/columns);
                int y=5;
                content.SetBounds(7,0,width,744);
                timeSection.SetBounds(0,y,width,204);y+=204;
                Control[] t=timeSection.Controls.Cast<Control>().ToArray();
                if(t.Length>=16)
                {
                    int labelX=narrow?228:width/2+10,inputX=labelX+(narrow?76:84);
                    t[1].Text=narrow?"开始":"开始日期";t[3].Text=narrow?"停止":"停止日期";
                    t[1].SetBounds(18,39,narrow?48:80,38);
                    startDate.SetBounds(narrow?70:102,39,narrow?146:174,38);
                    int dateInputX=narrow?labelX+50:inputX;
                    t[3].SetBounds(labelX,39,narrow?48:80,38);cutoffDate.SetBounds(dateInputX,39,width-dateInputX-18,38);
                    t[5].SetBounds(18,83,80,38);start.SetBounds(102,83,narrow?126:174,38);
                    t[7].SetBounds(labelX,83,narrow?76:80,38);cutoff.SetBounds(inputX,83,width-inputX-18,38);
                    t[9].SetBounds(18,127,80,38);
                    int numberWidth=narrow?66:108;
                    intervalHours.SetBounds(102,127,numberWidth,38);
                    t[11].SetBounds(108+numberWidth,127,44,38);
                    int minuteX=narrow?226:width/2+50;
                    intervalMinutes.SetBounds(minuteX,127,numberWidth,38);
                    t[13].SetBounds(minuteX+numberWidth+6,127,44,38);
                    repeat.SetBounds(18,169,160,30);
                    t[15].SetBounds(width-250,169,232,30);
                }
                int accountHeight=40+rows*44+4;
                accountSection.SetBounds(0,y,width,accountHeight);y+=accountHeight;
                accountGrid.SetBounds(18,39,width-36,rows*44+4);
                foreach(AccountEditor editor in editors)editor.Size=new Size(Math.Max(165,(accountGrid.Width-columns*6)/columns),42);
                int messageHeight=uniformMode?156:190;
                messageSection.SetBounds(0,y,width,messageHeight);y+=messageHeight;
                int tabWidth=narrow?104:150;
                uniformButton.SetBounds(width-tabWidth*2-18,5,tabWidth,33);
                individualButton.SetBounds(width-tabWidth-18,5,tabWidth,33);
                accountChoice.SetBounds(18,43,width-36,28);
                int frameY=uniformMode?44:76;
                messageFrame.SetBounds(18,frameY,width-36,86);
                common.SetBounds(12,9,messageFrame.Width-24,50);
                individual.Bounds=common.Bounds;
                counter.SetBounds(messageFrame.Width-94,messageFrame.Height-26,80,22);
                messageHint.SetBounds(20,messageHeight-27,width-40,23);
                previewSection.SetBounds(0,y,width,172);y+=172;
                previewRows.SetBounds(18,42,width-36,126);
                previewPrevious.SetBounds(width-158,5,36,28);
                previewPage.SetBounds(width-120,5,56,28);
                previewNext.SetBounds(width-62,5,36,28);
                for(int i=0;i<previewCards.Count;i++)
                {
                    PlanGlassPanel card=previewCards[i];card.SetBounds(0,i*42,previewRows.Width,40);
                    previewEmails[i].SetBounds(48,5,Math.Max(82,card.Width-326),30);
                    previewTimes[i].SetBounds(card.Width-268,5,174,30);
                    previewStates[i].SetBounds(card.Width-88,5,80,30);
                }
                content.Height=y+5;ApplyPlanGeometry();body.AutoScrollMinSize=new Size(0,(int)((y+10)*PlanScale));
                }finally{scalingPlan=false;}
            }

            internal void Reload()
            {
                loading = true;
                settings = storage.Load();
                if (settings == null) settings = PlanSettings.Default();
                DateTime today=Planner.Taiwan(DateTimeOffset.UtcNow).Date;
                DateTime parsedStart;
                bool startValid=DateTime.TryParseExact(settings.PlanDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsedStart);
                startDateDirty=string.IsNullOrWhiteSpace(settings.PlanDate);
                startDate.Value=startValid?parsedStart:today;
                DateTime parsedCutoff;
                bool cutoffValid=DateTime.TryParseExact(settings.CutoffDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsedCutoff);
                cutoffDateDirty=string.IsNullOrWhiteSpace(settings.CutoffDate);
                cutoffDate.Value=cutoffValid?parsedCutoff:(startValid?parsedStart:today);
                start.Value = TodayAt(settings.StartMinutes);
                cutoff.Value = TodayAt(settings.CutoffMinutes);
                int minutes = Math.Max(1, Math.Min(720, settings.IntervalMinutes));
                intervalHours.Value = Math.Min(intervalHours.Maximum, minutes / 60);
                intervalMinutes.Value = Math.Min(intervalMinutes.Maximum, minutes % 60);
                repeat.Checked = settings.DailyRepeat;
                UpdateDateMode();
                uniformMode = settings.UniformMessage;
                SetEditorText(common, settings.CommonMessage ?? "");
                accountGrid.SuspendLayout();
                foreach (AccountEditor old in editors.ToArray()) { accountGrid.Controls.Remove(old); old.Dispose(); }
                editors.Clear(); accountChoice.Items.Clear();
                int index = 0;
                foreach (AccountMeta meta in store.Settings.Accounts)
                {
                    PlanAccount saved = settings.Accounts.FirstOrDefault(delegate(PlanAccount a) { return a.Id == meta.Id; });
                    AccountEditor editor = new AccountEditor(meta, saved, index++, Authorize, store.IsPreview, RefreshSchedulePreview);
                    editors.Add(editor); accountGrid.Controls.Add(editor); accountChoice.Items.Add(meta.Email);
                }
                accountGrid.ResumeLayout();
                individualIndex = editors.Count == 0 ? -1 : 0;
                if (individualIndex >= 0) { accountChoice.SelectedIndex = individualIndex; SetEditorText(individual, editors[individualIndex].MessageText); }
                loading = false;
                SetMessageMode(uniformMode);
                LayoutBody();
                UpdateState();
                RefreshSchedulePreview();
            }

            private void CaptureSettings()
            {
                CommitIndividual();
                if(startDateDirty || string.IsNullOrWhiteSpace(settings.PlanDate))settings.PlanDate=startDate.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                if(cutoffDateDirty || string.IsNullOrWhiteSpace(settings.CutoffDate))settings.CutoffDate=cutoffDate.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                settings.StartMinutes = start.Value.Hour * 60 + start.Value.Minute;
                settings.CutoffMinutes = cutoff.Value.Hour * 60 + cutoff.Value.Minute;
                settings.IntervalMinutes = Math.Max(1, (int)intervalHours.Value * 60 + (int)intervalMinutes.Value);
                settings.DailyRepeat = repeat.Checked;
                settings.UniformMessage = uniformMode;
                settings.CommonMessage = common.Text;
                settings.Accounts = editors.Select(delegate(AccountEditor x) { return x.Value(); }).ToList();
            }

            private void SaveDraft()
            {
                CaptureSettings();
                settings.Enabled = false;
                settings = storage.SaveConfiguration(settings);
                if (planEngine != null) planEngine.Reload();
                UpdateState();
            }

            private async void EnablePlan()
            {
                CaptureSettings();
                if ((settings.UniformMessage && (settings.CommonMessage ?? "").Length > 500) || (!settings.UniformMessage && settings.Accounts.Any(delegate(PlanAccount account) { return account.Selected && (account.Message ?? "").Length > 500; })))
                {
                    MessageBox.Show(this, "发送内容最多 500 字。旧草稿已原样保留，请缩短后再启用。", "内容过长", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                string error = Planner.Validate(settings,DateTimeOffset.UtcNow);
                if (error.Length > 0) { MessageBox.Show(this, error, "无法启用", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                List<AccountMeta> selected = store.Settings.Accounts.Where(delegate(AccountMeta a) { return settings.Accounts.Any(delegate(PlanAccount p) { return p.Selected && p.Id == a.Id; }); }).ToList();
                if (store.IsPreview)
                {
                    MessageBox.Show(this, "预览模式只展示界面，不读取授权，也不会保存或执行计划。", "预览模式", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                string supportError;
                body.Enabled=false;enable.Enabled=false;
                try { supportError = await Task.Run(delegate{return CliProcess.AutomationSupportError();}); }
                finally { if(!IsDisposed){body.Enabled=true;enable.Enabled=true;} }
                if(IsDisposed)return;
                if (supportError.Length > 0)
                {
                    MessageBox.Show(this, supportError, "当前无法安全启用", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                List<string> unready = selected.Where(delegate(AccountMeta a) { return !AuthVault.IsReady(a); }).Select(delegate(AccountMeta a) { return a.Email; }).ToList();
                if (unready.Count > 0)
                {
                    MessageBox.Show(this, "以下账号尚未授权或独立授权已失效：\r\n\r\n" + string.Join("\r\n", unready.ToArray()) + "\r\n\r\n请点击对应账号旁的“授权”或“重登”，登录该账号；不会改动前台账号，也不会补发历史消息。", "需要授权", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                StringBuilder preview = new StringBuilder("请核对即将启用的计划（台湾时间）：\r\n\r\n");
                foreach (PlanOccurrence item in Planner.Build(settings, Planner.Taiwan(DateTimeOffset.UtcNow)))
                {
                    AccountMeta meta = selected.First(delegate(AccountMeta a) { return a.Id == item.AccountId; });
                    preview.Append(item.When.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append("  ").Append(meta.Email).Append("\r\n发送：").Append(item.Message).Append("\r\n\r\n");
                }
                preview.Append(settings.DailyRepeat ? "此计划将每天重复；可随时暂停。" : "此计划只在上述日期时间执行一次；可随时暂停。");
                if (MessageBox.Show(this, preview.ToString(), "确认启用", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                error=Planner.Validate(settings,DateTimeOffset.UtcNow);
                if(error.Length>0){MessageBox.Show(this,error,"时间已变化，无法启用",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
                settings.Enabled = true;
                settings = storage.SaveConfiguration(settings);
                if (planEngine != null) planEngine.Reload();
                UpdateState();
            }

            private void PausePlan()
            {
                CaptureSettings(); settings.Enabled = false; settings = storage.SaveConfiguration(settings);
                if (planEngine != null) planEngine.Reload();
                UpdateState();
            }

            private void UpdateState()
            {
                settings = storage.Load() ?? settings;
                state.Text = settings.Enabled ? "已启用" : "未启用";
                state.ForeColor = settings.Enabled ? Palette.Accents[2] : Palette.Muted;
                enable.Text = settings.Enabled ? "重新核对启用" : "▶  启用计划";
                pause.Visible = settings.Enabled;
                foreach (AccountEditor editor in editors) editor.RefreshAuth();
            }

            private void SetMessageMode(bool useUniform)
            {
                CommitIndividual();
                uniformMode = useUniform;
                common.Visible = useUniform;
                accountChoice.Visible = !useUniform;
                individual.Visible = !useUniform;
                ((PlanGlassButton)uniformButton).Active=useUniform;
                ((PlanGlassButton)individualButton).Active=!useUniform;
                LayoutBody();
                UpdateCounter();
                RefreshSchedulePreview();
            }

            private void SelectIndividual(int index)
            {
                if (loading) return;
                CommitIndividual();
                individualIndex = index;
                loading = true;
                SetEditorText(individual, index >= 0 && index < editors.Count ? editors[index].MessageText : "");
                loading = false;
                UpdateCounter();
            }

            private void CommitIndividual()
            {
                if (!loading && individualIndex >= 0 && individualIndex < editors.Count) editors[individualIndex].MessageText = individual.Text;
            }

            private void UpdateCounter()
            {
                int length = uniformMode ? common.TextLength : individual.TextLength;
                counter.Text = length.ToString(CultureInfo.InvariantCulture) + "/500";
                counter.ForeColor = length > 500 ? Palette.Accents[3] : Palette.Ink;
            }

            private void NormalizeInterval()
            {
                if (normalizingInterval) return;
                normalizingInterval = true;
                try
                {
                    int total = (int)intervalHours.Value * 60 + (int)intervalMinutes.Value;
                    total = Math.Max(1, Math.Min(720, total));
                    intervalHours.Value = total / 60;
                    intervalMinutes.Value = total % 60;
                }
                finally { normalizingInterval = false; }
                RefreshSchedulePreview();
            }

            private void UpdateDateMode()
            {
                bool enabled=!repeat.Checked;
                startDate.Enabled=enabled;cutoffDate.Enabled=enabled;
            }

            private static void SetEditorText(TextBoxBase editor, string value)
            {
                string text = value ?? "";
                editor.MaxLength = Math.Max(500, text.Length);
                editor.Text = text;
                editor.MaxLength = 500;
            }

            private void RefreshSchedulePreview()
            {
                if (loading || settings == null) return;
                CaptureSettings();
                DateTimeOffset now = Planner.Taiwan(DateTimeOffset.UtcNow);
                if (string.IsNullOrWhiteSpace(settings.PlanDate)) settings.PlanDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                List<PlanOccurrence> schedule = Planner.Build(settings, now);
                PlanSettings recorded=storage.Load();
                foreach (AccountEditor editor in editors)
                {
                    PlanOccurrence occurrence = schedule.FirstOrDefault(delegate(PlanOccurrence item) { return item.AccountId == editor.AccountId; });
                    PlanEvent recent = recorded.Events == null ? null : recorded.Events.LastOrDefault(delegate(PlanEvent item) { return item.AccountId == editor.AccountId; });
                    editor.RefreshSchedule(occurrence, recent);
                    editor.RefreshAuth();
                }
                int pages = Math.Max(1, (schedule.Count + 2) / 3);
                previewPageIndex = Math.Max(0, Math.Min(previewPageIndex, pages - 1));
                previewPage.Text = schedule.Count == 0 ? "0 / 0" : (previewPageIndex + 1).ToString(CultureInfo.InvariantCulture) + " / " + pages.ToString(CultureInfo.InvariantCulture);
                previewPrevious.Enabled = previewPageIndex > 0;
                previewNext.Enabled = previewPageIndex + 1 < pages;
                previewRows.SuspendLayout();
                int row = 0;
                foreach (PlanOccurrence occurrence in schedule.Skip(previewPageIndex * 3).Take(3))
                {
                    AccountMeta meta = store.Settings.Accounts.FirstOrDefault(delegate(AccountMeta item) { return item.Id == occurrence.AccountId; });
                    PlanGlassPanel line = previewCards[row];
                    Control badge = line.Controls.Cast<Control>().First(delegate(Control label) { return label.Name == "badge"; });
                    int globalIndex = previewPageIndex * 3 + row;
                    badge.Text = ((char)('A' + globalIndex % 26)).ToString(); ((PlanBadge)badge).Variant=globalIndex%3;
                    previewEmails[row].Text = meta == null ? occurrence.AccountId : meta.Email;
                    previewEmails[row].Tag = previewEmails[row].Text;
                    previewTimes[row].Text = occurrence.When.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    PlanEvent outcome=recorded.Events==null?null:recorded.Events.LastOrDefault(delegate(PlanEvent item){return EventMatchesOccurrence(item,occurrence);});
                    bool expired=outcome==null && !settings.DailyRepeat && now>occurrence.When.AddMinutes(2);
                    previewStates[row].Text = expired?"已过期":outcome==null?(settings.Enabled ? "待发送" : "待启用"):
                        outcome.Status=="success"?"成功":IsAuthorizationFailure(outcome.Detail)?"需重登":outcome.Status=="sending"?"发送中":outcome.Status=="unknown"?"结果未知":outcome.Status=="skipped"?"已跳过":"失败";
                    previewStates[row].Tag=outcome==null?(expired?previewEmails[row].Text+"\r\n已过计划时间，不会自动补发。":"尚无本轮执行结果；同一天可另设不同时间的新一轮。"):previewEmails[row].Text+"\r\n"+previewStates[row].Text+"\r\n计划时间："+outcome.ScheduledAt+"\r\n完成时间："+outcome.FinishedAt+"\r\n"+ExplainExecutionError(outcome.Detail);
                    if(outcome!=null)previewEmails[row].Tag=previewEmails[row].Text+" · "+ExplainExecutionError(outcome.Detail);
                    line.Visible = true; row++;
                }
                for (int index = row; index < previewCards.Count; index++) previewCards[index].Visible = false;
                previewEmpty.Visible = row == 0;
                previewRows.ResumeLayout();
            }

            internal int DiagnosticEditorCount { get { return editors.Count; } }
            internal void CompositeDiagnosticNativeInput(Bitmap destination)
            {
                PairedTextBox input=uniformMode?common:individual;
                if(!input.Visible || input.Width<1 || input.Height<1)return;
                using(Bitmap native=input.CaptureNativeBitmap())
                using(Graphics graphics=Graphics.FromImage(destination))
                {
                    Point clipOrigin=PointToClient(body.PointToScreen(Point.Empty));
                    graphics.SetClip(new Rectangle(clipOrigin,body.ClientSize));
                    graphics.DrawImageUnscaled(native,PointToClient(input.PointToScreen(Point.Empty)));
                }
            }
            internal string DiagnosticFirstPreviewState {get{return previewStates[0].Text;}}
            internal string DiagnosticFirstPreviewDetail {get{return Convert.ToString(previewStates[0].Tag,CultureInfo.InvariantCulture);}}
            internal string DiagnosticFirstPreviewTime {get{return previewTimes[0].Text;}}
            internal int DiagnosticVisiblePreviewRows { get { return previewCards.Count(delegate(PlanGlassPanel card) { return card.Visible; }); } }
            internal int DiagnosticIndividualLength { get { return individual.TextLength; } }
            internal string DiagnosticIndividualText {get{return individual.Text;}}
            internal bool DiagnosticUniformMode {get{return uniformMode;}}
            internal int DiagnosticScrollPaints {get{return body.ScrollRepaints;}}
            internal int DiagnosticScrollOffset {get{return -body.AutoScrollPosition.Y;}}
            internal int DiagnosticLastRepaintScrollOffset {get{return body.LastRepaintScrollOffset;}}
            internal void DiagnosticNativeScroll(bool top){body.DiagnosticNativeScroll(top);}
            internal void DiagnosticNativeLineDown(){body.DiagnosticNativeLineDown();}
            internal void DiagnosticNativeThumb(int position){body.DiagnosticNativeThumb(position);}
            internal void DiagnosticMouseWheelDown(){body.DiagnosticMouseWheelDown();}
            internal void DiagnosticProgrammaticScroll(int position){body.DiagnosticProgrammaticScroll(position);}
            internal int DiagnosticAncestorRimReplayPixels()
            {
                PlanGlassPanel probe=new PlanGlassPanel {Surface=false,Border=false};
                int oldOffset=DiagnosticScrollOffset;
                try
                {
                    content.Controls.Add(probe);
                    probe.SetBounds(28,oldOffset+body.ClientSize.Height-31,260,42);
                    probe.BringToFront();
                    using(Bitmap withRim=new Bitmap(probe.Width,probe.Height))
                    using(Bitmap withoutRim=new Bitmap(probe.Width,probe.Height))
                    {
                        bool oldBorder=body.Border;
                        try
                        {
                            body.Border=true;probe.DrawToBitmap(withRim,probe.ClientRectangle);
                            body.Border=false;probe.DrawToBitmap(withoutRim,probe.ClientRectangle);
                        }
                        finally{body.Border=oldBorder;}
                        int changed=0;
                        for(int y=0;y<withRim.Height;y++)for(int x=0;x<withRim.Width;x++)
                            if(withRim.GetPixel(x,y).ToArgb()!=withoutRim.GetPixel(x,y).ToArgb())changed++;
                        return changed;
                    }
                }
                finally{content.Controls.Remove(probe);probe.Dispose();}
            }
            internal void DiagnosticEditIndividual(string text){individual.Text=text;}
            internal void DiagnosticToggleRepeat(){repeat.Checked=!repeat.Checked;}
            internal void DiagnosticSetRepeat(bool value){repeat.Checked=value;}
            internal void DiagnosticPreviewPage(int page){previewPageIndex=page;RefreshSchedulePreview();}
            internal bool DiagnosticInputsReadable {get{return startDate.IsReadable && cutoffDate.IsReadable && start.IsReadable && cutoff.IsReadable && intervalHours.IsReadable && intervalMinutes.IsReadable;}}
            internal bool DiagnosticStoredEnabled { get { PlanSettings value = storage.Load(); return value != null && value.Enabled; } }
            internal int DiagnosticStoredInterval { get { PlanSettings value = storage.Load(); return value == null ? -1 : value.IntervalMinutes; } }
            internal string DiagnosticStoredStartDate {get{PlanSettings value=storage.Load();return value==null?"":value.PlanDate;}}
            internal string DiagnosticStoredCutoffDate {get{PlanSettings value=storage.Load();return value==null?"":value.CutoffDate;}}
            internal void DiagnosticSetAllSelected(bool value) { foreach (AccountEditor editor in editors) editor.IsSelected = value; RefreshSchedulePreview(); }
            internal void DiagnosticSetMode(bool useUniform, int accountIndex) { SetMessageMode(useUniform); if (!useUniform && accountIndex >= 0 && accountIndex < editors.Count) accountChoice.SelectedIndex = accountIndex; }
            internal void DiagnosticSetInterval(int hours, int minutes) { intervalHours.Value = Math.Max(intervalHours.Minimum, Math.Min(intervalHours.Maximum, hours)); intervalMinutes.Value = Math.Max(intervalMinutes.Minimum, Math.Min(intervalMinutes.Maximum, minutes)); NormalizeInterval(); }
            internal void DiagnosticSetTimes(int startMinutes, int cutoffMinutes) { start.Value = TodayAt(startMinutes); cutoff.Value = TodayAt(cutoffMinutes); RefreshSchedulePreview(); }
            internal void DiagnosticSetDates(DateTime from,DateTime to){startDate.Value=from;cutoffDate.Value=to;RefreshSchedulePreview();}
            internal void DiagnosticSaveCalendar(string path){startDate.SaveCalendarDiagnosticScreenshot(path);}
            internal void DiagnosticSaveDraft() { SaveDraft(); }
            internal bool DiagnosticBoundsInside() { return BoundsInside(this); }
            internal string DiagnosticBoundsProblem() { return FindOutside(this, "window"); }

            private static bool BoundsInside(Control parent)
            {
                foreach (Control child in parent.Controls)
                {
                    if (!child.Visible) continue;
                    ScrollableControl scroll = parent as ScrollableControl;
                    if (!(scroll != null && scroll.AutoScroll) && (child.Left < 0 || child.Top < 0 || child.Right > parent.ClientSize.Width + 1 || child.Bottom > parent.ClientSize.Height + 1)) return false;
                    if (!BoundsInside(child)) return false;
                }
                return true;
            }

            private static string FindOutside(Control parent, string path)
            {
                foreach (Control child in parent.Controls)
                {
                    if (!child.Visible) continue;
                    string childPath = path + "/" + child.GetType().Name + (string.IsNullOrEmpty(child.Text) ? "" : "[" + child.Text.Replace("\r", " ").Replace("\n", " ") + "]");
                    ScrollableControl scroll = parent as ScrollableControl;
                    if (!(scroll != null && scroll.AutoScroll) && (child.Left < 0 || child.Top < 0 || child.Right > parent.ClientSize.Width + 1 || child.Bottom > parent.ClientSize.Height + 1)) return childPath + " bounds=" + child.Bounds + " parent=" + parent.ClientSize;
                    string nested = FindOutside(child, childPath); if (nested.Length > 0) return nested;
                }
                return "";
            }

            private void Authorize(AccountMeta meta)
            {
                if (store.IsPreview)
                {
                    MessageBox.Show(this, "预览模式不会读取或创建真实账号授权。", "预览模式", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if(string.IsNullOrEmpty(CliSetup.EnsureAvailable(this)))return;
                using (AuthorizationDialog dialog = new AuthorizationDialog(meta)) dialog.ShowDialog(this);
                UpdateState();
            }

            private static DateTime TodayAt(int minutes) { DateTime today = DateTime.Today; return today.AddMinutes(Math.Max(0, Math.Min(1439, minutes))); }
            private static PlanDateInput DatePicker() {return new PlanDateInput();}
            private static PlanTimeInput TimePicker() { return new PlanTimeInput(); }
            private static PlanNumberInput Number(int min, int max, int value) { return new PlanNumberInput { Minimum = min, Maximum = max, Value = value, TextAlign = HorizontalAlignment.Center }; }
            private static CheckBox Check(string text) { return new PlanRepeatSwitch { ForeColor = Palette.Ink, Font = Kai(13, true) }; }
            internal static PairedTextBox Editor() {var editor=new PairedTextBox(18,true) { Multiline = true, WordWrap = true, BorderStyle = BorderStyle.None, BackColor = ThemeVisual.Current.PanelBottom, ForeColor = Color.White, ScrollBars = RichTextBoxScrollBars.None };editor.SizeChanged+=delegate{if(editor.Width<3||editor.Height<3)return;using(var path=GraphicsExtensions.CreateRoundedPath(editor.ClientRectangle,10)){var old=editor.Region;editor.Region=new Region(path);if(old!=null)old.Dispose();}};return editor; }
            private static Button ActionButton(string text, int width) {return new PlanGlassButton {Text=text,Size=new Size(width,38),Font=Kai(15,true)};}
            private static Font Kai(float size, bool bold) { return new Font("楷体", Math.Max(16,size+2), bold ? FontStyle.Bold : FontStyle.Bold, GraphicsUnit.Pixel); }
            private static Font Times(float size, bool bold) { return new Font("Times New Roman", Math.Max(16,size+2), bold ? FontStyle.Bold : FontStyle.Bold, GraphicsUnit.Pixel); }
            private static Label KaiLabel(string text, float size, bool bold) { return new PlanLineLabel { Text = text, AutoSize = false, ForeColor = Palette.Ink, Font = Kai(size, bold) }; }
            private static Label TimesLabel(string text, float size, bool bold) { return new PlanLineLabel { Text = text, AutoSize = false, ForeColor = Palette.Ink, Font = Times(size, bold) }; }

            private void ApplyColors()
            {
                ThemeLook look = ThemeVisual.Current;
                switch(ThemeSelection.Current)
                {
                    case "heritage":themeSubtitle.Text="山河有序 · 安排更从容的发送节奏";break;
                    case "spring":themeSubtitle.Text="春日有约 · 安排更从容的发送节奏";break;
                    case "summer":themeSubtitle.Text="夏日来信 · 与海风一起安排发送节奏";break;
                    case "autumn":themeSubtitle.Text="秋日拾光 · 安排更从容的发送节奏";break;
                    case "winter":themeSubtitle.Text="晴雪有期 · 安排更从容的发送节奏";break;
                    default:themeSubtitle.Text="从容有序 · 安排更舒适的发送节奏";break;
                }
                startDate.RefreshTheme();cutoffDate.RefreshTheme();start.RefreshTheme();cutoff.RefreshTheme();intervalHours.RefreshTheme();intervalMinutes.RefreshTheme();
                common.BackColor = ThemeVisual.Blend(look.PanelTop,look.PanelBottom,.5f); common.ForeColor = Color.White;
                individual.BackColor = ThemeVisual.Blend(look.PanelTop,look.PanelBottom,.5f); individual.ForeColor = Color.White;
                accountChoice.BackColor = look.PanelBottom; accountChoice.ForeColor = Color.White;
                foreach (AccountEditor editor in editors) editor.ApplyTheme();
            }

            internal override void RefreshGlass()
            {
                base.RefreshGlass(); ApplyColors();
                SetMessageMode(uniformMode); RefreshSchedulePreview(); Invalidate(true);
            }
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                GlassBackdrop.DrawReferenceBackground(e.Graphics,ClientRectangle,false);
                e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                using(var path=GraphicsExtensions.CreateRoundedPath(new Rectangle(1,1,Width-3,Height-3),28))
                using(var pen=new Pen(Color.FromArgb(218,Color.White),1.45f))
                using(var veil=new SolidBrush(Color.FromArgb(42,ThemeVisual.Current.PanelBottom)))
                {pen.Alignment=PenAlignment.Inset;e.Graphics.FillPath(veil,path);e.Graphics.DrawPath(pen,path);}
            }
        }

        private sealed class PlanLineLabel : Label
        {
            internal bool Pill;
            internal PlanLineLabel()
            {
                SetStyle(ControlStyles.SupportsTransparentBackColor,false);
                SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|
                    ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Opaque,true);
                BackColor=ThemeVisual.Current.PanelBottom;
            }
            protected override void OnPaintBackground(PaintEventArgs e) { }
            protected override void OnPaint(PaintEventArgs e)
            {
                PlanVisualPainting.PaintBackdrop(this,e.Graphics);
                if(Pill)using(var path=GraphicsExtensions.CreateRoundedPath(new Rectangle(1,1,Width-3,Height-3),15))
                using(var brush=new SolidBrush(Color.FromArgb(180,ThemeVisual.Current.ButtonBottom)))
                using(var pen=new Pen(ThemeVisual.Current.Rim,1.1f))
                {e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path);}
                StringAlignment alignment=TextAlign==ContentAlignment.MiddleRight?StringAlignment.Far:
                    TextAlign==ContentAlignment.MiddleCenter?StringAlignment.Center:StringAlignment.Near;
                if(Pill)TextRenderer.DrawText(e.Graphics,Text,Font,Rectangle.Inflate(ClientRectangle,-10,-2),ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                else PlanVisualPainting.DrawText(e.Graphics,Text,Font,ClientRectangle,Parent!=null && (Parent.Name=="planHeader" || Parent.Name=="planFooter")?ThemeVisual.HeaderInk:ForeColor,alignment,AutoEllipsis);
            }
        }

        private sealed class PlanTimelinePanel : Panel
        {
            internal PlanTimelinePanel()
            {
                SetStyle(ControlStyles.SupportsTransparentBackColor,false);
                SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|
                    ControlStyles.UserPaint|ControlStyles.ResizeRedraw|ControlStyles.Opaque,true);
                BackColor=ThemeVisual.Current.PanelBottom;
            }
            protected override void OnPaintBackground(PaintEventArgs e) { }
            protected override void OnPaint(PaintEventArgs e)
            {
                PlanVisualPainting.PaintBackdrop(this,e.Graphics);
                int count=Controls.Cast<Control>().Count(c=>c.Visible);
                if(count<2)return;
                using(var pen=new Pen(Color.FromArgb(165,ThemeVisual.Current.Accent),1.5f))
                    e.Graphics.DrawLine(pen,24,20,24,(count-1)*42+20);
            }
        }

        private sealed class PlanFlowPanel : FlowLayoutPanel
        {
            internal PlanFlowPanel()
            {
                SetStyle(ControlStyles.SupportsTransparentBackColor,false);
                SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|
                    ControlStyles.UserPaint|ControlStyles.ResizeRedraw|ControlStyles.Opaque,true);
                BackColor=ThemeVisual.Current.PanelBottom;
            }
            protected override void OnPaintBackground(PaintEventArgs e) { }
            protected override void OnPaint(PaintEventArgs e){PlanVisualPainting.PaintBackdrop(this,e.Graphics);}
        }

        private class PlanGlassPanel : Panel, IPlanBackdropLayer, IPlanBackdropReplay
        {
            internal int ScrollRepaints;
            internal int LastRepaintScrollOffset;
            private bool scrollRepaintQueued;
            private const int WmVScroll=0x0115;
            private const int SbLineDown=1;
            private const int SbThumbTrack=5;
            private const int SbEndScroll=8;
            private const int SbTop=6;
            private const int SbBottom=7;
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
            internal void DiagnosticNativeScroll(bool top){SendMessage(Handle,WmVScroll,new IntPtr(top?SbTop:SbBottom),IntPtr.Zero);}
            internal void DiagnosticNativeLineDown(){SendMessage(Handle,WmVScroll,new IntPtr(SbLineDown),IntPtr.Zero);}
            internal void DiagnosticNativeThumb(int position){int value=Math.Max(0,Math.Min(0xffff,position));SendMessage(Handle,WmVScroll,new IntPtr((value<<16)|SbThumbTrack),IntPtr.Zero);SendMessage(Handle,WmVScroll,new IntPtr(SbEndScroll),IntPtr.Zero);}
            internal void DiagnosticMouseWheelDown(){OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,Width/2,Height/2,-120));}
            internal void DiagnosticProgrammaticScroll(int position){SetScrollY(position);}
            internal void NotifyScrolledContentMoved(){QueueScrollRepaint();}
            internal bool Surface=true,Border=true,Divider,Flat;
            internal int Alpha=211;
            internal PlanGlassPanel()
            {
                SetStyle(ControlStyles.SupportsTransparentBackColor,false);
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
                BackColor = ThemeVisual.Current.PanelBottom;
            }

            protected override void OnPaintBackground(PaintEventArgs e) { }
            protected override void OnScroll(ScrollEventArgs e)
            {
                base.OnScroll(e);
                QueueScrollRepaint();
            }
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                QueueScrollRepaint();
            }
            protected override void WndProc(ref Message m)
            {
                int message=m.Msg;
                base.WndProc(ref m);
                // WM_VSCROLL can finish moving native child windows after the
                // Scroll event. Queueing here coalesces line/page/thumb paths
                // into a repaint at the final AutoScrollPosition.
                if(message==WmVScroll)QueueScrollRepaint();
                if(message==0x85 || message==0x133 || message==WmVScroll || message==0xA0 || message==0xF)PaintScrollChrome(IntPtr.Zero);
                if(message==0x317)PaintScrollChrome(m.WParam);
            }
            [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr h);
            [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h,IntPtr dc);
            private void PaintScrollChrome(IntPtr supplied){
                if(!AutoScroll || !VerticalScroll.Visible || !IsHandleCreated)return;
                IntPtr dc=supplied==IntPtr.Zero?GetWindowDC(Handle):supplied;if(dc==IntPtr.Zero)return;
                try{using(var g=Graphics.FromHdc(dc)){
                    int w=SystemInformation.VerticalScrollBarWidth;int x=Width-w;
                    using(var bg=new SolidBrush(ThemeVisual.Current.PanelBottom))g.FillRectangle(bg,x,0,w,Height);
                    g.SmoothingMode=SmoothingMode.AntiAlias;
                    int track=Math.Max(1,Height-2*w);int length=Math.Min(track,Math.Max(24,(int)(track*(double)VerticalScroll.LargeChange/(VerticalScroll.Maximum+1))));
                    int y=w+(int)((track-length)*(double)ScrollY/Math.Max(1,MaximumScrollY));
                    using(var path=GraphicsExtensions.CreateRoundedPath(new Rectangle(x+5,y,Math.Max(4,w-10),length),3))using(var fill=new SolidBrush(ThemeVisual.Blend(ThemeVisual.Current.Accent,ThemeVisual.Current.PanelTop,.4f)))g.FillPath(fill,path);
                    using(var pen=new Pen(Color.FromArgb(190,ThemeVisual.Current.Rim),1.4f)){int cx=x+w/2;g.DrawLines(pen,new[]{new Point(cx-3,w/2+1),new Point(cx,w/2-2),new Point(cx+3,w/2+1)});g.DrawLines(pen,new[]{new Point(cx-3,Height-w/2-1),new Point(cx,Height-w/2+2),new Point(cx+3,Height-w/2-1)});}
                }}finally{if(supplied==IntPtr.Zero)ReleaseDC(Handle,dc);}
            }
            private int ScrollY{get{return Math.Max(0,-AutoScrollPosition.Y);}}
            private int MaximumScrollY
            {
                get{return Math.Max(0,VerticalScroll.Maximum-VerticalScroll.LargeChange+1);}
            }
            private void SetScrollY(int position)
            {
                if(!AutoScroll)return;
                AutoScrollPosition=new Point(0,Math.Max(0,Math.Min(MaximumScrollY,position)));
            }
            private void QueueScrollRepaint()
            {
                if(IsDisposed || Disposing)return;
                if(!IsHandleCreated){Invalidate(true);return;}
                if(scrollRepaintQueued)return;
                scrollRepaintQueued=true;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        scrollRepaintQueued=false;
                        if(IsDisposed || Disposing)return;
                        ScrollRepaints++;
                        LastRepaintScrollOffset=ScrollY;
                        Invalidate(true);
                        Update();
                    });
                }
                catch(InvalidOperationException){scrollRepaintQueued=false;}
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                PlanVisualPainting.PaintBackdrop(this,e.Graphics);
                PaintLayer(e.Graphics,new Rectangle(Point.Empty,ClientSize),true);
            }
            void IPlanBackdropLayer.PaintPlanBackdropLayer(Graphics graphics,Rectangle rootBounds){PaintLayer(graphics,rootBounds,true);}
            void IPlanBackdropReplay.PaintPlanBackdropUnderlay(Graphics graphics,Rectangle rootBounds){PaintLayer(graphics,rootBounds,false);}
            void IPlanBackdropReplay.IntersectPlanBackdropViewport(Graphics graphics,Rectangle rootBounds)
            {
                if(!Surface && !Border)return;
                Rectangle box=new Rectangle(rootBounds.X+1,rootBounds.Y+1,rootBounds.Width-3,rootBounds.Height-3);
                if(box.Width<1 || box.Height<1){graphics.SetClip(Rectangle.Empty,CombineMode.Intersect);return;}
                using(GraphicsPath path=GraphicsExtensions.CreateRoundedPath(box,Math.Min(18,Math.Max(6,box.Height/3))))
                    graphics.SetClip(path,CombineMode.Intersect);
            }
            private void PaintLayer(Graphics graphics,Rectangle bounds,bool decorations)
            {
                if (bounds.Width < 4 || bounds.Height < 4) return;
                ThemeLook look = ThemeVisual.Current;
                Rectangle box = new Rectangle(bounds.X+1,bounds.Y+1,bounds.Width-3,bounds.Height-3);
                using (System.Drawing.Drawing2D.GraphicsPath path = GraphicsExtensions.CreateRoundedPath(box, Math.Min(18, Math.Max(6, box.Height / 3))))
                using (var fill = new LinearGradientBrush(box, Color.FromArgb(Alpha, look.PanelTop), Color.FromArgb(Math.Min(255,Alpha+18), look.PanelBottom),90f))
                using (Pen rim = new Pen(Color.FromArgb(225, look.Rim), 1.2f))
                {
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    if(Surface){if(Flat){using(var flat=new SolidBrush(ThemeVisual.Blend(look.PanelTop,look.PanelBottom,.5f)))graphics.FillPath(flat,path);}else graphics.FillPath(fill,path);}
                    if(decorations && Border)graphics.DrawPath(rim, path);
                    if(decorations && Divider)using(var line=new Pen(Color.FromArgb(95,look.Rim),1))graphics.DrawLine(line,bounds.X+16,bounds.Bottom-1,bounds.Right-16,bounds.Bottom-1);
                }
            }
        }

        private sealed class AccountEditor : PlanGlassPanel
        {
            private readonly AccountMeta meta;
            private readonly CheckBox selected;
            private readonly Control badge;
            private readonly Button authorizeButton;
            private readonly ToolTip tip = new ToolTip();
            private readonly bool preview;
            private string messageText;
            internal string AccountId { get { return meta.Id; } }
            internal string MessageText { get { return messageText ?? ""; } set { messageText = value ?? ""; } }
            internal bool IsSelected { get { return selected.Checked; } set { selected.Checked = value; } }

            internal AccountEditor(AccountMeta meta, PlanAccount saved, int index, Action<AccountMeta> authorize, bool preview, Action changed)
            {
                this.meta = meta;
                this.preview = preview;
                messageText = saved == null ? "" : saved.Message ?? "";
                Height = 42; Margin = new Padding(0, 0, 5, 4); Padding = Padding.Empty;
                string display=(meta.Email??"未命名账号").Split('@')[0];
                selected = new PlanAccountCheck { Checked = saved != null && saved.Selected, Text = display, Badge=((char)('A'+index%26)).ToString(),Variant=index };
                selected.SetBounds(5,3,Width-48,36); selected.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
                badge=selected;
                authorizeButton = new PlanGlassButton {Text="授权",Font=new Font("楷体",16,FontStyle.Bold,GraphicsUnit.Pixel),Radius=9};
                authorizeButton.SetBounds(Width-43,7,38,29); authorizeButton.Anchor=AnchorStyles.Top|AnchorStyles.Right;authorizeButton.Click+=delegate{authorize(meta);};
                selected.CheckedChanged += delegate { if (changed != null) changed(); };
                Controls.AddRange(new Control[] { selected, authorizeButton });authorizeButton.BringToFront();Alpha=75;
                tip.SetToolTip(selected, meta.Email); tip.SetToolTip(badge, meta.Email);
                RefreshAuth();
            }
            internal PlanAccount Value() { return new PlanAccount { Id = meta.Id, Selected = selected.Checked, Message = MessageText }; }
            internal void RefreshAuth()
            {
                if (preview) { authorizeButton.Text = "授权"; tip.SetToolTip(authorizeButton, "预览模式不读取真实授权"); return; }
                bool revoked = AuthVault.NeedsReauthorization(meta);
                bool ready = !revoked && AuthVault.IsReady(meta);
                authorizeButton.Text = revoked ? "重登" : ready ? "✓" : "授权";
                authorizeButton.ForeColor = revoked ? Palette.Accents[3] : ready ? Palette.Accents[2] : Palette.Ink;
                tip.SetToolTip(authorizeButton, revoked ? "官方已拒绝这份独立授权；点击重新登录对应账号。历史失败不会自动补发。" : ready ? "本地独立授权已保存；最终有效性以官方执行结果为准，点击可重新授权" : "单独授权，不借用当前桌面账号");
            }
            internal void RefreshSchedule(PlanOccurrence occurrence, PlanEvent recent)
            {
                string schedule = occurrence == null ? "预计：未选中或超出截止时间" : "预计：" + occurrence.When.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) + "（台湾）";
                string outcome = recent == null ? "最近：暂无执行记录" : "最近：" + (recent.Status == "success" ? "成功" : recent.Status == "sending" ? "进行中" : recent.Status == "unknown" ? "结果未知" : recent.Status == "skipped" ? "已跳过" : "失败") + (string.IsNullOrWhiteSpace(recent.Detail) ? "" : " · " + ExplainExecutionError(recent.Detail));
                string detail = meta.Email + "\r\n" + schedule + "\r\n" + outcome+(recent==null?"":"\r\n实际计划时间："+recent.ScheduledAt+"\r\n完成时间："+recent.FinishedAt);
                tip.SetToolTip(this, detail); tip.SetToolTip(selected, detail); tip.SetToolTip(badge, detail);
            }
            internal void ApplyTheme() { selected.ForeColor=Color.White;Invalidate(true); }
            protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
        }

        private sealed class AuthorizationDialog : Form
        {
            private readonly AccountMeta meta;
            private readonly PairedTextBox output = new PairedTextBox(16,false) { Multiline = true, WordWrap = true, ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Palette.Surface, ForeColor = ThemeVisual.Current.PanelBottom };
            private readonly Label status = new Label { Dock = DockStyle.Top, Height = 82, Padding = new Padding(10), Text = "正在启动官方浏览器授权…\r\n请在浏览器核对账号并确认登录。" };
            private readonly Button openPage = new Button { Text = "打开授权页", Dock = DockStyle.Bottom, Height = 36, Enabled = false };
            private string authorizationUrl;
            private Process process;
            internal AuthorizationDialog(AccountMeta meta)
            {
                this.meta = meta; Text = "独立授权 · " + meta.Email; Size = new Size(590, 430); StartPosition = FormStartPosition.CenterParent; Font = Palette.Font(12, false);
                Button cancel = new Button { Text = "取消 / 关闭", Dock = DockStyle.Bottom, Height = 38 };
                cancel.Click += delegate { CancelProcess(); Close(); };
                openPage.Click += delegate { OpenAuthorizationPage(); };
                Controls.Add(output); Controls.Add(status); Controls.Add(openPage); Controls.Add(cancel);
                Shown += async delegate { await RunLogin(); };
                FormClosing += delegate { CancelProcess(); };
            }
            private async Task RunLogin()
            {
                string home = null;
                try
                {
                    home=AuthVault.CreatePlainHome();
                    ProcessStartInfo psi = CliProcess.LoginStartInfo(home);
                    process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) Append(e.Data); };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) Append(e.Data); };
                    if (!process.Start()) throw new InvalidOperationException("无法启动 Codex CLI。");
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    status.Text = "正在打开浏览器，请核对账号并确认登录。\r\n如未弹出，请点击“打开授权页”；完成后程序会自动核对账号。";
                    await Task.Run(delegate { process.WaitForExit(); });
                    if (process.ExitCode != 0) throw new InvalidOperationException("授权未完成或已取消。");
                    status.Text = "网页授权已返回，正在核对账号身份并保存…";
                    AuthVault.CaptureAuthorizedHome(home, meta);
                    status.Text = "✓ 已完成独立授权，账号身份核对一致。";
                }
                catch (Exception ex) { status.Text = "授权未完成：" + SafeError(ex); }
                finally { if(process!=null){process.Dispose();process=null;}if(home!=null)AuthVault.DeletePlainHome(home); }
            }
            private void Append(string text)
            {
                if (IsDisposed || !IsHandleCreated) return;
                string clean = System.Text.RegularExpressions.Regex.Replace(text,"\u001b\\[[0-?]*[ -/]*[@-~]","");
                try { BeginInvoke((MethodInvoker)delegate {
                    if (IsDisposed) return;
                    if (output.TextLength < 20000) output.AppendText(clean + Environment.NewLine);
                    foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(clean,@"https://[^\s<>""']+"))
                    {
                        Uri link;
                        if(Uri.TryCreate(match.Value,UriKind.Absolute,out link) && link.Scheme=="https" &&
                            string.Equals(link.Host,"auth.openai.com",StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(link.UserInfo))
                        {authorizationUrl=link.AbsoluteUri;openPage.Enabled=true;break;}
                    }
                }); }
                catch (InvalidOperationException) { }
            }
            private void OpenAuthorizationPage()
            {
                if(string.IsNullOrEmpty(authorizationUrl))return;
                try { Process.Start(new ProcessStartInfo(authorizationUrl) { UseShellExecute=true }); }
                catch { status.Text="无法打开浏览器，请复制下方官方授权地址，在浏览器打开。"; }
            }
            private void CancelProcess() { CliProcess.Stop(process); }
        }

        private sealed class PlanEngine : IDisposable
        {
            private readonly AccountStore store;
            private readonly IClock clock;
            private readonly IPlanSender sender;
            private readonly IPlanStorage storage;
            private readonly SemaphoreSlim serial = new SemaphoreSlim(1, 1);
            private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
            private readonly object runGate = new object();
            private System.Threading.Timer timer;
            private Task activeTrigger;
            private volatile bool disposed;
            internal PlanEngine(AccountStore store, IClock clock, IPlanSender sender) : this(store, clock, sender, new FilePlanStorage()) { }
            internal PlanEngine(AccountStore store, IClock clock, IPlanSender sender, IPlanStorage storage) { this.store = store; this.clock = clock; this.sender = sender; this.storage = storage; }
            internal void Start()
            {
                RecoverInterruptedEvents();
                timer = new System.Threading.Timer(delegate { Trigger(); }, null, TimeSpan.Zero, TimeSpan.FromSeconds(20));
            }
            internal void Reload() { Trigger(); }
            private void Trigger()
            {
                lock (runGate)
                {
                    if (disposed || (activeTrigger != null && !activeTrigger.IsCompleted)) return;
                    activeTrigger = RunTriggeredAsync();
                }
            }
            private async Task RunTriggeredAsync()
            {
                try { await EvaluateAsync().ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch { }
            }
            internal async Task EvaluateAsync()
            {
                if (disposed || !await serial.WaitAsync(0, lifetime.Token).ConfigureAwait(false)) return;
                try
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    PlanSettings plan = storage.Load();
                    if (plan == null || !plan.Enabled) return;
                    DateTimeOffset now = Planner.Taiwan(clock.Now);
                    DateTimeOffset oneShotCutoff;
                    if(!plan.DailyRepeat && Planner.TryCutoff(plan,out oneShotCutoff) && now>oneShotCutoff.AddMinutes(2))return;
                    List<PlanOccurrence> occurrences = Planner.Build(plan, now);
                    foreach (PlanOccurrence occurrence in occurrences)
                    {
                        lifetime.Token.ThrowIfCancellationRequested();
                        now = Planner.Taiwan(clock.Now);
                        string key = OccurrenceKey(occurrence.AccountId, occurrence.When);
                        if (now < occurrence.When) continue;
                        PlanEvent record = new PlanEvent { Key = key, AccountId = occurrence.AccountId, ScheduledAt = occurrence.When.ToString("o", CultureInfo.InvariantCulture), FinishedAt = now.ToString("o", CultureInfo.InvariantCulture) };
                        if (now > occurrence.When.AddMinutes(2))
                        {
                            record.Status = "skipped"; record.Detail = "程序恢复时已错过计划时间，未补发";
                            TryClaim(record, occurrence, true);
                            continue;
                        }
                        AccountMeta account = store == null ? new AccountMeta { Id = occurrence.AccountId, Email = occurrence.AccountId } : store.Settings.Accounts.FirstOrDefault(delegate(AccountMeta a) { return a.Id == occurrence.AccountId; });
                        if (account == null || (sender.RequiresAuthorization && !AuthVault.IsReady(account)))
                        {
                            record.Status = "failed"; record.Detail = account != null && AuthVault.NeedsReauthorization(account) ? "独立授权已失效，请重新登录；本轮未发送" : "独立授权不可用，请点击授权；本轮未发送";
                            TryClaim(record, occurrence, true);
                            continue;
                        }
                        record.Status = "sending"; record.Detail = "已交给独立后台会话";
                        if (!TryClaim(record, occurrence, true)) continue;
                        SendResult result;
                        bool outcomeUnknown = false;
                        try { result = await sender.SendAsync(account, occurrence.Message, lifetime.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { result = SendResult.Uncertain("程序退出，发送已取消；结果未知，未自动重试"); }
                        catch (Exception) { result = SendResult.Fail("发送过程异常，结果未知，未自动重试"); outcomeUnknown = true; }
                        if (result == null) { result = SendResult.Fail("发送过程未返回结果，结果未知，未自动重试"); outcomeUnknown = true; }
                        record.Status = (outcomeUnknown || result.Unknown) ? "unknown" : result.Success ? "success" : "failed";
                        record.Detail = result.Detail ?? "";
                        record.FinishedAt = Planner.Taiwan(clock.Now).ToString("o", CultureInfo.InvariantCulture);
                        storage.Update(delegate(PlanSettings latest)
                        {
                            PlanEvent saved = latest.Events.FirstOrDefault(delegate(PlanEvent e) { return e.Key == record.Key; });
                            if (saved != null)
                            {
                                saved.Status = record.Status;
                                saved.Detail = record.Detail;
                                saved.FinishedAt = record.FinishedAt;
                            }
                            return latest;
                        });
                    }
                    storage.Update(delegate(PlanSettings latest) { Prune(latest, Planner.Taiwan(clock.Now)); return latest; });
                }
                finally { serial.Release(); }
            }

            private bool TryClaim(PlanEvent record, PlanOccurrence occurrence, bool requireCurrentSchedule)
            {
                bool claimed = false;
                storage.Update(delegate(PlanSettings latest)
                {
                    DateTimeOffset current = Planner.Taiwan(clock.Now);
                    if (latest == null || !latest.Enabled || latest.Events.Any(delegate(PlanEvent e) { return EventMatchesOccurrence(e, occurrence); })) return latest;
                    DateTimeOffset latestCutoff;
                    if(!latest.DailyRepeat && Planner.TryCutoff(latest,out latestCutoff) && current>latestCutoff.AddMinutes(2))return latest;
                    if (requireCurrentSchedule)
                    {
                        bool stillScheduled = Planner.Build(latest, current).Any(delegate(PlanOccurrence item)
                        {
                            return item.AccountId == occurrence.AccountId && item.When == occurrence.When && string.Equals(item.Message, occurrence.Message, StringComparison.Ordinal);
                        });
                        if (!stillScheduled) return latest;
                    }
                    latest.Events.Add(record);
                    claimed = true;
                    return latest;
                });
                return claimed;
            }
            private static void Prune(PlanSettings plan, DateTimeOffset now)
            {
                plan.Events = plan.Events.Where(delegate(PlanEvent e) { DateTimeOffset at; return !DateTimeOffset.TryParse(e.ScheduledAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out at) || now - at < TimeSpan.FromDays(60); }).ToList();
            }
            internal void RecoverInterruptedEvents()
            {
                try
                {
                    PlanSettings snapshot = storage.Load();
                    if (snapshot == null || snapshot.Events == null ||
                        !snapshot.Events.Any(delegate(PlanEvent e) { return e != null && e.Status == "sending"; })) return;
                    storage.Update(delegate(PlanSettings latest)
                    {
                        if (latest == null || latest.Events == null) return latest;
                        string finished = Planner.Taiwan(clock.Now).ToString("o", CultureInfo.InvariantCulture);
                        foreach (PlanEvent item in latest.Events.Where(delegate(PlanEvent e) { return e != null && e.Status == "sending"; }))
                        {
                            item.Status = "unknown";
                            item.Detail = "上次程序中断，发送结果未知；为避免重复发送，本轮不会自动重试";
                            item.FinishedAt = finished;
                        }
                        return latest;
                    });
                }
                catch { }
            }
            public void Dispose()
            {
                Task pending;
                lock (runGate)
                {
                    if (disposed) return;
                    disposed = true;
                    if (timer != null) { timer.Dispose(); timer = null; }
                    pending = activeTrigger;
                }
                lifetime.Cancel();
                if (pending != null && !pending.IsCompleted)
                {
                    try { pending.Wait(TimeSpan.FromSeconds(4)); }
                    catch (AggregateException) { }
                }
                RecoverInterruptedEvents();
            }
        }

        private static class Planner
        {
            internal static DateTimeOffset Taiwan(DateTimeOffset value) { return value.ToOffset(TimeSpan.FromHours(8)); }
            internal static bool TryCutoff(PlanSettings plan,out DateTimeOffset value)
            {
                DateTime date;
                string dateText=string.IsNullOrWhiteSpace(plan.CutoffDate)?plan.PlanDate:plan.CutoffDate;
                if(DateTime.TryParseExact(dateText,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date))
                {
                    value=new DateTimeOffset(date.Year,date.Month,date.Day,plan.CutoffMinutes/60,plan.CutoffMinutes%60,0,TimeSpan.FromHours(8));return true;
                }
                value=default(DateTimeOffset);return false;
            }
            internal static List<PlanOccurrence> Build(PlanSettings plan, DateTimeOffset nowTaiwan)
            {
                List<PlanOccurrence> result = new List<PlanOccurrence>();
                DateTime date,cutoffDate;
                string dateText = plan.DailyRepeat ? nowTaiwan.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : plan.PlanDate;
                if (!DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return result;
                string cutoffText=plan.DailyRepeat?dateText:(string.IsNullOrWhiteSpace(plan.CutoffDate)?dateText:plan.CutoffDate);
                if(!DateTime.TryParseExact(cutoffText,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out cutoffDate))return result;
                DateTimeOffset startAt=new DateTimeOffset(date.Year,date.Month,date.Day,plan.StartMinutes/60,plan.StartMinutes%60,0,TimeSpan.FromHours(8));
                DateTimeOffset cutoffAt=new DateTimeOffset(cutoffDate.Year,cutoffDate.Month,cutoffDate.Day,plan.CutoffMinutes/60,plan.CutoffMinutes%60,0,TimeSpan.FromHours(8));
                int index = 0;
                foreach (PlanAccount account in plan.Accounts.Where(delegate(PlanAccount a) { return a.Selected; }))
                {
                    DateTimeOffset when=startAt.AddMinutes(index*plan.IntervalMinutes);index++;
                    if(when>cutoffAt)continue;
                    string message = plan.UniformMessage ? plan.CommonMessage : account.Message;
                    result.Add(new PlanOccurrence { AccountId = account.Id, Message = message ?? "", When = when });
                }
                return result;
            }
            internal static string Validate(PlanSettings plan)
            {
                return Validate(plan,DateTimeOffset.UtcNow);
            }
            internal static string Validate(PlanSettings plan,DateTimeOffset nowUtc)
            {
                List<PlanAccount> selected = plan.Accounts.Where(delegate(PlanAccount a) { return a.Selected; }).ToList();
                if (selected.Count == 0) return "请至少选择一个账号。";
                if (plan.IntervalMinutes < 1) return "账号间隔至少为 1 分钟。";
                if (plan.UniformMessage && string.IsNullOrWhiteSpace(plan.CommonMessage)) return "请填写发送内容。";
                if (!plan.UniformMessage && selected.Any(delegate(PlanAccount a) { return string.IsNullOrWhiteSpace(a.Message); })) return "每个已选账号都需要填写发送内容。";
                DateTimeOffset now=Taiwan(nowUtc);
                DateTime startDay,cutoffDay;
                if(plan.DailyRepeat)
                {
                    if(plan.StartMinutes>plan.CutoffMinutes)return "开始时间不能晚于结束时间。";
                }
                else
                {
                    if(!DateTime.TryParseExact(plan.PlanDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out startDay))return "开始日期无效，请重新选择。";
                    string cutoffText=string.IsNullOrWhiteSpace(plan.CutoffDate)?plan.PlanDate:plan.CutoffDate;
                    if(!DateTime.TryParseExact(cutoffText,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out cutoffDay))return "停止日期无效，请重新选择。";
                    DateTimeOffset startAt=new DateTimeOffset(startDay.Year,startDay.Month,startDay.Day,plan.StartMinutes/60,plan.StartMinutes%60,0,TimeSpan.FromHours(8));
                    DateTimeOffset cutoffAt=new DateTimeOffset(cutoffDay.Year,cutoffDay.Month,cutoffDay.Day,plan.CutoffMinutes/60,plan.CutoffMinutes%60,0,TimeSpan.FromHours(8));
                    if(cutoffAt<startAt)return "停止日期时间不能早于开始日期时间。";
                    if(startAt<=now)return "开始日期时间必须晚于当前台湾时间。";
                }
                if (Build(plan, now).Count != selected.Count) return "有账号的计划时间超过停止日期时间，请缩短间隔或减少账号。";
                return "";
            }
        }

        private sealed class CodexCliSender : IPlanSender
        {
            public bool RequiresAuthorization { get { return true; } }
            public async Task<SendResult> SendAsync(AccountMeta account, string message, CancellationToken cancellation)
            {
                return await Task.Run(delegate
                {
                    cancellation.ThrowIfCancellationRequested();
                    string supportError = CliProcess.AutomationSupportError();
                    if (supportError.Length > 0) return SendResult.Fail(supportError);
                    string home = AuthVault.CreatePlainHome();
                    string work = Path.Combine(home, "empty-workspace");
                    Directory.CreateDirectory(work);
                    bool requestStarted=false;
                    try
                    {
                        AuthVault.Materialize(home, account);
                        ProcessStartInfo psi = CliProcess.ExecStartInfo(home, work);
                        using (Process process = new Process { StartInfo = psi })
                        {
                            if (!process.Start()) return SendResult.Fail("后台发送进程无法启动");
                            requestStarted=true;
                            string guarded = CliProcess.GuardedMessage(message);
                            StringBuilder errors=new StringBuilder();object errorGate=new object();
                            process.OutputDataReceived += delegate { };
                            process.ErrorDataReceived += delegate(object sender,DataReceivedEventArgs e) {
                                if(e.Data==null)return;
                                lock(errorGate){errors.AppendLine(e.Data);if(errors.Length>12000)errors.Remove(0,errors.Length-12000);}
                            };
                            process.BeginOutputReadLine();
                            process.BeginErrorReadLine();
                            CliProcess.WriteStrictUtf8(process, guarded);
                            DateTime deadline = DateTime.UtcNow.AddMinutes(2);
                            while (!process.WaitForExit(250))
                            {
                                if (cancellation.IsCancellationRequested)
                                {
                                    CliProcess.Stop(process);
                                    cancellation.ThrowIfCancellationRequested();
                                }
                                if (DateTime.UtcNow >= deadline)
                                {
                                    CliProcess.Stop(process);
                                    string detail;lock(errorGate){detail=CleanExecutionError(errors.ToString());}
                                    return SendResult.Uncertain("发送超时，结果未知，未自动重试"+(detail.Length==0?"":"："+detail));
                                }
                            }
                            process.WaitForExit(); // Drain redirected error callbacks before reading the bounded buffer.
                            AuthVault.TryCaptureAuthorizedHome(home, account);
                            if (process.ExitCode != 0) {
                                string detail;lock(errorGate){detail=CleanExecutionError(errors.ToString());}
                                return SendResult.Fail(ExplainExecutionError("后台失败（退出码 "+process.ExitCode.ToString(CultureInfo.InvariantCulture)+"）"+(detail.Length==0?"：CLI 未提供错误详情":"："+detail)));
                            }
                        }
                        AuthVault.CaptureAuthorizedHome(home, account);
                        return SendResult.Ok("发送成功");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { return requestStarted ? SendResult.Uncertain(SafeError(ex)+" 发送结果未知，未自动重试。") : SendResult.Fail(SafeError(ex)); }
                    finally
                    {
                        // A failed or cancelled request can still rotate the refresh token.
                        // Preserve it only after the account identity check, then erase the plain home.
                        AuthVault.TryCaptureAuthorizedHome(home, account);
                        AuthVault.DeletePlainHome(home);
                    }
                }, cancellation);
            }
        }

        private static bool IsAuthorizationFailure(string detail)
        {
            string value=(detail??"").ToLowerInvariant();
            return new[]{"token_revoked","invalidated oauth token","refresh_token_reused","refresh_token_expired","invalid_grant","401 unauthorized","独立授权已失效"}.Any(delegate(string marker){return value.Contains(marker);});
        }
        private static bool AuthorizationRejected(IEnumerable<PlanEvent> events,string accountId,DateTime credentialWriteUtc)
        {
            if(events==null)return false;
            PlanEvent latest=events.LastOrDefault(delegate(PlanEvent e){return e.AccountId==accountId && (e.Status=="success" || (e.Status=="failed" && IsAuthorizationFailure(e.Detail)));});
            if(latest==null || latest.Status=="success")return false;
            DateTimeOffset finished;
            return !DateTimeOffset.TryParse(latest.FinishedAt,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out finished) || credentialWriteUtc<=finished.UtcDateTime;
        }
        private static string ExplainExecutionError(string detail)
        {
            if(!IsAuthorizationFailure(detail))return detail??"";
            const string hint="独立授权已失效，请点击账号旁的“重登”，重新登录该账号。历史失败不会自动补发。";
            if((detail??"").StartsWith(hint,StringComparison.Ordinal))return detail;
            return hint+"\r\n技术详情："+(detail??"");
        }
        private static string CleanExecutionError(string raw)
        {
            string text=System.Text.RegularExpressions.Regex.Replace(raw??"","\u001b\\[[0-?]*[ -/]*[@-~]","");
            text=System.Text.RegularExpressions.Regex.Replace(text,@"(?i)(bearer\s+|(?:access_token|refresh_token|id_token|api_key)\s*[:=]\s*)[^\s,""}]+","$1[已隐藏]");
            text=System.Text.RegularExpressions.Regex.Replace(text,@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b","[已隐藏]");
            text=System.Text.RegularExpressions.Regex.Replace(text,@"https?://[^\s<>""']+","[网址已隐藏]");
            string[] lines=text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
            string selected=string.Join(" · ",lines.Where(delegate(string line){return line.IndexOf("error",StringComparison.OrdinalIgnoreCase)>=0 || line.IndexOf("failed",StringComparison.OrdinalIgnoreCase)>=0;}).Take(3).ToArray());
            if(selected.Length==0)selected=string.Join(" · ",lines.Reverse().Take(2).Reverse().ToArray());
            return selected.Length>600?selected.Substring(0,600)+"…":selected;
        }

        private static class CliProcess
        {
            private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
            internal static string GuardedMessage(string message)
            {return "This is a scheduled lightweight message. Reply briefly to the user message only. Do not use tools, run commands, browse files, inspect the computer, or modify anything. User message:\n"+message;}

            internal static void ConfigureStrictUtf8Input(ProcessStartInfo psi)
            {
                if (psi == null) throw new ArgumentNullException("psi");
                psi.RedirectStandardInput = true;
            }

            internal static void WriteStrictUtf8(Process process, string value)
            {
                if (process == null) throw new ArgumentNullException("process");
                byte[] payload = StrictUtf8.GetBytes(value ?? "");
                Stream pipe = process.StandardInput.BaseStream;
                pipe.Write(payload, 0, payload.Length);
                pipe.Flush();
                pipe.Close();
            }

            internal static void Stop(Process process)
            {
                try {
                    if(process==null || process.HasExited)return;
                    using(var killer=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"taskkill.exe"),"/PID "+process.Id.ToString(CultureInfo.InvariantCulture)+" /T /F") {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden}))
                        if(killer!=null)killer.WaitForExit(3000);
                    if(!process.HasExited)process.Kill();
                    process.WaitForExit(2000);
                }catch{try{if(process!=null&&!process.HasExited)process.Kill();}catch{}}
            }
            private static readonly object CapabilityGate = new object();
            private static readonly string[] DisabledFeatures = { "shell_tool", "unified_exec", "code_mode_host", "apps", "plugins", "browser_use", "browser_use_external", "computer_use", "in_app_browser", "multi_agent", "multi_agent_v2", "hooks", "memories", "image_generation", "view_image", "workspace_dependencies" };
            private static string capabilityResult;
            internal static ProcessStartInfo LoginStartInfo(string home)
            {
                ProcessStartInfo psi = Base(home, null, "login -c cli_auth_credentials_store=file");
                psi.EnvironmentVariables["NO_COLOR"] = "1";
                psi.EnvironmentVariables["TERM"] = "dumb";
                psi.RedirectStandardInput = false;
                return psi;
            }
            internal static ProcessStartInfo ExecStartInfo(string home, string work)
            {
                ProcessStartInfo psi = Base(home, work, BuildExecArguments());
                ConfigureStrictUtf8Input(psi);
                return psi;
            }
            internal static string BuildExecArguments()
            {
                StringBuilder value = new StringBuilder("exec - --ephemeral --ignore-user-config --ignore-rules --strict-config --skip-git-repo-check --sandbox read-only --color never");
                foreach (string feature in DisabledFeatures) value.Append(" --disable ").Append(feature);
                value.Append(" -c cli_auth_credentials_store=file -c shell_environment_policy.inherit=none -c web_search=disabled -c approval_policy=never");
                return value.ToString();
            }
            internal static string AutomationSupportError()
            {
                lock (CapabilityGate)
                {
                    if (capabilityResult == "") return capabilityResult;
                    try
                    {
                        using (Process process = new Process { StartInfo = Base(null, null, "exec --help") })
                        {
                            if (!process.Start()) return capabilityResult = "无法启动官方 Codex CLI，计划未启用。";
                            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                            Task<string> stderr = process.StandardError.ReadToEndAsync();
                            if (!process.WaitForExit(8000))
                            {
                                try { process.Kill(); } catch { }
                                return capabilityResult = "无法核对 Codex CLI 的安全参数，计划未启用。";
                            }
                            Task.WaitAll(new Task[] { stdout, stderr }, 2000);
                            string help = (stdout.IsCompleted ? stdout.Result : "") + (stderr.IsCompleted ? stderr.Result : "");
                            string[] required = { "--ephemeral", "--ignore-user-config", "--ignore-rules", "--skip-git-repo-check", "--sandbox", "--color", "--disable", "--strict-config" };
                            if (process.ExitCode != 0 || required.Any(delegate(string flag) { return help.IndexOf(flag, StringComparison.Ordinal) < 0; }))
                                return capabilityResult = "当前 Codex CLI 版本不支持所需的隔离参数，为了不影响电脑和桌面账号，计划未启用。";
                        }
                        using (Process process = new Process { StartInfo = Base(null, null, "features list") })
                        {
                            if (!process.Start()) return capabilityResult = "无法核对 Codex CLI 工具隔离能力，计划未启用。";
                            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                            Task<string> stderr = process.StandardError.ReadToEndAsync();
                            if (!process.WaitForExit(8000))
                            {
                                try { process.Kill(); } catch { }
                                return capabilityResult = "无法核对 Codex CLI 工具隔离能力，计划未启用。";
                            }
                            Task.WaitAll(new Task[] { stdout, stderr }, 2000);
                            string features = (stdout.IsCompleted ? stdout.Result : "") + (stderr.IsCompleted ? stderr.Result : "");
                            if (process.ExitCode != 0 || DisabledFeatures.Any(delegate(string feature)
                            {
                                return !features.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Any(delegate(string line) { return line.TrimStart().StartsWith(feature + " ", StringComparison.Ordinal); });
                            })) return capabilityResult = "当前 Codex CLI 无法确认所有工具都已隔离，计划未启用。";
                        }
                        return capabilityResult = "";
                    }
                    catch (Exception ex) { return capabilityResult = SafeError(ex) + " 计划未启用。"; }
                }
            }
            private static ProcessStartInfo Base(string home, string work, string arguments)
            {
                string command = FindCodex();
                bool batch = command.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || command.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
                ProcessStartInfo psi = new ProcessStartInfo();
                if (batch)
                {
                    psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                    psi.Arguments = "/d /s /c \"\"" + command + "\" " + arguments + "\"";
                }
                else { psi.FileName = command; psi.Arguments = arguments; }
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                if (!string.IsNullOrWhiteSpace(work)) psi.WorkingDirectory = work;
                if (!string.IsNullOrWhiteSpace(home)) psi.EnvironmentVariables["CODEX_HOME"] = home;
                psi.EnvironmentVariables.Remove("OPENAI_API_KEY");
                psi.EnvironmentVariables.Remove("CODEX_API_KEY");
                ApplySystemProxy(psi);
                return psi;
            }
            private static void ApplySystemProxy(ProcessStartInfo psi)
            {
                if(!string.IsNullOrWhiteSpace(psi.EnvironmentVariables["HTTPS_PROXY"]) ||
                    !string.IsNullOrWhiteSpace(psi.EnvironmentVariables["https_proxy"]) ||
                    !string.IsNullOrWhiteSpace(psi.EnvironmentVariables["ALL_PROXY"]) ||
                    !string.IsNullOrWhiteSpace(psi.EnvironmentVariables["all_proxy"]))return;
                try
                {
                    Uri target=new Uri("https://chatgpt.com");
                    Uri proxy=System.Net.WebRequest.GetSystemWebProxy().GetProxy(target);
                    if(proxy==null || proxy==target || (proxy.Scheme!="http" && proxy.Scheme!="https") ||
                        !string.IsNullOrEmpty(proxy.UserInfo) || !string.IsNullOrEmpty(proxy.Query) || !string.IsNullOrEmpty(proxy.Fragment))return;
                    psi.EnvironmentVariables["HTTPS_PROXY"]=proxy.AbsoluteUri;
                    if(string.IsNullOrWhiteSpace(psi.EnvironmentVariables["HTTP_PROXY"]))psi.EnvironmentVariables["HTTP_PROXY"]=proxy.AbsoluteUri;
                }
                catch { }
            }
            private static string FindCodex()
            {
                string discovered=CliSetup.Find();
                if(!string.IsNullOrEmpty(discovered))return discovered;
                List<string> folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).ToList();
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrWhiteSpace(appData)) folders.Add(Path.Combine(appData, "npm"));
                foreach (string folder in folders)
                {
                    if (string.IsNullOrWhiteSpace(folder)) continue;
                    foreach (string name in new[] { "codex.exe", "codex.cmd", "codex.bat" })
                    {
                        try {
                            string file = Path.Combine(folder.Trim(), name);
                            if(!File.Exists(file))continue;
                            if(name!="codex.exe") {
                                string nativeRoot=Path.Combine(folder.Trim(),"node_modules","@openai","codex","node_modules","@openai");
                                if(Directory.Exists(nativeRoot))foreach(string package in Directory.GetDirectories(nativeRoot,"codex-win32-*")) {
                                    string vendor=Path.Combine(package,"vendor");
                                    if(Directory.Exists(vendor))foreach(string target in Directory.GetDirectories(vendor)) {
                                        string native=Path.Combine(target,"bin","codex.exe");if(File.Exists(native))return native;
                                    }
                                }
                            }
                            return file;
                        } catch { }
                    }
                }
                throw new FileNotFoundException("未找到官方 Codex CLI，请先安装或将其加入 PATH。");
            }
        }

        private static class AuthVault
        {
            private static readonly object Sync = new object();
            private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexAccountDock|StaggerAuth|v1");
            private static string Root { get { return Path.Combine(PortablePaths.Data, "stagger-auth"); } }
            private static string RuntimeRoot { get { return Path.Combine(PortablePaths.Data, "stagger-runtime"); } }
            private static string FileFor(string id) { return Path.Combine(Root, SafeName(id) + ".bin"); }
            internal static bool IsReady(AccountMeta account)
            {
                byte[] raw = null;
                try { if (NeedsReauthorization(account)) return false; raw = Load(account); return AuthCodec.Parse(raw).StableId == account.Id; }
                catch { return false; }
                finally { if (raw != null) Array.Clear(raw, 0, raw.Length); }
            }
            internal static bool NeedsReauthorization(AccountMeta account)
            {
                try { return AuthorizationRejected(PlanStorage.Load().Events, account.Id, File.GetLastWriteTimeUtc(FileFor(account.Id))); }
                catch { return false; }
            }
            internal static string CreatePlainHome()
            {
                PortablePaths.Ensure();
                Directory.CreateDirectory(RuntimeRoot);
                RestrictToCurrentUser(RuntimeRoot);
                string path = Path.Combine(RuntimeRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(path);
                RestrictToCurrentUser(path);
                return path;
            }
            internal static void Materialize(string home, AccountMeta account)
            {
                byte[] raw = Load(account);
                try { File.WriteAllBytes(Path.Combine(home, "auth.json"), raw); }
                finally { Array.Clear(raw, 0, raw.Length); }
            }
            internal static void CaptureAuthorizedHome(string home, AccountMeta account)
            {
                string auth = Path.Combine(home, "auth.json");
                if (!File.Exists(auth)) throw new InvalidDataException("官方授权未生成账号凭据。");
                byte[] raw = File.ReadAllBytes(auth);
                try
                {
                    AuthIdentity identity = AuthCodec.Parse(raw);
                    if (identity.StableId != account.Id) throw new InvalidDataException("授权账号与所选账号不一致，请重新选择正确账号。");
                    byte[] encrypted = ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
                    try
                    {
                        lock (Sync)
                        {
                            Directory.CreateDirectory(Root);
                            string target = FileFor(account.Id);
                            string temp = target + ".tmp";
                            File.WriteAllBytes(temp, encrypted);
                            if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
                        }
                    }
                    finally { Array.Clear(encrypted, 0, encrypted.Length); }
                }
                finally { Array.Clear(raw, 0, raw.Length); }
            }
            internal static void TryCaptureAuthorizedHome(string home, AccountMeta account)
            {
                try { CaptureAuthorizedHome(home, account); }
                catch { }
            }
            internal static void DeleteAccount(string accountId)
            {
                if (string.IsNullOrWhiteSpace(accountId)) return;
                lock (Sync)
                {
                    DeleteAccountFiles(Root, accountId);
                }
            }
            internal static string FileForRoot(string root, string accountId)
            {
                if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("授权目录为空。", "root");
                return Path.Combine(root, SafeName(accountId) + ".bin");
            }
            internal static void DeleteAccountFiles(string root, string accountId)
            {
                if (string.IsNullOrWhiteSpace(accountId)) return;
                string target = FileForRoot(root, accountId);
                if (File.Exists(target)) File.Delete(target);
                string temp = target + ".tmp";
                if (File.Exists(temp)) File.Delete(temp);
            }
            internal static void DeletePlainHome(string home)
            {
                try
                {
                    string root = Path.GetFullPath(RuntimeRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    string target = Path.GetFullPath(home).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    string leaf = new DirectoryInfo(home).Name;
                    Guid parsed;
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(leaf, "N", out parsed)) return;
                    string auth = Path.Combine(home, "auth.json");
                    if (File.Exists(auth)) { long length = new FileInfo(auth).Length; if (length > 0 && length < 10485760) File.WriteAllBytes(auth, new byte[length]); File.Delete(auth); }
                    if (Directory.Exists(home)) Directory.Delete(home, true);
                }
                catch { }
            }
            private static byte[] Load(AccountMeta account)
            {
                byte[] raw;
                lock (Sync)
                {
                    byte[] encrypted = File.ReadAllBytes(FileFor(account.Id));
                    try { raw = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser); }
                    finally { Array.Clear(encrypted, 0, encrypted.Length); }
                }
                if (AuthCodec.Parse(raw).StableId != account.Id) { Array.Clear(raw, 0, raw.Length); throw new InvalidDataException("独立授权身份不匹配。"); }
                return raw;
            }
            private static string SafeName(string value)
            {
                byte[] bytes;
                using (SHA256 sha = SHA256.Create()) bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
                try
                {
                    StringBuilder result = new StringBuilder(bytes.Length * 2);
                    foreach (byte b in bytes) result.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                    return result.ToString();
                }
                finally { Array.Clear(bytes, 0, bytes.Length); }
            }
            private static void RestrictToCurrentUser(string path)
            {
                SecurityIdentifier sid = WindowsIdentity.GetCurrent().User;
                if (sid == null) throw new UnauthorizedAccessException();
                DirectorySecurity security = new DirectorySecurity();
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(path).SetAccessControl(security);
            }
        }

        private interface IClock { DateTimeOffset Now { get; } }
        private sealed class SystemClock : IClock { public DateTimeOffset Now { get { return DateTimeOffset.UtcNow; } } }
        private sealed class FakeClock : IClock { internal DateTimeOffset NowValue; internal FakeClock(DateTimeOffset now) { NowValue = now; } public DateTimeOffset Now { get { return NowValue; } } }
        private interface IPlanSender { bool RequiresAuthorization { get; } Task<SendResult> SendAsync(AccountMeta account, string message, CancellationToken cancellation); }
        private sealed class FakeSender : IPlanSender
        {
            internal int Count;
            public bool RequiresAuthorization { get { return false; } }
            public Task<SendResult> SendAsync(AccountMeta account, string message, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                Count++;
                return Task.FromResult(SendResult.Ok("fake"));
            }
        }
        private sealed class ControlledSender : IPlanSender
        {
            private readonly TaskCompletionSource<SendResult> completion = new TaskCompletionSource<SendResult>();
            private readonly ManualResetEventSlim started = new ManualResetEventSlim(false);
            internal int Count;
            public bool RequiresAuthorization { get { return false; } }
            public Task<SendResult> SendAsync(AccountMeta account, string message, CancellationToken cancellation)
            {
                Count++;
                started.Set();
                cancellation.Register(delegate { completion.TrySetCanceled(); });
                return completion.Task;
            }
            internal void Complete() { completion.TrySetResult(SendResult.Ok("controlled")); }
            internal bool WaitUntilStarted(int milliseconds) { return started.Wait(milliseconds); }
        }
        private sealed class UncertainSender : IPlanSender
        {
            public bool RequiresAuthorization { get { return false; } }
            public Task<SendResult> SendAsync(AccountMeta account,string message,CancellationToken cancellation){return Task.FromResult(SendResult.Uncertain("发送超时，结果未知，未自动重试"));}
        }
        private sealed class ThrowingSender : IPlanSender
        {
            public bool RequiresAuthorization { get { return false; } }
            public Task<SendResult> SendAsync(AccountMeta account, string message, CancellationToken cancellation)
            {
                throw new InvalidOperationException("synthetic sender failure");
            }
        }
        private sealed class SendResult { internal bool Success; internal bool Unknown; internal string Detail; internal static SendResult Ok(string detail) { return new SendResult { Success = true, Detail = detail }; } internal static SendResult Fail(string detail) { return new SendResult { Detail = detail }; } internal static SendResult Uncertain(string detail) { return new SendResult { Unknown = true, Detail = detail }; } }
        private sealed class PlanOccurrence { internal string AccountId; internal string Message; internal DateTimeOffset When; }

        private sealed class PlanSettings
        {
            public int Version = 1;
            public bool Enabled;
            public bool DailyRepeat;
            public bool UniformMessage = true;
            public string PlanDate = "";
            public string CutoffDate = "";
            public int StartMinutes = 540;
            public int CutoffMinutes = 1350;
            public int IntervalMinutes = 100;
            public string CommonMessage = "";
            public List<PlanAccount> Accounts = new List<PlanAccount>();
            public List<PlanEvent> Events = new List<PlanEvent>();
            internal static PlanSettings Default() { return new PlanSettings(); }
        }
        private sealed class PlanAccount { public string Id = ""; public bool Selected; public string Message = ""; }
        private sealed class PlanEvent { public string Key = ""; public string AccountId = ""; public string ScheduledAt = ""; public string FinishedAt = ""; public string Status = ""; public string Detail = ""; }

        private interface IPlanStorage
        {
            PlanSettings Load();
            PlanSettings Update(Func<PlanSettings, PlanSettings> change);
            PlanSettings SaveConfiguration(PlanSettings value);
        }
        private sealed class FilePlanStorage : IPlanStorage
        {
            public PlanSettings Load() { return PlanStorage.Load(); }
            public PlanSettings Update(Func<PlanSettings, PlanSettings> change) { return PlanStorage.Update(change); }
            public PlanSettings SaveConfiguration(PlanSettings value) { return PlanStorage.SaveConfiguration(value); }
        }
        private sealed class MemoryPlanStorage : IPlanStorage
        {
            private readonly object sync = new object();
            internal PlanSettings Value;
            internal int UpdateCount;
            internal MemoryPlanStorage(PlanSettings value) { Value = value; }
            public PlanSettings Load() { lock (sync) return Value; }
            public PlanSettings Update(Func<PlanSettings, PlanSettings> change) { lock (sync) { UpdateCount++; Value = change(Value) ?? Value; return Value; } }
            public PlanSettings SaveConfiguration(PlanSettings value)
            {
                lock (sync)
                {
                    value.Events = Value == null || Value.Events == null ? new List<PlanEvent>() : Value.Events;
                    Value = value;
                    return Value;
                }
            }
        }
        private static class PlanStorage
        {
            private static readonly object Sync = new object();
            private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexAccountDock|StaggerPlan|v1");
            private static string PathName { get { return Path.Combine(PortablePaths.Data, "stagger-plan.bin"); } }
            internal static DateTime LastWrite {get{try{return File.GetLastWriteTimeUtc(PathName);}catch{return DateTime.MinValue;}}}
            internal static PlanSettings Load()
            {
                lock (Sync)
                {
                    try
                    {
                        if (!File.Exists(PathName)) return PlanSettings.Default();
                        byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(PathName), Entropy, DataProtectionScope.CurrentUser);
                        try
                        {
                            PlanSettings value = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<PlanSettings>(Encoding.UTF8.GetString(plain)) ?? PlanSettings.Default();
                            if (value.Accounts == null) value.Accounts = new List<PlanAccount>();
                            if (value.Events == null) value.Events = new List<PlanEvent>();
                            return value;
                        }
                        finally { Array.Clear(plain, 0, plain.Length); }
                    }
                    catch { return PlanSettings.Default(); }
                }
            }
            internal static void Save(PlanSettings value)
            {
                lock (Sync)
                {
                    SaveUnlocked(value);
                }
            }
            internal static PlanSettings Update(Func<PlanSettings, PlanSettings> change)
            {
                lock (Sync)
                {
                    PlanSettings current = Load();
                    var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
                    string before=serializer.Serialize(current);
                    PlanSettings next = change(current) ?? current;
                    if(!string.Equals(before,serializer.Serialize(next),StringComparison.Ordinal))SaveUnlocked(next);
                    return next;
                }
            }
            internal static PlanSettings SaveConfiguration(PlanSettings value)
            {
                return Update(delegate(PlanSettings current)
                {
                    value.Events = current == null || current.Events == null ? new List<PlanEvent>() : current.Events;
                    return value;
                });
            }
            private static void SaveUnlocked(PlanSettings value)
            {
                PortablePaths.Ensure();
                byte[] plain = Encoding.UTF8.GetBytes(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(value));
                byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                Array.Clear(plain, 0, plain.Length);
                string temp = PathName + ".tmp"; File.WriteAllBytes(temp, encrypted);
                if (File.Exists(PathName)) File.Replace(temp, PathName, null); else File.Move(temp, PathName);
            }
        }

        private static string SafeError(Exception ex)
        {
            if (ex is FileNotFoundException) return ex.Message;
            if (ex is InvalidDataException) return ex.Message;
            if (ex is UnauthorizedAccessException) return "当前用户无法写入独立授权目录。";
            return "操作未完成，请稍后重试。";
        }
    }
}
