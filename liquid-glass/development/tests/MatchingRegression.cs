using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DanmuCinema;
using DanmuCinema.Desktop;

class MatchingRegression
{
    sealed class Fixture : HttpMessageHandler
    {
        public int Requests;
        public readonly List<string> Queries = new List<string>();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            Requests++; string path=request.RequestUri.AbsolutePath, body="{}";
            if(request.RequestUri.Host=="api.bgm.tv") {
                Check(!request.Headers.Contains("X-AppId"),"别名服务不接收官方接口凭据");
                body=path.Contains("/search/") ? Json.Write(new{data=new[]{new{id=42,name="サイバーパンク エッジランナーズ",name_cn="赛博朋克 边缘行者"}}})
                    : Json.Write(new{id=42,name="サイバーパンク エッジランナーズ",name_cn="赛博朋克 边缘行者",infobox=new[]{new{key="别名",value=new[]{new{v="Cyberpunk: Edgerunners"}}}}});
            } else if(path.Contains("/bangumi/") || path.EndsWith("/episodes")) {
                int count=path.Contains("/100")||path.Contains("/film/")?1:20;
                var rows=Enumerable.Range(1,count).Select(i=>new{episodeId=1000+i,episodeNumber=i,episodeTitle="第 "+i+" 集",Number=i,Title="第 "+i+" 集",CommentId=1000+i}).ToArray();
                body=path.EndsWith("/episodes")?Json.Write(rows):Json.Write(new{success=true,bangumi=new{episodes=rows}});
            } else if(path.Contains("/search")) {
                string query=System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["keyword"]??"";Queries.Add(query);
                if(query.Contains("赛博朋克")) body=Json.Write(new{success=true,animes=new[]{new{animeId=300,animeTitle="赛博朋克 边缘行者",typeDescription="TV动画",episodeCount=10}}});
                else if(query.Contains("Cyberpunk")) body="{\"success\":true,\"animes\":[]}";
                else if(request.RequestUri.Host=="api.dandanplay.net") body="{\"success\":true,\"animes\":[]}";
                else body=Json.Write(new[]{
                    new{Id="film",Name="爱情公寓",Category="电影",EpisodeSize=1,SiteId="mock"},
                    new{Id="season2",Name="爱情公寓 第二季",Category="电视剧",EpisodeSize=20,SiteId="mock"},
                    new{Id="season1",Name="爱情公寓 第一季",Category="电视剧",EpisodeSize=20,SiteId="mock"}});
            } else if(path.EndsWith("/match")) {
                string sent=await request.Content.ReadAsStringAsync();
                if(sent.Contains("Seija Musou")) body=sent.Contains("hashOnly")?"{\"success\":true,\"isMatched\":false,\"matches\":[]}":Json.Write(new{success=true,isMatched=false,matches=new[]{
                    new{animeId=5922,episodeId=59220001,animeTitle="恋姬无双",episodeTitle="第1话 其他作品",typeDescription="TV动画"},
                    new{animeId=17662,episodeId=176620001,animeTitle="圣者无双～上班族的异世界生存之道～",episodeTitle="第1话 治愈士公会",typeDescription="TV动画"}}});
                else if(sent.Contains("爱情公寓")) body="{\"success\":true,\"isMatched\":false,\"matches\":[]}";
                else {
                    Check(sent.Contains("hashOnly"),"hash 命中时优先 hashOnly 且不触发文件名回退");
                    Check(Json.Text(Json.Object(sent),"fileHash")=="900150983cd24fb0d6963f7d28e17f72","文件 hash 使用真实内容 MD5");
                    body=Json.Write(new{success=true,isMatched=true,matches=new[]{new{animeId=500,episodeId=5001,animeTitle="测试动画",episodeTitle="第 1 话",typeDescription="TV动画"}}});
                }
            } else if(path=="/Library/VirtualFolders" && request.Method==HttpMethod.Get) body=Json.Write(new[]{new{Name="我的影片",Locations=new[]{"C:\\existing-media"}}});
            else if(path=="/Library/VirtualFolders") {Check(request.RequestUri.Query.Contains("name="),"新增媒体库携带实际唯一名称");body="";}
            else throw new Exception("Unexpected mock route: "+path);
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)};
        }
    }
    static readonly List<string> checks=new List<string>();
    static void Check(bool valid,string text){if(!valid)throw new Exception(text);lock(checks)checks.Add(text);}
    static object Call(ElectronBridge bridge,string name,object data)
    {
        var task=(Task<object>)typeof(ElectronBridge).GetMethod("Dispatch",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bridge,new object[]{name,Json.Object(Json.Write(data))});
        while(!task.IsCompleted){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(1);}
        return task.GetAwaiter().GetResult();
    }
    static T Wait<T>(Task<T> task) { while(!task.IsCompleted){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(1);}return task.GetAwaiter().GetResult(); }
    [STAThread] static int Main(string[] args)
    {
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);Paths.Root=Path.Combine(output,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Paths.Root);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try {
            Check(SmartMatching.CleanTitle("2026.10 Cyberpunk: Edgerunners 2")=="Cyberpunk: Edgerunners","英文查询清除日期与发布序号");
            Check(SmartMatching.CleanTitle("[Moozzi2]  圣者无双～上班族的异世界生存之道～ (BD 1920x1080 x264-10Bit Flac)")=="圣者无双～上班族的异世界生存之道～","作品目录的蓝光规格不污染文件名回退匹配");
            Check(SmartMatching.SeasonTitle("爱情公寓1")==1&&SmartMatching.SeasonTitle("爱情公寓2")==2,"中文紧凑季度名称保留季号");
            Check(SmartMatching.Queries("爱情公寓2").Contains("爱情公寓"),"紧凑季度支持基础作品名回退");
            var settings=new AppSettings{MediaFolder=Path.Combine(Paths.Root,"videos"),EnableDandan=true,EnableAnimeko=false,EnableBahamut=false,EnableExistingDanmu=true,EncryptedToken=SettingsStore.Protect("fixture-token")};
            DandanConfig.Ensure(settings);SettingsStore.AtomicWrite(DandanConfig.FilePath,Json.Write(new{AppId="regression-fixture",AppSecret="fixture-only"}),false);
            var fixture=new Fixture();using(var api=new JellyfinApi(settings,fixture))using(var catalog=new DanmuCatalog(settings,api,fixture))using(var core=new DesktopController(app,settings,false,catalog))using(var writer=new StreamWriter(new MemoryStream())) {
                string folder=Path.Combine(settings.MediaFolder,"爱情公寓1");Directory.CreateDirectory(folder);
                var files=Enumerable.Range(1,20).Select(i=>{string file=Path.Combine(folder,"爱情公寓 ("+i+").mp4");File.WriteAllText(file,"synthetic "+i);return new Dictionary<string,object>{{"Id","fixture"+i},{"Name","爱情公寓"},{"Path",file},{"Type","Episode"},{"IndexNumber",1}};}).ToArray();
                core.Library.Replace(files);var bridge=new ElectronBridge(app,core,writer);
                Call(bridge,"select",new{keys=core.Library.Entries.Select(x=>x.Key).ToArray(),selected=true});
                foreach(int season in new[]{1,2}) {
                    if(season==2)foreach(var file in files)file["ParentIndexNumber"]=2;
                    var found=Json.Read<object[]>(Json.Write(Call(bridge,"search",new{keyword="爱情公寓"+season,animeOnly=false,smart=true}))).Cast<Dictionary<string,object>>().ToArray();
                    Check(Json.Text(found[0],"name")=="爱情公寓 第"+(season==1?"一":"二")+"季","整季选择优先第 "+season+" 季电视剧而非电影");
                    Check(Json.Text(found[0],"category")=="电视剧"&&Json.Text(found[0],"count")=="20","候选向界面传递来源类型及集数");
                    Call(bridge,"episodes",new{id=Json.Text(found[0],"id")});Call(bridge,"plan",new{scope="selection",id=Json.Text(found[0],"id")});
                    Check(core.BatchPlan.Count==20&&core.BatchPlan.All(x=>x.Selected&&SmartMatching.RemoteNumber(x.Remote)==x.Number),"第 "+season+" 季20集按本地编号逐一匹配");
                    var movie=found.First(x=>Json.Text(x,"category")=="电影");Call(bridge,"episodes",new{id=Json.Text(movie,"id")});bool blocked=false;
                    try{Call(bridge,"plan",new{scope="selection",id=Json.Text(movie,"id")});}catch(InvalidOperationException e){blocked=e.Message.Contains("电影");}
                    Check(blocked,"同名电影不能静默生成仅第一集的整季计划");
                    // A restored frontend draft uses stable IDs, even after a different search.
                    Call(bridge,"plan",new{scope="selection",id=Json.Text(found[0],"id")});Check(core.BatchPlan.Count(x=>x.Selected)==20,"恢复弹窗草稿时按来源 ID 重载完整季度");
                }
                var english=Wait(catalog.Search("2026.10 Cyberpunk: Edgerunners 2",true,true,0,"dandan"));
                Check(english.Items.Length==1&&Json.Text((Dictionary<string,object>)english.Items[0],"Name").Contains("赛博朋克"),"英文搜索通过可信别名找到中文动画");
                int requests=fixture.Requests;Wait(catalog.Search("2026.10 Cyberpunk: Edgerunners 2",true,true,0,"dandan"));Check(requests==fixture.Requests,"重复英文搜索复用别名与官方响应缓存，无新请求");
                Check(!fixture.Queries.Any(x=>x.StartsWith("2026.10")),"日期前缀不进入弹幕搜索接口");
                string hashFile=Path.Combine(folder,"hash-fixture.mkv");File.WriteAllText(hashFile,"abc");
                var video=new Dictionary<string,object>{{"Path",hashFile},{"Name","测试动画"},{"Type","Episode"}};
                var identified=Wait(catalog.IdentifyFile(video,CancellationToken.None));Check(identified.HashMatched&&identified.Recommended!=null&&Json.Text(identified.Recommended,"CommentId")=="5001","新核心文件识别保留官方 hash 精确识别与 episodeId");
                requests=fixture.Requests;Wait(catalog.IdentifyFile(video,CancellationToken.None));Check(requests==fixture.Requests,"第二次文件识别从本地缓存读取");
                core.Library.Replace(new[]{video});Call(bridge,"select",new{keys=core.Library.Entries.Select(x=>x.Key).ToArray(),selected=true});
                var rpcMatch=Json.Object(Json.Write(Call(bridge,"identify",new{})));Check(Json.Text(rpcMatch,"status").Contains("hash 精确识别成功")&&Json.Array(rpcMatch,"sources").Length==1,"新架构界面 RPC 正确调用 hash 识别核心");
                string release=Path.Combine(settings.MediaFolder,"[Moozzi2]  圣者无双～上班族的异世界生存之道～ (BD 1920x1080 x264-10Bit Flac)");Directory.CreateDirectory(release);
                string fallbackFile=Path.Combine(release,"Seija Musou (1).mkv");File.WriteAllText(fallbackFile,"synthetic release");
                var fallback=Wait(catalog.IdentifyFile(new Dictionary<string,object>{{"Path",fallbackFile},{"Type","Episode"}},CancellationToken.None));
                Check(!fallback.HashMatched&&fallback.Recommended!=null&&Json.Text(fallback.Recommended,"CommentId")=="176620001","hash 未收录且英文文件名有歧义时，中文作品目录和集号确认正确候选");
                string added=Wait(api.AddLibrary(folder,"我的影片","mixed"));Check(added=="爱情公寓1","默认同名媒体库自动采用文件夹名称");
                Check(JellyfinApi.UniqueLibraryName(folder,"自定义",new[]{"自定义","自定义 (2)"})=="自定义 (3)","自定义媒体库重名时生成唯一后缀");
                Check(files.All(f=>File.ReadAllText(Json.Text(f,"Path")).StartsWith("synthetic ")),"所有合成媒体内容保持不变");
            }
            File.WriteAllText(Path.Combine(output,"result.json"),Json.Write(new{passed=true,checks=checks}));Console.WriteLine(Json.Write(checks));return 0;
        } catch(Exception error){File.WriteAllText(Path.Combine(output,"result.json"),Json.Write(new{passed=false,error=error.ToString(),checks=checks}));Console.Error.WriteLine(error);return 1;}
        finally{app.Shutdown();}
    }
}
