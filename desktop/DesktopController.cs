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
        public readonly LibraryNavigation Navigation = new LibraryNavigation();
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
    public sealed class DesktopController : IDisposable
    {
        public readonly AppSettings Settings;
        public readonly ServiceManager Services;
        public readonly DanmuGateway Gateway;
        public readonly MediaLibrary Library = new MediaLibrary();
        public readonly DesktopSession Session = new DesktopSession();
        public readonly Scheduler Scheduler = new Scheduler(new SystemClock());
        public ShellWindow Window { get; private set; }
        public event Action Changed;
        public string Status = "就绪", ServerStatus = "已停止", PluginStatus = "启动并登录后检测", SessionStatus = "暂无播放", ScheduleStatus = "尚未设置定时任务";
        public bool Busy { get; private set; }
        public bool Loading { get; private set; }
        public bool Closing { get; private set; }
        public bool ReleasingWindow { get; private set; }
        public bool BatchRunning { get; private set; }
        public List<BatchEntry> BatchPlan;
        public string BatchTitle = "", BatchStatus = "";
        public bool BatchKeep;
        public double ScheduleInitial;
        readonly Application application;
        readonly DispatcherTimer statusTimer, scheduleTimer;
        readonly Forms.NotifyIcon tray;
        readonly Icon icon;
        readonly EventWaitHandle showSignal;
        readonly RegisteredWaitHandle showWait;
        readonly Forms.ToolStripItem traySchedule, trayCancel;
        CancellationTokenSource batchCancellation;
        bool checking, disposed, warningShown, awakeHeld;
        DateTime lastLibraryAttempt, lastLibraryRefresh;
        public DesktopController(Application application, AppSettings settings, bool infrastructure = true)
        {
            this.application = application; Settings = settings;
            Services = new ServiceManager(settings); Gateway = new DanmuGateway(settings);
            statusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
            statusTimer.Tick += StatusTick;
            scheduleTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(250) };
            scheduleTimer.Tick += ScheduleTick;
            if (!infrastructure) return;
            using (var stream = typeof(DesktopController).Assembly.GetManifestResourceStream("DanmuCinema.AppIcon"))
            using (var source = new Icon(stream)) icon = (Icon)source.Clone();
            tray = new Forms.NotifyIcon { Icon = icon, Text = "弹幕影院：服务已停止", Visible = true };
            tray.DoubleClick += OpenFromTray;
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开控制窗口", null, (s, e) => ShowWindow());
            menu.Items.Add("启动服务", null, async (s, e) => await Execute(StartAll));
            menu.Items.Add("停止服务", null, async (s, e) => await Execute(StopAll));
            menu.Items.Add("打开媒体库", null, (s, e) => Open(LocalUrl + "/web/"));
            traySchedule = menu.Items.Add("当前没有定时任务", null, (s, e) => { Session.Page = "schedule"; ShowWindow(); Window.Navigate("schedule"); });
            trayCancel = menu.Items.Add("取消定时", null, (s, e) => CancelSchedule()); trayCancel.Enabled = false;
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出并停止服务", null, async (s, e) => await Exit()); tray.ContextMenuStrip = menu;
            showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DanmuCinema-Show-" + Program.StableId(Paths.Root));
            showWait = ThreadPool.RegisterWaitForSingleObject(showSignal, (s, t) => application.Dispatcher.BeginInvoke(new Action(() => { if (!disposed && !Closing) ShowWindow(); })), null, Timeout.Infinite, false);
            SystemEvents.PowerModeChanged += PowerChanged;
            statusTimer.Start();
        }
        public string LocalUrl { get { return "http://127.0.0.1:" + Settings.Port; } }
        public async Task Initialize(bool forceStart)
        {
            if (forceStart || Settings.StartServicesOnLaunch || Services.OwnsProcess) await Execute(StartAll);
            else await UpdateStatus();
        }
        public void Publish() { var handler = Changed; if (handler != null) handler(); }
        void OpenFromTray(object sender, EventArgs e) { ShowWindow(); }
        public void ShowWindow()
        {
            if (Closing || disposed) return;
            if (Window == null)
            {
                Window = new ShellWindow(this);
                application.MainWindow = Window.View;
                statusTimer.Interval = TimeSpan.FromSeconds(4);
                Window.Show();
            }
            else Window.Show();
        }
        public void ReleaseWindow()
        {
            if (Window == null || ReleasingWindow) return;
            ReleasingWindow = true;
            try
            {
                var old = Window; Window = null;
                // Application.MainWindow otherwise keeps the last closed window alive.
                application.MainWindow = null;
                old.Release();
                statusTimer.Interval = TimeSpan.FromSeconds(30);
            }
            finally { ReleasingWindow = false; }
        }
        public async Task Execute(Func<Task> action)
        {
            if (Busy || Loading || BatchRunning || Closing || Scheduler.State == ScheduleState.Executing) return;
            Busy = true; Status = "正在处理，请稍候…"; Publish();
            try { await action(); if (Status == "正在处理，请稍候…") Status = "操作完成"; }
            catch (Exception e)
            {
                Status = e.Message; Log.Write(e.Message);
                if (Window != null) System.Windows.MessageBox.Show(Window.View, e.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
                else Notify("需要处理", e.Message);
            }
            finally { Busy = false; Publish(); }
            await UpdateStatus();
        }
        public async Task StartAll()
        {
            await Services.Start();
            try { Gateway.Start(); } catch (Exception e) { throw new InvalidOperationException("视频服务已启动，但弹幕端口启动失败：" + e.Message); }
            if (!String.IsNullOrEmpty(Services.Api.Token) && !String.IsNullOrEmpty(Settings.UserId)) await Services.Api.SetOriginalPolicy(Settings.PreferOriginal);
        }
        public async Task StopAll() { await Gateway.Stop(); await Services.Stop(); }
        public async Task ScanLibrary() { await Services.Api.Request("POST", "Library/Refresh", new { }, true); Log.Write("已提交媒体库扫描。"); lastLibraryRefresh = DateTime.MinValue; }
        async void StatusTick(object sender, EventArgs e) { await UpdateStatus(); }
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
                else if (Window != null)
                {
                    ServerStatus = "运行中 · Jellyfin " + Json.Text(await Services.Api.PublicInfo(), "Version") + " · HTTP " + Settings.Port;
                    if (String.IsNullOrEmpty(Services.Api.Token)) { PluginStatus = "请初始化 / 登录管理员"; SessionStatus = "尚未登录"; }
                    else
                    {
                        try
                        {
                            var plugin = (await Services.Api.Plugins()).OfType<Dictionary<string, object>>().FirstOrDefault(x => Json.Text(x, "Name").IndexOf("Danmu", StringComparison.OrdinalIgnoreCase) >= 0);
                            PluginStatus = plugin == null ? "未加载，停止服务后修复组件" : Json.Text(plugin, "Status") + " · " + Json.Text(plugin, "Version");
                            var playing = (await Services.Api.Sessions()).OfType<Dictionary<string, object>>().Where(x => Json.Child(x, "NowPlayingItem") != null).ToArray();
                            SessionStatus = playing.Length == 0 ? "暂无播放" : String.Join("；", playing.Select(x => Json.Text(x, "Client") + " · " + Json.Text(Json.Child(x, "NowPlayingItem"), "Name") + " · " + Json.Text(Json.Child(x, "PlayState"), "PlayMethod")));
                        }
                        catch { PluginStatus = "登录已失效或接口异常，请重新登录"; }
                    }
                }
                else ServerStatus = "运行中 · HTTP " + Settings.Port;
                if (tray != null) tray.Text = Services.OwnsProcess ? "弹幕影院：视频与弹幕后台运行中" : "弹幕影院：服务已停止";
            }
            catch { ServerStatus = "服务正在启动或暂时无响应"; }
            finally { checking = false; Publish(); }
            if (Window != null && Services.OwnsProcess && !String.IsNullOrEmpty(Services.Api.Token) && !Busy && !Loading && (Library.Entries.Length == 0 || Session.Page == "library" && (DateTime.UtcNow - lastLibraryRefresh).TotalSeconds >= 30) && (DateTime.UtcNow - lastLibraryAttempt).TotalSeconds >= 10)
            {
                try { await LoadLibrary(); } catch { Status = "暂时无法读取媒体库，请登录或刷新重试。"; Publish(); }
            }
        }
        public async Task LoadLibrary()
        {
            if (Loading) return;
            Loading = true; lastLibraryAttempt = DateTime.UtcNow; Status = "正在读取媒体库…"; Publish();
            try
            {
                var items = (await Services.Api.Items("")).OfType<Dictionary<string, object>>().ToArray();
                var entries = await Task.Run(() => MediaLibrary.Build(items));
                if (disposed || Closing) return;
                Library.ReplaceEntries(entries); lastLibraryRefresh = DateTime.UtcNow;
                Status = items.Length == 0 ? "媒体库暂无文件，请扫描媒体库。" : "媒体库已加载，共 " + entries.Length + " 个视频。";
            }
            finally { Loading = false; Publish(); }
        }
        public async Task RefreshMetadata()
        {
            var items = Library.Entries.Select(x => x.Item).ToArray();
            Loading = true; Publish();
            try { Library.ReplaceEntries(await Task.Run(() => MediaLibrary.Build(items))); }
            finally { Loading = false; Publish(); }
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
            if (BatchRunning || Busy || Loading || BatchPlan == null || Closing) return;
            if (!BatchPlan.Any(x => x.Selected && x.Remote != null)) throw new InvalidOperationException("没有选择可下载的集数。");
            BatchRunning = true; batchCancellation = new CancellationTokenSource(); Publish();
            try
            {
                var result = await BatchDownloads.Run(BatchPlan, episode => download == null ? Gateway.Catalog.Download(episode, batchCancellation.Token) : download(episode, batchCancellation.Token), BatchKeep, batchCancellation.Token,
                    (entry, done, total) => { BatchStatus = "处理 " + done + " / " + total + " · 第 " + entry.Number + " 集 " + entry.Status; Publish(); }, 1000, entry => Gateway.Catalog.RecordAssociation(entry.Local, entry.Remote));
                BatchStatus = (result.Cancelled ? "已停止" : "下载完成") + "：保存 " + result.Saved + " 集，跳过 " + result.Skipped + " 集，失败 " + result.Failed + " 集。"; Log.Write(BatchStatus);
            }
            catch (Exception e) { BatchStatus = "下载未完成：" + e.Message; Log.Write(BatchStatus); }
            finally { BatchRunning = false; batchCancellation.Dispose(); batchCancellation = null; Publish(); }
            await RefreshMetadata();
        }
        public void CancelBatch() { if (batchCancellation != null) batchCancellation.Cancel(); }
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
                ScheduleInitial = Scheduler.Remaining.TotalSeconds; warningShown = false;
                ScheduleStatus = "定时" + PowerActions.Name(action) + "进行中"; scheduleTimer.Start(); Publish();
            }
            catch { Scheduler.Cancel(); ReleaseAwake(); throw; }
        }
        public void CancelSchedule()
        {
            if (!Scheduler.Cancel()) return;
            scheduleTimer.Stop(); ReleaseAwake(); ScheduleStatus = "已取消定时，原定操作不会执行。"; Log.Write(ScheduleStatus); UpdateTraySchedule(); Publish();
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
                warningShown = true; Session.Page = "schedule"; ShowWindow(); if (Window != null) Window.Navigate("schedule");
                Notify("即将" + PowerActions.Name(Scheduler.Action), "可取消定时或按 Esc。执行阶段开始后无法撤回。");
            }
            UpdateTraySchedule(); Publish(); if (!execute) return;
            scheduleTimer.Stop(); ReleaseAwake(); Busy = true;
            ScheduleStatus = "正在执行" + PowerActions.Name(Scheduler.Action) + "，无法再取消。"; Publish();
            try { if (Scheduler.Action == PowerAction.StopServices) await StopAll(); else await Task.Run(() => PowerActions.Execute(Scheduler.Action)); Scheduler.Finish(true); ScheduleStatus = "定时操作已完成。"; }
            catch (Exception error) { Scheduler.Finish(false); ScheduleStatus = "执行失败：" + error.Message; ShowWindow(); Notify("定时操作失败", error.Message); }
            finally { Busy = false; Log.Write(ScheduleStatus); UpdateTraySchedule(); Publish(); }
            await UpdateStatus();
        }
        void UpdateTraySchedule()
        {
            if (traySchedule == null) return;
            // Avoid rebuilding tray text four times a second when its visible value is unchanged.
            string text = Scheduler.Active ? PowerActions.Name(Scheduler.Action) + " · 剩余 " + Scheduler.FormatRemaining(Scheduler.Remaining) : "当前没有定时任务";
            if (traySchedule.Text != text) traySchedule.Text = text;
            trayCancel.Enabled = Scheduler.Active;
        }
        void ReleaseAwake() { if (!awakeHeld) return; try { PowerActions.KeepAwake(false); } catch (Exception e) { Log.Write(e.Message); } finally { awakeHeld = false; } }
        void Notify(string title, string message) { if (tray == null) return; tray.BalloonTipTitle = title; tray.BalloonTipText = message; tray.ShowBalloonTip(4000); }
        public async Task Exit()
        {
            if (Closing) return;
            if (Busy || Loading || BatchRunning || Scheduler.State == ScheduleState.Executing) { Status = "当前操作尚未完成，请停止下载或等待完成后退出。"; ShowWindow(); Publish(); return; }
            if (Scheduler.Active)
            {
                scheduleTimer.Stop(); ShowWindow();
                if (System.Windows.MessageBox.Show(Window.View, "当前定时尚未完成。退出将取消任务，确定退出？", "退出弹幕影院", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                { Scheduler.OnResume(); warningShown = false; scheduleTimer.Start(); return; }
            }
            CancelSchedule(); Closing = true; statusTimer.Stop(); Status = "正在停止服务并退出…"; Publish();
            try { await StopAll(); ReleaseWindow(); Dispose(); application.Shutdown(); }
            catch (Exception e) { Closing = false; statusTimer.Start(); Status = "停止服务失败：" + e.Message; ShowWindow(); Publish(); }
        }
        public static void Open(string path) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            statusTimer.Stop(); statusTimer.Tick -= StatusTick; scheduleTimer.Stop(); scheduleTimer.Tick -= ScheduleTick;
            SystemEvents.PowerModeChanged -= PowerChanged;
            if (showWait != null) showWait.Unregister(null); if (showSignal != null) showSignal.Dispose();
            Scheduler.Cancel(); ReleaseAwake();
            if (tray != null) { tray.Visible = false; tray.DoubleClick -= OpenFromTray; if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose(); tray.Dispose(); }
            if (icon != null) icon.Dispose(); Services.Dispose(); Gateway.Dispose(); Changed = null;
        }
    }
}
