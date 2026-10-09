using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DanmuCinema.Desktop
{
    public static class MotionTests
    {
        sealed class Fixture : HttpMessageHandler
        {
            public int Requests;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            { Requests++; await Task.Delay(50, cancellation); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"comments\":[{\"p\":\"1,1,16777215,0\",\"m\":\"fixture\"}]}") }; }
        }
        static readonly List<string> report = new List<string>();
        static readonly FieldInfo Surface = typeof(DialogWindow).GetField("animatedSurface", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo Backdrop = typeof(DialogWindow).GetField("backdrop", BindingFlags.Instance | BindingFlags.NonPublic);
        public static int Run()
        {
            string original = Paths.Root, output = Path.Combine(Paths.TestOutputFor(original), "motion"); Directory.CreateDirectory(output); Paths.Root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Paths.Root);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; DesktopController controller = null; DanmuCatalog catalog = null; JellyfinApi api = null;
            try
            {
                System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext()); Ui.InstallTheme(app);
                var deferred = new Border { Width = 200, Height = 120, Background = Brushes.Blue };
                SurfaceMotion.FadeIn(deferred); Pause(220);
                Check(!SystemParameters.ClientAreaAnimation || deferred.Opacity == 0, "未加载的视图保持初始帧，构建耗时不消耗淡入时间");
                var owner = new Window { Width = 960, Height = 720, Content = deferred, Background = Ui.Brush("#183F72"), Style = (Style)app.FindResource(typeof(Window)) }; owner.Show();
                CheckEntrance(deferred, "延后加载的视图"); owner.Close();
                var settings = new AppSettings { MediaFolder = Path.Combine(Paths.Root, "videos"), EnableDandan = true, EnableExistingDanmu = false, EnableAnimeko = false, EnableBahamut = false, DownloadIntervalSeconds = 1 };
                Directory.CreateDirectory(settings.MediaFolder); Directory.CreateDirectory(Path.GetDirectoryName(DandanConfig.FilePath)); SettingsStore.AtomicWrite(DandanConfig.FilePath, Json.Write(new DandanConfig { AppId = "fixture", EncryptedAppSecret = SettingsStore.Protect("fixture-only") }), false);
                var fixture = new Fixture(); api = new JellyfinApi(settings); catalog = new DanmuCatalog(settings, api, fixture); controller = new DesktopController(app, settings, false, catalog);
                var files = Enumerable.Range(1, 2).Select(i => { string path = Path.Combine(settings.MediaFolder, "S01E" + i + ".mkv"); File.WriteAllText(path, "fixture"); return new Dictionary<string, object> { { "Id", "fixture" + i }, { "Path", path }, { "Name", "动画测试" }, { "SeriesName", "动画测试" }, { "Type", "Episode" }, { "ParentIndexNumber", 1 }, { "IndexNumber", i } }; }).ToArray();
                var episodes = Enumerable.Range(1, 2).Select(i => new Dictionary<string, object> { { "Id", "dandan:" + (100 + i) }, { "Provider", "dandan" }, { "Number", i.ToString() }, { "Title", "第" + i + "集" }, { "AnimeId", "dandan:10" }, { "Site", "fixture" } }).ToArray();
                controller.Library.Replace(files); controller.ShowWindow(); controller.Window.Navigate("library"); Pause(180); var main = controller.Window.View;
                controller.Session.Match = new MatchState { Item = files[0], Selected = files, Scope = DanmuMatchScope.Selection, Keyword = "动画测试", ServiceId = "dandan", SourceIndex = 0, EpisodeIndex = 0, Sources = new object[] { new Dictionary<string, object> { { "Id", "dandan:10" }, { "Provider", "dandan" }, { "Name", "动画测试" }, { "Site", "fixture" } } }, Episodes = episodes.Cast<object>().ToArray() };
                controller.Window.Match(files[0], DanmuMatchScope.Selection, files);
                var pane = Children<FloatingPane>(main).Single(); var match = Children<MatchView>(pane).Single();
                CheckEntrance(pane, "匹配已选影片浮层"); Check(match.Opacity == 1 && !match.HasAnimatedProperties, "新浮层只淡入一次，正文不叠加第二段动画");
                Click(pane, "预览已选影片并下载");
                var batch = Children<BatchView>(pane).Single(); Pause(35);
                Check(!SystemParameters.ClientAreaAnimation || match.Opacity < 1 && match.IsVisible && !batch.IsVisible, "打开批量预览先淡出来源正文");
                Pause(105); CheckEntrance(batch, "嵌套批量预览正文");
                controller.Window.RequestWorkspaceClose(); Pause(35); Check(!SystemParameters.ClientAreaAnimation || batch.Opacity < 1 && batch.IsVisible, "关闭子预览采用统一淡出"); Pause(320);
                Check(controller.Window.WorkspaceVisible && match.IsVisible && match.IsHitTestVisible && !batch.IsVisible, "子级关闭返回来源，父级恢复可操作");
                controller.Window.RequestWorkspaceClose(); CheckExit(pane, "匹配浮层"); Check(!controller.Window.WorkspaceVisible && main.IsVisible && main.Opacity == 1, "来源关闭保留不透明主窗口");
                var source = new SourcesWindow(controller); controller.Window.Track(source); var surface = (Grid)Surface.GetValue(source);
                CheckEntrance(surface, "管理接口完整表面"); Check(((Image)Backdrop.GetValue(source)).Source == null, "打开动画结束释放背景快照");
                var child = new DialogWindow("子弹窗动画", 780, 540); controller.Window.Track(child, source); CheckEntrance((Grid)Surface.GetValue(child), "原生子弹窗完整表面");
                child.Close(); var childSurface = (Grid)Surface.GetValue(child); CheckExit(childSurface, "原生子弹窗"); Check(source.IsVisible && main.IsVisible, "原生子弹窗关闭保留直接父级与主窗口");
                source.Close(); Check(((Image)Backdrop.GetValue(source)).Source != null, "关闭管理接口淡向父窗口快照，不淡向空白底色");
                CheckExit(surface, "管理接口"); Check(source.Opacity == 1 && main.Opacity == 1 && main.IsVisible, "淡出不使用原生窗口透明度，保留防闪白机制"); Check(Surface.GetValue(source) == null && ((Image)Backdrop.GetValue(source)).Source == null, "关闭后释放动画树与背景快照");
                foreach (bool accept in new[] { true, false })
                {
                    var alert = new AlertWindow("动画确认", "仅模拟，不修改媒体", true, "模拟确定", "模拟取消") { Owner = main };
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) }; timer.Tick += (s, e) => { timer.Stop(); Click(alert, accept ? "模拟确定" : "模拟取消"); }; timer.Start();
                    Check(alert.ShowDialog() == accept, "统一动画保持模态弹窗结果：" + accept);
                }
                var immediate = new DialogWindow("快速关闭", 780, 540); controller.Window.Track(immediate); immediate.Close(); Pause(220); Check(!immediate.IsVisible && main.IsVisible, "打开尚未完成时关闭不重显或影响父窗口");
                controller.Window.Navigate("tasks"); controller.Window.ShowBatch(); pane = Children<FloatingPane>(main).Single(); CheckEntrance(pane, "任务页独立批量预览"); controller.Window.RequestWorkspaceClose(); Pause(200); Check(!controller.Window.WorkspaceVisible, "独立预览关闭不误开旧来源页面");
                controller.SetDownloadState(files[0], "下载失败", "单集下载", "fixture", episodes[0]); controller.Publish(); Pause(100);
                var retry = Children<Button>(main).Single(x => (string)x.Tag == "retry" && ((TaskRow)x.DataContext).Task.Item == files[0]); var row = Ui.Ancestor<DataGridRow>(retry); retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); CheckEntrance(row, "任务重试反馈"); Settle(controller);
                controller.BatchPlan = new List<BatchEntry> { new BatchEntry { Local = files[1], Remote = episodes[1], Number = 2, Selected = true, Status = "下载失败" } };
                controller.Window.ShowBatch(); Pause(200); var feedback = Children<BatchView>(main).Single().Children.OfType<StackPanel>().Last(); controller.Publish(); Click(main, "仅重试失败项"); CheckEntrance(feedback, "整季重试反馈"); Settle(controller);
                Check(DesktopController.HasSidecar(files[0]) && DesktopController.HasSidecar(files[1]), "重试动效不影响模拟单集与批量下载结果");
                controller.Window.RequestWorkspaceClose(); controller.ReleaseWindow(); controller.ShowWindow(); Pause(300); Check(controller.Window.View.IsVisible, "托盘释放取消旧动画回调，恢复的新窗口不被关闭");
                Check(fixture.Requests > 0 && fixture.Requests <= 2, "重试验证仅使用模拟响应，真实 API 未调用（模拟请求 " + fixture.Requests + "）");
                report.Add("PASS: " + report.Count(x => x.StartsWith("PASS ")) + " current motion checks; no real API/video operations."); return 0;
            }
            catch (Exception error) { report.Add("FAIL " + error); return 1; }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "report.txt"), report);
                try { if (controller != null) { controller.ReleaseWindow(); controller.Dispose(); } if (catalog != null) catalog.Dispose(); if (api != null) api.Dispose(); app.Shutdown(); }
                catch (Exception error) { report.Add("FAIL cleanup " + error); }
                Paths.Root = original; File.WriteAllLines(Path.Combine(output, "report.txt"), report);
            }
        }
        static void CheckEntrance(FrameworkElement element, string name)
        {
            bool middle = false; for (int i = 0; i < 6; i++) { Pause(20); middle |= element.Opacity > 0 && element.Opacity < 1; }
            Pause(100); Check(!SystemParameters.ClientAreaAnimation || middle, name + "有可见的淡入中间帧"); Check(element.Opacity == 1 && element.IsHitTestVisible, name + "淡入完成后不透明且可操作");
        }
        static void CheckExit(FrameworkElement element, string name)
        { Pause(35); Check(!SystemParameters.ClientAreaAnimation || element.Opacity > 0 && element.Opacity < 1 && !element.IsHitTestVisible, name + "使用120ms淡出并阻止重复操作"); Pause(180); }
        static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject { if (root == null) yield break; if (root is T) yield return (T)root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Children<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
        static void Click(DependencyObject root, string label) { Children<Button>(root).Single(x => (x.Content as string) == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        static void Settle(DesktopController controller) { int attempts = 0; while ((controller.Busy || controller.Loading || controller.BatchRunning) && attempts++ < 600) Pause(25); if (attempts >= 600) throw new TimeoutException(); }
        static void Check(bool condition, string name) { if (!condition) throw new Exception(name); report.Add("PASS " + name); }
        static void Pause(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
    }
}
