using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DanmuCinema.Desktop
{
    // Owns application lifetime. Nothing in this object may retain a closed visual tree.
    public sealed class DesktopSession
    {
        public string Page = "overview", Filter = "", Ip = "", ScrollKey = "";
        public int Sort, Order;
        public bool LibraryDetails;
        public System.Windows.Rect? WorkspaceBounds;
        public readonly LibraryNavigation Navigation = new LibraryNavigation();
        public readonly LibraryNavigation CacheNavigation = new LibraryNavigation();
        public int CacheFilter;
        public readonly Dictionary<string, object> Draft = new Dictionary<string, object>();
        public readonly Dictionary<string, double> ScrollOffsets = new Dictionary<string, double>();
        public MatchState Match;
    }
    public sealed class MatchState
    {
        public Dictionary<string, object> Item;
        public Dictionary<string, object>[] Selected;
        public DanmuMatchScope Scope;
        public string Keyword, ServiceId, Status = "选择作品和对应集数，核对后下载。";
        public bool AnimeOnly, Smart = true, Open = true;
        public object[] Sources = new object[0], Episodes = new object[0];
        public int SourceIndex = -1, EpisodeIndex = -1;
    }
    public sealed partial class DesktopController : IDisposable
    {
        public readonly AppSettings Settings;
        public readonly ServiceManager Services;
        public readonly DanmuGateway Gateway;
        public readonly AutomaticDanmu Automatic;
        public readonly MediaLibrary Library = new MediaLibrary();
        public readonly DesktopSession Session = new DesktopSession();
        public readonly Scheduler Scheduler = new Scheduler(new SystemClock());
        public bool FrontendVisible;
        public bool Headless;
        public event Action RequestForeground;
        public void StartHeadless() { SystemEvents.PowerModeChanged += PowerChanged; statusTimer.Start(); }
        public async Task ShutdownHeadless()
        {
            Closing = true; statusTimer.Stop();
            CancelSchedule();
            if (libraryCancellation != null) libraryCancellation.Cancel();
            CancelBatch(); PausePreparation();
            if (currentDownload != null && !currentDownload.Completion.Task.IsCompleted) {
                currentDownload.Cancellation.Cancel(); await currentDownload.Completion.Task;
            }
            await StopAll();
        }
        public event Action Changed;
        public string Status = "就绪", ServerStatus = "已停止", PluginStatus = "启动并登录后检测", SessionStatus = "暂无播放", ScheduleStatus = "尚未设置定时任务";
        public bool Busy { get; private set; }
        public bool Loading { get; private set; }
        public LibraryLoadProgress LibraryProgress { get; private set; }
        public bool Closing { get; private set; }
        public bool ReleasingWindow { get; private set; }
        public bool BatchRunning { get; private set; }
        public List<BatchEntry> BatchPlan;
        public string BatchTitle = "", BatchStatus = "";
        public bool BatchKeep;
        public double ScheduleInitial;
        readonly Application application;
        readonly DispatcherTimer statusTimer, scheduleTimer;
        CancellationTokenSource batchCancellation;
        CancellationTokenSource libraryCancellation;
        bool checking, disposed, warningShown, awakeHeld, weeklyKeepAwake;
        DateTime lastLibraryAttempt, lastLibraryRefresh;
        public DesktopController(Application application, AppSettings settings, bool infrastructure = true, DanmuCatalog catalog = null)
            : this(application, settings, infrastructure, catalog, null) { }
        public DesktopController(Application application, AppSettings settings, bool infrastructure, DanmuCatalog catalog, System.Net.Http.HttpMessageHandler jellyfinHandler)
        {
            this.application = application; Settings = settings;
            Log.Configure(settings.LogRetentionDays);
            Services = new ServiceManager(settings, jellyfinHandler); Gateway = new DanmuGateway(settings, catalog);
            Automatic = new AutomaticDanmu(settings, Services.Api, Gateway.Catalog, () => Services.OwnsProcess && !Services.Transitioning && !Closing && !Busy && !BatchRunning && !CancellingDownloads);
            Automatic.Saved += AutoSaved;
            Automatic.Progress += AutoProgress;
            Gateway.PreparePlayback = Automatic.PrepareForPlayback;
            statusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
            statusTimer.Tick += StatusTick;
            scheduleTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(250) };
            scheduleTimer.Tick += ScheduleTick;
        }
        public string LocalUrl { get { return "http://127.0.0.1:" + Settings.Port; } }
        public async Task Initialize(bool forceStart)
        {
            if (forceStart || Settings.StartServicesOnLaunch || Services.OwnsProcess) await Execute(StartAll);
            else await UpdateStatus();
        }
        public void Publish() { var handler = Changed; if (handler != null) handler(); }
        public void ShowWindow() { var handler = RequestForeground; if (handler != null) handler(); }
        public void ReleaseWindow() { }
        public async Task Execute(Func<Task> action, bool showError = true)
        {
            if (Busy || Loading || BatchRunning || Closing || CancellingDownloads || Scheduler.State == ScheduleState.Executing) return;
            Busy = true; Status = "正在处理，请稍候…"; Publish();
            try { await action(); if (Status == "正在处理，请稍候…") Status = "操作完成"; }
            catch (Exception e)
            {
                Status = e.Message; Log.Write(e.Message);
                if (showError) Notify("需要处理", e.Message);
            }
            finally { Busy = false; Publish(); }
            await UpdateStatus();
        }
        public async Task StartAll()
        {
            await Services.Start();
            try { Gateway.Start(); } catch (Exception e) { throw new InvalidOperationException("视频服务已启动，但弹幕端口启动失败：" + e.Message); }
            if (!String.IsNullOrEmpty(Services.Api.Token) && !String.IsNullOrEmpty(Settings.UserId)) await Services.Api.SetOriginalPolicy(Settings.PreferOriginal);
            Automatic.Start();
        }
        public async Task StopAll() { await Automatic.Stop(); await Gateway.Stop(); await Services.Stop(); }
        void AutoSaved(string video)
        { application.Dispatcher.BeginInvoke(new Action(async () => { if (disposed || Closing) return; Status = "自动弹幕已缓存并保存 · " + Path.GetFileName(video); if (!Loading && !Busy && !BatchRunning) await RefreshMetadata(); Publish(); })); }
        public Task ScanLibrary() { return ReadLibrary(true); }
        public async Task RemoveLibrary(Dictionary<string,object> library)
        {
            if(Busy||Loading||BatchRunning||Preparing||CancellingDownloads||Closing||currentDownload!=null&&!currentDownload.Completion.Task.IsCompleted)
                throw new InvalidOperationException("当前有任务正在处理，请等待或暂停任务后再删除媒体库。");
            bool resume=Automatic.Running;Busy=true;Status="正在删除媒体库记录…";Publish();
            try {
                await Automatic.Stop();
                var remaining=await new LibraryTypeManager(Services.Api).Remove(library);
                string[] removed=Json.Array(library,"Locations").Select(Convert.ToString).ToArray();
                string[] retained=remaining.SelectMany(x=>Json.Array(x,"Locations")).Select(Convert.ToString).ToArray();
                Func<string,string,bool> below=(file,folder)=>!String.IsNullOrEmpty(folder)&&!String.IsNullOrEmpty(file)&&(String.Equals(file.TrimEnd('\\'),folder.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)||file.StartsWith(folder.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase));
                Func<Dictionary<string,object>,bool> keep=item=>!removed.Any(root=>below(Json.Text(item,"Path"),root))||retained.Any(root=>below(Json.Text(item,"Path"),root));
                Library.Selection.Clear();Session.Match=null;Session.Navigation.Visit(null,"");BatchPlan=null;
                Library.ReplaceEntries(Library.Entries.Where(x=>keep(x.Item)).ToArray());
                if(preparationPlan!=null){preparationPlan=preparationPlan.Skip(preparationIndex).Where(keep).ToList();preparationIndex=0;}
                var fallback=remaining.FirstOrDefault(x=>MediaAuto.Valid(Json.Text(x,"CollectionType")==""?"mixed":Json.Text(x,"CollectionType"))&&Json.Array(x,"Locations").Length>0);
                if(fallback==null || Settings.LibraryName==Json.Text(library,"Name") || removed.Any(root=>below(Settings.MediaFolder,root))&&!retained.Any(root=>below(Settings.MediaFolder,root))) {
                    Settings.MediaFolder=fallback==null?"":Convert.ToString(Json.Array(fallback,"Locations")[0]);
                    Settings.LibraryName=fallback==null?"我的影片":Json.Text(fallback,"Name");Settings.LibraryType=fallback==null||Json.Text(fallback,"CollectionType")==""?"mixed":Json.Text(fallback,"CollectionType");
                    SettingsStore.Save(Settings);
                }
                Publish();
                try {await ScanLibrary();Status="媒体库记录已删除，视频、字幕与弹幕文件保留；请刷新客户端媒体库。";}
                catch(Exception error){Status="媒体库记录已删除，索引刷新未完成，请重新扫描。";Log.Write(Status+" "+error.Message);}
            } catch(Exception error){Status=error.Message;throw;}
            finally {Busy=false;if(resume&&!Closing&&!disposed)Automatic.Start();Publish();}
        }
        public async Task ChangeLibraryType(Dictionary<string, object> library, string type)
        {
            bool resume = Automatic.Running; await Automatic.Stop();
            try
            {
                Status = "正在调整媒体库识别方式…"; Publish();
                bool changed = await new LibraryTypeManager(Services.Api).Change(library, type);
                if (Json.Text(library, "Name") == Settings.LibraryName)
                { Settings.LibraryType = type; SettingsStore.Save(Settings); Session.Draft.Remove("library-type"); }
                if (!changed) { Status = "此媒体库已使用所选识别方式。"; return; }
                Library.Selection.Clear(); Session.Match = null; BatchPlan = null;
                await ScanLibrary(); Status = "媒体库识别方式调整完成 · " + MediaAuto.Label(type) + "；请刷新客户端媒体库。"; Publish();
            }
            finally { if (resume && !Closing) Automatic.Start(); }
        }
        async void StatusTick(object sender, EventArgs e) { Log.CleanupIfDue(); await UpdateStatus(); }
        public async Task UpdateStatus()
        {
            if (checking || Closing || disposed || Services.Transitioning || Busy || BatchRunning) return;
            checking = true;
            try
            {
                if (!Services.OwnsProcess)
                {
                    ServerStatus = Services.DesiredRunning ? "异常停止，请查看日志" : (Paths.FindServer() == null ? "组件未安装" : "已停止");
                    PluginStatus = "启动并登录后检测"; SessionStatus = "暂无播放";
                    if (Gateway.Running) await Gateway.Stop();
                }
                else if (FrontendVisible)
                {
                    ServerStatus = "运行中 · 服务版本 " + Json.Text(await Services.Api.PublicInfo(), "Version") + " · HTTP " + Settings.Port;
                    if (String.IsNullOrEmpty(Services.Api.Token)) { PluginStatus = "请初始化 / 登录管理员"; SessionStatus = "尚未登录"; }
                    else
                    {
                        try
                        {
                            var plugin = (await Services.Api.Plugins()).OfType<Dictionary<string, object>>().FirstOrDefault(x => Json.Text(x, "Name").IndexOf("Danmu", StringComparison.OrdinalIgnoreCase) >= 0);
                            PluginStatus = plugin == null ? "未加载，停止服务后修复组件" : Json.Text(plugin, "Status") + " · " + Json.Text(plugin, "Version");
                            var playing = (await Services.Api.Sessions()).OfType<Dictionary<string, object>>().Where(x => Json.Child(x, "NowPlayingItem") != null).ToArray();
                            SessionStatus = playing.Length == 0 ? "暂无播放" : String.Join("；", playing.Select(x => "客户端播放 · " + Json.Text(Json.Child(x, "NowPlayingItem"), "Name") + " · " + Json.Text(Json.Child(x, "PlayState"), "PlayMethod")));
                        }
                        catch { PluginStatus = "登录已失效或接口异常，请重新登录"; }
                    }
                }
                else ServerStatus = "运行中 · HTTP " + Settings.Port;
            }
            catch { ServerStatus = "服务正在启动或暂时无响应"; }
            finally { checking = false; Publish(); }
            if (FrontendVisible && Services.OwnsProcess && !String.IsNullOrEmpty(Services.Api.Token) && !Busy && !Loading && (Library.Entries.Length == 0 || Session.Page == "library" && (DateTime.UtcNow - lastLibraryRefresh).TotalSeconds >= 30) && (DateTime.UtcNow - lastLibraryAttempt).TotalSeconds >= 10)
            {
                try { await LoadLibrary(); } catch { Status = "暂时无法读取媒体库，请登录或刷新重试。"; Publish(); }
            }
        }
        public Task LoadLibrary() { return ReadLibrary(false); }
        async Task ReadLibrary(bool scan)
        {
            if (Loading) return;
            Loading = true; lastLibraryAttempt = DateTime.UtcNow;
            var cancellation = new CancellationTokenSource(); libraryCancellation = cancellation;
            Status = scan ? "正在准备扫描媒体库…" : "正在读取媒体库…";
            LibraryProgress = new LibraryLoadProgress(Status); Publish();
            try
            {
                var items = await new LibraryScanner(Services.Api).Load(scan, progress =>
                { if (disposed || Closing) return; LibraryProgress = progress; Status = progress.Message; Publish(); }, cancellation.Token);
                var previous=Library.Entries.ToDictionary(x=>x.Key,x=>Json.Text(x.Item,"DanmuCinemaCategory"));
                foreach(var item in items){string kind;if(previous.TryGetValue(LibraryEntry.Identity(item),out kind)&&kind=="动漫")item["DanmuCinemaCategory"]=kind;}
                await Gateway.Catalog.ClassifyLibrary(items,Settings.MediaFolder,scan,progress=>{
                    if(disposed||Closing)return;
                    application.Dispatcher.BeginInvoke(new Action(()=>{if(disposed||Closing||!Loading||libraryCancellation!=cancellation)return;LibraryProgress=progress;Status=progress.Message;Publish();}));
                },cancellation.Token);
                bool classificationSynced=true;
                try {await Services.Api.SyncClassification(items,cancellation.Token);}
                catch {cancellation.Token.ThrowIfCancellationRequested();classificationSynced=false;Log.Write("影片列表分类已完成，客户端动漫信息暂未同步；请确认已安装最新组件，之后重新扫描重试。");}
                LibraryProgress = new LibraryLoadProgress("正在整理影片列表…"); Status = LibraryProgress.Message; Publish();
                var entries = await Task.Run(() => MediaLibrary.Build(items));
                if (disposed || Closing) return;
                Library.ReplaceEntries(entries); lastLibraryRefresh = DateTime.UtcNow;
                Status = items.Length == 0 ? "媒体库暂无文件，请检查媒体目录。" : (scan ? "媒体库扫描完成，共 " : "媒体库已加载，共 ") + entries.Length + " 个视频。";
                if(!classificationSynced)Status+=" 客户端分类同步未完成，请查看日志。";
                if (scan) Log.Write(Status);
            }
            finally { libraryCancellation = null; cancellation.Dispose(); Loading = false; LibraryProgress = null; if (!disposed) Publish(); }
        }
        public async Task RefreshMetadata()
        {
            var items = Library.Entries.Select(x => x.Item).ToArray();
            Loading = true; Publish();
            try { Library.ReplaceEntries(await Task.Run(() => MediaLibrary.Build(items))); }
            finally { Loading = false; Publish(); }
        }
        public async Task DeleteMedia(MediaDeletePlan plan)
        {
            bool resume = Automatic.Running;
            var affected = Library.Entries.Where(x => plan.Affects(Json.Text(x.Item, "Path"))).Select(x => Json.Text(x.Item, "Id")).ToArray();
            await Automatic.Stop();
            Exception failure = null;
            try
            {
                await Task.Run(() => MediaDeletion.Execute(plan, CancellationToken.None));
                Status = plan.Title + "完成，共 " + plan.Targets.Length + " 个目标。"; Log.Write(Status);
            }
            catch (Exception e) { failure = e; }
            try
            {
                // Also reflect partial deletion on I/O errors. Prevent the active playback from recreating removed XML.
                if (plan.Kind == MediaDeleteKind.Danmu) Automatic.SuppressUntilPlaybackEnds(affected);
                Library.ReplaceEntries(await Task.Run(() => MediaLibrary.Build(Library.Entries.Select(x => x.Item).Where(x => File.Exists(Json.Text(x, "Path"))).ToArray())));
                Publish();
            }
            finally { if (resume && !Closing) Automatic.Start(); }
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            if (plan.Kind != MediaDeleteKind.Danmu && Services.OwnsProcess)
                try { await Services.Api.Request("POST", "Library/Refresh", new { }, true); }
                catch { Log.Write("本地文件已删除，Jellyfin 列表稍后可手动扫描刷新。"); }
        }
        public async Task RefreshDanmu()
        {
            var selected = Library.SelectedItems;
            if (selected.Length == 0) throw new InvalidOperationException("请先选择影片。");
            int success = 0, failed = 0;
            foreach (var item in selected) { try { await Services.Api.Request("GET", "api/danmu/" + Json.Text(item, "Id") + "/refresh", null, true); success++; } catch { failed++; } }
            await RefreshMetadata(); Status = "刷新弹幕：成功 " + success + " 个，失败 " + failed + " 个。"; Log.Write(Status);
        }
        public async Task SavePreferences(int port, int danmuPort, bool closeToTray, bool launch, bool original, bool autoStart)
        {
            if ((Services.OwnsProcess || Gateway.Running) && (port != Settings.Port || danmuPort != Settings.DanmuPort)) throw new InvalidOperationException("请先停止服务，再修改端口。");
            if (port < 1024 || port > 65535 || danmuPort < 1024 || danmuPort > 65535 || port == danmuPort) throw new ArgumentException("端口须在 1024 到 65535 之间，并且不能相同。");
            if (Services.OwnsProcess && !String.IsNullOrEmpty(Services.Api.Token)) await Services.Api.SetOriginalPolicy(original);
            var before = Json.Read<AppSettings>(Json.Write(Settings)); bool oldAuto = AutoStart.Enabled;
            try
            {
                Settings.Port = port; Settings.DanmuPort = danmuPort; Settings.CloseToTray = closeToTray; Settings.StartServicesOnLaunch = launch; Settings.PreferOriginal = original;
                SettingsStore.Save(Settings); AutoStart.Set(autoStart);
            }
            catch
            {
                Settings.Port = before.Port; Settings.DanmuPort = before.DanmuPort; Settings.CloseToTray = before.CloseToTray; Settings.StartServicesOnLaunch = before.StartServicesOnLaunch; Settings.PreferOriginal = before.PreferOriginal;
                SettingsStore.Save(Settings); try { AutoStart.Set(oldAuto); } catch { } throw;
            }
            Log.Write("设置已保存；关闭窗口行为立即生效。");
        }
        public Task SaveLogRetention(int days)
        {
            if (days < 1 || days > 3650) throw new ArgumentException("日志保留天数必须在 1 到 3650 之间。");
            int previous = Settings.LogRetentionDays; Settings.LogRetentionDays = days;
            try { SettingsStore.Save(Settings); } catch { Settings.LogRetentionDays = previous; throw; }
            Log.Configure(days);
            Status = "日志最多保留 " + days + " 天，过期日志已清理。";
            Publish(); return Task.FromResult(0);
        }
        public Task ClearLogs()
        { Status = Log.ClearAll().Message; Publish(); return Task.FromResult(0); }
        public async Task RunScript(string name, bool elevated)
        {
            if (!elevated && (Services.OwnsProcess || Gateway.Running)) throw new InvalidOperationException("请先停止服务，再安装或修复运行组件。");
            var args = "-NoProfile -ExecutionPolicy Bypass -File " + AutoStart.Quote(Path.Combine(Paths.Root, "scripts", name));
            if (elevated) args += " -MediaPort " + Settings.Port + " -DanmuPort " + Settings.DanmuPort;
            var info = new ProcessStartInfo("powershell.exe", args) { UseShellExecute = elevated, WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = !elevated };
            if (elevated) info.Verb = "runas";
            else { info.RedirectStandardOutput = info.RedirectStandardError = true; info.StandardOutputEncoding = info.StandardErrorEncoding = System.Text.Encoding.UTF8; }
            using (var process = new Process { StartInfo = info })
            {
                if (!elevated) process.OutputDataReceived += (s, e) => { if (!String.IsNullOrWhiteSpace(e.Data)) Log.Write(e.Data); };
                process.Start(); Task<string> errors = null;
                if (!elevated) { process.BeginOutputReadLine(); errors = process.StandardError.ReadToEndAsync(); }
                await Task.Run(() => process.WaitForExit()); string error = errors == null ? "" : await errors;
                if (process.ExitCode != 0) throw new InvalidOperationException("操作未完成：" + error);
            }
        }
        public async Task RunBatch(Func<Dictionary<string, object>, CancellationToken, Task<string>> download = null)
        {
            if (BatchRunning || Busy || Loading || BatchPlan == null || Closing || CancellingDownloads) return;
            if (!BatchPlan.Any(x => x.Selected && x.Remote != null)) throw new InvalidOperationException("没有选择可下载的集数。");
            var operation = BeginDownload(BatchPlan.Where(x => x.Selected && x.Remote != null).Select(x => x.Local));
            BatchRunning = true; batchCancellation = operation.Cancellation; Publish();
            try
            {
                foreach (var entry in BatchPlan.Where(x => x.Selected && x.Remote != null)) SetDownloadState(entry.Local, entry.Status, "批量下载", Gateway.Catalog.EpisodeSource(entry.Remote), entry.Remote);
                var result = await BatchDownloads.Run(BatchPlan, episode => download == null ? Gateway.Catalog.Download(episode, batchCancellation.Token) : download(episode, batchCancellation.Token), BatchKeep, batchCancellation.Token,
                    (entry, done, total) => { SetDownloadState(entry.Local, entry.Status, "批量下载", Gateway.Catalog.EpisodeSource(entry.Remote), entry.Remote); BatchStatus = "处理 " + done + " / " + total + " · 第 " + entry.Number + " 集 " + entry.Status; Publish(); }, Settings.DownloadIntervalSeconds * 1000, entry => Gateway.Catalog.RecordAssociation(entry.Local, entry.Remote));
                foreach (var entry in BatchPlan.Where(x => x.Selected && x.Remote != null)) SetDownloadState(entry.Local, entry.Status, "批量下载", Gateway.Catalog.EpisodeSource(entry.Remote), entry.Remote);
                BatchStatus = (result.Cancelled ? "已停止" : "下载完成") + "：保存 " + result.Saved + " 集，跳过 " + result.Skipped + " 集，失败 " + result.Failed + " 集。"; Log.Write(BatchStatus);
            }
            catch (Exception e) { BatchStatus = "下载未完成：" + e.Message; Log.Write(BatchStatus); }
            finally { BatchRunning = false; batchCancellation = null; Publish(); }
            try { await RefreshMetadata(); } finally { FinishDownload(operation); }
        }
        public void CancelBatch() { if (Preparing) PausePreparation(); else if (batchCancellation != null) batchCancellation.Cancel(); }
        public async void StartBatch()
        {
            try { await RunBatch(); }
            catch (Exception error) { BatchStatus = error.Message; Log.Write(error.Message); Publish(); }
        }
        public void StartSchedule(TimeSpan? delay, DateTime? localTime, PowerAction action, bool awake)
        {
            if (Closing || Scheduler.Active || Scheduler.State == ScheduleState.Executing) return;
            PowerActions.Validate(action);
            try
            {
                if (delay.HasValue) Scheduler.StartDelay(delay.Value, action);
                else
                {
                    var local = DateTime.SpecifyKind(localTime.Value, DateTimeKind.Unspecified);
                    if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local)) throw new ArgumentException("此时间处于夏令时切换区间，请使用倒计时。");
                    Scheduler.StartAt(new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)), action);
                }
                if (awake) { PowerActions.KeepAwake(true); awakeHeld = true; }
                weeklyKeepAwake = false;
                ScheduleInitial = Scheduler.Remaining.TotalSeconds; warningShown = false;
                ScheduleStatus = "定时" + PowerActions.Name(action) + "进行中"; scheduleTimer.Start(); Publish();
            }
            catch { Scheduler.Cancel(); ReleaseAwake(); throw; }
        }
        public void CancelSchedule()
        {
            if (!Scheduler.Cancel()) return;
            weeklyKeepAwake = false;
            scheduleTimer.Stop(); ReleaseAwake(); ScheduleStatus = "已取消定时，原定操作不会执行。"; Log.Write(ScheduleStatus); UpdateTraySchedule(); Publish();
        }
        public void StartWeeklySchedule(int days, TimeSpan time, PowerAction action, bool awake)
        {
            if (Closing || Scheduler.Active || Scheduler.State == ScheduleState.Executing) return;
            PowerActions.Validate(action);
            try
            {
                Scheduler.StartWeekly(days, time, action);
                if (awake) { PowerActions.KeepAwake(true); awakeHeld = true; }
                weeklyKeepAwake = awake;
                ScheduleInitial = Scheduler.Remaining.TotalSeconds; warningShown = false;
                ScheduleStatus = "每周" + PowerActions.Name(action) + "计划进行中"; scheduleTimer.Start(); Publish();
            }
            catch { Scheduler.Cancel(); ReleaseAwake(); throw; }
        }
        void PowerChanged(object sender, PowerModeChangedEventArgs e)
        { if (e.Mode == PowerModes.Resume) application.Dispatcher.BeginInvoke(new Action(() => { if (!disposed) { Scheduler.OnResume(); warningShown = false; } })); }
        async void ScheduleTick(object sender, EventArgs e)
        {
            if (!Scheduler.Active || Closing) return;
            if (Busy || Loading || BatchRunning) { UpdateTraySchedule(); Publish(); return; }
            if (!warningShown && Scheduler.State == ScheduleState.Warning && Scheduler.Remaining <= TimeSpan.Zero) Scheduler.OnResume();
            bool execute = Scheduler.Poll();
            if (Scheduler.State == ScheduleState.Warning && !warningShown)
            {
                warningShown = true; Session.Page = "schedule"; ShowWindow();
                Notify("即将" + PowerActions.Name(Scheduler.Action), "可取消定时或按 Esc。执行阶段开始后无法撤回。");
            }
            UpdateTraySchedule(); Publish(); if (!execute) return;
            scheduleTimer.Stop(); ReleaseAwake(); Busy = true;
            ScheduleStatus = "正在执行" + PowerActions.Name(Scheduler.Action) + "，无法再取消。"; Publish();
            try { if (Scheduler.Action == PowerAction.StopServices) await StopAll(); else await Task.Run(() => PowerActions.Execute(Scheduler.Action)); Scheduler.Finish(true); ScheduleStatus = "定时操作已完成。"; }
            catch (Exception error) { Scheduler.Finish(false); ScheduleStatus = "执行失败：" + error.Message; ShowWindow(); Notify("定时操作失败", error.Message); }
            finally
            {
                Busy = false;
                if (Scheduler.Active && !Closing)
                {
                    try { if (weeklyKeepAwake) { PowerActions.KeepAwake(true); awakeHeld = true; } warningShown = false; ScheduleInitial = Scheduler.Remaining.TotalSeconds; scheduleTimer.Start(); }
                    catch (Exception error) { Scheduler.Cancel(); weeklyKeepAwake = false; ScheduleStatus = "计划已停止：" + error.Message; }
                }
                else weeklyKeepAwake = false;
                Log.Write(ScheduleStatus); UpdateTraySchedule(); Publish();
            }
            await UpdateStatus();
        }
        void UpdateTraySchedule() { }
        void ReleaseAwake() { if (!awakeHeld) return; try { PowerActions.KeepAwake(false); } catch (Exception e) { Log.Write(e.Message); } finally { awakeHeld = false; } }
        public event Action<string,string> Notice;
        void Notify(string title, string message) { var handler=Notice; if(handler!=null)handler(title,message); }
        public async Task Exit() { await ShutdownHeadless(); application.Shutdown(); }
        public static void Open(string path) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            statusTimer.Stop(); statusTimer.Tick -= StatusTick; scheduleTimer.Stop(); scheduleTimer.Tick -= ScheduleTick;
            SystemEvents.PowerModeChanged -= PowerChanged;
            Scheduler.Cancel(); ReleaseAwake();
            if (preparationCancellation != null) preparationCancellation.Cancel();
            if (libraryCancellation != null) libraryCancellation.Cancel();
            Automatic.Progress -= AutoProgress; Automatic.Saved -= AutoSaved; Automatic.Dispose();
            Services.Dispose(); Gateway.Dispose(); Changed = null; RequestForeground = null; Notice = null;
        }
    }
}
