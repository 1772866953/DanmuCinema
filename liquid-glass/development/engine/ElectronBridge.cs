using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DanmuCinema.Desktop
{
    // Private inherited pipes only: there is no remotely accessible control port.
    public sealed class ElectronBridge
    {
        readonly DesktopController core;
        readonly StreamWriter output;
        readonly Application app;
        readonly SemaphoreSlim mutation = new SemaphoreSlim(1, 1);
        Dictionary<string, object>[] sources = new Dictionary<string, object>[0], episodes = new Dictionary<string, object>[0];
        Dictionary<string, object> episodeAnime;
        readonly Dictionary<string, MediaDeletePlan> deletions = new Dictionary<string, MediaDeletePlan>();
        public ElectronBridge(Application application, DesktopController controller, StreamWriter writer)
        { app = application; core = controller; output = writer; }
        void Send(object value) { lock (output) { output.WriteLine(Json.Write(value)); output.Flush(); } }
        public static int Run()
        {
            string root = Environment.GetEnvironmentVariable("DANMU_DATA_ROOT");
            if (String.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root)) return 2;
            Paths.Root = Path.GetFullPath(root);
            Paths.RuntimeOverride = Environment.GetEnvironmentVariable("DANMU_RUNTIME_ROOT");
            System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false;
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            var settings = SettingsStore.Load();
            if (!File.Exists(Paths.SettingsFile)) settings.MediaFolder = "";
            DandanConfig.Ensure(settings);
            using (var controller = new DesktopController(application, settings, false))
            {
                controller.Headless = true;
                var bridge = new ElectronBridge(application, controller, writer);
                controller.Changed += () => bridge.Send(new { type = "state", data = bridge.State() });
                controller.RequestForeground += () => bridge.Send(new { type = "foreground" });
                controller.Notice += (title,message) => bridge.Send(new { type="notice", title=title, message=message });
                application.Startup += async (s, e) => {
                    controller.StartHeadless();
                    bridge.Send(new { type = "ready", data = bridge.State() });
                    var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
                    Task.Run(async () => {
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null) {
                            string request = line;
                            application.Dispatcher.BeginInvoke(new Action(async () => await bridge.Receive(request)));
                        }
                        application.Dispatcher.BeginInvoke(new Action(async () => { controller.CancelSchedule(); await controller.StopAll(); application.Shutdown(); }));
                    });
                    try { await controller.Initialize(Environment.GetEnvironmentVariable("DANMU_START_SERVICES") == "1"); }
                    catch (Exception error) { controller.Status = error.Message; controller.Publish(); }
                };
                return application.Run();
            }
        }
        object State()
        {
            var s = core.Settings;
            return new {
                status = core.Status, serverStatus = core.ServerStatus, pluginStatus = core.PluginStatus, playback = core.SessionStatus,
                running = core.Services.OwnsProcess, gateway = core.Gateway.Running, busy = core.Busy, loading = core.Loading,
                progress = core.LibraryProgress == null ? null : new { message = core.LibraryProgress.Message, percent = core.LibraryProgress.Percent },
                count = core.Library.Entries.Length, selected = core.Library.Selection.Count,
                settings = new { s.Port, s.DanmuPort, s.MediaFolder, s.LibraryName, s.LibraryType, s.AdminName, s.CloseToTray, s.StartServicesOnLaunch, s.PreferOriginal,
                    s.EnableDandan, s.EnableAnimeko, s.EnableBahamut, s.EnableExistingDanmu, s.AnimeOnly, s.CacheRetentionMonths, s.DownloadIntervalSeconds, s.UiScalePercent, s.LogRetentionDays,
                    customApis = SettingsStore.Unprotect(s.EncryptedAdditionalApis) },
                batchRunning = core.BatchRunning, preparing = core.Preparing, paused = core.PreparationPaused, canCancel = core.CanCancelDownloads,
                taskStatus = core.Preparing ? core.PreparationStatus : core.BatchStatus,
                tasks = core.DownloadTasks.Select(x => new { id = LibraryEntry.Identity(x.Item), x.Name, x.Filename, x.Status, x.Origin, x.Detail, x.Retryable }).ToArray(),
                plan = core.BatchPlan == null ? null : core.BatchPlan.Select((x,i) => new { index = i, x.Number, x.Selected, x.Status, local = Path.GetFileName(Json.Text(x.Local,"Path")), remote = x.Remote == null ? "" : Json.Text(x.Remote,"Title"), matched = x.Remote != null }).ToArray(),
                schedule = new { active = core.Scheduler.Active, remaining = Scheduler.FormatRemaining(core.Scheduler.Remaining), status = core.ScheduleStatus,
                    target = core.Scheduler.Active ? core.Scheduler.Target.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "", weekly = core.Scheduler.Weekly, days = core.Scheduler.WeeklyLabel,
                    warning = core.Scheduler.State == ScheduleState.Warning },
                addresses = NetworkInfo.Addresses(), sourceChoices = core.Gateway.Catalog.SourceChoices()
            };
        }
        static string S(Dictionary<string, object> p, string key) { return Json.Text(p, key); }
        static int I(Dictionary<string, object> p, string key, int fallback = 0) { int n; return Int32.TryParse(S(p,key), out n) ? n : fallback; }
        static bool B(Dictionary<string, object> p, string key) { return S(p,key).Equals("true", StringComparison.OrdinalIgnoreCase); }
        void Persist(Action change)
        {
            var settings=core.Settings; var before=Json.Read<AppSettings>(Json.Write(settings));
            try{change();SettingsStore.Save(settings);}
            catch{foreach(var property in typeof(AppSettings).GetProperties().Where(x=>x.CanRead&&x.CanWrite))property.SetValue(settings,property.GetValue(before,null),null);throw;}
        }
        LibraryEntry[] Selected() { return core.Library.Entries.Where(x => core.Library.Selection.Contains(x.Key)).ToArray(); }
        void Background(Task task) { Observe(task); }
        async void Observe(Task task) { try { await task; } catch (Exception error) { core.Status = error.Message; Log.Write(error.Message); core.Publish(); } }
        async Task Receive(string line)
        {
            Dictionary<string, object> request = null; bool locked = false;
            try {
                request = Json.Object(line); string method = S(request,"method");
                var p = Json.Child(request,"params") ?? new Dictionary<string, object>();
                // Cancellation and observations must remain responsive during API calls.
                if (!new[] { "state", "library", "logs", "history", "cache", "cancel", "pause", "scheduleCancel", "shutdown" }.Contains(method)) {
                    await mutation.WaitAsync(); locked = true;
                }
                var result = await Dispatch(method, p);
                Send(new { id = S(request,"id"), result = result });
            }
            catch (Exception error) { Send(new { id = request == null ? "" : S(request,"id"), error = error.Message }); }
            finally { if (locked) mutation.Release(); }
        }
        async Task<object> Dispatch(string method, Dictionary<string, object> p)
        {
            var s = core.Settings;
            switch (method) {
                case "visibility": core.FrontendVisible=B(p,"visible"); return true;
                case "state": return State();
                case "start": await core.StartAll(); await core.UpdateStatus(); if(!String.IsNullOrEmpty(core.Services.Api.Token)) Background(core.LoadLibrary()); core.Publish(); return State();
                case "stop": await core.StopAll(); await core.UpdateStatus(); core.Publish(); return State();
                case "scan": Background(core.ScanLibrary()); return true;
                case "refresh": Background(core.LoadLibrary()); return true;
                case "library":
                    return core.Library.Browse(S(p,"directory") == "" ? null : S(p,"directory"), S(p,"filter"), (LibrarySort)Math.Max(0, Math.Min(4,I(p,"sort"))), B(p,"descending"), s.MediaFolder)
                        .Select(x => new { id = x.Key, name = x.IsFolder ? x.Name : MediaPresentation.Title(x.Item), filename = x.Name, folder = x.IsFolder, path = x.IsFolder ? x.FolderPath : Json.Text(x.Item,"Path"),
                            size = x.Size.HasValue ? (double?)Math.Round(x.Size.Value / 1000000.0,2) : null, modified = x.ModifiedUtc.HasValue ? x.ModifiedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—",
                            type = MediaPresentation.Category(x), bitrate = x.Bitrate > 0 ? (x.Bitrate/1000000.0).ToString("0.0")+" Mbps" : "—",
                            danmu = x.IsFolder ? x.Members.Count(y => y.HasXml) + " / " + x.Members.Length + " 集已就绪" : x.DanmuLabel, count = x.IsFolder ? x.Members.Length : 1,
                            searchTitle = MediaNames.SearchTitle(x.IsFolder ? x.Members[0].Item : x.Item), keys = x.SelectionKeys, selected = x.SelectionKeys.All(core.Library.Selection.Contains), mixed = x.SelectionKeys.Any(core.Library.Selection.Contains) && !x.SelectionKeys.All(core.Library.Selection.Contains) }).ToArray();
                case "select":
                    foreach (string key in Json.Array(p,"keys").Select(Convert.ToString)) { if (core.Library.Entries.Any(x => x.Key == key)) core.Library.Selection.Set(key,B(p,"selected")); }
                    core.Publish(); return true;
                case "selectClear": core.Library.Selection.Clear(); core.Publish(); return true;
                case "cover":
                    var coverItem = core.Library.Entries.FirstOrDefault(x=>x.Key==S(p,"key")); if(coverItem==null)return null;
                    string imageId=Json.Text(coverItem.Item,"SeriesId"),tag=Json.Text(coverItem.Item,"SeriesPrimaryImageTag");
                    if(String.IsNullOrEmpty(imageId)||String.IsNullOrEmpty(tag)){imageId=Json.Text(coverItem.Item,"Id");tag=Json.Text(Json.Child(coverItem.Item,"ImageTags"),"Primary");}
                    if(String.IsNullOrEmpty(tag))return null;
                    string posterKey=DandanApiCache.Key("poster:"+s.ServerId+":"+imageId+":"+tag),encoded=await Task.Run(()=>core.Gateway.Catalog.Cache.Read(posterKey));
                    if(encoded==null){if(!core.Services.OwnsProcess||String.IsNullOrEmpty(core.Services.Api.Token))return null;
                        byte[] image=await core.Services.Api.Poster(imageId,tag,CancellationToken.None);if(image==null||image.Length==0||image.Length>4*1024*1024)return null;
                        encoded=Convert.ToBase64String(image);core.Gateway.Catalog.Cache.Write(posterKey,"poster",MediaNames.SearchTitle(coverItem.Item),encoded,MediaNames.SearchTitle(coverItem.Item));}
                    return "data:image/jpeg;base64,"+encoded;
                case "prepare": var videos = B(p,"all") ? core.Library.Entries : Selected(); if (videos.Length == 0) throw new InvalidOperationException("请先选择影片。"); core.StartPreparation(videos.Select(x=>x.Item)); return true;
                case "pause": core.CancelBatch(); return true;
                case "resume": core.StartPreparation(new Dictionary<string, object>[0],true); return true;
                case "cancel": await core.CancelCurrentDownloads(); return true;
                case "retry": var t = core.DownloadTasks.FirstOrDefault(x => LibraryEntry.Identity(x.Item) == S(p,"id")); if(t != null) core.RetryTask(t); else core.RetryBatch(B(p,"failures")); return true;
                case "sourceChoices": return core.Gateway.Catalog.SourceChoices();
                case "search":
                    SearchHistory.Add("danmu",S(p,"keyword"));
                    var searchSeed = Selected().FirstOrDefault(); int season = I(p,"season");
                    if(season==0) season=searchSeed==null?SmartMatching.SeasonTitle(S(p,"keyword")):SmartMatching.Season(searchSeed.Item);
                    var found = await core.Gateway.Catalog.Search(S(p,"keyword").Trim(), B(p,"animeOnly"), B(p,"smart"), season, S(p,"provider") == "" ? null : S(p,"provider"), CancellationToken.None, Selected().Length > 1 || searchSeed != null && SmartMatching.IsEpisodic(searchSeed.Item));
                    sources = found.Items.OfType<Dictionary<string, object>>().ToArray();
                    episodes = new Dictionary<string,object>[0]; episodeAnime = null;
                    return sources.Select((x,i) => new { index=i, name=Json.Text(x,"Name"), site=Json.Text(x,"Site"), year=Json.Text(x,"Year"), id=Json.Text(x,"Id"), category=Json.Text(x,"Category"), count=Json.Text(x,"EpisodeSize") }).ToArray();
                case "identify":
                    var local = Selected().FirstOrDefault(); if (local == null) throw new InvalidOperationException("请先选择本地影片。");
                    var match = await core.Gateway.Catalog.IdentifyFile(local.Item,CancellationToken.None);
                    sources = match.Animes; episodes = new Dictionary<string,object>[0]; episodeAnime = null;
                    return new { status = match.Status, sources = sources.Select((x,i) => new { index=i,name=Json.Text(x,"Name"),site=Json.Text(x,"Site"),id=Json.Text(x,"Id"),category=Json.Text(x,"Category"),count=Json.Text(x,"EpisodeSize") }).ToArray() };
                case "episodes":
                    int sourceIndex = I(p,"index",-1);
                    var requestedAnime = S(p,"id") != "" ? core.Gateway.Catalog.KnownAnime(S(p,"id")) : sourceIndex >= 0 && sourceIndex < sources.Length ? sources[sourceIndex] : null;
                    if(requestedAnime == null) throw new InvalidOperationException("请重新搜索并选择作品。");
                    episodes = new Dictionary<string,object>[0]; episodeAnime = null;
                    episodes = (await core.Gateway.Catalog.Episodes(requestedAnime)).OfType<Dictionary<string, object>>().ToArray(); episodeAnime = requestedAnime;
                    return episodes.Select((x,i) => new { index=i, id=Json.Text(x,"Id"), number=Json.Text(x,"Number"), title=Json.Text(x,"Title") }).ToArray();
                case "plan":
                    var chosen = Selected(); if(chosen.Length == 0) throw new InvalidOperationException("请选择本地影片或文件夹。");
                    if (S(p,"scope") == "season") { var seed=chosen[0].Item; chosen=core.Library.Entries.Where(x=>SmartMatching.SameSeason(seed,x.Item)).ToArray(); }
                    MediaLibrary.RequireSameSeason(chosen.Select(x=>x.Item));
                    if(S(p,"id") != "" && Json.Text(episodeAnime,"Id") != S(p,"id")) {
                        episodeAnime=core.Gateway.Catalog.KnownAnime(S(p,"id"));
                        if(episodeAnime==null)throw new InvalidOperationException("请重新搜索并选择作品。");
                        episodes=(await core.Gateway.Catalog.Episodes(episodeAnime)).OfType<Dictionary<string,object>>().ToArray();
                    }
                    if(episodeAnime==null || episodes.Length==0)throw new InvalidOperationException("请先选择作品并读取完整剧集。");
                    if(chosen.Length>1 && !DanmuCatalog.IsSeries(episodeAnime))throw new InvalidOperationException("当前选择的是电影或单条弹幕来源，不能匹配整季。请返回选择对应季度的电视剧或动画来源，核对来源旁的类型和集数。");
                    core.BatchPlan = BatchMatching.Plan(chosen.Select(x=>x.Item), episodes); core.BatchTitle="弹幕批量下载"; core.BatchKeep=B(p,"keep"); core.Publish(); return State();
                case "planSelect":
                    if(core.BatchRunning) throw new InvalidOperationException("下载中不能调整列表。");
                    int pi=I(p,"index",-1); if(core.BatchPlan != null && pi>=0 && pi<core.BatchPlan.Count) core.BatchPlan[pi].Selected=core.BatchPlan[pi].Remote!=null && B(p,"selected"); core.Publish(); return true;
                case "downloadBatch": core.BatchKeep=B(p,"keep"); core.StartBatch(); return true;
                case "downloadSingle":
                    if(S(p,"id") != "" && Json.Text(episodeAnime,"Id") != S(p,"id")) {
                        episodeAnime=core.Gateway.Catalog.KnownAnime(S(p,"id"));
                        if(episodeAnime==null)throw new InvalidOperationException("请重新搜索并选择作品。");
                        episodes=(await core.Gateway.Catalog.Episodes(episodeAnime)).OfType<Dictionary<string,object>>().ToArray();
                    }
                    int ep=I(p,"episode",-1); var item=Selected().FirstOrDefault(); if(item==null || ep<0 || ep>=episodes.Length) throw new InvalidOperationException("请选择本地影片和在线集数。");
                    if(S(p,"episodeId") != "") {ep=Array.FindIndex(episodes,x=>Json.Text(x,"Id")==S(p,"episodeId"));if(ep<0)throw new InvalidOperationException("在线集数已更改，请重新选择。");}
                    Background(core.DownloadSingle(item.Item,episodes[ep])); return true;
                case "refreshDanmu": await core.RefreshDanmu(); return true;
                case "history": return SearchHistory.List(S(p,"scope"));
                case "historyAdd": SearchHistory.Add(S(p,"scope"),S(p,"text")); return true;
                case "historyRemove": SearchHistory.Remove(S(p,"scope"),S(p,"text")); return true;
                case "historyClear": SearchHistory.Clear(S(p,"scope")); return true;
                case "cache": return (await Task.Run(()=>core.Gateway.Catalog.Cache.Entries())).Select(x=>new { id=x.Key, anime=x.Anime, kind=x.TypeLabel, label=x.Label, size=x.SizeLabel, created=x.CreatedLabel, expires=x.ExpiresLabel }).ToArray();
                case "cacheRemove": await Task.Run(()=>core.Gateway.Catalog.Cache.Remove(Json.Array(p,"keys").Select(Convert.ToString))); return true;
                case "cacheClear": await Task.Run(()=>core.Gateway.Catalog.Cache.Clear(B(p,"expired"))); return true;
                case "cacheRetention": core.Gateway.Catalog.Cache.SetRetention(I(p,"months",3)); core.Publish(); return true;
                case "logs": return Log.RecentText();
                case "logsClear": await core.ClearLogs(); return true;
                case "logRetention": await core.SaveLogRetention(I(p,"days",30)); return true;
                case "sources":
                    DanmuCatalog.ValidateAdditionalApis(S(p,"customApis"));
                    Persist(()=>{s.EnableDandan=B(p,"dandan"); s.EnableAnimeko=B(p,"animeko"); s.EnableBahamut=B(p,"bahamut"); s.EnableExistingDanmu=B(p,"existing"); s.AnimeOnly=B(p,"animeOnly"); s.EncryptedAdditionalApis=SettingsStore.Protect(S(p,"customApis"));}); core.Publish(); return true;
                case "preferences":
                    int port=I(p,"port",s.Port), dp=I(p,"danmuPort",s.DanmuPort);
                    if((core.Services.OwnsProcess||core.Gateway.Running)&&(port!=s.Port||dp!=s.DanmuPort)) throw new InvalidOperationException("请先停止服务再修改端口。");
                    if(port<1024||port>65535||dp<1024||dp>65535||port==dp) throw new ArgumentException("端口须在 1024–65535 之间，且不能相同。");
                    int scale=I(p,"scale",100), interval=I(p,"interval",3); if((scale!=100&&scale!=120)||interval<1||interval>30) throw new ArgumentException("设置数值无效。");
                    if(core.Services.OwnsProcess && !String.IsNullOrEmpty(core.Services.Api.Token)) await core.Services.Api.SetOriginalPolicy(B(p,"original"));
                    Persist(()=>{s.Port=port;s.DanmuPort=dp;s.CloseToTray=B(p,"closeToTray");s.StartServicesOnLaunch=B(p,"launch");s.PreferOriginal=B(p,"original");s.UiScalePercent=scale;s.DownloadIntervalSeconds=interval;});
                    core.Publish(); return true;
                case "initialize": if(!core.Services.OwnsProcess) await core.StartAll(); await core.Services.Api.Initialize(S(p,"name"),S(p,"password")); await core.Services.Api.SetOriginalPolicy(s.PreferOriginal); core.Publish(); return true;
                case "addLibrary":
                    string addedName=await core.Services.Api.AddLibrary(S(p,"path"),S(p,"name"),S(p,"type"));s.MediaFolder=S(p,"path");s.LibraryName=addedName;s.LibraryType=S(p,"type");SettingsStore.Save(s);Background(core.ScanLibrary());return new {name=addedName};
                case "libraries": return await new LibraryTypeManager(core.Services.Api).Libraries();
                case "removeLibrary":
                    await core.RemoveLibrary(Json.Child(p,"library"));sources=new Dictionary<string,object>[0];episodes=new Dictionary<string,object>[0];episodeAnime=null;return new {status=core.Status};
                case "libraryType": var libs=await new LibraryTypeManager(core.Services.Api).Libraries(); var lib=libs.FirstOrDefault(x=>Json.Text(x,"Name")==S(p,"name")); if(lib==null)throw new InvalidOperationException("找不到媒体库。"); await core.ChangeLibraryType(lib,S(p,"type"));return true;
                case "connection": return new { server="http://"+S(p,"ip")+":"+s.Port, danmu="http://"+S(p,"ip")+":"+s.DanmuPort+"/"+SettingsStore.Unprotect(s.EncryptedDanmuKey), admin=s.AdminName };
                case "schedule":
                    var action=(PowerAction)I(p,"action",4); if(!Enum.IsDefined(typeof(PowerAction),action))throw new ArgumentException("无效的到时操作。");
                    if(S(p,"mode")=="weekly")core.StartWeeklySchedule(I(p,"days"),TimeSpan.Parse(S(p,"time")),action,B(p,"awake"));
                    else if(S(p,"mode")=="at")core.StartSchedule(null,DateTime.Parse(S(p,"date")+" "+S(p,"time")),action,B(p,"awake"));
                    else core.StartSchedule(TimeSpan.FromSeconds(I(p,"seconds",1800)),null,action,B(p,"awake")); return State();
                case "scheduleCancel":core.CancelSchedule();return true;
                case "deletePlan":
                    var kind=(MediaDeleteKind)I(p,"kind",2);if(!Enum.IsDefined(typeof(MediaDeleteKind),kind))throw new ArgumentException("无效操作。");
                    var plan=MediaDeletion.Plan(Selected(),kind,s.MediaFolder);string token=Guid.NewGuid().ToString("N");deletions.Clear();deletions[token]=plan;return new { token=token,message=plan.Confirmation };
                case "deleteConfirm":var id=S(p,"token");MediaDeletePlan pending;if(!deletions.TryGetValue(id,out pending))throw new InvalidOperationException("删除确认已过期。");deletions.Remove(id);await core.DeleteMedia(pending);return true;
                case "firewall":await core.RunScript("configure-firewall.ps1",true);return true;
                case "shutdown":
                    await core.ShutdownHeadless();
                    app.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>app.Shutdown()));return true;
                default:throw new InvalidOperationException("未知操作："+method);
            }
        }
    }
}
