using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DanmuCinema.Desktop
{
    // Only this round's cancellation, floating layers, motion and vector edges.
    public static class InteractionFixTests
    {
        sealed class Fixture : HttpMessageHandler
        {
            public int Requests;
            public TaskCompletionSource<HttpResponseMessage> Pending;
            public TaskCompletionSource<bool> Started = new TaskCompletionSource<bool>();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            { Requests++; Started.TrySetResult(true); return Pending == null ? Task.FromResult(Response()) : Pending.Task; }
            public static HttpResponseMessage Response() { return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"comments\":[{\"p\":\"1,1,16777215,0\",\"m\":\"fixture\"}]}") }; }
        }
        static readonly List<string> report = new List<string>();
        const string Xml = "<i><d p='1,1,25,16777215,0,0,0,0'>fixture</d></i>";
        public static int Run()
        {
            string original = Paths.Root, output = Path.Combine(original, "tests", "output", "interaction-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(output); Paths.Root = output; var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            DesktopController controller = null; DanmuCatalog catalog = null; JellyfinApi api = null;
            try
            {
                System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext()); Ui.InstallTheme(app);
                var settings = new AppSettings { MediaFolder = Path.Combine(output, "videos"), DownloadIntervalSeconds = 1, EnableDandan = true, EnableExistingDanmu = false, EnableAnimeko = false, EnableBahamut = false };
                Directory.CreateDirectory(Path.GetDirectoryName(DandanConfig.FilePath)); SettingsStore.AtomicWrite(DandanConfig.FilePath, Json.Write(new DandanConfig { AppId = "fixture", EncryptedAppSecret = SettingsStore.Protect("fixture-only") }), false);
                var fixture = new Fixture(); api = new JellyfinApi(settings); catalog = new DanmuCatalog(settings, api, fixture); controller = new DesktopController(app, settings, false, catalog);
                var folder = Path.Combine(settings.MediaFolder, "测试番剧"); Directory.CreateDirectory(folder);
                var files = Enumerable.Range(1, 4).Select(i => { string path = Path.Combine(folder, "S01E" + i.ToString("D2") + ".mkv"); File.WriteAllText(path, "video " + i); return new Dictionary<string, object> { { "Id", "fixture" + i }, { "Path", path }, { "Name", "测试番剧" }, { "SeriesName", "测试番剧" }, { "Type", "Episode" }, { "ParentIndexNumber", 1 }, { "IndexNumber", i } }; }).ToArray();
                Func<int, Dictionary<string, object>> episode = i => new Dictionary<string, object> { { "Id", "dandan:" + (100 + i) }, { "Provider", "dandan" }, { "Number", i.ToString() }, { "Title", "第" + i + "集" }, { "AnimeId", "dandan:10" }, { "Site", "弹弹play 官方" } };
                File.WriteAllText(Sidecar(files[2]), "pending old XML"); File.WriteAllText(Sidecar(files[3]), "unrelated XML"); controller.Library.Replace(files);
                controller.BatchPlan = BatchMatching.Plan(files.Take(3), Enumerable.Range(1, 3).Select(episode));
                var started = new TaskCompletionSource<bool>(); var late = new TaskCompletionSource<string>();
                var batch = controller.RunBatch((remote, token) => { if (SmartMatching.RemoteNumber(remote) == 1) return Task.FromResult(Xml); started.TrySetResult(true); return late.Task; }); Wait(started.Task);
                Check(File.Exists(Sidecar(files[0])) && File.Exists(Sidecar(files[2])), "取消前包含已下载文件及等待下载的旧XML");
                var cancelling = controller.CancelCurrentDownloads(); Check(!cancelling.IsCompleted && controller.CancellingDownloads, "取消先等待当前下载结束，不与写入并发删除"); late.SetResult(Xml); Wait(Task.WhenAll(batch, cancelling));
                Check(files.Take(3).All(x => !File.Exists(Sidecar(x))) && File.ReadAllText(Sidecar(files[3])) == "unrelated XML", "取消清理本次已下载和未下载影片的XML，不影响其他任务");
                Check(files.All(x => File.ReadAllText(Json.Text(x, "Path")) == "video " + Json.Text(x, "IndexNumber")), "取消不改动视频文件内容");
                Check(!controller.CanCancelDownloads && controller.BatchPlan.All(x => x.Status == "已取消并清理"), "已取消的批次无法继续下载或重复清理");
                controller.BatchPlan = BatchMatching.Plan(new[] { files[0] }, new[] { episode(1) }); Wait(controller.RunBatch((remote, token) => Task.FromResult("<i/>"))); Wait(controller.CancelCurrentDownloads());
                Check(controller.Status.Contains("清理 0 个"), "尚未产生XML的任务也可正常取消");
                var prepared = new TaskCompletionSource<bool>(); var preparing = controller.PrepareVideos(files.Take(3), false, async (item, token) => { File.WriteAllText(Sidecar(item), Xml); prepared.TrySetResult(true); await Task.Delay(100, token); return true; }); Wait(prepared.Task); controller.PausePreparation(); Wait(preparing); Wait(controller.CancelCurrentDownloads());
                Check(!controller.PreparationPaused && files.Take(3).All(x => !File.Exists(Sidecar(x))), "暂停的提前准备队列可取消，完成文件删除且未完成队列清除");
                fixture.Pending = new TaskCompletionSource<HttpResponseMessage>(); var single = controller.DownloadSingle(files[0], episode(1)); Wait(fixture.Started.Task); var singleCancel = controller.CancelCurrentDownloads(); fixture.Pending.SetResult(Fixture.Response()); try { Wait(single); } catch (OperationCanceledException) { } Wait(singleCancel); fixture.Pending = null;
                Check(!File.Exists(Sidecar(files[0])) && !controller.CancellingDownloads, "单集取消阻止迟到响应再次生成XML");
                controller.Library.Replace(files); controller.ShowWindow(); controller.Window.Navigate("library"); Pause(220); var main = controller.Window.View;
                Check(Buttons(main).All(x => (x.Content as string) != "更多") && new[] { "扫描媒体库", "管理接口" }.All(x => Buttons(main).Any(b => (b.Content as string) == x)) && Children<CheckBox>(main).Any(x => x.Content is TextBlock && ((TextBlock)x.Content).Text == "完整列信息"), "媒体库工具栏完整展示原更多菜单功能，移除下载任务快捷按钮");
                var open = Buttons(main).Single(x => (x.Content as string) == "打开文件夹"); var row = Ui.Ancestor<DataGridRow>(open); var point = open.TranslatePoint(new Point(0, open.ActualHeight / 2), row);
                Check(open.HorizontalContentAlignment == HorizontalAlignment.Center && Math.Abs(point.Y - row.ActualHeight / 2) < 2, "文件夹按钮在弹幕单元格水平与垂直居中");
                var rootRow = (LibraryRow)Children<DataGrid>(main).Single().Items[0]; rootRow.Selected = true; Pause(35);
                var bulk = (WrapPanel)Buttons(main).Single(x => (x.Content as string) == "匹配已选影片").Parent;
                Check(!SystemParameters.ClientAreaAnimation || (bulk.Opacity > 0 && bulk.Opacity < 1 && bulk.MaxHeight < Double.PositiveInfinity), "选中后操作栏淡入并平滑展开，文字不缩放"); Pause(210); Capture(main, Path.Combine(output, "library.png"));
                var history = Children<HistoryInput>(main).Single(); history.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var popup = (Popup)typeof(HistoryInput).GetField("popup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(history);
                Check(popup.PopupAnimation == PopupAnimation.Slide, "历史下拉使用展开动画"); popup.IsOpen = false;
                foreach (var combo in Children<ComboBox>(main)) { combo.ApplyTemplate(); var drop = (Popup)combo.Template.FindName("PART_Popup", combo); Check(drop.PopupAnimation == PopupAnimation.Slide, "排序下拉使用展开动画"); }
                controller.Window.Navigate("overview"); Pause(180); Check(!Children<TextBlock>(main).Any(x => x.Text == "弹幕下载任务"), "服务总览移除弹幕下载任务卡片");
                controller.Window.Navigate("tasks"); Pause(180); Check(Buttons(main).Any(x => (x.Content as string) == "取消并删除弹幕") && Buttons(main).Where(x => new[] { "为已选影片准备", "暂停准备", "继续准备", "批量下载预览", "停止并保留弹幕" }.Contains(x.Content as string)).All(x => x.ToolTip != null), "下载任务取消入口与各按钮功能提示可见"); Capture(main, Path.Combine(output, "tasks.png"));
                controller.Window.Navigate("library"); Pause(160);
                controller.Session.Match = new MatchState { Item = files[0], Selected = files.Take(3).ToArray(), Scope = DanmuMatchScope.Selection, Keyword = "测试番剧", ServiceId = "dandan", Sources = new object[] { new Dictionary<string, object> { { "Id", "dandan:10" }, { "Provider", "dandan" }, { "Name", "测试番剧" }, { "Site", "弹弹play 官方" } } }, Episodes = Enumerable.Range(1, 3).Select(episode).Cast<object>().ToArray(), SourceIndex = 0, EpisodeIndex = 0, Open = true };
                controller.Window.Match(files[0], DanmuMatchScope.Selection, files.Take(3).ToArray()); Pause(240);
                var pane = Children<FloatingPane>(main).Single(); var originalBounds = pane.Bounds; pane.ResizeBy("bottom-right", -200, -80); pane.MoveBy(40, 20); Pause(80);
                Check(pane.Bounds.Width <= originalBounds.Width && pane.Bounds.X >= originalBounds.X && pane.Bounds.Width > 0, "浮层支持拖动与缩放，并保持在主窗口范围内");
                Check(!Buttons(pane).Any(x => (x.Content as string) == "展开" || (x.Content as string) == "收起") && Buttons(pane).Any(x => (string)x.Tag == "close"), "浮层右上角仅保留叉叉");
                Click(pane, "预览已选影片并下载"); Settle(controller); Pause(220); Check(Children<BatchView>(pane).Single().Visibility == Visibility.Visible, "下载预览在当前浮层进入子级");
                controller.Window.RequestWorkspaceClose(); Pause(220); Check(controller.Window.WorkspaceVisible && Children<MatchView>(pane).Single().Visibility == Visibility.Visible && Children<BatchView>(pane).Single().Visibility == Visibility.Collapsed, "关闭下载预览只返回上一级来源页面");
                Capture(main, Path.Combine(output, "floating.png")); controller.Window.RequestWorkspaceClose(); Pause(230); Check(!controller.Window.WorkspaceVisible && main.IsVisible, "关闭来源页面只关闭浮层，保留主窗口");
                var parent = new SourcesWindow(controller); controller.Window.Track(parent); Pause(220); var child = new DialogWindow("子弹窗测试", 780, 540); controller.Window.Track(child, parent); Pause(200); child.Close(); Pause(35);
                var surfaceField = typeof(DialogWindow).GetField("animatedSurface", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic); var closingSurface = (Grid)surfaceField.GetValue(child);
                Check(!SystemParameters.ClientAreaAnimation || child.IsVisible, "子弹窗关闭经过淡出阶段再销毁"); Pause(200); Check(!child.IsVisible && parent.IsVisible && main.IsVisible, "原生子弹窗淡出关闭不关闭父窗口");
                Check(surfaceField.GetValue(child) == null && closingSurface.Children.Count == 0 && !closingSurface.HasAnimatedProperties, "弹窗关闭后释放完整动画树，避免淡出时钟保留窗口资源");
                parent.Close(); Pause(220); Check(main.IsVisible, "父弹窗淡出后主窗口保留");
                var alert = new AlertWindow("确认测试", "仅模拟，不删除文件", true, "测试确认", "测试取消") { Owner = main };
                app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Click(alert, "测试确认"))); Check(alert.ShowDialog() == true, "带淡出动画的确认弹窗保持确定结果");
                foreach (double dpi in new[] { 96.0, 120.0, 144.0, 192.0 })
                {
                    var border = new SmoothBorder { Width = 140, Height = 46, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), BorderBrush = (Brush)Ui.Resource("Accent"), Background = (Brush)Ui.Resource("Surface") }; border.Measure(new Size(140, 46)); border.Arrange(new Rect(0, 0, 140, 46));
                    var image = new RenderTargetBitmap((int)(140 * dpi / 96), (int)(46 * dpi / 96), dpi, dpi, PixelFormats.Pbgra32); image.Render(border); var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
                    Check(Enumerable.Range(0, pixels.Length / 4).Any(i => pixels[i * 4 + 3] > 0 && pixels[i * 4 + 3] < 255), "圆角矢量边缘具有抗锯齿覆盖：" + (dpi / 96 * 100) + "%缩放"); Save(image, Path.Combine(output, "focus-" + dpi + ".png"));
                }
                Check(fixture.Requests == 1, "全部测试仅一次模拟网络响应，真实弹幕接口未调用");
                report.Add("PASS: " + report.Count(x => x.StartsWith("PASS ")) + " current interaction checks; no real API/video operations."); return 0;
            }
            catch (Exception error) { report.Add("FAIL " + error); return 1; }
            finally { if (controller != null) { controller.ReleaseWindow(); controller.Dispose(); } if (catalog != null) catalog.Dispose(); if (api != null) api.Dispose(); app.Shutdown(); Paths.Root = original; File.WriteAllLines(Path.Combine(output, "report.txt"), report); File.WriteAllText(Path.Combine(original, "tests", "output", "interaction-latest.txt"), output); }
        }
        static string Sidecar(Dictionary<string, object> item) { return Path.ChangeExtension(Json.Text(item, "Path"), ".xml"); }
        static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject { if (root == null) yield break; if (root is T) yield return (T)root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Children<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
        static IEnumerable<Button> Buttons(DependencyObject root) { return Children<Button>(root); }
        static void Click(DependencyObject root, string label) { Buttons(root).Single(x => (x.Content as string) == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        static void Check(bool value, string name) { if (!value) throw new Exception(name); report.Add("PASS " + name); }
        static void Settle(DesktopController controller) { while (controller.Busy || controller.Loading || controller.BatchRunning) Pause(30); }
        static void Wait(Task task) { int count = 0; while (!task.IsCompleted && count++ < 1200) Pause(25); if (!task.IsCompleted) throw new TimeoutException(); task.GetAwaiter().GetResult(); }
        static void Pause(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        static void Capture(FrameworkElement view, string path) { view.UpdateLayout(); var image = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(view); Save(image, path); }
        static void Save(BitmapSource image, string path) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var file = File.Create(path)) encoder.Save(file); }
    }
}
