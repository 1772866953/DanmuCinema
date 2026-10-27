using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DanmuCinema;

class ClassificationRegression
{
    sealed class Fixture:HttpMessageHandler
    {
        public int Requests;
        public int SyncRows, AnimeRows;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            if(request.RequestUri.AbsolutePath=="/DanmuCinema/Classification") {
                var syncRows=Json.Array(Json.Object(request.Content.ReadAsStringAsync().GetAwaiter().GetResult()),"Items").OfType<Dictionary<string,object>>().ToArray();
                SyncRows+=syncRows.Length;AnimeRows+=syncRows.Count(x=>Json.Text(x,"Anime")=="True");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"Updated\":0,\"Skipped\":0}")});
            }
            Requests++;string query=System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["keyword"]??"";
            if(query.Contains("网络异常"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest){Content=new StringContent("{}")});
            object[] rows=query.Contains("星海旅行")?new object[]{new{animeId=12,animeTitle="星海旅行 第一季",typeDescription="TV动画",episodeCount=12}}:
                query.Contains("爱情公寓")?new object[]{new{animeId=20,animeTitle="爱情公寓 第一季",typeDescription="电视剧",episodeCount=20},new{animeId=21,animeTitle="爱情魔法",typeDescription="TV动画",episodeCount=12}}:
                query.Contains("合成电影")?new object[]{new{animeId=30,animeTitle="合成电影",typeDescription="电影",episodeCount=1}}:new object[0];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Json.Write(new{success=true,animes=rows}))});
        }
    }
    static readonly List<string> checks=new List<string>();
    static void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
    static Dictionary<string,object>[] Videos(string root,string name,int count,bool movie=false)
    {
        string folder=Path.Combine(root,name);Directory.CreateDirectory(folder);
        return Enumerable.Range(1,count).Select(i=>{string file=Path.Combine(folder,movie?name+".mp4":name+" ("+i+").mkv");File.WriteAllText(file,"synthetic "+i);return new Dictionary<string,object>{{"Id",Guid.NewGuid().ToString("N")},{"Path",file},{"Name",name},{"Type",movie?"Movie":"Episode"},{"SeriesName",movie?"":name},{"IndexNumber",i}};}).ToArray();
    }
    static int Main(string[] args){try{return Run(args).GetAwaiter().GetResult();}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    static async Task<int> Run(string[] args)
    {
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);Paths.Root=Path.Combine(output,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Paths.Root);
        var settings=new AppSettings{MediaFolder=Path.Combine(Paths.Root,"videos"),EnableDandan=true,EnableExistingDanmu=false,EnableAnimeko=false,EnableBahamut=false,DownloadIntervalSeconds=1,EncryptedToken=SettingsStore.Protect("fixture-token")};
        DandanConfig.Ensure(settings);SettingsStore.AtomicWrite(DandanConfig.FilePath,Json.Write(new{AppId="classification-fixture",AppSecret="fixture-only"}),false);
        var fixture=new Fixture();using(var api=new JellyfinApi(settings,fixture))using(var catalog=new DanmuCatalog(settings,api,fixture)) {
            var anime=Videos(settings.MediaFolder,"星海旅行 第一季",12);var tv=Videos(settings.MediaFolder,"爱情公寓1",20);var film=Videos(settings.MediaFolder,"合成电影",1,true);
            var items=anime.Concat(tv).Concat(film).ToArray();var before=items.ToDictionary(x=>Json.Text(x,"Path"),x=>File.ReadAllText(Json.Text(x,"Path")));var progress=new List<LibraryLoadProgress>();
            await catalog.ClassifyLibrary(items,settings.MediaFolder,false,progress.Add,CancellationToken.None);
            Check(anime.All(x=>MediaPresentation.Category(x)=="动漫"),"官方动漫结果应用到完整12集");
            Check(tv.All(x=>MediaPresentation.Category(x)=="电视剧"),"爱情公寓保持电视剧，同名模糊动漫候选不误分类");
            Check(MediaPresentation.Category(film[0])=="电影","非动漫的独立电影保持电影");
            Check(DanmuCatalog.IsAnime(new Dictionary<string,object>{{"Provider","dandan"},{"Category","TV特别放送"}}),"官方动画特别放送类型同样可识别为动漫");
            Check(items.All(x=>Json.Text(x,"Type")== (anime.Contains(x)||tv.Contains(x)?"Episode":"Movie")),"分类标签不修改底层视频类型或季集结构");
            Check(fixture.Requests<=5,"按作品归组查询，33个视频无需逐集请求");
            int first=fixture.Requests;await catalog.ClassifyLibrary(items,settings.MediaFolder,false,null,CancellationToken.None);
            Check(fixture.Requests==first,"重复读取完全采用分类缓存");
            var cache=catalog.Cache.Entries().Where(x=>x.Kind=="classification").ToArray();Check(cache.Length==3&&cache.All(x=>x.TypeLabel=="媒体分类"),"分类结果按作品保存并可通过缓存管理识别");
            Check(progress.Last().Percent==100&&progress.Last().Message.Contains("3 / 3"),"分类进度覆盖全部作品并到达100%");
            var library=new MediaLibrary();library.Replace(items);var folders=library.Browse(null,"",LibrarySort.Type,false,settings.MediaFolder);
            Check(folders.Count(x=>MediaPresentation.Category(x)=="动漫")==1&&folders.Count(x=>MediaPresentation.Category(x)=="电视剧")==1,"文件夹层面同样显示动漫与电视剧分类");
            settings.EnableDandan=false;await catalog.ClassifyLibrary(items,settings.MediaFolder,false,null,CancellationToken.None);Check(fixture.Requests==first&&anime.All(x=>MediaPresentation.Category(x)=="动漫"),"接口停用时仍可离线显示已缓存的动漫分类");settings.EnableDandan=true;
            var failed=Videos(settings.MediaFolder,"网络异常",2);await catalog.ClassifyLibrary(failed,settings.MediaFolder,false,null,CancellationToken.None);first=fixture.Requests;
            Check(!catalog.Cache.Entries().Any(x=>x.Kind=="classification"&&x.Anime=="网络异常"),"接口异常不写入非动漫分类缓存");
            await catalog.ClassifyLibrary(failed,settings.MediaFolder,false,null,CancellationToken.None);Check(fixture.Requests==first,"后台刷新不会连续重试异常接口");
            await catalog.ClassifyLibrary(failed,settings.MediaFolder,true,null,CancellationToken.None);Check(fixture.Requests>first,"主动重新扫描可重试异常分类");
            await api.SyncClassification(items.Concat(failed).ToArray(),CancellationToken.None);Check(fixture.SyncRows==33&&fixture.AnimeRows==12,"同步客户端时仅包含已确认的分类，接口异常作品不被错误覆盖");
            Check(items.All(x=>File.ReadAllText(Json.Text(x,"Path"))==before[Json.Text(x,"Path")]),"分类扫描不修改媒体文件内容");
            Check(items.All(x=>Directory.GetFiles(Path.GetDirectoryName(Json.Text(x,"Path"))).All(f=>f.EndsWith(".mkv")||f.EndsWith(".mp4"))),"分类扫描不在媒体目录写入配置或说明文件");
        }
        File.WriteAllText(Path.Combine(output,"result.json"),Json.Write(new{passed=true,checks=checks}));Console.WriteLine(Json.Write(checks));return 0;
    }
}
