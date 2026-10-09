using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DanmuCinema;
using DanmuCinema.Desktop;

public static class MediaTypeTests
{
    static readonly List<string> report = new List<string>();
    static void Check(bool value,string text) { if(!value) throw new Exception(text); report.Add("PASS: "+text); Console.WriteLine("PASS: "+text); }
    static Dictionary<string,object> Library(string name,string type,params string[] paths)
    { return new Dictionary<string,object>{{"Name",name},{"ItemId",name},{"CollectionType",type},{"Locations",paths},{"LibraryOptions",new Dictionary<string,object>{{"PathInfos",paths.Select(p=>new Dictionary<string,object>{{"Path",p}}).ToArray()},{"EnableRealtimeMonitor",false},{"SaveLocalMetadata",false},{"PreferredMetadataLanguage","zh"},{"SubtitleFetcherOrder",new[]{"fixture-preserved"}}}}}; }
    sealed class Fixture : HttpMessageHandler
    {
        public readonly List<Dictionary<string,object>> Libraries=new List<Dictionary<string,object>>();
        public readonly List<string> Writes=new List<string>();
        public bool Playing,Scanning,FailCreate,LostResponse;
        public object Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            string route=request.RequestUri.AbsolutePath;
            var query=System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query); string content="";
            if(route=="/ScheduledTasks") content=Json.Write(new[]{new {Key="RefreshLibrary",State=Scanning?"Running":"Idle"}});
            else if(route=="/Sessions") content=Playing?"[{\"NowPlayingItem\":{\"Id\":\"fixture\"}}]":"[]";
            else if(route=="/Library/VirtualFolders")
            {
                if(request.Method==HttpMethod.Get) content=Json.Write(Libraries);
                else if(request.Method==HttpMethod.Delete) { Writes.Add(request.Method+" "+request.RequestUri); Libraries.RemoveAll(x=>Json.Text(x,"Name")==query["name"]); }
                else if(request.Method==HttpMethod.Post)
                {
                    Writes.Add(request.Method+" "+request.RequestUri); string body=await request.Content.ReadAsStringAsync(); Body=Json.Child(Json.Object(body),"LibraryOptions");
                    if(FailCreate) { FailCreate=false; return new HttpResponseMessage(HttpStatusCode.InternalServerError); }
                    var options=(Dictionary<string,object>)Body;
                    var item=Library(query["name"],query["collectionType"]??"",Json.Array(options,"PathInfos").OfType<Dictionary<string,object>>().Select(x=>Json.Text(x,"Path")).ToArray()); item["LibraryOptions"]=options; Libraries.Add(item);
                    if(LostResponse) { LostResponse=false; return new HttpResponseMessage(HttpStatusCode.InternalServerError); }
                }
                else throw new Exception("Unexpected method");
            }
            else throw new Exception("Unexpected route "+route);
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(content)};
        }
    }
    static AppSettings Settings() { return new AppSettings{EncryptedToken=SettingsStore.Protect("fixture"),EnableDandan=false,EnableExistingDanmu=false,EnableAnimeko=false,EnableBahamut=false}; }
    static string Hash(string file) { using(var sha=SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(file))); }
    static async Task Unit(string root)
    {
        Paths.Root=root; string first=Path.Combine(root,"fake-media1"),second=Path.Combine(root,"fake-media2"); Directory.CreateDirectory(first);Directory.CreateDirectory(second);
        var fixture=new Fixture();var old=Library("测试媒体库 & +","movies",first,second);fixture.Libraries.Add(old);fixture.Libraries.Add(Library("其他电影","movies",first));
        string options=Json.Write(Json.Child(old,"LibraryOptions"));
        using(var api=new JellyfinApi(Settings(),fixture))
        {
            var manager=new LibraryTypeManager(api,()=>Task.FromResult(0));Check(await manager.Change(old,"mixed"),"Movie library can switch to automatic mode");
            Check(fixture.Libraries.Count==2 && Json.Text(fixture.Libraries.First(x=>Json.Text(x,"Name")=="其他电影"),"CollectionType")=="movies","Changing one library leaves other libraries unchanged");
            Check(fixture.Writes.Count==2 && fixture.Writes[0].Contains("refreshLibrary=true") && fixture.Writes[1].Contains("refreshLibrary=false") && !fixture.Writes[1].Contains("collectionType"),"Automatic mode omits collectionType and removes the old index before creating the new library");
            Check(Json.Write(fixture.Body)==options && Json.Array(fixture.Libraries.Last(),"Locations").Length==2,"Multiple media paths and all original options preserved");
            Check(!File.Exists(Path.Combine(Paths.Data,"media-library-change.json")),"Successful change removes recovery journal");
            Check(!await manager.Change(fixture.Libraries.Last(),"mixed") && fixture.Writes.Count==2,"Same mode is a no-op");
            var current=fixture.Libraries.Last();fixture.FailCreate=true;bool failed=false;try {await manager.Change(current,"tvshows");} catch(InvalidOperationException e){failed=e.Message.Contains("已恢复");}
            Check(failed && Json.Text(fixture.Libraries.Last(),"CollectionType")=="" && !File.Exists(Path.Combine(Paths.Data,"media-library-change.json")),"Failed rebuild restores automatic library including paths and options");
            current=fixture.Libraries.Last();fixture.LostResponse=true;Check(await manager.Change(current,"tvshows") && Json.Text(fixture.Libraries.Last(),"CollectionType")=="tvshows","Lost response after successful create is reconciled without duplicate libraries");
            current=fixture.Libraries.Last();int before=fixture.Writes.Count;fixture.Playing=true;try{await manager.Change(current,"mixed");}catch(InvalidOperationException){}
            Check(fixture.Writes.Count==before,"Active playback prevents library changes");fixture.Playing=false;fixture.Scanning=true;try{await manager.Change(current,"mixed");}catch(InvalidOperationException){}
            Check(fixture.Writes.Count==before,"Active scan prevents library changes");fixture.Scanning=false;
            SettingsStore.AtomicWrite(Path.Combine(Paths.Data,"media-library-change.json"),Json.Write(new {Original=current,Target="mixed"}));fixture.Libraries.Remove(current);await manager.RecoverPending();
            Check(fixture.Libraries.Any(x=>Json.Text(x,"Name")==Json.Text(current,"Name") && Json.Text(x,"CollectionType")=="tvshows"),"Interrupted change restores missing library when reopened");
        }
        Check(MediaAuto.Valid(new AppSettings().LibraryType) && new AppSettings().LibraryType=="mixed","Fresh app defaults to automatic movie, TV and anime recognition");
        Check(MediaAuto.Episode("Film (2020) [1080p].mkv")==0 && MediaAuto.Episode("[Group] Show [09][Ma10p_1080p].mkv")==9,"Release episode numbers are distinguished from movie years and resolution");
        Check(MediaAuto.Season("作品S2/[Group][01-12]/Show [01].mkv")==2,"Parent anime season retained through release subdirectory");
        string filmFolder=Path.Combine(root,"Franchise");Directory.CreateDirectory(filmFolder);File.WriteAllText(Path.Combine(filmFolder,"Film Part 1.mkv"),"fixture");File.WriteAllText(Path.Combine(filmFolder,"Film Part 2.mkv"),"fixture");
        Check(!MediaAuto.SeriesFolder(filmFolder),"Numbered movie franchise is not turned into a series");
    }
    static async Task Integration(string root)
    {
        Paths.Root=root;string media=Path.Combine(root,"media"),anime=Path.Combine(media,"幼女战记S2"),release=Path.Combine(anime,"[LoliHouse][01-12][1080P][简繁内封字幕]"),tv=Path.Combine(media,"Example TV","Season 01"),film=Path.Combine(media,"Example Film (2020)");
        Directory.CreateDirectory(release);Directory.CreateDirectory(tv);Directory.CreateDirectory(film);
        string sample=Path.Combine(root,"sample.mkv"),sampleFilm=Path.Combine(root,"sample.mp4");
        foreach(var n in Enumerable.Range(1,3)) {File.Copy(sample,Path.Combine(release,"[LoliHouse] Youjo Senki ["+n.ToString("D2")+"][1080P].mkv"),true);File.Copy(sample,Path.Combine(tv,"Example TV S01E"+n.ToString("D2")+".mkv"),true);}
        File.Copy(sampleFilm,Path.Combine(film,"Example Film (2020).mp4"),true);
        var originals=Directory.GetFiles(media,"*",SearchOption.AllDirectories).ToDictionary(x=>x,Hash);string xml=Path.ChangeExtension(originals.Keys.First(),".xml");File.WriteAllText(xml,"<i><d p=\"1,1,25,16777215,0,0,fixture,0\">fixture</d></i>");originals[xml]=Hash(xml);
        Check(MediaAuto.SeriesFolder(anime) && MediaAuto.SeriesFolder(Path.Combine(media,"Example TV")) && !MediaAuto.SeriesFolder(media) && !MediaAuto.SeriesFolder(film),"Automatic classifier groups nested anime and season TV while keeping mixed root and movie separate");
        var settings=Settings();settings.EncryptedToken="";settings.Port=28496;settings.DanmuPort=28497;settings.MediaFolder=media;
        using(var services=new ServiceManager(settings))
        {
            try
            {
                await services.Start();await services.Api.Initialize("media_type_fixture","Fixture_TestPassword_946");
                var options=new {PathInfos=new[]{new{Path=media}},EnableInternetProviders=false,EnableRealtimeMonitor=false,SaveLocalMetadata=false,MetadataSavers=new string[0],SubtitleFetcherOrder=new string[0],DisabledSubtitleFetchers=new[]{"Danmu"},PreferredMetadataLanguage="zh",TypeOptions=new[]{"Movie","Series","Season","Episode"}.Select(t=>new{Type=t,MetadataFetchers=new string[0],ImageFetchers=new string[0]}).ToArray()};
                await services.Api.Request("POST","Library/VirtualFolders?name=MixedFixture&collectionType=movies&refreshLibrary=false",new{LibraryOptions=options},true);
                await new LibraryScanner(services.Api).Load(true,null,CancellationToken.None);
                var manager=new LibraryTypeManager(services.Api);var library=(await manager.Libraries()).Single();
                Check(Json.Text(library,"CollectionType")=="movies","Real server fixture initially uses movie library");
                Check(await manager.Change(library,"mixed"),"Real Jellyfin library switches from movies to automatic mode");
                var items=await new LibraryScanner(services.Api).Load(true,null,CancellationToken.None);
                File.WriteAllText(Path.Combine(root,"server-items.json"),Json.Write(items.Select(x=>new{Name=Json.Text(x,"Name"),Type=Json.Text(x,"Type"),Path=Json.Text(x,"Path"),SeriesName=Json.Text(x,"SeriesName"),Season=Json.Text(x,"ParentIndexNumber"),Episode=Json.Text(x,"IndexNumber")})));
                var episodes=items.Where(x=>Json.Text(x,"Type")=="Episode").ToArray();var movies=items.Where(x=>Json.Text(x,"Type")=="Movie").ToArray();
                Check(episodes.Length==6 && movies.Length==1,"One automatic library returns six TV/anime episodes and one independent movie");
                var animeEpisodes=episodes.Where(x=>Json.Text(x,"Path").StartsWith(anime+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).ToArray();
                Check(animeEpisodes.Length==3 && animeEpisodes.All(x=>Json.Text(x,"SeriesName")=="幼女战记S2" && Json.Text(x,"ParentIndexNumber")=="2") && animeEpisodes.Select(x=>Json.Text(x,"IndexNumber")).OrderBy(x=>x).SequenceEqual(new[]{"1","2","3"}),"Anime release subdirectory has correct work, season and episode metadata");
                Check(episodes.Where(x=>Json.Text(x,"Path").StartsWith(tv)).Select(x=>Json.Text(x,"SeriesId")).Distinct().Count()==1,"TV episodes share a series grouping for clients");
                Check((await services.Api.Plugins()).OfType<Dictionary<string,object>>().Any(x=>Json.Text(x,"Name")=="DanmuCinema Playback" && Json.Text(x,"Status")=="Active"),"Bundled server classifier plugin loads without external plugins");
                Check(originals.All(x=>File.Exists(x.Key)&&Hash(x.Key)==x.Value),"Real server migration and scan leave all test video and XML bytes unchanged");
                await manager.Change((await manager.Libraries()).Single(),"movies");await new LibraryScanner(services.Api).Load(true,null,CancellationToken.None);
                Check(Json.Text((await manager.Libraries()).Single(),"CollectionType")=="movies","Manual movie mode remains available after automatic mode");
            }
            finally { services.Stop().GetAwaiter().GetResult(); }
        }
    }
    static IEnumerable<DependencyObject> Children(DependencyObject root)
    { yield return root;foreach(DependencyObject node in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var child in Children(node))yield return child; }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
            if(args.Length<3 || args[2]!="--ui-only") {Unit(root).GetAwaiter().GetResult();Integration(root).GetAwaiter().GetResult();}
            else report.AddRange(File.ReadAllLines(args[1]).TakeWhile(x=>!x.StartsWith("System.Exception")));
            Paths.Root=root;var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Ui.InstallTheme(app);
            using(var controller=new DesktopController(app,Settings(),false))
            {
                var shell=new ShellWindow(controller);shell.Navigate("library");Check(Children(shell.View).OfType<Button>().Any(x=>Convert.ToString(x.Content)=="调整媒体库识别方式"),"Library page exposes recognition adjustment button");
                shell.Navigate("setup");Check(Children(shell.View).OfType<Button>().Any(x=>Convert.ToString(x.Content)=="调整媒体库识别方式"),"Setup page exposes the same adjustment entry");
                var window=new LibraryTypeWindow(controller);Check(Children(window).OfType<ComboBox>().Any(x=>x.Items.Count==3),"Adjustment dialog offers automatic, movie and TV/anime modes");window.Close();shell.Release();
            }
            app.Shutdown();File.WriteAllLines(args[1],report);return 0;
        }
        catch(Exception error){File.WriteAllLines(args[1],report.Concat(new[]{error.ToString()}));Console.Error.WriteLine(error.ToString());return 1;}
    }
}
