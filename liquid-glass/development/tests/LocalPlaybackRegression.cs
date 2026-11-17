using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DanmuCinema;
using DanmuCinema.Desktop;
class LocalPlaybackRegression
{
    sealed class Offline : HttpMessageHandler {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Requests++;throw new Exception("Real APIs are forbidden in this test.");}
    }
    static object Call(ElectronBridge bridge,string method,object value){var task=(Task<object>)typeof(ElectronBridge).GetMethod("Dispatch",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bridge,new[]{(object)method,Json.Object(Json.Write(value))});while(!task.IsCompleted){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}return task.GetAwaiter().GetResult();}
    static readonly List<string> checks=new List<string>();
    static void Check(bool valid,string label){if(!valid)throw new Exception(label);checks.Add(label);}
    [STAThread] static int Main(string[] args){
        string output=Path.GetFullPath(args[0]);Paths.Root=Path.Combine(output,"fixture");Directory.CreateDirectory(Paths.Root);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try{var settings=new AppSettings{MediaFolder=Path.Combine(Paths.Root,"media"),EnableDandan=false,EnableExistingDanmu=false,EnableAnimeko=false,EnableBahamut=false};Directory.CreateDirectory(settings.MediaFolder);var handler=new Offline();
            using(var api=new JellyfinApi(settings))using(var catalog=new DanmuCatalog(settings,api,handler))using(var core=new DesktopController(app,settings,false,catalog))using(var memory=new MemoryStream())using(var writer=new StreamWriter(memory)){
                var files=Enumerable.Range(1,3).Select(i=>{string video=Path.Combine(settings.MediaFolder,"第 "+i+" 集.mkv");File.WriteAllText(video,"read-only fixture "+i);return new Dictionary<string,object>{{"Id","local"+i},{"Path",video},{"Name","第 "+i+" 集"},{"Type","Episode"},{"IndexNumber",i},{"SeriesName","本机测试"}};}).ToArray();core.Library.Replace(files);var bridge=new ElectronBridge(app,core,writer);var keys=core.Library.Entries.Select(x=>x.Key).ToArray();
                var rows=Json.Read<object[]>(Json.Write(Call(bridge,"playbackResolve",new{keys=keys.Reverse().ToArray()})));Check(rows.Length==3&&Json.Text((Dictionary<string,object>)rows[0],"key")==keys[2],"播放解析保留调用方提供的剧集顺序");Check(rows.All(x=>File.Exists(Json.Text((Dictionary<string,object>)x,"path"))),"只返回已存在的本地媒体文件");
                var natural=Json.Read<object[]>(Json.Write(Call(bridge,"playbackResolve",new{keys=keys.Reverse().ToArray(),natural=true})));Check(Json.Text((Dictionary<string,object>)natural[0],"key")==keys[0],"文件夹播放按自然名称从首集开始，避免误从后面的集数起播");
                core.Library.Selection.Set(keys[1],true);var selected=Json.Read<object[]>(Json.Write(Call(bridge,"playbackResolve",new{keys=new string[0]})));Check(selected.Length==1&&Json.Text((Dictionary<string,object>)selected[0],"key")==keys[1],"播放所选使用实际选择集，不受当前文件夹限制");
                bool invalid=false;try{Call(bridge,"playbackResolve",new{keys=new[]{"unknown-key"}});}catch(InvalidOperationException){invalid=true;}Check(invalid,"未知或已移除的影片不能绕过媒体库校验");
                string file=Json.Text(files[1],"Path"),before=File.ReadAllText(file),xml=Path.ChangeExtension(file,".xml");File.WriteAllText(xml,"<i><d p=\"1,1,25,16777215\">缓存弹幕</d></i>");string original=File.ReadAllText(xml);var ready=Json.Object(Json.Write(Call(bridge,"playbackPrepare",new{key=keys[1],token=Guid.NewGuid().ToString()})));Check(Json.Text(ready,"ready").Equals("true",StringComparison.OrdinalIgnoreCase)&&handler.Requests==0,"已有弹幕直接复用，不访问弹幕 API");Check(File.ReadAllText(file)==before&&File.ReadAllText(xml)==original,"本机准备保留已有视频和 XML 内容");
                bool disabledPublished=false,cancelSawDisabled=false;Action changed=()=>{if(!settings.AutoMatchDanmu)disabledPublished=true;};core.Changed+=changed;
                var inflight=new CancellationTokenSource();inflight.Token.Register(()=>cancelSawDisabled=disabledPublished);var jobs=(Dictionary<string,CancellationTokenSource>)typeof(ElectronBridge).GetField("localPlaybackJobs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(bridge);jobs[Guid.NewGuid().ToString()]=inflight;
                Call(bridge,"autoMatch",new{enabled=false});
                Check(cancelSawDisabled,"关闭开关先通知播放器再取消请求，不将正常取消显示为弹幕错误");jobs.Clear();inflight.Dispose();core.Changed-=changed;
                Check(!settings.AutoMatchDanmu&&!SettingsStore.Load().AutoMatchDanmu,"自动在线匹配开关关闭后立即持久保存");
                var offlineReady=Json.Object(Json.Write(Call(bridge,"playbackPrepare",new{key=keys[1],token=Guid.NewGuid().ToString()})));
                Check(Json.Text(offlineReady,"ready").Equals("true",StringComparison.OrdinalIgnoreCase),"关闭在线匹配后已有XML仍返回就绪");
                var offlineMissing=Json.Object(Json.Write(Call(bridge,"playbackPrepare",new{key=keys[0],token=Guid.NewGuid().ToString()})));
                Check(Json.Text(offlineMissing,"ready").Equals("false",StringComparison.OrdinalIgnoreCase)&&handler.Requests==0,"关闭后缺失XML不访问API");
                Check(!core.Automatic.PrepareItem(files[0],CancellationToken.None).GetAwaiter().GetResult()&&handler.Requests==0,"自动准备入口同样遵守在线开关");
                Call(bridge,"autoMatch",new{enabled=true});Check(settings.AutoMatchDanmu,"重新开启恢复自动匹配资格");
                // First request after a cold/partial scan: only one indexed item,
                // while its directory already contains the whole season.
                for(int n=4;n<=12;n++)File.WriteAllText(Path.Combine(settings.MediaFolder,"第 "+n+" 集.mkv"),"fixture "+n);
                string other=Path.Combine(settings.MediaFolder,"下一季");Directory.CreateDirectory(other);File.WriteAllText(Path.Combine(other,"第 1 集.mkv"),"other season");File.WriteAllText(Path.Combine(settings.MediaFolder,"说明.txt"),"not video");
                if(args.Length>2&&File.Exists(args[2]))foreach(var media in Directory.GetFiles(settings.MediaFolder,"*.mkv"))File.Copy(args[2],media,true);
                var duplicate=new Dictionary<string,object>(files[0]);duplicate["Id"]="duplicate-index";core.Library.Replace(new[]{files[0],duplicate});
                var firstQueue=Json.Read<object[]>(Json.Write(Call(bridge,"playbackResolve",new{keys=new[]{keys[0]},siblings=true,natural=true}))).Cast<Dictionary<string,object>>().ToArray();
                File.WriteAllText(Path.Combine(output,"resolved-queue.json"),Json.Write(firstQueue));
                Check(firstQueue.Length==12,"冷启动索引仅一集时，首次解析从实际同级目录补全12集并去重");
                Check(firstQueue.Select(x=>Path.GetFileName(Json.Text(x,"path"))).SequenceEqual(Enumerable.Range(1,12).Select(n=>"第 "+n+" 集.mkv")),"补全队列自然排序1/2/10，不混入其他季度子目录及非视频");
                var unindexed=firstQueue.First(x=>Path.GetFileName(Json.Text(x,"path"))=="第 2 集.mkv");
                var unindexedReady=Json.Object(Json.Write(Call(bridge,"playbackPrepare",new{key=Json.Text(unindexed,"key"),token=Guid.NewGuid().ToString()})));
                Check(Json.Text(unindexedReady,"ready").Equals("true",StringComparison.OrdinalIgnoreCase)&&handler.Requests==0,"首次队列补全的未索引单集同样复用XML，不访问网络");
                core.Library.Replace(files);
                var explicitQueue=Json.Read<object[]>(Json.Write(Call(bridge,"playbackResolve",new{keys=new[]{keys[2],keys[0]},siblings=true})));
                Check(explicitQueue.Length==2&&Json.Text((Dictionary<string,object>)explicitQueue[0],"key")==keys[2],"明确多选只播放选择项，不擅自扩展或改变顺序");
                invalid=false;try{Call(bridge,"playbackPrepare",new{key=keys[1],token="invalid"});}catch(InvalidOperationException){invalid=true;}Check(invalid,"非法播放任务标识被拒绝");Check((bool)Call(bridge,"playbackCancel",new{token=Guid.NewGuid().ToString()}),"取消已结束任务是安全的幂等操作");
            }File.WriteAllText(Path.Combine(output,"result.json"),Json.Write(new{passed=true,checks=checks}));Console.WriteLine(Json.Write(new{passed=true,checks=checks.Count}));return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{app.Shutdown();}
    }
}
