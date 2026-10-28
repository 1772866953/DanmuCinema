using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DanmuCinema;

// Opt-in live check: only reads the supplied media directory. All settings,
// imported caches and temporary credentials use an isolated test data root.
class ManualSearchProbe
{
    sealed class Meter:DelegatingHandler
    {
        public int Requests,HashRequests,NameRequests;
        public Meter():base(new HttpClientHandler{AllowAutoRedirect=false}){}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(Requests>=6)throw new InvalidOperationException("Live probe request budget reached");
            Requests++;
            if(request.Content!=null){string body=await request.Content.ReadAsStringAsync();if(body.Contains("hashOnly"))HashRequests++;if(body.Contains("fileNameOnly"))NameRequests++;}
            return await base.SendAsync(request,token).ConfigureAwait(false);
        }
    }
    static int Main(string[] args){try{return Run(args).GetAwaiter().GetResult();}catch(Exception e){Console.Error.WriteLine("Probe failed: "+e.GetType().Name);return 1;}}
    static async Task<int> Run(string[] args)
    {
        string output=Path.GetFullPath(args[0]),install=Path.GetFullPath(args[1]),video=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
        Paths.Root=install;var credentials=DandanConfig.Load();if(!credentials.Ready)throw new InvalidOperationException("Official API configuration is not ready");
        Paths.Root=Path.Combine(output,"isolated-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(Path.Combine(Paths.Root,"config"));Directory.CreateDirectory(Paths.Data);string config=DandanConfig.FilePath;
        try{
            SettingsStore.AtomicWrite(config,Json.Write(new{AppId=credentials.AppId,AppSecret=credentials.Secret}),false);
            foreach(string name in new[]{"anime-catalog-cache.json","danmu-associations.json"}){string source=Path.Combine(install,"data",name);if(File.Exists(source))File.Copy(source,Path.Combine(Paths.Data,name),true);}
            var files=Directory.GetFiles(Path.GetDirectoryName(video),"*.mkv").OrderBy(path=>SmartMatching.Episode(new Dictionary<string,object>{{"Path",path}})).Select(path=>new Dictionary<string,object>{{"Path",path},{"Name",Path.GetFileNameWithoutExtension(path)},{"Type","Episode"}}).ToArray();
            var snapshots=files.Select(item=>new FileInfo(Json.Text(item,"Path"))).Select(file=>new{file.FullName,file.Length,Ticks=file.LastWriteTimeUtc.Ticks}).ToArray();
            var settings=new AppSettings{EnableDandan=true,EnableAnimeko=false,EnableBahamut=false,EnableExistingDanmu=false};var meter=new Meter();
            using(var api=new JellyfinApi(settings))using(var catalog=new DanmuCatalog(settings,api,meter)){
                string cache=Path.Combine(install,"data","dandan-cache");int imported=0;
                if(Directory.Exists(cache))foreach(string file in Directory.EnumerateFiles(cache,"*.json",SearchOption.AllDirectories))try{
                    var entry=Json.Read<ApiCacheEntry>(File.ReadAllText(file));
                    if(new[]{"hash","match","search"}.Contains(entry.Kind)&&CacheRetention.Expiry(entry.CreatedUtc,settings.CacheRetentionMonths)>DateTime.UtcNow){catalog.Cache.Write(entry.Key,entry.Kind,entry.Label,entry.Content,entry.Anime);imported++;}
                }catch{ }
                var result=await catalog.ManualSearch("百人女友",files,true,true,0,"dandan",CancellationToken.None);
                var candidates=result.Items.Cast<Dictionary<string,object>>().ToArray();
                if(candidates.Length==0)throw new InvalidOperationException("No candidate");
                var best=candidates[0];var episodes=(await catalog.Episodes(best)).Cast<Dictionary<string,object>>().ToArray();
                var plan=BatchMatching.Plan(files,episodes);int first=meter.Requests;
                await catalog.ManualSearch("百人女友",files,true,true,0,"dandan",CancellationToken.None);
                bool unchanged=snapshots.All(old=>{var file=new FileInfo(old.FullName);return file.Length==old.Length&&file.LastWriteTimeUtc.Ticks==old.Ticks;});
                bool complete=plan.Count==files.Length&&plan.All(row=>row.Selected&&row.Remote!=null)&&plan.Select(row=>Json.Text(row.Remote,"Id")).Distinct().Count()==files.Length;
                var report=new{passed=complete&&unchanged&&meter.Requests==first,anime=Json.Text(best,"Name"),priority=Json.Text(best,"Priority"),method=Json.Text(best,"MatchMethod"),result.Summary,files=files.Length,matched=plan.Count(row=>row.Selected),uniqueEpisodes=plan.Where(row=>row.Remote!=null).Select(row=>Json.Text(row.Remote,"Id")).Distinct().Count(),singleQuery=MediaNames.SingleSearchTitle(files[Math.Min(9,files.Length-1)]),importedCache=imported,networkRequests=first,hashRequests=meter.HashRequests,filenameRequests=meter.NameRequests,repeatRequests=meter.Requests-first,mediaUnchanged=unchanged};
                File.WriteAllText(Path.Combine(output,"result.json"),Json.Write(report));Console.WriteLine(Json.Write(report));return report.passed?0:1;
            }
        }finally{if(File.Exists(config))File.Delete(config);if(File.Exists(config+".tmp"))File.Delete(config+".tmp");}
    }
}
