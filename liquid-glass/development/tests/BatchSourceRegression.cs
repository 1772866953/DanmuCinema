using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DanmuCinema;
using DanmuCinema.Desktop;

class BatchSourceRegression
{
    sealed class Fixture:HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;string route=request.RequestUri.AbsolutePath;
            string body=route.EndsWith("/match")?Json.Write(new{success=true,isMatched=true,matches=new[]{new{animeId=700,episodeId=70001,animeTitle="赛博朋克：边缘行者 2",episodeTitle="第1话 内容",typeDescription="网络放送"}}}):route.Contains("/bangumi/")?Json.Write(new{success=true,bangumi=new{episodes=Enumerable.Range(1,13).Select(i=>new{episodeId=70000+i,episodeNumber=i,episodeTitle="第"+i+"话"}).ToArray()}}):null;
            if(body==null)throw new Exception("Unexpected fixture route");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});
        }
    }
    static List<string> checks=new List<string>();
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
    static T Wait<T>(Task<T> task){while(!task.IsCompleted){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(1);}return task.GetAwaiter().GetResult();}
    static object Call(ElectronBridge bridge,string method,object p){return Wait((Task<object>)typeof(ElectronBridge).GetMethod("Dispatch",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bridge,new object[]{method,Json.Object(Json.Write(p))}));}
    [STAThread]static int Main(string[] args)
    {
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);Paths.Root=Path.Combine(output,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Paths.Root);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try{
            var settings=new AppSettings{EnableDandan=true,EnableAnimeko=false,EnableBahamut=false,EnableExistingDanmu=false};DandanConfig.Ensure(settings);SettingsStore.AtomicWrite(DandanConfig.FilePath,Json.Write(new{AppId="batch-fixture",AppSecret="fixture-only"}),false);
            var folder=Path.Combine(Paths.Root,"赛博朋克S2");Directory.CreateDirectory(folder);
            var local=Enumerable.Range(1,10).Select(i=>{var path=Path.Combine(folder,"Cyberpunk.Edgerunners.S02E"+i.ToString("00")+".mkv");File.WriteAllText(path,"fixture "+i);return new Dictionary<string,object>{{"Id","local-"+i},{"Path",path},{"Name","统一作品名"},{"Type","Episode"},{"ParentIndexNumber",2},{"IndexNumber",i}};}).ToArray();
            var before=local.Select(x=>new FileInfo(Json.Text(x,"Path"))).Select(x=>new{x.FullName,x.Length,Ticks=x.LastWriteTimeUtc.Ticks}).ToArray();var handler=new Fixture();
            using(var api=new JellyfinApi(settings,handler))using(var catalog=new DanmuCatalog(settings,api,handler))using(var core=new DesktopController(app,settings,false,catalog))using(var writer=new StreamWriter(new MemoryStream())){
                var search=Wait(catalog.ManualSearch("赛博朋克",local,false,true,0,"dandan"));var candidate=(Dictionary<string,object>)search.Items[0];string id=Json.Text(candidate,"Id");
                Check(Json.Text(candidate,"Category")=="网络放送"&&!DanmuCatalog.IsSeries(candidate),"复现哈希成功但来源仅有网络放送、缺少总集数的元数据");
                var eps=Wait(catalog.Episodes(catalog.KnownAnime(id))).Cast<Dictionary<string,object>>().ToArray();
                Check(eps.Length==13&&DanmuCatalog.IsSeries(candidate,eps),"完整13集可确认未知类型为剧集来源");
                Check(Json.Text(catalog.KnownAnime(id),"EpisodeSize")=="13"&&DanmuCatalog.IsSeries(catalog.KnownAnime(id)),"读取完整剧集后保存实际集数与已核验状态");
                int calls=handler.Calls;Wait(catalog.ManualSearch("赛博朋克",local,false,true,0,"dandan"));Check(handler.Calls==calls&&Json.Text(catalog.KnownAnime(id),"EpisodeSize")=="13","重复匹配复用缓存且旧搜索摘要不会清空已核验集数");
                core.Library.Replace(local);var bridge=new ElectronBridge(app,core,writer);Call(bridge,"select",new{keys=core.Library.Entries.Select(x=>x.Key).ToArray(),selected=true});Call(bridge,"episodes",new{id});Call(bridge,"plan",new{id,scope="selection"});
                Check(core.BatchPlan.Count==10&&core.BatchPlan.All(x=>x.Selected&&x.Remote!=null)&&core.BatchPlan.Select(x=>x.Number).SequenceEqual(Enumerable.Range(1,10)),"同目录10集可针对13集远端来源生成逐集预览，不再误报电影");
                Check(handler.Calls==calls,"预览与重新读取集数均复用官方API缓存，无额外网络请求");
                Check(!DanmuCatalog.IsSeries(new Dictionary<string,object>{{"Category","电影"}},eps),"明确电影仍不能因本地同目录或多个条目而强行作为整季");
                Check(!DanmuCatalog.IsSeries(new Dictionary<string,object>{{"Category","网络放送"}},eps.Take(1)),"未知类型的单条弹幕仍不能批量套给整季");
                var duplicate=eps.Select(x=>new Dictionary<string,object>{{"Id","same"},{"Number",x["Number"]}}).ToArray();Check(!DanmuCatalog.IsSeries(new Dictionary<string,object>(),duplicate),"重复弹幕ID不会被当成完整多集来源");
            }
            Check(before.All(x=>new FileInfo(x.FullName).Length==x.Length&&File.GetLastWriteTimeUtc(x.FullName).Ticks==x.Ticks),"匹配与预览不修改视频文件");
            File.WriteAllText(Path.Combine(output,"result.json"),Json.Write(new{passed=true,checks}));Console.WriteLine(Json.Write(new{passed=true,cases=checks.Count}));return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{app.Shutdown();}
    }
}
