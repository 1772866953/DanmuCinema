using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    public sealed partial class DanmuCatalog
    {
        static Dictionary<string,object> Ranked(Dictionary<string,object> item,int priority,string method,string reason,Dictionary<string,object> episode=null)
        {
            var copy=new Dictionary<string,object>(item);
            copy["Priority"]=Math.Max(0,Math.Min(100,priority));copy["MatchMethod"]=method;copy["MatchReason"]=reason;
            if(episode!=null)copy["SuggestedEpisodeId"]=Json.Text(episode,"Id");
            return copy;
        }
        static int Priority(Dictionary<string,object> item)
        { int value;return Int32.TryParse(Json.Text(item,"Priority"),out value)?value:0; }
        Dictionary<string,object>[] ConfirmedWorks(Dictionary<string,object>[] local,string providerId)
        {
            var candidates=new List<Dictionary<string,object>>();
            var enabled=new HashSet<string>(Providers().Select(p=>p.Id));
            foreach(var record in DanmuAssociations.Read().Values)
            {
                if(!enabled.Contains(record.Provider)||!String.IsNullOrEmpty(providerId)&&record.Provider!=providerId)continue;
                var reference=new Dictionary<string,object>{{"Path",record.VideoPath},{"Type","Episode"},{"ParentIndexNumber",record.Season}};
                if(!local.Any(item=>SmartMatching.SameSeason(item,reference)&&String.Equals(MediaNames.FileTitle(item),MediaNames.FileTitle(reference),StringComparison.OrdinalIgnoreCase)))continue;
                var xml=new FileInfo(Path.ChangeExtension(record.VideoPath,".xml"));
                if(!File.Exists(record.VideoPath)||!xml.Exists||xml.Length!=record.XmlLength||xml.LastWriteTimeUtc.Ticks!=record.XmlWriteTicks)continue;
                Dictionary<string,object> anime;
                lock(sync)animes.TryGetValue(record.AnimeId??"",out anime);
                if(anime!=null)candidates.Add(Ranked(anime,94,"season","同目录、同作品及季度的已确认弹幕记录"));
            }
            var unique=candidates.GroupBy(a=>Json.Text(a,"Id")).Select(g=>g.First()).ToArray();
            if(unique.Length>1)foreach(var item in unique){item["Priority"]=76;item["MatchReason"]="同目录存在多个已确认作品，请核对季度与集数";}
            return unique;
        }
        public async Task<CatalogSearch> ManualSearch(string keyword,IEnumerable<Dictionary<string,object>> selected,bool animeOnly,bool smart,int season=0,string providerId=null,CancellationToken cancellation=default(CancellationToken),bool refresh=false)
        {
            cancellation.ThrowIfCancellationRequested();
            if(String.IsNullOrWhiteSpace(keyword)||keyword.Length>200)throw new ArgumentException("请输入不超过200字的作品名。");
            var local=selected.ToArray();
            var files=local.Select(item=>{
                string path=Json.Text(item,"Path");if(String.IsNullOrWhiteSpace(path))return "metadata|"+Json.Text(item,"Id")+"|"+Json.Text(item,"Name");var file=new FileInfo(path);
                return path.ToUpperInvariant()+"|"+(file.Exists?file.Length:0)+"|"+(file.Exists?file.LastWriteTimeUtc.Ticks:0)+"|"+SmartMatching.Season(item)+"|"+SmartMatching.Episode(item);
            }).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            string appId="";try{if(settings.EnableDandan)appId=DandanConfig.Load().AppId;}catch{ }
            string key=DandanApiCache.Key("manual-resolution-v1|"+Json.Write(new{keyword=keyword.Trim(),animeOnly,smart,season,providerId,files,enabled=Providers().Select(p=>p.Id+"|"+p.Url).ToArray(),appId}));
            string cached=refresh?null:Cache.Read(key);
            if(cached!=null)try{
                var saved=Json.Read<CatalogSearch>(cached);
                if(saved.Items.Length>0){
                    var linked=saved.Items.OfType<Dictionary<string,object>>().Where(a=>Json.Text(a,"MatchMethod")=="season").ToArray();
                    if(linked.Length>0){var current=ConfirmedWorks(local,providerId);if(linked.Any(a=>!current.Any(b=>Json.Text(a,"Id")==Json.Text(b,"Id"))))throw new InvalidDataException("同目录来源已改变。");}
                    lock(sync)foreach(var item in saved.Items.OfType<Dictionary<string,object>>()){
                        var clean=new Dictionary<string,object>(item);foreach(string field in new[]{"Priority","MatchMethod","MatchReason","SuggestedEpisodeId"})clean.Remove(field);
                        animes[Json.Text(clean,"Id")]=clean;
                    }
                    saved.Summary="已读取本地匹配缓存。"+saved.Summary;return saved;
                }
            }catch{ }
            var result=await ManualSearchCore(keyword,local,animeOnly,smart,season,providerId,cancellation).ConfigureAwait(false);
            // Only successful, strong resolutions are reused. Empty results,
            // timeout failures and ambiguous filename hints stay retryable.
            if(result.Items.OfType<Dictionary<string,object>>().Any(a=>Priority(a)>=89))Cache.Write(key,"match",keyword,Json.Write(result),Json.Text(result.Items.OfType<Dictionary<string,object>>().First(),"Name"));
            return result;
        }
        async Task<CatalogSearch> ManualSearchCore(string keyword,IEnumerable<Dictionary<string,object>> selected,bool animeOnly,bool smart,int season,string providerId,CancellationToken cancellation)
        {
            if(String.IsNullOrWhiteSpace(keyword))throw new ArgumentException("请输入作品名。");
            if(keyword.Length>200)throw new ArgumentException("搜索词过长。");
            var local=selected.Where(item=>!String.IsNullOrWhiteSpace(Json.Text(item,"Path"))).GroupBy(item=>Json.Text(item,"Path"),StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToArray();
            var available=Providers();
            if(!String.IsNullOrEmpty(providerId)&&!available.Any(p=>p.Id==providerId))throw new InvalidOperationException("所选接口已停用，请重新选择弹幕接口。");
            if(season==0)season=local.Length>0?SmartMatching.Season(local[0]):SmartMatching.SeasonTitle(keyword);
            var found=new List<Dictionary<string,object>>();var states=new List<object>();var notes=new List<string>();
            using(var budget=CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                budget.CancelAfter(TimeSpan.FromSeconds(90));
                // A season needs representative files, not one request per episode.
                // Every representative gets its hash attempt before filename lookup.
                var accessible=local.Where(item=>File.Exists(Json.Text(item,"Path"))).ToArray();
                var samples=accessible.Length==0?accessible:new[]{accessible[0],accessible[accessible.Length/2],accessible[accessible.Length-1]}.Distinct().Take(3).ToArray();
                bool official=smart&&samples.Length>0&&(String.IsNullOrEmpty(providerId)||providerId=="dandan")&&available.Any(p=>p.Id=="dandan")&&DandanConfig.Load().Ready;
                if(official)
                {
                    foreach(var item in samples)
                    {
                        try
                        {
                            var match=await IdentifyFile(item,budget.Token,"hashOnly").ConfigureAwait(false);
                            if(match.HashMatched&&match.Recommended!=null)
                            {
                                foreach(var anime in match.Animes)found.Add(Ranked(anime,100,"hash","文件哈希精确命中 · "+Path.GetFileName(Json.Text(item,"Path")),match.Recommended));
                                notes.Add("弹弹play hash 精确识别成功，已停止名称回退。整季仍需在预览中核对逐集对应关系。");
                                return SearchResult(found,states,notes,animeOnly);
                            }
                            if(match.TemporaryFailure){notes.Add("哈希识别请求未完成，继续使用其他证据");break;}
                        }
                        catch(Exception){cancellation.ThrowIfCancellationRequested();notes.Add("哈希识别暂不可用，继续核对名称");break;}
                    }
                    try
                    {
                        var match=await IdentifyFile(samples[0],budget.Token,"fileNameOnly").ConfigureAwait(false);
                        foreach(var anime in match.Animes)
                        {
                            bool confirmed=match.Recommended!=null&&Json.Text(match.Recommended,"AnimeId")==Json.Text(anime,"Id");
                            int score=confirmed?96:Math.Min(87,25+(int)Math.Round(SmartMatching.Score(MediaNames.SearchTitle(samples[0]),anime,season)*.62));
                            found.Add(Ranked(anime,score,"filename",confirmed?"文件名、季度与集号一致":"官方文件名候选，需要核对作品及季度",confirmed?match.Recommended:null));
                        }
                    }
                    catch(Exception){cancellation.ThrowIfCancellationRequested();notes.Add("文件名识别未完成，继续搜索作品名");}
                }
                if(smart&&local.Length>0)found.AddRange(ConfirmedWorks(local,providerId));
                if(found.Any(a=>Priority(a)>=94))return SearchResult(found,states,notes,animeOnly);
                var queries=new List<string>{keyword};
                if(smart&&local.Length>0)
                {
                    // Keep the clicked filename first; the work folder is separate
                    // fallback evidence and never replaces the single-episode label.
                    queries.Insert(0,MediaNames.FileTitle(local[0]));queries.Add(MediaNames.SearchTitle(local[0]));
                }
                queries=queries.Where(q=>!String.IsNullOrWhiteSpace(q)).Select(q=>smart?SmartMatching.CleanTitle(q):q.Trim()).Where(q=>q.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
                foreach(string query in queries)
                {
                    if(budget.IsCancellationRequested)break;
                    try
                    {
                        var result=await Search(query,animeOnly,smart,season,providerId,budget.Token,local.Length>1||local.Any(SmartMatching.IsEpisodic)).ConfigureAwait(false);
                        states.AddRange(result.Sources??new object[0]);
                        foreach(var anime in result.Items.OfType<Dictionary<string,object>>())
                        {
                            int score=(int)Math.Round(SmartMatching.Score(query,anime,season)*.89);
                            bool wrongType=(local.Length>1||local.Any(SmartMatching.IsEpisodic))&&!IsSeries(anime);
                            if(wrongType)score=Math.Min(score,35);
                            found.Add(Ranked(anime,score,"title",wrongType?"同名电影或单条来源，不适合整季匹配":"作品名 / 别名、季度匹配；请核对集数"));
                        }
                        if(result.Items.OfType<Dictionary<string,object>>().Any(a=>SmartMatching.Score(query,a,season)>=90))break;
                    }
                    catch(Exception){cancellation.ThrowIfCancellationRequested();notes.Add("部分名称查询未完成");}
                }
            }
            return SearchResult(found,states,notes,animeOnly);
        }
        CatalogSearch SearchResult(List<Dictionary<string,object>> found,List<object> states,List<string> notes,bool animeOnly)
        {
            var items=found.Where(a=>!animeOnly||IsAnime(a)).GroupBy(a=>Json.Text(a,"Id")).Select(g=>g.OrderByDescending(Priority).First())
                .OrderByDescending(Priority).ThenByDescending(a=>Json.Text(a,"Provider")=="dandan").ThenBy(a=>Json.Text(a,"Name")).ToArray();
            string summary=String.Join("；",notes.Distinct());
            if(items.Length==0)
            {
                string errors=String.Join("；",states.OfType<Dictionary<string,object>>().Where(s=>Json.Text(s,"Error")!="").Select(s=>Json.Text(s,"Name")+"："+Json.Text(s,"Error")).Distinct());
                summary=(summary==""?"未找到可靠候选。":summary+"；")+(errors==""?"可更换关键词或来源后重试。":errors);
            }
            else if(summary=="")summary="找到 "+items.Length+" 部作品，已按匹配优先度排序。";
            return new CatalogSearch{Items=items.Cast<object>().ToArray(),Sources=states.ToArray(),Summary=summary};
        }
    }
}
