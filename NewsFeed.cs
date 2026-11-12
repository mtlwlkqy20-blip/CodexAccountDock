using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexAccountDock
{
    internal sealed class NewsItem
    {
        internal string Id,Title,Summary,Source,Category,OriginalUrl,Stamp;
    }
    internal static class NewsFeed
    {
        internal const string Url="https://shop.smartkey.fun/signal/api/daily";
        private const int MaximumBytes=2*1024*1024;
        // This is called only by the manual-refresh event. Nothing is written to disk.
        internal static async Task<List<NewsItem>> FetchAsync(CancellationToken token)
        {
            string json=await PublicFeedTransport.FetchJsonAsync(Url,token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return Parse(json);
        }
        internal static List<NewsItem> Parse(string json)
        {
            if(json==null || json.Length>MaximumBytes)throw new FormatException("日报内容无效或过大");
            var root=new JavaScriptSerializer {MaxJsonLength=MaximumBytes,RecursionLimit=30}.Deserialize<Dictionary<string,object>>(json);
            IEnumerable raw=JsonUtil.Value(JsonUtil.Dict(JsonUtil.Value(root,"data")),"items") as IEnumerable;
            if(raw==null)throw new FormatException("日报数据格式无法识别");
            var result=new List<NewsItem>();
            var ids=new HashSet<string>();
            var categoryCounts=new Dictionary<string,int>();
            int scanned=0;
            foreach(object value in raw)
            {
                if(++scanned>1000 || result.Count>=18)break;
                var item=JsonUtil.Dict(value);if(item==null || !object.Equals(JsonUtil.Value(item,"selected"),true))continue;
                string title=Clean(JsonUtil.Text(item,"title"),240);if(title.Length==0)continue;
                string source=Clean(JsonUtil.Text(JsonUtil.Dict(JsonUtil.Value(item,"source")),"name"),100);
                string summary=Clean(JsonUtil.Text(item,"summary"),6000);
                string original=Clean(JsonUtil.Text(JsonUtil.Dict(JsonUtil.Value(item,"links")),"original"),2048);
                Uri link;if(!Uri.TryCreate(original,UriKind.Absolute,out link) || link.Scheme!="https" || !string.IsNullOrEmpty(link.UserInfo))original="";
                // The source category is authoritative.  Do not let a headline or tag
                // containing another category word move the article to the wrong tab.
                string category=MapCategory(Clean(JsonUtil.Text(item,"category"),100));
                if(category.Length==0)continue;
                int categoryCount;
                categoryCounts.TryGetValue(category,out categoryCount);
                if(categoryCount>=3)continue;
                string stamp=Clean(JsonUtil.Text(item,"publishedAt"),80);if(stamp.Length==0)stamp=Clean(JsonUtil.Text(item,"discoveredAt"),80);
                DateTimeOffset time;if(DateTimeOffset.TryParse(stamp,CultureInfo.InvariantCulture,DateTimeStyles.None,out time))stamp=time.ToOffset(TimeSpan.FromHours(8)).ToString("MM-dd HH:mm",CultureInfo.InvariantCulture);
                string id;
                using(var sha=SHA256.Create())id=Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(original.Length>0?original:source+"\n"+title)));
                if(!ids.Add(id))continue;
                result.Add(new NewsItem {Id=id,Title=title,Summary=summary,Source=source.Length==0?"来源未标注":source,Category=category,OriginalUrl=original,Stamp=stamp});
                categoryCounts[category]=categoryCount+1;
            }
            return result;
        }
        internal static string MapCategory(string text)
        {
            switch((text??"").Trim().ToLowerInvariant())
            {
                case "ai-models":return "模型";
                case "ai-products":return "产品";
                case "industry":return "行业";
                case "paper":return "论文";
                case "tip":return "教程";
                case "opinion":return "观点";
                default:return "";
            }
        }
        private static string Clean(string value,int limit)
        {
            value=value??"";if(value.Length>limit)value=value.Substring(0,limit);
            return new string(value.Where(c=>!char.IsControl(c) || c=='\n' || c=='\t').ToArray()).Trim();
        }
        internal static int SelfTest()
        {
            PublicFeedTransport.SelfTest();
            string json="{\"data\":{\"items\":[{\"selected\":true,\"category\":\"tip\",\"title\":\"开发者工具\",\"summary\":\"<script>not executed</script>\",\"links\":{\"original\":\"file:///C:/Windows/win.ini\"}},{\"selected\":true,\"category\":\"tip\",\"title\":\"开发者工具\",\"summary\":\"<script>not executed</script>\",\"links\":{\"original\":\"file:///C:/Windows/win.ini\"}},{\"selected\":false,\"category\":\"tip\",\"title\":\"unselected\"}]}}";
            List<NewsItem> items=Parse(json);
            if(items.Count!=1 || items[0].OriginalUrl!="" || items[0].Category!="教程" || !items[0].Summary.Contains("<script>"))throw new Exception("News selection / dedup / link policy");
            bool malformed=false;try{Parse("{\"data\":{}}");}catch(FormatException){malformed=true;}
            string[] sourceCategories={"ai-models","ai-products","industry","paper","tip","opinion"};
            string[] displayCategories={"模型","产品","行业","论文","教程","观点"};
            for(int i=0;i<sourceCategories.Length;i++)
                if(MapCategory(sourceCategories[i])!=displayCategories[i])throw new Exception("Exact category mapping");
            if(!malformed || MapCategory("unknown")!="" || MapCategory(" ai-models ")!="模型")throw new Exception("News validation");

            // Four rounds are interleaved in source order.  The fourth story in each
            // category must be ignored while the first 18 retain their source order.
            var batchRows=new List<string>();
            for(int round=0;round<4;round++)
                for(int categoryIndex=0;categoryIndex<sourceCategories.Length;categoryIndex++)
                    batchRows.Add("{\"selected\":true,\"category\":\""+sourceCategories[categoryIndex]+"\",\"title\":\""+sourceCategories[(categoryIndex+1)%sourceCategories.Length]+" misleading "+round+"-"+categoryIndex+"\",\"links\":{\"original\":\"https://example.com/"+round+"/"+categoryIndex+"\"}}");
            string batch="{\"data\":{\"items\":["+string.Join(",",batchRows)+"]}}";
            List<NewsItem> batchItems=Parse(batch);
            if(batchItems.Count!=18)throw new Exception("Per-category production limit");
            for(int i=0;i<displayCategories.Length;i++)
                if(batchItems.Count(item=>item.Category==displayCategories[i])!=3)throw new Exception("Per-category production limit");
            for(int i=0;i<batchItems.Count;i++)
            {
                int expectedRound=i/sourceCategories.Length,expectedCategory=i%sourceCategories.Length;
                if(batchItems[i].Category!=displayCategories[expectedCategory] || !batchItems[i].Title.EndsWith(expectedRound+"-"+expectedCategory,StringComparison.Ordinal))throw new Exception("Stable source order");
            }

            string sparse="{\"data\":{\"items\":[{\"selected\":true,\"category\":\"opinion\",\"title\":\"opinion 1\"},{\"selected\":true,\"category\":\"opinion\",\"title\":\"opinion 2\"},{\"selected\":true,\"category\":\"unknown\",\"title\":\"unknown\"}]}}";
            List<NewsItem> sparseItems=Parse(sparse);
            if(sparseItems.Count!=2 || sparseItems.Any(item=>item.Category!="观点"))throw new Exception("Sparse categories must not be fabricated");
            string sharedLink="{\"data\":{\"items\":[{\"selected\":true,\"category\":\"paper\",\"title\":\"first\",\"links\":{\"original\":\"https://example.com/shared\"}},{\"selected\":true,\"category\":\"ai-models\",\"title\":\"renamed\",\"links\":{\"original\":\"https://example.com/shared\"}}]}}";
            if(Parse(sharedLink).Count!=1)throw new Exception("Cross-category article deduplication");
            bool oversized=false;try{Parse(new string('x',MaximumBytes+1));}catch(FormatException){oversized=true;}
            if(!oversized)throw new Exception("Bounded payload policy");
            Console.WriteLine("NEWS_FEED_OK selected_only six_categories three_per_category all_union_18 stable_order sparse_categories dedup bounded_payload https_links no_html_execution no_network no_credentials");return 0;
        }
    }
}
