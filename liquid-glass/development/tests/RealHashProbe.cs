using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DanmuCinema;

// Explicit opt-in verifier: reads one user-selected video, never writes beside it.
class RealHashProbe
{
    sealed class Meter : DelegatingHandler
    {
        public int Requests, HashRequests, NameRequests;
        public Meter():base(new HttpClientHandler{AllowAutoRedirect=false}){}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            Requests++;
            if(request.Content!=null){string body=await request.Content.ReadAsStringAsync();if(body.Contains("hashOnly"))HashRequests++;if(body.Contains("fileNameOnly"))NameRequests++;}
            return await base.SendAsync(request,cancellation).ConfigureAwait(false);
        }
    }
    static int Main(string[] args) {try{return Run(args).GetAwaiter().GetResult();}catch(Exception e){Console.Error.WriteLine("Probe failed: "+e.GetType().Name);return 1;}}
    static async Task<int> Run(string[] args)
    {
        string output=Path.GetFullPath(args[0]),install=Path.GetFullPath(args[1]),video=Path.GetFullPath(args[2]);
        Directory.CreateDirectory(output);Paths.Root=install;var credentials=DandanConfig.Load();
        if(!credentials.Ready)throw new Exception("Installed official API config is not ready.");
        Paths.Root=Path.Combine(output,"isolated-data");Directory.CreateDirectory(Path.Combine(Paths.Root,"config"));
        string config=DandanConfig.FilePath;
        try {
            SettingsStore.AtomicWrite(config,Json.Write(new{AppId=credentials.AppId,AppSecret=credentials.Secret}),false);
            var before=new FileInfo(video);long length=before.Length,ticks=before.LastWriteTimeUtc.Ticks;
            var settings=new AppSettings{EnableDandan=true,EnableAnimeko=false,EnableBahamut=false,EnableExistingDanmu=false};
            var meter=new Meter();using(var api=new JellyfinApi(settings))using(var catalog=new DanmuCatalog(settings,api,meter)) {
                var feature=await catalog.Feature(video,CancellationToken.None);
                string independent;
                using(var stream=File.OpenRead(video))using(var md5=MD5.Create()) {byte[] bytes=new byte[(int)Math.Min(stream.Length,16L*1024*1024)];int at=0;while(at<bytes.Length){int read=stream.Read(bytes,at,bytes.Length-at);if(read==0)throw new EndOfStreamException();at+=read;}independent=BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
                if(feature.Hash!=independent)throw new Exception("Hash mismatch.");
                string bodyKey=Json.Write(new{fileHash=feature.Hash,fileSize=feature.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),matchMode="hashOnly"});
                string key=DandanApiCache.Key(credentials.AppId.Trim()+"|/api/v2/match|"+bodyKey);
                string existing=Path.Combine(install,"data","dandan-cache");bool imported=false;
                if(Directory.Exists(existing))foreach(var file in Directory.GetFiles(existing,key+".json",SearchOption.AllDirectories)) {
                    var entry=Json.Read<ApiCacheEntry>(File.ReadAllText(file));
                    if(entry.Key==key&&CacheRetention.Expiry(entry.CreatedUtc,settings.CacheRetentionMonths)>DateTime.UtcNow&&Json.Array(Json.Object(entry.Content),"Matches").Length>0){catalog.Cache.Write(key,"match","实文件验证",entry.Content);imported=true;break;}
                }
                var item=new Dictionary<string,object>{{"Path",video},{"Name",Path.GetFileNameWithoutExtension(video)},{"Type","Episode"}};
                var match=await catalog.IdentifyFile(item,CancellationToken.None);
                int first=meter.Requests;
                // Empty hash results intentionally aren't cached. Recheck only a
                // positive hash match; a known miss should not spend quota twice.
                if(match.HashMatched)await catalog.IdentifyFile(item,CancellationToken.None);
                before.Refresh();bool unchanged=before.Length==length&&before.LastWriteTimeUtc.Ticks==ticks;
                var result=new{passed=match.Recommended!=null&&unchanged&&meter.Requests==first,hashCorrect=feature.Hash==independent,sampledBytes=Math.Min(length,16L*1024*1024),hashMatched=match.HashMatched,method=match.Method,anime=match.Recommended==null?"":Json.Text(match.Recommended,"AnimeTitle"),episode=match.Recommended==null?"":Json.Text(match.Recommended,"Title"),episodeId=match.Recommended==null?"":Json.Text(match.Recommended,"CommentId"),match.Status,importedCache=imported,networkRequests=first,hashRequests=meter.HashRequests,filenameRequests=meter.NameRequests,repeatRequests=meter.Requests-first,mediaUnchanged=unchanged};
                File.WriteAllText(Path.Combine(output,"result.json"),Json.Write(result));Console.WriteLine(Json.Write(result));return result.passed?0:1;
            }
        } finally {if(File.Exists(config))File.Delete(config);}
    }
}
