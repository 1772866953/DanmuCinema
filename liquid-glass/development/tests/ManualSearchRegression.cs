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

class ManualSearchRegression
{
    sealed class Fixture:HttpMessageHandler
    {
        public string Mode="hash";
        public List<string> Calls=new List<string>();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            string route=request.RequestUri.AbsolutePath,body="{}";
            if(route.EndsWith("/match")){
                var sent=Json.Object(await request.Content.ReadAsStringAsync());string mode=Json.Text(sent,"matchMode"),file=Json.Text(sent,"fileName");Calls.Add(mode+"|"+file);
                int number=SmartMatching.Episode(new Dictionary<string,object>{{"Path",file}});if(number==0)number=1;
                bool hit=Mode=="hash"&&mode=="hashOnly"||Mode=="filename"&&mode=="fileNameOnly";
                body=hit?Json.Write(new{success=true,isMatched=true,matches=new[]{new{animeId=600,episodeId=60000+number,animeTitle="完整作品 第三季",episodeTitle="第"+number+"话 内容",typeDescription="TV动画"}}}):"{\"success\":true,\"isMatched\":false,\"matches\":[]}";
            }else if(route.Contains("/bangumi/")){
                body=Json.Write(new{success=true,bangumi=new{episodes=Enumerable.Range(1,12).Select(i=>new{episodeId=60000+i,episodeNumber=i,episodeTitle="第"+i+"话"}).ToArray()}});
            }else if(request.RequestUri.Host=="api.bgm.tv"){
                Calls.Add("alias");body="{\"data\":[]}";
            }else if(route.Contains("/search")){
                string query=System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["keyword"]??"";Calls.Add("search|"+query);
                body=Mode=="title"?Json.Write(new{success=true,animes=new[]{new{animeId=601,animeTitle="完整作品 第一季",typeDescription="TV动画",episodeCount=12},new{animeId=600,animeTitle="完整作品 第三季",typeDescription="TV动画",episodeCount=12}}}):"{\"success\":true,\"animes\":[]}";
            }else throw new Exception("Unexpected mock route "+route);
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)};
        }
    }
    static List<string> checks=new List<string>();
    static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks.Add(name);}
    static T Wait<T>(Task<T> task){while(!task.IsCompleted){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(1);}return task.GetAwaiter().GetResult();}
    static object Call(ElectronBridge bridge,string method,object parameters){return Wait((Task<object>)typeof(ElectronBridge).GetMethod("Dispatch",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bridge,new object[]{method,Json.Object(Json.Write(parameters))}));}
    static Dictionary<string,object>[] Rows(object result){return Json.Read<object[]>(Json.Write(result)).Cast<Dictionary<string,object>>().ToArray();}
    static int Score(Dictionary<string,object> item){int n;return Int32.TryParse(Json.Text(item,"Priority"),out n)?n:0;}
    static Dictionary<string,object>[] Files(string root,string work,string label)
    {
        string directory=Path.Combine(root,work,"[LoliHouse][25-36][1080P][简繁内封字幕]");Directory.CreateDirectory(directory);
        return Enumerable.Range(1,12).Select(i=>{
            string path=Path.Combine(directory,"Hyakkano - 25  ("+i+").mkv");File.WriteAllText(path,label+" file "+i);
            return new Dictionary<string,object>{{"Id",label+"-"+i},{"Path",path},{"Type","Episode"},{"Name","被刮削器统一成的总名称"},{"SeriesName","完整作品"},{"ParentIndexNumber",1},{"IndexNumber",25}};
        }).ToArray();
    }
    [STAThread]static int Main(string[] args)
    {
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);Paths.Root=Path.Combine(output,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Paths.Root);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try{
            var settings=new AppSettings{EnableDandan=true,EnableAnimeko=false,EnableBahamut=false,EnableExistingDanmu=false};
            DandanConfig.Ensure(settings);SettingsStore.AtomicWrite(DandanConfig.FilePath,Json.Write(new{AppId="manual-fixture",AppSecret="fixture-only"}),false);
            var handler=new Fixture();using(var api=new JellyfinApi(settings,handler))using(var catalog=new DanmuCatalog(settings,api,handler))using(var core=new DesktopController(app,settings,false,catalog))using(var writer=new StreamWriter(new MemoryStream())){
                var files=Files(Paths.Root,"完整作品S3","hash");
                Check(SmartMatching.Season(files[9])==3,"明确的上级 S3 目录优先于错误的刮削季号");
                Check(SmartMatching.Episode(files[9])==10&&MediaNames.FileTitle(files[9])=="Hyakkano","重复前缀25不会覆盖末尾的单集序号10");
                Check(MediaNames.SingleSearchTitle(files[9]).Contains("Hyakkano S3")&&MediaNames.SingleSearchTitle(files[9]).Contains("第 10 集"),"单集搜索提取实际文件中的作品与集号，不使用总目录名");
                var hash=Wait(catalog.ManualSearch("百人女友",files,true,true,0,"dandan"));var best=(Dictionary<string,object>)hash.Items[0];
                Check(Score(best)==100&&Json.Text(best,"MatchMethod")=="hash","简称搜索先哈希命中，最高优先度100");
                Check(handler.Calls.Count==1&&handler.Calls[0].StartsWith("hashOnly|"),"整季哈希命中后只需一次识别请求，不继续名称搜索");
                int count=handler.Calls.Count;Wait(catalog.ManualSearch("百人女友",files,true,true,0,"dandan"));Check(handler.Calls.Count==count,"重复手动搜索完全复用本地匹配缓存");
                var missing=new Dictionary<string,object>(files[0]);missing["Path"]=Path.Combine(Path.GetDirectoryName(Json.Text(files[0],"Path")),"missing.mkv");
                var stillHash=Wait(catalog.ManualSearch("首个文件离线",new[]{missing,files[1]},true,true,0,"dandan"));
                Check(stillHash.Items.Length==1&&Score((Dictionary<string,object>)stillHash.Items[0])==100,"首个文件失效时仍从其他可访问影片进行哈希识别");
                core.Library.Replace(files);var bridge=new ElectronBridge(app,core,writer);
                Call(bridge,"select",new{keys=core.Library.Entries.Select(x=>x.Key).ToArray(),selected=true});
                var visible=Rows(Call(bridge,"library",new{directory=Path.GetDirectoryName(Json.Text(files[0],"Path"))}));
                Check(visible.Any(x=>Json.Text(x,"searchTitle").Contains("Hyakkano S3 · 第 10 集")),"媒体列表 RPC 传回提取后的单集搜索文本");
                string focus=LibraryEntry.Identity(files[9]);var focused=Json.Object(Json.Write(Call(bridge,"search",new{keyword="Hyakkano S3 · 第 10 集",focusKey=focus,smart=true,animeOnly=true,provider="dandan",details=true})));
                var sources=Json.Array(focused,"sources").Cast<Dictionary<string,object>>().ToArray();
                Check(Json.Text(sources[0],"priority")=="100"&&Json.Text(sources[0],"reason").Contains("(10).mkv"),"单集搜索锁定实际点击文件并显示0–100优先度及依据");
                var episodes=Rows(Call(bridge,"episodes",new{id=Json.Text(sources[0],"id")}));
                Check(episodes.Count(x=>Json.Text(x,"recommended")=="True")==1&&episodes.Any(x=>Json.Text(x,"recommended")=="True"&&Json.Text(x,"number")=="10"),"哈希命中后自动定位对应的第10集，而非默认第一集");
                var online=Enumerable.Range(1,12).Select(i=>new Dictionary<string,object>{{"Number",i},{"Title","第"+i+"话"}}).ToArray();
                var plan=BatchMatching.Plan(files,online);Check(plan.Count==12&&plan.All(x=>x.Selected)&&plan.Select(x=>x.Number).SequenceEqual(Enumerable.Range(1,12)),"同一季度12个重命名文件逐集对应1–12，不重复匹配首集");
                var absolute=Enumerable.Range(25,12).Select(i=>new Dictionary<string,object>{{"Number",i},{"Title","第"+i+"话"}}).ToArray();
                plan=BatchMatching.Plan(files,absolute);Check(plan.All(x=>x.Selected)&&plan.Select(x=>x.Number).SequenceEqual(Enumerable.Range(25,12)),"仅在完整目录范围与远端集数一致时将1–12换算为25–36");
                Check(BatchMatching.RemoteNumber(files[0],new[]{new Dictionary<string,object>{{"Number",25}},new Dictionary<string,object>{{"Number",27}}})==1,"不完整或有缺号的远端列表不进行偏移猜测");
                string xml=Path.ChangeExtension(Json.Text(files[0],"Path"),".xml");File.WriteAllText(xml,"<i/>");
                var recognized=Wait(catalog.IdentifyFile(files[0],CancellationToken.None));catalog.RecordAssociation(files[0],recognized.Recommended);
                handler.Mode="none";var sibling=Wait(catalog.ManualSearch("另一个简称",new[]{files[10]},true,true,0,"dandan"));
                Check(sibling.Items.Length==1&&Score((Dictionary<string,object>)sibling.Items[0])==94,"哈希未收录后，可按同目录的有效已确认弹幕记录找回完整作品");
                File.AppendAllText(xml,"changed");
                var invalid=Wait(catalog.ManualSearch("另一个简称",new[]{files[10]},true,true,0,"dandan"));
                Check(!invalid.Items.Cast<Dictionary<string,object>>().Any(a=>Score(a)==94),"已确认XML被外部修改后，旧的同目录匹配缓存失效");
                count=handler.Calls.Count;Wait(catalog.ManualSearch("另一个简称",new[]{files[10]},true,true,0,"dandan"));
                Check(handler.Calls.Count>count,"空匹配不会长期阻止下一次主动搜索重试");
                handler.Mode="filename";var byNameFiles=Files(Paths.Root,"完整作品S3-name","name");
                count=handler.Calls.Count;var byName=Wait(catalog.ManualSearch("完整作品",byNameFiles,true,true,3,"dandan"));
                var calls=handler.Calls.Skip(count).ToArray();
                Check(calls.Take(3).All(c=>c.StartsWith("hashOnly|"))&&calls[3].StartsWith("fileNameOnly|"),"代表文件的哈希都未命中后才进入文件名回退，最多取样三集");
                Check(Score((Dictionary<string,object>)byName.Items[0])==96,"名称、季度与集号均确认的文件名匹配低于哈希优先度");
                handler.Mode="title";var titleFiles=Files(Paths.Root,"完整作品S3-title","title");
                var titled=Wait(catalog.ManualSearch("完整作品",titleFiles,true,true,3,"dandan"));var ranked=titled.Items.Cast<Dictionary<string,object>>().ToArray();
                Check(Json.Text(ranked[0],"Name").Contains("第三季")&&ranked.All(x=>Score(x)>=0&&Score(x)<94),"名称回退依季号排序，第一季不会盖过第三季，分值受证据等级限制");
                Check(ranked.Select(Score).SequenceEqual(ranked.Select(Score).OrderByDescending(x=>x)),"搜索结果按优先度从高到低排列");
                count=handler.Calls.Count;Wait(catalog.ManualSearch("完整作品",titleFiles,true,true,3,"dandan"));Check(handler.Calls.Count==count,"名称回退成功后也复用有效缓存，不重复请求哈希空匹配");
                Check(files.All(f=>File.ReadAllText(Json.Text(f,"Path")).StartsWith("hash file")),"识别和搜索不改动视频内容");
            }
            File.WriteAllText(Path.Combine(output,"result.json"),Json.Write(new{passed=true,checks}));Console.WriteLine(Json.Write(checks));return 0;
        }catch(Exception e){File.WriteAllText(Path.Combine(output,"result.json"),Json.Write(new{passed=false,error=e.ToString(),checks}));Console.Error.WriteLine(e);return 1;}
        finally{app.Shutdown();}
    }
}
