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

class BridgeContract
{
    sealed class Fixture : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            Requests++; string body=request.RequestUri.AbsolutePath.Contains("/bangumi/")
                ? Json.Write(new {success=true,bangumi=new {episodes=Enumerable.Range(1,12).Select(i=>new{episodeId=100+i,episodeNumber=i,episodeTitle="第 "+i+" 集"}).ToArray()}})
                : "{\"success\":true,\"animes\":[{\"animeId\":10,\"animeTitle\":\"测试番剧\",\"typeDescription\":\"动漫\",\"episodeCount\":12}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});
        }
    }
    static readonly List<string> checks=new List<string>();
    static void Check(bool valid,string text){if(!valid)throw new Exception(text);checks.Add(text);}
    static object Call(ElectronBridge bridge,string name,object data)
    {
        var task=(Task<object>)typeof(ElectronBridge).GetMethod("Dispatch",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bridge,new object[]{name,Json.Object(Json.Write(data))});
        while(!task.IsCompleted){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(1);}
        return task.GetAwaiter().GetResult();
    }
    [STAThread] static int Main(string[] args)
    {
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);Paths.Root=Path.Combine(output,"fixture");Directory.CreateDirectory(Paths.Root);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try{
            var settings=new AppSettings{MediaFolder=Path.Combine(Paths.Root,"videos"),EnableDandan=true,EnableAnimeko=false,EnableBahamut=false,EnableExistingDanmu=false};
            DandanConfig.Ensure(settings);SettingsStore.AtomicWrite(DandanConfig.FilePath,Json.Write(new{AppId="contract-fixture",AppSecret="fixture-only"}),false);
            var fixture=new Fixture();using(var api=new JellyfinApi(settings))using(var catalog=new DanmuCatalog(settings,api,fixture))using(var core=new DesktopController(app,settings,false,catalog))using(var stream=new MemoryStream())using(var writer=new StreamWriter(stream)){
                string folder=Path.Combine(settings.MediaFolder,"测试番剧","[Studio][01-12][1080p]");Directory.CreateDirectory(folder);
                var files=Enumerable.Range(1,12).Select(i=>{string file=Path.Combine(folder,"[Studio] 测试番剧 S01E"+i.ToString("D2")+".mkv");File.WriteAllText(file,"synthetic fixture "+i);return new Dictionary<string,object>{{"Id","fixture"+i},{"Name","测试番剧 第"+i+"集"},{"Path",file},{"Type","Episode"},{"SeriesName","测试番剧"},{"ParentIndexNumber",1},{"IndexNumber",i}};}).ToArray();
                core.Library.Replace(files);var bridge=new ElectronBridge(app,core,writer);
                var root=Json.Read<object[]>(Json.Write(Call(bridge,"library",new{sort=0})));Check(root.Length==1,"目录首页只展示作品文件夹");
                var row=(Dictionary<string,object>)root[0];Check(Json.Array(row,"keys").Length==12,"文件夹关联子目录内完整12集");
                Call(bridge,"select",new{keys=Json.Array(row,"keys"),selected=true});Check(core.Library.SelectedItems.Length==12,"整文件夹选择传入业务核心");
                var sources=Json.Read<object[]>(Json.Write(Call(bridge,"search",new{keyword="测试番剧",animeOnly=true,smart=true,provider="dandan"})));Check(sources.Length==1,"新界面搜索桥接保留官方搜索结果");
                int requests=fixture.Requests;Call(bridge,"search",new{keyword="测试番剧",animeOnly=true,smart=true,provider="dandan"});Check(fixture.Requests==requests,"重复搜索采用本地缓存，无额外API请求");
                var episodes=Json.Read<object[]>(Json.Write(Call(bridge,"episodes",new{index=0})));Check(episodes.Length==12,"作品详情返回完整12集");
                Call(bridge,"plan",new{scope="selection"});Check(core.BatchPlan.Count==12&&core.BatchPlan.All(x=>x.Selected&&x.Remote!=null),"已选文件夹的整季计划默认勾选12集");
                Call(bridge,"planSelect",new{index=0,selected=false});Check(!core.BatchPlan[0].Selected,"预览取消勾选实际修改业务计划");
                Call(bridge,"planSelect",new{index=0,selected=true});Check(core.BatchPlan[0].Selected,"预览重新勾选实际修改业务计划");
                string stamp=String.Join("|",files.Select(f=>File.ReadAllText(Json.Text(f,"Path"))));
                Call(bridge,"cacheClear",new{expired=false});Check(String.Join("|",files.Select(f=>File.ReadAllText(Json.Text(f,"Path"))))==stamp,"缓存清理保留全部测试媒体文件");
            }
            File.WriteAllText(Path.Combine(output,"result.json"),Json.Write(new{passed=true,checks=checks}));Console.WriteLine(Json.Write(checks));return 0;
        }catch(Exception error){File.WriteAllText(Path.Combine(output,"result.json"),Json.Write(new{passed=false,error=error.ToString(),checks=checks}));Console.Error.WriteLine(error);return 1;}
        finally{app.Shutdown();}
    }
}
