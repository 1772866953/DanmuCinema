using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    public sealed partial class DanmuCatalog
    {
        readonly Dictionary<string, DateTime> classificationRetry = new Dictionary<string, DateTime>();
        DateTime lastClassificationRequest=DateTime.MinValue;
        static string ClassificationTitle(Dictionary<string, object> item, string mediaRoot)
        {
            string path=Json.Text(item,"Path"), folder=MediaNames.WorkFolder(path), name=Path.GetFileName(folder);
            bool root=String.Equals((folder??"").TrimEnd('\\'),(mediaRoot??"").TrimEnd('\\'),StringComparison.OrdinalIgnoreCase);
            if(!root && !String.IsNullOrWhiteSpace(name) && !Regex.IsMatch(name,@"^(?:电影|电视剧|动漫|动画|视频|媒体|Movies|TV|Anime|Videos|Media)$",RegexOptions.IgnoreCase)) {
                string clean=SmartMatching.CleanTitle(name);
                if(clean.Length>=2)return clean;
            }
            string title=Json.Text(item,"SeriesName");
            if(String.IsNullOrWhiteSpace(title))title=Path.GetFileNameWithoutExtension(path);
            title=Regex.Replace(title??"",@"(?:^|[\s._-])EP?\s*\d{1,3}(?!\d)|第\s*\d{1,3}\s*[集话話]"," ",RegexOptions.IgnoreCase);
            return SmartMatching.CleanTitle(title);
        }
        static string ClassificationIdentity(Dictionary<string, object> item, string mediaRoot)
        {
            string title=ClassificationTitle(item,mediaRoot), folder=MediaNames.WorkFolder(Json.Text(item,"Path"));
            // Standalone movies sharing a directory must never share a classification.
            string location=SmartMatching.IsStandaloneMovie(item)?Json.Text(item,"Path"):folder;
            return (location??"").ToUpperInvariant()+"|"+title.ToUpperInvariant()+"|"+SmartMatching.Season(item);
        }
        bool CachedAnimeHash(Dictionary<string,object> item, DandanConfig credentials)
        {
            try {
                var file=new FileInfo(Json.Text(item,"Path"));if(!file.Exists)return false;
                string feature=Cache.Read(DandanApiCache.Key("hash16m|"+file.FullName.ToUpperInvariant()+"|"+file.Length+"|"+file.LastWriteTimeUtc.Ticks));
                if(feature==null)return false;
                var hash=Json.Read<VideoFeature>(feature);
                if(hash.Length!=file.Length || hash.WriteTicks!=file.LastWriteTimeUtc.Ticks)return false;
                string body=Json.Write(new{fileHash=hash.Hash,fileSize=file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),matchMode="hashOnly"});
                string response=Cache.Read(DandanApiCache.Key(credentials.AppId.Trim()+"|/api/v2/match|"+body));if(response==null)return false;
                var result=Json.Object(response);var rows=Json.Array(result,"Matches").OfType<Dictionary<string,object>>().ToArray();
                return Json.Text(result,"IsMatched")=="True" && rows.Length==1 &&
                    IsAnime(new Dictionary<string,object>{{"Provider","dandan"},{"Category",First(rows[0],"TypeDescription","Type")}});
            } catch {return false;}
        }
        /// <summary>Classify a work once, without changing server item types or media files.</summary>
        public async Task ClassifyLibrary(Dictionary<string,object>[] items, string mediaRoot, bool rescan,
            Action<LibraryLoadProgress> progress, CancellationToken cancellation)
        {
            var groups=items.GroupBy(x=>ClassificationIdentity(x,mediaRoot),StringComparer.OrdinalIgnoreCase).ToArray();
            DandanConfig credentials=null;
            try{credentials=DandanConfig.Load();}catch{}
            bool online=settings.EnableDandan && credentials!=null && credentials.Ready;
            int done=0;
            foreach(var group in groups) {
                cancellation.ThrowIfCancellationRequested();
                var seed=group.OrderBy(x=>SmartMatching.Episode(x)==0?Int32.MaxValue:SmartMatching.Episode(x)).First();
                string title=ClassificationTitle(seed,mediaRoot),key=DandanApiCache.Key("media-classification-v1|"+group.Key);
                string cached=Cache.Read(key);bool? anime=null;bool storedAnime=false;
                if(cached!=null)try{var record=Json.Object(cached);storedAnime=Json.Text(record,"Anime")=="True";if(storedAnime)anime=true;else if(!rescan)anime=false;}catch{}
                if(anime==null && online && CachedAnimeHash(seed,credentials))anime=true;
                DateTime retry;bool cooling=classificationRetry.TryGetValue(key,out retry)&&retry>DateTime.UtcNow&&!rescan;
                if(anime==null && online && !cooling && title.Length>=2) {
                    if(progress!=null)progress(new LibraryLoadProgress("正在识别动漫分类 "+(done+1)+" / "+groups.Length+" · "+title,100.0*done/Math.Max(1,groups.Length)));
                    try {
                        double wait=Math.Max(0,Math.Max(1,settings.DownloadIntervalSeconds)*1000-(DateTime.UtcNow-lastClassificationRequest).TotalMilliseconds);
                        if(wait>0)await Task.Delay((int)wait,cancellation).ConfigureAwait(false);
                        lastClassificationRequest=DateTime.UtcNow;
                        int season=SmartMatching.Season(seed);
                        var result=await Search(title,false,true,season,"dandan",cancellation).ConfigureAwait(false);
                        if(result.Sources.OfType<Dictionary<string,object>>().Any(x=>Json.Text(x,"Error")!=""))throw new IOException("分类接口暂不可用");
                        // Merely returning a fuzzy or namesake film is not evidence
                        // of animation. Accept a relevant official anime entry.
                        var relevant=result.Items.OfType<Dictionary<string,object>>().Where(x=>SmartMatching.Score(title,x,season)>=90).ToArray();
                        anime=relevant.Any(IsAnime);
                        Cache.Write(key,"classification",title,Json.Write(new{Anime=anime.Value}),title);
                        classificationRetry.Remove(key);
                    } catch {cancellation.ThrowIfCancellationRequested();classificationRetry[key]=DateTime.UtcNow.AddMinutes(10);}
                }
                if(anime==true) {
                    foreach(var item in group){item["DanmuCinemaCategory"]="动漫";item["DanmuCinemaClassificationKnown"]=true;}
                    if(!storedAnime)Cache.Write(key,"classification",title,Json.Write(new{Anime=true}),title);
                } else if(anime==false)foreach(var item in group){item.Remove("DanmuCinemaCategory");item["DanmuCinemaClassificationKnown"]=true;}
                else foreach(var item in group)item["DanmuCinemaClassificationKnown"]=false;
                done++;
                if(progress!=null)progress(new LibraryLoadProgress("正在识别动漫分类 "+done+" / "+groups.Length,100.0*done/Math.Max(1,groups.Length)));
            }
        }
    }
}
