using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexAccountDock
{
    internal sealed class ResetForecast
    {
        internal string Summary="",Note="",SourceUrl="",CheckedAt="";
        internal bool Stale,HasAnnouncement,WindowExpired;
        internal bool Ready,IsInferred;
        internal string WindowLabel="",EventTitle="",WindowEndsAt="",EventUpdatedAt="",FetchedAt="";
    }

    // Public forecast only: no account credentials, no quota mutation and no disk cache.
    internal static class ResetFeed
    {
        private const string Url="https://shop.smartkey.fun/signal/api/aihot-reset";
        private const int MaximumBytes=2*1024*1024;
        private sealed class Candidate
        {
            internal Dictionary<string,object> Event;
            internal DateTimeOffset? End;
            internal DateTimeOffset Updated;
        }

        internal static async Task<ResetForecast> FetchAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string json=await PublicFeedTransport.FetchJsonAsync(Url,token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return Parse(json,DateTimeOffset.Now);
        }

        internal static ResetForecast Parse(string json,DateTimeOffset now)
        {
            if(json==null || json.Length>MaximumBytes)throw new FormatException("预告内容无效或过大");
            var root=new JavaScriptSerializer {MaxJsonLength=MaximumBytes,RecursionLimit=30}.Deserialize<Dictionary<string,object>>(json);
            var data=JsonUtil.Dict(JsonUtil.Value(root,"data"));
            object raw=JsonUtil.Value(data,"events");IEnumerable events=raw as IEnumerable;
            if(events==null || raw is string || raw is IDictionary)throw new FormatException("预告数据格式无法识别");
            var result=new ResetForecast {Ready=true,Summary="暂无待确认的第三方预告",Note="公开预告不代表个人额度到账，实际额度以查询结果为准。",CheckedAt=Clean(JsonUtil.Text(data,"checkedAt"),80),Stale=object.Equals(JsonUtil.Value(root,"stale"),true)};
            result.FetchedAt=now.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz",CultureInfo.InvariantCulture);
            DateTimeOffset checkedAt;
            if(DateTimeOffset.TryParse(result.CheckedAt,CultureInfo.InvariantCulture,DateTimeStyles.None,out checkedAt))
            {
                result.CheckedAt=checkedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz",CultureInfo.InvariantCulture);
                result.Stale|=checkedAt<now.AddHours(-24);
            }
            var candidates=new List<Candidate>();int scanned=0;
            foreach(object value in events)
            {
                if(++scanned>500)break;
                var item=JsonUtil.Dict(value);
                if(item==null || JsonUtil.Text(item,"status")!="announced")continue;
                var schedule=JsonUtil.Dict(JsonUtil.Value(item,"schedule"));
                var estimate=JsonUtil.Dict(JsonUtil.Value(item,"estimate"));
                DateTimeOffset end,updated;
                string through=JsonUtil.Text(schedule,"through");
                if(!DateTimeOffset.TryParse(through,CultureInfo.InvariantCulture,DateTimeStyles.None,out end))through=JsonUtil.Text(estimate,"through");
                string stamp=JsonUtil.Text(item,"updatedAt");if(string.IsNullOrWhiteSpace(stamp))stamp=JsonUtil.Text(item,"createdAt");
                DateTimeOffset.TryParse(stamp,CultureInfo.InvariantCulture,DateTimeStyles.None,out updated);
                candidates.Add(new Candidate {Event=item,Updated=updated,End=DateTimeOffset.TryParse(through,CultureInfo.InvariantCulture,DateTimeStyles.None,out end)?(DateTimeOffset?)end:null});
            }
            Candidate current=candidates.Where(item=>!item.End.HasValue || item.End.Value>=now).OrderByDescending(item=>item.Updated).FirstOrDefault();
            if(current==null)return result;
            var selected=current.Event;
            var selectedSchedule=JsonUtil.Dict(JsonUtil.Value(selected,"schedule"));
            var selectedEstimate=JsonUtil.Dict(JsonUtil.Value(selected,"estimate"));
            string label=Clean(JsonUtil.Text(selectedSchedule,"label"),240);bool inferred=false;
            if(label.Length==0){label=Clean(JsonUtil.Text(selectedEstimate,"label"),240);inferred=JsonUtil.Text(selectedEstimate,"basis")=="model";}
            if(label.Length==0)label="具体时间待确认";
            result.HasAnnouncement=true;result.WindowExpired=current.End.HasValue && current.End.Value<now;
            result.WindowLabel=label;result.IsInferred=inferred;
            result.EventTitle=Clean(JsonUtil.Text(selected,"title"),180);
            if(current.End.HasValue)result.WindowEndsAt=current.End.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz",CultureInfo.InvariantCulture);
            if(current.Updated!=DateTimeOffset.MinValue)result.EventUpdatedAt=current.Updated.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz",CultureInfo.InvariantCulture);
            result.Summary=result.WindowExpired?"预告窗口已过，等待公开确认":(inferred?"第三方推算 · ":"第三方预告 · ")+label;
            result.Note=(result.WindowExpired?label+"。":"")+(inferred?"含第三方模型推算，尚未核验。":"尚未核验。")+"不代表个人额度已到账，以实际查询为准。";
            IEnumerable posts=JsonUtil.Value(selected,"posts") as IEnumerable;
            if(posts!=null)
                foreach(object post in posts)
                {
                    string link=Clean(JsonUtil.Text(JsonUtil.Dict(post),"url"),2048);
                    if(IsSafeSource(link)){result.SourceUrl=link;break;}
                }
            return result;
        }

        internal static bool IsSafeSource(string value)
        {
            Uri uri;return Uri.TryCreate(value,UriKind.Absolute,out uri) && uri.Scheme=="https" && string.IsNullOrEmpty(uri.UserInfo);
        }
        private static string Clean(string value,int limit)
        {
            value=value??"";if(value.Length>limit)value=value.Substring(0,limit);
            return new string(value.Where(c=>!char.IsControl(c)).ToArray()).Trim();
        }

        internal static int SelfTest()
        {
            var now=new DateTimeOffset(2026,10,24,12,0,0,TimeSpan.FromHours(8));
            string pending="{\"status\":\"announced\",\"updatedAt\":\"2026-10-24T11:00:00+08:00\",\"schedule\":{},\"estimate\":{\"through\":\"2026-10-25T12:00:00+08:00\",\"label\":\"明日窗口\",\"basis\":\"model\"},\"posts\":[{\"url\":\"file:///C:/Windows/win.ini\"},{\"url\":\"https://example.com/post\"}]}";
            string expired="{\"status\":\"announced\",\"updatedAt\":\"2026-10-23T11:00:00+08:00\",\"schedule\":{\"through\":\"2026-10-23T12:00:00+08:00\",\"label\":\"旧窗口\"}}";
            Func<string,string> wrap=rows=>"{\"stale\":false,\"data\":{\"checkedAt\":\"2026-10-24T11:30:00+08:00\",\"events\":["+rows+"]}}";
            ResetForecast parsed=Parse(wrap(expired+","+pending),now);
            if(!parsed.HasAnnouncement || parsed.WindowExpired || parsed.Stale || !parsed.Summary.Contains("明日窗口") || !parsed.Summary.Contains("推算") || parsed.SourceUrl!="https://example.com/post")throw new Exception("Forecast selection / fallback / source policy");
            if(!parsed.Ready || !parsed.IsInferred || parsed.WindowLabel!="明日窗口" || parsed.WindowEndsAt.Length==0 || parsed.EventUpdatedAt.Length==0 || !parsed.CheckedAt.StartsWith("2026-") || parsed.FetchedAt==parsed.CheckedAt || !parsed.FetchedAt.StartsWith("2026-"))throw new Exception("Structured forecast and distinct query/source timestamps");
            parsed=Parse(wrap(expired),now);
            if(parsed.HasAnnouncement || parsed.WindowExpired || !parsed.Note.Contains("不代表个人"))throw new Exception("Expired events must not be promoted to current announcements");
            parsed=Parse(wrap("{\"status\":\"confirmed\",\"title\":\"已重置\"}"),now);
            if(!parsed.Ready || parsed.HasAnnouncement || parsed.SourceUrl.Length>0 || parsed.WindowLabel.Length>0)throw new Exception("Historical confirmation is not pending personal quota");
            parsed=Parse(wrap(pending.Replace("\"schedule\":{}","\"title\":\"公开消息\",\"schedule\":{\"label\":\"今晚窗口\",\"through\":\"2026-10-25T12:00:00+08:00\"}")),now);
            if(parsed.IsInferred || parsed.WindowLabel!="今晚窗口" || parsed.EventTitle!="公开消息")throw new Exception("Announced time separate from inferred estimate");
            parsed=Parse("{\"stale\":true,\"data\":{\"events\":[]}}",now);
            if(!parsed.Stale || parsed.HasAnnouncement)throw new Exception("Stale empty response");
            if(!Parse(wrap("").Replace("2026-10-24T11:30:00+08:00","2026-10-20T11:30:00+08:00"),now).Stale)throw new Exception("Old checkedAt");
            if(Parse(wrap(pending.Replace("\"schedule\":{}","\"schedule\":{\"through\":\"invalid\"}").Replace("2026-10-25T12:00:00+08:00","2026-10-23T12:00:00+08:00")),now).HasAnnouncement)throw new Exception("Expired fallback estimate must not be current");
            foreach(string invalid in new[]{"null","{}","{\"data\":{\"events\":\"invalid\"}}",new string('x',MaximumBytes+1)})
            {
                bool failed=false;try{Parse(invalid,now);}catch(Exception){failed=true;}
                if(!failed)throw new Exception("Invalid response accepted");
            }
            if(IsSafeSource("http://example.com") || IsSafeSource("https://user:pass@example.com"))throw new Exception("Unsafe URL policy");
            using(var cancel=new CancellationTokenSource())
            {
                cancel.Cancel();bool cancelled=false;try{FetchAsync(cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
                if(!cancelled)throw new Exception("Pre-cancelled request");
            }
            Console.WriteLine("RESET_FEED_OK structured_states full_timestamps empty pending expired stale source_fallback bounded_payload safe_links pre_cancel no_personal_quota_mutation no_disk no_network");return 0;
        }

        internal static int LiveTest()
        {
            try
            {
                ResetForecast value=FetchAsync(CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine("RESET_FEED_LIVE_OK pending="+value.HasAnnouncement+" expired="+value.WindowExpired+" stale="+value.Stale+" source="+(value.SourceUrl.Length>0)+" manual_request=1 no_disk");return 0;
            }
            catch(Exception ex){Console.WriteLine("RESET_FEED_LIVE_FAILED "+ex.GetType().Name);return 1;}
        }
    }
}
