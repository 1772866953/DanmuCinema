using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DanmuCinema.Desktop
{
    // Deliberately only the current fixes, with synthetic API/config/window data.
    public static class CurrentFixTests
    {
        const string Title = "你遭难了吗？";
        const string Release = "[动漫国字幕组&VCB-Studio] 你遭难了吗？ 10-bit 1080p HEVC BDRip [Fin]";
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool PatBlt(IntPtr dc, int x, int y, int width, int height, uint operation);
        [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
        [StructLayout(LayoutKind.Sequential)] struct BitmapInfo
        {
            public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize;
            public int XPixels, YPixels; public uint Used, Important;
        }
        sealed class Fixture : HttpMessageHandler
        {
            public int Requests;
            public bool Offline;
            public readonly List<string> Queries = new List<string>();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Requests++; token.ThrowIfCancellationRequested();
                if (Offline) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                string query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["keyword"];
                if (query != null) Queries.Add(query);
                string json = request.RequestUri.AbsolutePath.Contains("bangumi")
                    ? "{\"success\":true,\"bangumi\":{\"episodes\":[{\"episodeId\":101,\"episodeNumber\":1,\"episodeTitle\":\"漂流\"}]}}"
                    : query == Title ? Json.Write(new { success = true, animes = new[] { new { animeId = 10, animeTitle = Title, typeDescription = "动漫", episodeCount = 12 } } }) : "{\"success\":true,\"animes\":[]}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
            }
        }
        public static int Run()
        {
            string original = Paths.Root, output = Paths.TestOutputFor(original); Directory.CreateDirectory(output);
            var report = new List<string>(); var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            DesktopController controller = null; JellyfinApi api = null; DanmuCatalog catalog = null;
            try
            {
                string fixtureRoot = Path.Combine(output, "current-" + Guid.NewGuid().ToString("N"));
                Paths.Root = fixtureRoot; Directory.CreateDirectory(Paths.Data);
                Ui.InstallTheme(app); SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                var settings = new AppSettings { EnableDandan = true, EnableAnimeko = false, EnableBahamut = false, EnableExistingDanmu = false };
                WriteFixtureConfig(output, fixtureRoot, "fixture-app");
                var handler = new Fixture(); api = new JellyfinApi(settings); catalog = new DanmuCatalog(settings, api, handler);
                Check(SmartMatching.CleanTitle(Release) == Title && SmartMatching.Queries(Release)[0] == Title, "历史中的实际发布文件夹名优先转成干净番剧名", report);
                var initial = Wait(catalog.Search(Title, true, true, 0, "dandan"));
                Wait(catalog.Episodes((Dictionary<string, object>)initial.Items.Single()));
                string stalePath = "/api/v2/search/anime?keyword=" + Uri.EscapeDataString(Release);
                catalog.Cache.Write(DandanApiCache.Key("fixture-app|" + stalePath + "|"), "search", Release, "{\"success\":true,\"animes\":[]}");
                int requests = handler.Requests; handler.Offline = true;
                controller = new DesktopController(app, settings, false, catalog); controller.ShowWindow(); Pump();
                var state = new MatchState { Keyword = "原来的查询", AnimeOnly = true, Smart = true, ServiceId = "dandan" };
                var match = new MatchWindow(controller, controller.Window, state, false); controller.Window.Track(match); Pump();
                SearchHistory.Add("danmu", Release);
                SetLoading(controller, true); ChooseHistory(match, Release); Pause(70);
                Check(handler.Requests == requests && state.Keyword == Release, "媒体库读取期间选历史保留搜索请求，未误发或丢弃", report);
                SetLoading(controller, false); WaitMatch(controller, match);
                Check(state.Sources.Length == 1 && Json.Text((Dictionary<string, object>)state.Sources[0], "Provider") == "dandan" && state.Episodes.Length == 1, "历史选择实际返回官方候选及集数，不受旧零结果缓存阻挡", report);
                Check(handler.Requests == requests, "历史选择完全命中本地搜索及集数缓存，离线且无新增 API 请求", report);
                ChooseHistory(match, Release); WaitMatch(controller, match);
                Check(state.Sources.Length == 1 && handler.Requests == requests, "重复历史搜索继续使用相同有效缓存", report);
                TestPeriodic(controller, match, handler, requests, report);
                match.Close(); Pump();
                TestNativeClose(controller, report);
                controller.ReleaseWindow(); Pump(); controller.Dispose(); controller = null;
                using (var restarted = new DanmuCatalog(settings, api, handler))
                {
                    var cached = Wait(restarted.Search(Release, true, true, 0, "dandan"));
                    Check(cached.Items.Length == 1 && handler.Requests == requests, "重启后历史文件夹名仍离线命中规范标题缓存", report);
                }
                // A second independent fixture requires no replacement/deletion
                // of files that antivirus may temporarily hold in the first one.
                string freshRoot = Path.Combine(output, "current-" + Guid.NewGuid().ToString("N")); Paths.Root = freshRoot; Directory.CreateDirectory(Paths.Data);
                WriteFixtureConfig(output, freshRoot, "fixture-fresh-app"); handler.Offline = false; handler.Queries.Clear();
                using (var freshCatalog = new DanmuCatalog(settings, api, handler))
                {
                    var fresh = Wait(freshCatalog.Search(Release, true, true, 0, "dandan"));
                    Check(fresh.Items.Length == 1 && handler.Requests == requests + 1 && handler.Queries.SequenceEqual(new[] { Title }), "缺少缓存时历史名只请求一次干净标题，不先发送无效发布名称", report);
                    handler.Offline = true; Wait(freshCatalog.Search(Release, true, true, 0, "dandan"));
                    Check(handler.Requests == requests + 1, "首次成功结果写入本地，第二次历史搜索离线复用", report);
                }
                string[] docs = Directory.GetFiles(Path.Combine(Paths.WorkspaceRootFor(original), "docs")).Select(Path.GetFileName).OrderBy(x => x).ToArray();
                Check(docs.SequenceEqual(new[] { "LICENSE-Danmu.txt", "LICENSE-Jellyfin.txt", "THIRD-PARTY.md" }), "docs 仅保留第三方说明与两个许可证", report);
                report.Add("PASS: " + report.Count + " current-fix checks"); return 0;
            }
            catch (Exception error) { report.Add("FAIL: " + error); return 1; }
            finally
            {
                if (controller != null) { controller.ReleaseWindow(); controller.Dispose(); }
                if (catalog != null) catalog.Dispose(); if (api != null) api.Dispose(); app.Shutdown(); Paths.Root = original;
                File.WriteAllLines(Path.Combine(output, "current-fixes-test.txt"), report, new System.Text.UTF8Encoding(false));
            }
        }
        static void WriteFixtureConfig(string output, string fixtureRoot, string appId)
        {
            string root = Path.GetFullPath(fixtureRoot), allowed = Path.GetFullPath(output);
            string expected = Path.Combine(root, "config", "dandanplay.json");
            // DandanConfig.FilePath is derived from Paths.Root (DandanConfig.cs).
            // Validate the resolved target immediately before either write.
            if (!String.Equals(Path.GetDirectoryName(root), allowed, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("current-", StringComparison.Ordinal) ||
                !String.Equals(Path.GetFullPath(Paths.Root), root, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(Path.GetFullPath(DandanConfig.FilePath), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("测试配置路径未隔离；禁止写入。");
            Directory.CreateDirectory(Path.GetDirectoryName(expected));
            SettingsStore.AtomicWrite(expected, Json.Write(new DandanConfig { AppId = appId, EncryptedAppSecret = SettingsStore.Protect("fixture-secret") }), false);
        }
        static void ChooseHistory(MatchWindow match, string term)
        {
            var input = Children<HistoryInput>(match).Single(); input.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
            var popup = (Popup)typeof(HistoryInput).GetField("popup", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input);
            Children<Button>(popup.Child).Single(x => x.Content is TextBlock && ((TextBlock)x.Content).Text == term).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
        static void SetLoading(DesktopController controller, bool value)
        { typeof(DesktopController).GetProperty("Loading").GetSetMethod(true).Invoke(controller, new object[] { value }); controller.Publish(); }
        static void WaitMatch(DesktopController controller, Window match)
        {
            int tries = 0;
            while ((controller.Busy || !Children<TextBox>(match).Single().IsEnabled) && tries++ < 100) Pause(20);
            if (tries >= 100) throw new TimeoutException("历史搜索未完成"); Pump();
        }
        static void TestPeriodic(DesktopController controller, MatchWindow match, Fixture fixture, int requests, List<string> report)
        {
            var lists = Children<ListBox>(match).ToArray(); var data = lists.Select(x => x.ItemsSource).ToArray(); var editor = Children<TextBox>(match).Single();
            for (int i = 0; i < 20; i++)
            {
                SetLoading(controller, true); Wait(controller.UpdateStatus()); SetLoading(controller, false); controller.Publish(); Pump();
                Check(lists.Select((x, j) => Object.ReferenceEquals(x.ItemsSource, data[j]) && x.IsEnabled).All(x => x) && editor.IsEnabled,
                    "周期状态/媒体库通知保持弹窗控件与候选不变：" + i, report);
            }
            Check(fixture.Requests == requests, "周期状态刷新不重新调用官方搜索", report);
            Check(NativeErasePixel(match) == CanvasPixel, "周期刷新后的原生背景擦除输出深色像素", report);
        }
        static uint CanvasPixel { get { var c = ((SolidColorBrush)Ui.Resource("Canvas")).Color; return (uint)(c.R | c.G << 8 | c.B << 16); } }
        static void TestNativeClose(DesktopController controller, List<string> report)
        {
            foreach (var state in new[] { WindowState.Normal, WindowState.Maximized })
            {
                var main = controller.Window.View; main.WindowState = state; Pump();
                var source = (HwndSource)PresentationSource.FromVisual(main);
                source.CompositionTarget.BackgroundColor = Colors.White;
                Check(NativeErasePixel(main) == CanvasPixel && source.CompositionTarget.BackgroundColor == ((SolidColorBrush)Ui.Resource("Canvas")).Color,
                    "原生擦除和合成目标被白色污染时均恢复深色：" + state, report);
                for (int i = 0; i < 4; i++)
                {
                    var first = new DialogWindow("一级隔离弹窗", 780, 560); controller.Window.Track(first, main);
                    var second = new DialogWindow("二级隔离弹窗", 780, 560); controller.Window.Track(second, first); Pump();
                    Check(NativeErasePixel(first) == CanvasPixel && NativeErasePixel(second) == CanvasPixel, "父子弹窗的原生擦除帧都为深色：" + state + " " + i, report);
                    second.Close();
                    Check(NativeErasePixel(first) == CanvasPixel, "二级关闭后立即暴露的一级原生帧为深色：" + state + " " + i, report);
                    first.Close();
                    Check(NativeErasePixel(main) == CanvasPixel, "不等 Dispatcher 稳定，一级关闭后的主窗口擦除帧为深色：" + state + " " + i, report);
                    Pump(); Check(main.IsVisible && main.IsActive && IsWindowVisible(new WindowInteropHelper(main).Handle), "快速连续关闭后主窗口可见并保留前台：" + state + " " + i, report);
                }
            }
        }
        static uint NativeErasePixel(Window window)
        {
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero), bits, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                var info = new BitmapInfo { Size = 40, Width = 64, Height = -64, Planes = 1, Bits = 32 };
                bitmap = CreateDIBSection(dc, ref info, 0, out bits, IntPtr.Zero, 0); if (dc == IntPtr.Zero || bitmap == IntPtr.Zero) throw new Exception("原生测试画布无法创建");
                previous = SelectObject(dc, bitmap); PatBlt(dc, 0, 0, 64, 64, 0x00FF0062);
                SendMessage(new WindowInteropHelper(window).Handle, 0x14, dc, IntPtr.Zero);
                return GetPixel(dc, 16, 16);
            }
            finally { if (previous != IntPtr.Zero) SelectObject(dc, previous); if (bitmap != IntPtr.Zero) DeleteObject(bitmap); if (dc != IntPtr.Zero) DeleteDC(dc); }
        }
        static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
        { if (root is T) yield return (T)root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var item in Children<T>(VisualTreeHelper.GetChild(root, i))) yield return item; }
        static void Pump()
        { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
        static void Pause(int milliseconds)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        static T Wait<T>(Task<T> task) { Wait((Task)task); return task.GetAwaiter().GetResult(); }
        static void Wait(Task task)
        { int tries = 0; while (!task.IsCompleted && tries++ < 500) Pause(20); if (!task.IsCompleted) throw new TimeoutException("隔离请求超时"); task.GetAwaiter().GetResult(); }
        static void Check(bool passed, string name, List<string> report) { if (!passed) throw new Exception(name); report.Add("PASS " + name); }
    }
}
