using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DanmuCinema;
using DanmuCinema.Desktop;

class LibraryRemovalRegression
{
    sealed class Fixture:HttpMessageHandler
    {
        public List<Dictionary<string,object>> Libraries=new List<Dictionary<string,object>>(),Items=new List<Dictionary<string,object>>();
        public bool Playing,Scanning,FailDelete,LostReply;public int Deleted,Version;
        object Scan(){return new{Id="scan",Key="RefreshLibrary",State=Scanning?"Running":"Idle",LastExecutionResult=new{StartTimeUtc=Version.ToString(),EndTimeUtc=Version.ToString(),Status="Completed"}};}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            string route=request.RequestUri.AbsolutePath,method=request.Method.Method;object body=null;var status=HttpStatusCode.OK;
            if(method=="DELETE") {
                Check(route=="/Library/VirtualFolders","删除请求只访问媒体库定义接口");
                Check(System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["refreshLibrary"]=="false","移除记录后由控制器统一扫描索引");
                if(FailDelete)status=HttpStatusCode.InternalServerError;
                else {string name=System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["name"];var library=Libraries.Single(x=>Json.Text(x,"Name")==name);Libraries.Remove(library);Deleted++;if(LostReply)status=HttpStatusCode.InternalServerError;}
            } else if(route=="/Library/VirtualFolders")body=Libraries;
            else if(route=="/ScheduledTasks")body=new[]{Scan()};
            else if(route=="/ScheduledTasks/Running/scan"){Version++;body=new{};}
            else if(route=="/ScheduledTasks/scan")body=Scan();
            else if(route=="/Sessions")body=Playing?new object[]{new{NowPlayingItem=new{Id="playing"}}}:new object[0];
            else if(route=="/Items") {var visible=Items.Where(item=>Libraries.Any(lib=>Json.Array(lib,"Locations").Select(Convert.ToString).Any(root=>Json.Text(item,"Path").StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase)))).ToArray();body=new{Items=visible,TotalRecordCount=visible.Length};}
            else throw new Exception("Unexpected request: "+method+" "+route);
            return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(body==null?"":Json.Write(body))});
        }
    }
    static readonly List<string> checks=new List<string>();
    static void Check(bool valid,string name){if(!valid)throw new Exception(name);checks.Add(name);}
    static T Wait<T>(Task<T> task){Pump(task);return task.GetAwaiter().GetResult();}
    static void Wait(Task task){Pump(task);task.GetAwaiter().GetResult();}
    static void Pump(Task task){while(!task.IsCompleted){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(1);}}
    static Dictionary<string,object> Clone(Dictionary<string,object> value){return Json.Object(Json.Write(value));}
    static bool Refused(Func<Task> action,string message){try{Wait(action());return false;}catch(InvalidOperationException e){return e.Message.Contains(message);}}
    [STAThread]static int Main(string[] args)
    {
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);Paths.Root=Path.Combine(output,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Paths.Root);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try {
            var fixture=new Fixture();string a=Path.Combine(Paths.Root,"库 A & 测试"),b=Path.Combine(Paths.Root,"库 B");Directory.CreateDirectory(a);Directory.CreateDirectory(b);
            var first=new Dictionary<string,object>{{"Name","库 A & 测试"},{"ItemId","library-a"},{"CollectionType","mixed"},{"Locations",new[]{a}}};
            var second=new Dictionary<string,object>{{"Name","库 B"},{"ItemId","library-b"},{"CollectionType","tvshows"},{"Locations",new[]{b}}};fixture.Libraries.Add(first);fixture.Libraries.Add(second);
            foreach(string folder in new[]{a,b})foreach(string suffix in new[]{"mp4","srt","xml"})File.WriteAllText(Path.Combine(folder,"影片 (1)."+suffix),"synthetic "+suffix);
            var media=Directory.GetFiles(Paths.Root,"*",SearchOption.AllDirectories).ToDictionary(x=>x,x=>File.ReadAllText(x));
            fixture.Items.AddRange(new[]{a,b}.Select((folder,i)=>new Dictionary<string,object>{{"Id","video-"+i},{"Path",Path.Combine(folder,"影片 (1).mp4")},{"Type","Episode"},{"IndexNumber",1}}));
            var settings=new AppSettings{MediaFolder=a,LibraryName=Json.Text(first,"Name"),EncryptedToken=SettingsStore.Protect("fixture-token"),EnableDandan=false,EnableExistingDanmu=false,EnableAnimeko=false,EnableBahamut=false};
            using(var api=new JellyfinApi(settings,fixture)) {
                var manager=new LibraryTypeManager(api);
                fixture.Scanning=true;Check(Refused(()=>manager.Remove(Clone(first)),"扫描"),"扫描期间阻止媒体库删除");fixture.Scanning=false;
                fixture.Playing=true;Check(Refused(()=>manager.Remove(Clone(first)),"播放"),"播放期间阻止媒体库删除");fixture.Playing=false;
                var stale=Clone(first);stale["Locations"]=new[]{b};Check(Refused(()=>manager.Remove(stale),"改变"),"目录与确认快照不一致时拒绝删除");
                stale=Clone(first);stale["ItemId"]="old-id";Check(Refused(()=>manager.Remove(stale),"改变"),"同名媒体库标识改变时拒绝删除");
                fixture.FailDelete=true;Check(Refused(()=>manager.Remove(Clone(first)),"未完成")&&fixture.Libraries.Count==2,"服务器拒绝删除时保留媒体库记录");fixture.FailDelete=false;
            }
            using(var core=new DesktopController(app,settings,false,null,fixture)) {
                core.Library.Replace(fixture.Items);core.Library.Selection.Set(core.Library.Entries[0].Key,true);core.BatchPlan=new List<BatchEntry>();
                Wait(core.RemoveLibrary(Clone(first)));
                Check(fixture.Libraries.Count==1&&Json.Text(fixture.Libraries[0],"Name")=="库 B","只删除确认的媒体库，保留其他媒体库");
                Check(settings.MediaFolder==b&&settings.LibraryName=="库 B"&&settings.LibraryType=="tvshows","删除当前默认库后切换到剩余媒体库");
                Check(core.Library.Entries.Length==1&&Json.Text(core.Library.Entries[0].Item,"Path").StartsWith(b),"删除后自动更新影片索引并保留另一库影片");
                Check(core.Library.Selection.Count==0&&core.BatchPlan==null&&!core.Busy&&!core.Loading,"删除完成后清除失效选择和批量计划，释放任务状态");
                Check(Refused(()=>core.RemoveLibrary(Clone(first)),"改变"),"重复删除不影响其他媒体库");
                fixture.LostReply=true;Wait(core.RemoveLibrary(Clone(second)));
                Check(fixture.Libraries.Count==0&&core.Library.Entries.Length==0,"删除响应丢失时通过服务器列表确认成功，最后一库可删除");
                Check(settings.MediaFolder==""&&settings.LibraryName=="我的影片"&&settings.LibraryType=="mixed","删除最后一个媒体库后恢复可添加媒体库的空状态");
                Check(settings.EncryptedToken!=""&&SettingsStore.Unprotect(settings.EncryptedToken)=="fixture-token","删除媒体库保留管理员登录和其他应用设置");
            }
            Check(media.All(x=>File.Exists(x.Key)&&File.ReadAllText(x.Key)==x.Value),"全部视频、字幕、弹幕文件内容保持不变");
            Check(Directory.Exists(a)&&Directory.Exists(b),"媒体文件夹均保留");
            File.WriteAllText(Path.Combine(output,"result.json"),Json.Write(new{passed=true,checks=checks}));Console.WriteLine(Json.Write(checks));return 0;
        }catch(Exception e){Console.Error.WriteLine(e);File.WriteAllText(Path.Combine(output,"result.json"),Json.Write(new{passed=false,error=e.ToString(),checks=checks}));return 1;}
        finally{app.Shutdown();}
    }
}
