using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DanmuCinema.Desktop
{
    public static class DesktopTests
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        public static int Run()
        {
            // All settings, geometry, histories and screenshots stay in a disposable fixture.
            var report = new List<string>(); string originalRoot = Paths.Root;
            string output = Path.Combine(originalRoot, "tests", "output"); Directory.CreateDirectory(output);
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            DesktopController controller = null;
            try
            {
                Paths.Root = Path.Combine(output, "wpf-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Paths.Data);
                Ui.InstallTheme(application);
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                controller = new DesktopController(application, new AppSettings(), false);
                Assert(controller.Window == null && application.Windows.Count == 0, "后台启动不创建窗口", report);
                controller.Library.ReplaceEntries(new[] { Entry("episode (10).mkv", 10), Entry("episode (2).mkv", 2) });
                controller.ShowWindow(); Pump();
                foreach (var key in new[] { "overview", "library", "connect", "setup", "settings", "schedule", "cache", "logs" }) { controller.Window.Navigate(key); Pump(); Assert(controller.Window.View.IsVisible, "WPF 页面可加载：" + key, report); }
                controller.Session.Page = "library"; controller.Window.Navigate("library"); Pump();
                Assert(controller.Library.Browse(null, "", LibrarySort.Name, false).Length == 1, "默认按文件夹展示", report);
                Assert(controller.Library.View("", LibrarySort.Name, false)[0].Name.Contains("(2)"), "名称按集号自然排序", report);
                var grid = Ui.Child<DataGrid>(controller.Window.View);
                var all = (CheckBox)grid.Columns[0].Header; all.IsChecked = true; all.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert(controller.Library.Selection.Count == 2 && grid.SelectedItems.Count == 1, "WPF 表头全选包含文件夹内影片", report);
                all.IsChecked = false; all.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Assert(controller.Library.Selection.Count == 0, "WPF 表头取消全选", report);
                var row = new LibraryRow(controller.Library.Entries[0], controller.Library.Selection); Assert(row.Size.EndsWith(" MB"), "大小统一显示 MB", report);
                row.Selected = true; Assert(controller.Library.Selection.Count == 1, "WPF 复选框更新共享选择模型", report);
                controller.Session.Filter = "episode"; controller.Session.Navigation.Visit(Path.Combine(Paths.Root, "videos"), "episode"); controller.Session.Sort = 2;
                controller.Window.View.WindowState = WindowState.Maximized; Pump();
                controller.ReleaseWindow(); Pump(); Assert(controller.Window == null && application.MainWindow == null && application.Windows.Count == 0, "托盘释放所有窗口且不结束应用", report);
                controller.ShowWindow(); Pump(); Assert(controller.Window.View.WindowState == WindowState.Maximized, "托盘恢复最大化状态", report);
                Assert(controller.Session.Page == "library" && controller.Session.Filter == "episode" && controller.Session.Sort == 2 && controller.Library.Selection.Count == 1, "恢复页面、筛选、排序和多选", report);
                grid = Ui.Child<DataGrid>(controller.Window.View); Assert(grid.Items.Count == 2 && grid.SelectedItems.Count == 1, "恢复后真实列表与复选状态一致", report);
                controller.Window.View.Close(); Pump(); Assert(controller.Window == null, "最大化首次关闭即进入托盘", report);
                controller.ShowWindow(); Pump(); controller.Window.View.WindowState = WindowState.Normal; controller.Window.View.Width = 1080; controller.Window.View.Height = 750; Pump(); controller.ReleaseWindow(); Pump(); controller.ShowWindow(); Pump();
                Assert(Math.Abs(controller.Window.View.Width - 1080) < 2 && Math.Abs(controller.Window.View.Height - 750) < 2, "恢复普通窗口比例", report);
                var references = new List<WeakReference>(); var visuals = new List<WeakReference>();
                for (int i = 0; i < 8; i++) references.Add(Cycle(controller, visuals));
                Settle(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Pump();
                Assert(visuals.All(x => !x.IsAlive), "托盘切换后完整控件树均可回收", report);
                Assert(references.Count(x => x.IsAlive) <= 1 && references.Where(x => x.IsAlive).All(x => ((Window)x.Target).Content == null), "反复打开不累积窗口；运行时仅可能缓存最后一个空窗口", report);
                controller.ShowWindow(); Pump(); controller.Scheduler.StartDelay(TimeSpan.FromMinutes(20), PowerAction.StopServices);
                controller.ReleaseWindow(); Pump(); Assert(controller.Scheduler.Active, "释放界面不取消后台定时任务", report); controller.Scheduler.Cancel();
                TestHistory(report);
                TestPresentation(controller, report, output);
                TestLibraryControls(controller, report);
                TestDeletionControls(controller, report, output);
                TestDialogs(controller, report, output);
                TestSeasonMatching(application, controller, report);
                TestBackgroundBatch(controller, report);
                controller.ReleaseWindow(); Pump(); controller.Dispose();
                controller = new DesktopController(application, new AppSettings(), false); controller.ShowWindow(); Pump();
                Assert(Math.Abs(controller.Window.View.Width - 1080) < 2 && Math.Abs(controller.Window.View.Height - 750) < 2, "新控制器冷启动恢复窗口大小", report);
                report.Add("PASS: " + report.Count + " WPF checks"); return 0;
            }
            catch (Exception e) { report.Add("FAIL: " + e); return 1; }
            finally { if (controller != null) { controller.ReleaseWindow(); controller.Dispose(); } application.Shutdown(); Paths.Root = originalRoot; File.WriteAllLines(Path.Combine(output, "wpf-test.txt"), report, new System.Text.UTF8Encoding(false)); }
        }
        static LibraryEntry Entry(string name, int number)
        {
            return new LibraryEntry { Key = "id:" + number, Name = name, Size = 35L * 1000 * 1000 * 1000, Type = "Episode", SourceLabel = "", Item = new Dictionary<string, object> { { "Id", number.ToString() }, { "Path", Path.Combine(Paths.Root, "videos", name) }, { "Name", name }, { "Type", "Episode" } } };
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static WeakReference Cycle(DesktopController controller, List<WeakReference> visuals)
        { controller.ReleaseWindow(); Pump(); controller.ShowWindow(); Pump(); var weak = new WeakReference(controller.Window.View); visuals.Add(new WeakReference(controller.Window.View.Content)); controller.ReleaseWindow(); Pump(); return weak; }
        static void Pump()
        { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
        static void Settle()
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            EventHandler tick = null; tick = (s, e) => { timer.Stop(); timer.Tick -= tick; frame.Continue = false; };
            timer.Tick += tick; timer.Start(); Dispatcher.PushFrame(frame);
        }
        static void TestHistory(List<string> report)
        {
            SearchHistory.Add("library", "已删除词"); SearchHistory.Remove("library", "已删除词");
            using (var history = new HistoryInput("library", "已删除词", true)) { history.RestoreText("已删除词"); history.Commit(); }
            Assert(SearchHistory.List("library").Length == 0, "恢复筛选或关闭窗口不会重新保存已删除历史", report);
            using (var history = new HistoryInput("library", "", true)) { history.Editor.Text = "新查询"; history.Commit(); }
            Assert(SearchHistory.List("library").SequenceEqual(new[] { "新查询" }), "新查询仍可保存历史", report);
        }
        static void TestLibraryControls(DesktopController controller, List<string> report)
        {
            controller.ShowWindow(); controller.Window.Navigate("library"); Pump();
            var search = Descendants<HistoryInput>(controller.Window.View).Single(); var grid = Ui.Child<DataGrid>(controller.Window.View);
            search.Editor.Text = "(2)"; Pump();
            Assert(grid.Items.Count == 1 && controller.Library.Selection.Count == 1 && grid.SelectedItems.Count == 0, "真实筛选控件隐藏影片时保留勾选", report);
            search.Editor.Clear(); Pump(); Assert(grid.Items.Count == 2 && grid.SelectedItems.Count == 1, "清除筛选恢复真实勾选状态", report);
            var back = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.XButton1) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseUpEvent };
            controller.Window.View.RaiseEvent(back); Pump(); Assert(controller.Session.Navigation.Current.Directory == null, "WPF 鼠标侧键后退恢复文件夹视图", report);
            var forward = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.XButton2) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseUpEvent };
            controller.Window.View.RaiseEvent(forward); Pump(); Assert(controller.Session.Navigation.Current.Directory != null, "WPF 鼠标侧键前进恢复目录", report);
            controller.Window.Navigate("settings"); Pump(); var box = Descendants<TextBox>(controller.Window.View).First(); box.Text = "18096";
            controller.ReleaseWindow(); Pump(); controller.ShowWindow(); Pump();
            Assert(Descendants<TextBox>(controller.Window.View).First().Text == "18096", "托盘恢复保留尚未保存的设置编辑", report);
        }
        static void TestPresentation(DesktopController controller, List<string> report, string output)
        {
            controller.ShowWindow(); controller.Window.Navigate("library"); Pump();
            var window = controller.Window.View;
            var search = Descendants<HistoryInput>(window).Single();
            var mouseDown = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
            search.Editor.RaiseEvent(mouseDown); Pump();
            Assert(search.IsHistoryOpen && !mouseDown.Handled && System.Windows.Input.Mouse.Captured == null, "首次点击历史输入不截获鼠标或消费编辑点击", report);
            var popup = (Popup)typeof(HistoryInput).GetField("popup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(search);
            var historyClear = Descendants<Button>(popup.Child).Single(x => (x.Content as string) == "清空历史");
            var historyTitle = Descendants<TextBlock>(popup.Child).Single(x => x.Text == "搜索历史");
            Assert(historyClear.FontSize == historyTitle.FontSize && historyClear.ActualHeight < 35, "清空历史使用与标题相同字号及紧凑按钮", report);
            Capture((FrameworkElement)popup.Child, Path.Combine(output, "wpf-history.png"));
            var historyArrow = search.Children.OfType<Button>().Single();
            Assert(!historyArrow.Focusable, "历史箭头不抢占编辑焦点", report);
            Assert(((ScrollViewer)search.Editor.Template.FindName("PART_ContentHost", search.Editor)).Background != null, "空白编辑区域参与鼠标命中", report);
            var outside = (Button)window.FindName("Maximize");
            outside.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent });
            Assert(!search.IsHistoryOpen, "点击其他控件直接关闭历史且不阻止本次操作", report);
            var host = (ContentControl)window.FindName("PageHost");
            Assert(host.Opacity == 1 && !host.HasAnimatedProperties && ((TranslateTransform)host.RenderTransform).Y == 0, "切页正文始终完全不透明且不发生分数位移", report);
            Assert(TextOptions.GetTextFormattingMode(host) == TextFormattingMode.Display && TextOptions.GetTextRenderingMode(host) == TextRenderingMode.ClearType, "正文使用像素对齐的 ClearType 渲染", report);
            Assert(window.WindowStyle == WindowStyle.SingleBorderWindow && outside.Content is System.Windows.Shapes.Path, "自绘标题栏保留系统窗口样式并使用加粗矢量图标", report);
            var handle = new WindowInteropHelper(window).Handle;
            Assert((GetWindowLongPtr(handle, -16).ToInt64() & 0x00C00000) == 0x00C00000, "窗口原生 WS_CAPTION 标志存在，支持系统缩放过渡", report);
            // TRANSITIONS_FORCEDISABLED is documented as a setter-only attribute.
            Assert(Ui.EnableWindowTransitions(window), "系统窗口过渡设置成功提交给 DWM", report);
            outside.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Settle();
            Assert(window.WindowState == WindowState.Maximized && (string)outside.Tag == "restore", "标题栏原生命令最大化并更新还原图标", report);
            outside.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Settle();
            Assert(window.WindowState == WindowState.Normal && (string)outside.Tag == "maximize", "原生命令还原并保留普通窗口状态", report);
            var alert = new AlertWindow("退出弹幕影院", "当前定时尚未完成。\n退出将取消任务，确定退出？", true) { Owner = window };
            bool result = true;
            alert.Loaded += (s, e) => alert.Dispatcher.BeginInvoke(new Action(() => { Capture(alert, Path.Combine(output, "wpf-alert.png")); alert.Close(); }));
            result = alert.ShowDialog() == true; Pump();
            Assert(!result && alert.Content == null, "主题提示框关闭默认为取消并释放控件", report);
            var info = new AlertWindow("无法匹配", "请先选择影片。\n选择媒体库内的视频后，可以匹配单集或整个季度。", false) { Owner = window };
            info.Loaded += (s, e) => info.Dispatcher.BeginInvoke(new Action(() => { Capture(info, Path.Combine(output, "wpf-information.png")); Descendants<Button>(info).Single(x => (x.Content as string) == "知道了").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }));
            Assert(info.ShowDialog() == true && info.Content == null, "主题信息框确认后关闭并释放控件", report);
        }
        static void TestDialogs(DesktopController controller, List<string> report, string output)
        {
            controller.ShowWindow(); controller.Window.View.WindowState = WindowState.Normal; controller.Window.View.Width = 1200; controller.Window.View.Height = 820;
            TestCachePage(controller, report, output);
            foreach (var page in new[] { "overview", "library", "settings", "schedule" }) { controller.Window.Navigate(page); Settle(); Capture(controller.Window.View, Path.Combine(output, "wpf-" + page + ".png")); }
            controller.Window.View.Width = 1020; controller.Window.View.Height = 700; Pump();
            var cancel = Descendants<Button>(controller.Window.View).Single(x => (x.Content as string) == "取消定时");
            var position = cancel.TranslatePoint(new Point(0, 0), controller.Window.View);
            Assert(position.Y >= 0 && position.Y + cancel.ActualHeight < controller.Window.View.ActualHeight, "最小窗口尺寸下取消定时按钮始终可见", report);
            controller.Window.View.Width = 1200; controller.Window.View.Height = 820; Pump();
            var video = Path.Combine(Paths.Root, "videos", "episode (2).mkv"); Directory.CreateDirectory(Path.GetDirectoryName(video)); File.WriteAllText(video, "synthetic video");
            var local = new Dictionary<string, object> { { "Id", "2" }, { "Name", "测试番剧第 2 集" }, { "Path", video }, { "Type", "Episode" }, { "SeriesName", "测试番剧" }, { "ParentIndexNumber", 1 }, { "IndexNumber", 2 } };
            var remote = new Dictionary<string, object> { { "Number", "2" }, { "Title", "第 2 集" }, { "Provider", "fixture" }, { "Site", "测试源" }, { "Id", "test-2" } };
            controller.Session.Match = new MatchState { Item = local, Selected = new[] { local, local }, Keyword = "测试番剧", Sources = new object[] { new Dictionary<string, object> { { "Name", "测试番剧" }, { "Year", "2026" }, { "Site", "测试源" } } }, Episodes = new object[] { remote }, SourceIndex = 0, EpisodeIndex = 0 };
            var match = new MatchWindow(controller, controller.Window, controller.Session.Match, false); controller.Window.Track(match); Settle(); Capture(match, Path.Combine(output, "wpf-match.png"));
            var text = Descendants<TextBlock>(match).Select(x => x.Text).ToArray();
            Assert(text.Any(x => x.Contains("测试源")) && text.Any(x => x.Contains("第 2 集")), "匹配弹窗真实绑定显示作品来源和集数", report);
            Assert(Descendants<TabControl>(match).Single().Items.Count == 3, "匹配弹窗提供单集、整季、已选影片三个页签", report);
            var tabs = Descendants<TabControl>(match).Single();
            foreach (TabItem tab in tabs.Items)
            {
                tabs.SelectedItem = tab; Pump();
                var panel = (FrameworkElement)tab.Content;
                Assert(panel.ActualHeight >= panel.DesiredSize.Height && panel.ActualHeight >= 80, "下载页签内容完整显示：" + tab.Header, report);
            }
            tabs.SelectedIndex = 0;
            Capture(match, Path.Combine(output, "wpf-match-150.png"), 144);
            var sources = new SourcesWindow(controller); controller.Window.Track(sources); Settle(); Capture(sources, Path.Combine(output, "wpf-sources.png"));
            Assert(Descendants<CheckBox>(sources).Count() == 5 && !Descendants<TextBox>(sources).Any(x => x.Text.Contains("AppSecret")), "官方接口只显示复选框，保留全部动漫源设置", report);
            Assert(Descendants<CheckBox>(sources).All(x => x.FocusVisualStyle == null && x.Template.FindName("mark", x) is System.Windows.Shapes.Path), "复选框使用抗锯齿矢量勾选，取消默认虚线焦点框", report);
            Capture(sources, Path.Combine(output, "wpf-sources-150.png"), 144);
            controller.BatchPlan = new List<BatchEntry> { new BatchEntry { Local = local, Remote = remote, Selected = true, Number = 2, Status = "待下载" } }; controller.BatchTitle = "测试番剧 · 测试源"; controller.BatchStatus = "可匹配 1 集";
            controller.Window.ShowBatch(); Pump();
            Assert(Application.Current.Windows.OfType<BatchWindow>().Count() == 1, "WPF 批量预览可打开", report);
            controller.ReleaseWindow(); Pump(); Assert(Application.Current.Windows.Count == 0 && controller.Session.Match.Open, "托盘同时释放弹窗且保留匹配状态", report);
            controller.ShowWindow(); Pump(); Assert(Application.Current.Windows.OfType<MatchWindow>().Count() == 1, "恢复时重建匹配弹窗和候选状态", report);
            foreach (var dialog in Application.Current.Windows.OfType<DialogWindow>().ToArray()) dialog.Close(); Pump();
            foreach (var state in new[] { WindowState.Normal, WindowState.Maximized })
            {
                controller.Window.View.WindowState = state; Pump();
                foreach (int i in Enumerable.Range(0, 3))
                {
                    var child = new SourcesWindow(controller); controller.Window.Track(child); Pump();
                    using (var other = new System.Windows.Forms.Form { Text = "Isolated activation fixture", Width = 180, Height = 100 })
                    { other.Show(); other.Activate(); Pump(); controller.Window.View.Activate(); child.Activate(); Pump(); child.Close(); Pump(); }
                    Assert(controller.Window != null && controller.Window.View.IsVisible && IsWindowVisible(new WindowInteropHelper(controller.Window.View).Handle), "切换其他窗口后关闭子窗口保留主窗口：" + state + " " + i, report);
                }
            }
            controller.Window.View.WindowState = WindowState.Normal;
            controller.Window.View.Width = 1080; controller.Window.View.Height = 750;
        }
        sealed class SeasonFixture : HttpMessageHandler
        {
            public int Matches, Details;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                await Task.Delay(10, token); string content;
                if (request.RequestUri.AbsolutePath.EndsWith("match")) { Matches++; content = "{\"success\":true,\"isMatched\":true,\"matches\":[{\"animeId\":10,\"episodeId\":101,\"animeTitle\":\"测试番剧 第三季\",\"episodeTitle\":\"第1话\"}]}"; }
                else if (request.RequestUri.AbsolutePath.Contains("bangumi")) { Details++; content = Json.Write(new { success = true, bangumi = new { animeTitle = "测试番剧 第三季", episodes = Enumerable.Range(1, 12).Select(i => new { episodeId = 100 + i, episodeNumber = i, episodeTitle = "第" + i + "话" }).ToArray() } }); }
                else content = "{\"success\":true,\"animes\":[{\"animeId\":10,\"animeTitle\":\"测试番剧 第三季\",\"typeDescription\":\"动漫\"}]}";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
            }
        }
        static void WaitIdle(DesktopController controller)
        { int attempts = 0; while (controller.Busy && attempts++ < 40) Settle(); if (controller.Busy) throw new TimeoutException("匹配测试未完成"); Pump(); }
        static void TestSeasonMatching(Application app, DesktopController original, List<string> report)
        {
            var settings = new AppSettings { EnableExistingDanmu = false, EnableAnimeko = false, EnableBahamut = false, MediaFolder = Path.Combine(Paths.Root, "season-fixture") };
            Directory.CreateDirectory(settings.MediaFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(DandanConfig.FilePath));
            SettingsStore.AtomicWrite(DandanConfig.FilePath, Json.Write(new DandanConfig { AppId = "fixture-app", EncryptedAppSecret = SettingsStore.Protect("fixture-secret") }), false);
            var files = Enumerable.Range(1, 12).Select(i => { string path = Path.Combine(settings.MediaFolder, "Hyakkano - 25 (" + i + ").mkv"); File.WriteAllText(path, "synthetic episode " + i); return new Dictionary<string, object> { { "Id", "fixture" + i }, { "Path", path }, { "Name", "测试番剧 第三季" }, { "Type", "Episode" }, { "IndexNumber", 25 }, { "ParentIndexNumber", 3 } }; }).ToArray();
            var handler = new SeasonFixture();
            using (var api = new JellyfinApi(settings))
            using (var test = new DesktopController(app, settings, false, new DanmuCatalog(settings, api, handler)))
            {
                test.Library.Replace(files); test.ShowWindow(); Pump();
                test.Window.Match(files[0], DanmuMatchScope.Selection, files); WaitIdle(test);
                var match = app.Windows.OfType<MatchWindow>().Single(); var tabs = Descendants<TabControl>(match).Single();
                Assert(test.Session.Match.Episodes.Length == 12 && handler.Matches == 1 && handler.Details == 1 && (DanmuMatchScope)((TabItem)tabs.SelectedItem).Tag == DanmuMatchScope.Selection, "多选12集后自动从hash单集展开全集并默认已选影片页签", report);
                Descendants<Button>(match).Single(x => (x.Content as string) == "预览已选影片并下载").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); WaitIdle(test);
                Assert(test.BatchPlan.Count == 12 && test.BatchPlan.All(x => x.Selected), "12集预览按实际集号匹配且默认全部勾选", report);
                var batch = app.Windows.OfType<BatchWindow>().Single(); var grid = Descendants<DataGrid>(batch).Single();
                Assert(grid.Columns[0] is DataGridTemplateColumn, "批量复选框使用直接绑定模板，不启动DataGrid编辑事务", report);
                var checkbox = Descendants<CheckBox>(grid).First(); checkbox.IsChecked = false; Pump(); test.Publish(); Pump(); Assert(!test.BatchPlan[0].Selected, "批量预览复选框能取消勾选且刷新无编辑事务异常", report);
                checkbox.IsChecked = true; Pump(); Assert(test.BatchPlan[0].Selected, "批量预览复选框能重新勾选", report);
                var previousPlan = test.BatchPlan;
                Descendants<Button>(match).Single(x => (x.Content as string) == "预览已选影片并下载").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); WaitIdle(test);
                Assert(!Object.ReferenceEquals(previousPlan, test.BatchPlan) && Object.ReferenceEquals(((BatchRow)grid.Items[0]).Entry, test.BatchPlan[0]) && grid.Items.Count == 12, "重复预览复用弹窗时绑定最新匹配计划，避免下载旧来源", report);
                batch.Close(); match.Close(); Pump();
                test.Window.Match(null, DanmuMatchScope.Single, new Dictionary<string, object>[0]); Pump(); match = app.Windows.OfType<MatchWindow>().Single();
                Assert((DanmuMatchScope)((TabItem)Descendants<TabControl>(match).Single().SelectedItem).Tag == DanmuMatchScope.Season, "未选影片也可直接打开整季搜索匹配", report);
                var target = Descendants<ComboBox>(match).Single(x => x.Items.Count > 0 && x.Items[0] is Choice && ((Choice)x.Items[0]).Label.Contains("season-fixture"));
                Descendants<TextBox>(match).Single().Text = "测试番剧 第三季";
                Descendants<Button>(match).Single(x => (x.Content as string) == "搜索在线弹幕").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); WaitIdle(test);
                Assert(target.SelectedIndex == 0 && test.Session.Match.Item != null, "无预选搜索后自动关联唯一同名同季度的本地目录", report);
                Descendants<Button>(match).Single(x => (x.Content as string) == "预览整季并全部下载").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); WaitIdle(test);
                Assert(test.BatchPlan.Count == 12 && test.BatchPlan.All(x => x.Selected), "无预选搜索后可直接完整匹配12集", report);
                foreach (var child in app.Windows.OfType<DialogWindow>().ToArray()) child.Close(); Pump();
                var duplicate = new Dictionary<string, object>(files[0]); string alternate = Path.Combine(settings.MediaFolder, "another-version", "episode (1).mkv"); Directory.CreateDirectory(Path.GetDirectoryName(alternate)); File.WriteAllText(alternate, "alternate synthetic version"); duplicate["Path"] = alternate; duplicate["Id"] = "duplicate-fixture";
                test.Library.Replace(files.Concat(new[] { duplicate })); test.Window.Match(null, DanmuMatchScope.Single, new Dictionary<string, object>[0]); Pump(); match = app.Windows.OfType<MatchWindow>().Single();
                Descendants<TextBox>(match).Single().Text = "测试番剧 第三季"; Descendants<Button>(match).Single(x => (x.Content as string) == "搜索在线弹幕").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); WaitIdle(test);
                Assert(test.Session.Match.Item == null, "存在多个同番同季度目录时不自动选择或覆盖，保留明确目录选择", report);
                test.ReleaseWindow(); Pump();
            }
            app.MainWindow = original.Window.View;
        }
        static void TestBackgroundBatch(DesktopController controller, List<string> report)
        {
            var completion = new TaskCompletionSource<string>(); var task = controller.RunBatch((episode, cancellation) => completion.Task);
            Assert(controller.BatchRunning, "批量下载由后台控制器持有", report);
            controller.Window.ShowBatch(); Pump(); var weak = new WeakReference(Application.Current.Windows.OfType<BatchWindow>().Single());
            controller.ReleaseWindow(); Pump(); Settle(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert(controller.BatchRunning && (!weak.IsAlive || ((Window)weak.Target).Content == null), "下载期间托盘释放弹窗而不中断任务", report);
            completion.SetResult("<i><d p='1,1,25,16777215,0,0,0,0'>测试弹幕</d></i>");
            int attempts = 0; while (!task.IsCompleted && attempts++ < 40) Settle(); if (!task.IsCompleted) throw new TimeoutException("批量测试未完成"); task.GetAwaiter().GetResult();
            string xml = Path.ChangeExtension(Json.Text(controller.BatchPlan[0].Local, "Path"), ".xml");
            Assert(!controller.BatchRunning && File.Exists(xml) && !File.Exists(xml + ".bak"), "托盘中批量下载完成且不产生 XML 备份", report);
        }
        static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        { if (root is T) yield return (T)root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
        static void TestCachePage(DesktopController controller, List<string> report, string output)
        {
            var cache = controller.Gateway.Catalog.Cache;
            cache.Write(DandanApiCache.Key("fixture-match"), "match", "测试番剧 · hash 识别", "fixture", "测试番剧");
            cache.Write(DandanApiCache.Key("fixture-comments"), "comment", "测试番剧 · 第 2 集", "fixture", "测试番剧");
            RecognitionTests.WriteAgedCache(cache, DandanApiCache.Key("fixture-expired"), "search", "已过期搜索", "fixture");
            controller.Window.Navigate("cache"); Settle();
            var view = Descendants<CachePage>(controller.Window.View).Single();
            var grid = Descendants<DataGrid>(view).Single(); var filter = Descendants<ComboBox>(view).Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "缓存类型");
            var retention = Descendants<ComboBox>(view).Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "统一缓存有效期");
            Assert(retention.Items.Count == 5 && retention.SelectedIndex == 1, "统一缓存有效期提供五个选项并默认三个月", report);
            Assert(grid.Items.Count == 2 && grid.Items.Cast<CacheRow>().All(x => x.Folder) && grid.Items.Cast<CacheRow>().Sum(x => x.Members.Length) == 3, "缓存页按动漫文件夹展示全部条目", report);
            Capture(controller.Window.View, Path.Combine(output, "wpf-cache.png"));
            filter.SelectedIndex = 3; Pump(); Assert(grid.Items.Count == 1, "缓存页按弹幕类型筛选", report);
            grid.SelectedIndex = 0; Descendants<Button>(view).Single(x => (x.Content as string) == "删除选中").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Settle();
            Assert(grid.Items.Count == 0 && cache.Entries().Length == 2, "缓存页实际删除所选条目并刷新", report);
            filter.SelectedIndex = 0; Descendants<Button>(view).Single(x => (x.Content as string) == "清理过期").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Settle();
            Assert(grid.Items.Count == 1 && cache.Entries().All(x => x.ExpiresUtc > DateTime.UtcNow), "缓存页实际清理过期并保留有效识别", report);
            retention.SelectedIndex = 4;
            Descendants<Button>(view).Single(x => (x.Content as string) == "应用有效期").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Settle();
            Assert(cache.RetentionMonths == 0 && cache.Entries().Single().ExpiresLabel == "长期" && SettingsStore.Load().CacheRetentionMonths == 0, "应用长期有效期即时更新已有缓存并持久化", report);
            controller.Window.View.Width = 1020; controller.Window.View.Height = 700; Pump();
            var clear = Descendants<Button>(view).Single(x => (x.Content as string) == "清空全部缓存"); var position = clear.TranslatePoint(new Point(), controller.Window.View);
            Assert(position.X >= 0 && position.X + clear.ActualWidth <= controller.Window.View.ActualWidth && grid.ActualHeight > 80, "缓存页最小窗口下按钮与列表完整显示", report);
            var note = Descendants<TextBlock>(view).Single(x => x.Text.StartsWith("首次成功请求后保存"));
            Assert(note.TextWrapping == TextWrapping.Wrap && note.ActualHeight > 24 && note.ActualWidth <= view.ActualWidth, "长提示在最小窗口自动换行且完整显示", report);
            Capture(controller.Window.View, Path.Combine(output, "wpf-cache-small.png"));
            clear.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Settle(); Assert(grid.Items.Count == 0 && cache.Entries().Length == 0, "缓存页实际清空全部缓存", report);
            cache.Write(DandanApiCache.Key("context-1"), "comment", "第1集", "fixture", "右键测试动漫"); cache.Write(DandanApiCache.Key("context-2"), "match", "文件识别", "fixture", "右键测试动漫");
            cache.Write(DandanApiCache.Key("context-other"), "comment", "第1集", "fixture", "另一个动漫");
            Descendants<Button>(view).Single(x => (x.Content as string) == "刷新").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Settle();
            view.OpenAnime("右键测试动漫"); Pump(); Assert(grid.Items.Count == 2 && grid.Items.Cast<CacheRow>().All(x => !x.Folder), "进入动漫缓存文件夹显示所属条目", report);
            var context = view.CreateContextMenu((CacheRow)grid.Items[0]); ((MenuItem)context.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle();
            Assert(grid.Items.Count == 1 && cache.Entries().Length == 2, "缓存条目右键删除只移除该条，保留同动漫其他缓存", report);
            cache.Write(DandanApiCache.Key("context-hidden"), "hash", "文件特征", "fixture", "右键测试动漫");
            Descendants<Button>(view).Single(x => (x.Content as string) == "刷新").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Settle(); filter.SelectedIndex = 3;
            view.OpenAnime(null); Pump(); context = view.CreateContextMenu(grid.Items.Cast<CacheRow>().Single(x => x.Anime == "右键测试动漫")); ((MenuItem)context.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle();
            Assert(cache.Entries().Length == 1 && cache.Entries().Single().Anime == "另一个动漫", "筛选后右键删除动漫文件夹也删除隐藏类型，不影响其他动漫", report);
            clear.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Settle();
            controller.Window.Navigate("overview"); Pump(); Assert(view.Children.Count == 0 && grid.ItemsSource == null, "切页释放缓存控件和条目引用", report);
            Assert(context.Items.Count == 0 && context.PlacementTarget == null, "切页解除缓存右键菜单及其控件引用", report);
            controller.Window.View.Width = 1200; controller.Window.View.Height = 820;
        }
        static void TestDeletionControls(DesktopController controller, List<string> report, string output)
        {
            var original = controller.Library.Entries; string originalRoot = controller.Settings.MediaFolder;
            string root = Path.Combine(Paths.Root, "ui-deletion"), folder = Path.Combine(root, "测试番剧"); Directory.CreateDirectory(folder);
            string video = Path.Combine(folder, "episode.mkv"), xml = Path.ChangeExtension(video, ".xml"); File.WriteAllText(video, "fixture"); File.WriteAllText(xml, "fixture XML"); File.WriteAllText(Path.Combine(folder, "poster.jpg"), "fixture poster");
            var local = new Dictionary<string, object> { { "Id", "deletefixture" }, { "Path", video }, { "Name", "测试影片" }, { "Type", "Episode" } };
            controller.Settings.MediaFolder = root; controller.Library.Replace(new[] { local }); controller.Window.Navigate("library"); Pump();
            var entry = controller.Library.Browse(null, "", LibrarySort.Name, false).Single();
            var menu = controller.Window.CreateLibraryContextMenu(entry);
            Assert(menu.Items.Cast<MenuItem>().Select(x => (string)x.Header).SequenceEqual(new[] { "删除整个文件夹…", "删除视频…", "删除弹幕…" }), "媒体列表右键菜单提供三种删除范围", report);
            menu.PlacementTarget = controller.Window.View; menu.IsOpen = true; Settle(); Capture(menu, Path.Combine(output, "wpf-delete-menu.png")); menu.IsOpen = false; menu.PlacementTarget = null;
            InvokeDeletion(controller, menu, 1, false, output);
            Assert(File.Exists(video) && File.Exists(xml) && controller.Library.Entries.Length == 1, "取消真实删除确认保留视频、弹幕及列表", report);
            InvokeDeletion(controller, menu, 2, true, output);
            Assert(!File.Exists(xml) && File.Exists(video) && !controller.Library.Entries.Single().HasXml, "右键删除弹幕后真实列表立即更新", report);
            InvokeDeletion(controller, menu, 1, true, output);
            Assert(!File.Exists(video) && controller.Library.Entries.Length == 0 && Directory.Exists(folder), "右键删除视频后列表移除影片并保留父目录", report);
            InvokeDeletion(controller, menu, 0, true, output);
            Assert(!Directory.Exists(folder) && Directory.Exists(root), "右键删除整个文件夹移除其余内容并保留媒体根目录", report);
            menu.Items.Clear(); controller.Settings.MediaFolder = originalRoot; controller.Library.ReplaceEntries(original); controller.Window.Navigate("library"); Pump();
        }
        static void InvokeDeletion(DesktopController controller, ContextMenu menu, int action, bool accept, string output)
        {
            bool handled = false; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += (s, e) =>
            {
                var dialog = Application.Current.Windows.OfType<AlertWindow>().FirstOrDefault(); if (dialog == null) return;
                timer.Stop(); handled = true; Capture(dialog, Path.Combine(output, "wpf-delete-confirm.png"));
                Descendants<Button>(dialog).Single(x => (x.Content as string) == (accept ? "永久删除" : "取消")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            };
            timer.Start(); ((MenuItem)menu.Items[action]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            int attempts = 0; while (!handled && attempts++ < 40) Settle(); timer.Stop(); if (!handled) throw new Exception("删除确认未显示");
            attempts = 0; while (controller.Busy && attempts++ < 40) Settle(); if (controller.Busy) throw new TimeoutException("删除操作未完成");
        }
        static void Capture(FrameworkElement window, string path, double dpi = 96)
        {
            window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * dpi / 96), (int)(window.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(path)) encoder.Save(stream);
        }
        static void Assert(bool value, string name, List<string> report) { if (!value) throw new Exception(name); report.Add("PASS " + name); }
    }
}
