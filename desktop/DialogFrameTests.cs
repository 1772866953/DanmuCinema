using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace DanmuCinema.Desktop
{
    // Capture the displayed desktop pixels of an isolated, foreground fixture.
    // RenderTargetBitmap/WM_ERASEBKGND tests cannot see DWM close transitions.
    public static class DialogFrameTests
    {
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(System.Drawing.Point point);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] static extern IntPtr SetActiveWindow(IntPtr hwnd);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        static readonly List<string> report = new List<string>();
        public static int Run()
        {
            string original = Paths.Root, output = Path.Combine(original, "tests", "output", "dialog-frames-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(original, "tests", "output", "dialog-frames-latest.txt"), output);
            Paths.Root = output;
            System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) => File.WriteAllText(Path.Combine(output, "dispatcher-error.txt"), e.Exception.ToString());
            AppDomain.CurrentDomain.UnhandledException += (s, e) => File.WriteAllText(Path.Combine(output, "unhandled-error.txt"), e.ExceptionObject.ToString());
            System.Windows.Forms.Application.ThreadException += (s, e) => File.WriteAllText(Path.Combine(output, "forms-error.txt"), e.Exception.ToString());
            DesktopController controller = null;
            try
            {
                Ui.InstallTheme(app); SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                var settings = new AppSettings { EnableDandan = false, EnableAnimeko = false, EnableBahamut = false, EnableExistingDanmu = false, StartServicesOnLaunch = false };
                controller = new DesktopController(app, settings, false); controller.ShowWindow(); controller.Window.Navigate("library");
                controller.Window.View.Topmost = true; ShowWindow(new WindowInteropHelper(controller.Window.View).Handle, 5); controller.Window.View.Activate();
                var items = Enumerable.Range(1, 12).Select(i => new Dictionary<string, object> { { "Id", "fixture-" + i }, { "Name", "录帧测试番剧 第 " + i + " 集" }, { "Path", Path.Combine(output, "videos", "录帧测试番剧", "episode-" + i.ToString("D2") + ".mkv") }, { "Type", "Episode" }, { "SeriesName", "录帧测试番剧" }, { "ParentIndexNumber", 1 }, { "IndexNumber", i } }).ToArray();
                controller.Library.Replace(items); controller.Publish(); Pause(500);
                TestClosePolicy(controller.Window.View);
                TestHistoryClose();
                int mainClosing = 0; controller.Window.View.Closing += (s, e) => mainClosing++;
                foreach (var state in new[] { WindowState.Normal, WindowState.Maximized })
                {
                    var main = controller.Window.View; main.WindowState = state;
                    if (state == WindowState.Normal) { main.Width = 1200; main.Height = 820; main.Left = 50; main.Top = 50; }
                    Pause(500);
                    for (int repeat = 0; repeat < 3; repeat++)
                    {
                        var episodes = Enumerable.Range(1, 12).Select(i => (object)new Dictionary<string, object> { { "Id", "fixture-remote-" + i }, { "Number", i.ToString() }, { "Title", "第 " + i + " 集" }, { "Provider", "fixture" }, { "Site", "离线测试源" } }).ToArray();
                        var matchState = new MatchState { Item = items[0], Selected = items, Scope = DanmuMatchScope.Selection, Keyword = "录帧测试番剧", Sources = new object[] { new Dictionary<string, object> { { "Name", "录帧测试番剧" }, { "Site", "离线测试源" } } }, Episodes = episodes, SourceIndex = 0, EpisodeIndex = 0 };
                        var match = new MatchWindow(controller, controller.Window, matchState, false) { Topmost = true }; controller.Window.Track(match, main); ShowWindow(new WindowInteropHelper(match).Handle, 5); match.Activate(); Pause(250);
                        Children<Button>(match).Single(x => (x.Content as string) == "预览已选影片并下载").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pause(250);
                        var batch = app.Windows.OfType<BatchWindow>().Single();
                        batch.Topmost = true; ShowWindow(new WindowInteropHelper(batch).Handle, 5); batch.Activate(); Pause(250);
                        Check(batch.Owner == match && match.Owner == main, "真实来源→批量预览的父子关系：" + state + " " + repeat);
                        NativeRect bounds; GetWindowRect(new WindowInteropHelper(main).Handle, out bounds);
                        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(main).Handle).Bounds;
                        var crop = Rectangle.Intersect(new Rectangle(bounds.Left + 14, bounds.Top + 14, bounds.Right - bounds.Left - 28, bounds.Bottom - bounds.Top - 28), screen);
                        crop = new Rectangle(crop.Left + (crop.Width - 800) / 2, crop.Top + (crop.Height - 450) / 2, 800, 450);
                        main.Topmost = match.Topmost = batch.Topmost = true;
                        // Model the exact video: another light app is between
                        // the main window and its two dark owned dialogs.
                        using (var behind = new System.Windows.Forms.Form { Text = "模拟后方浅色应用", BackColor = System.Drawing.Color.White, TopMost = true, StartPosition = System.Windows.Forms.FormStartPosition.Manual, Bounds = crop })
                        {
                        int noTransition = 1; DwmSetWindowAttribute(behind.Handle, 3, ref noTransition, sizeof(int));
                        behind.Show(); SetActiveWindow(new WindowInteropHelper(batch).Handle); ArrangeInterveningWindow(main, match, batch, behind.Handle); Pause(400);
                        Check(IsAbove(behind.Handle, new WindowInteropHelper(main).Handle) && IsAbove(new WindowInteropHelper(match).Handle, behind.Handle), "浅色模拟应用位于主窗口与弹窗之间：" + state + " " + repeat);
                        uint frontProcess; GetWindowThreadProcessId(GetForegroundWindow(), out frontProcess);
                        report.Add("INFO foreground=" + frontProcess + " self=" + Process.GetCurrentProcess().Id + " visible=" + IsWindowVisible(new WindowInteropHelper(batch).Handle) + " crop=" + crop);
                        using (var recording = new Recording(crop))
                        {
                            recording.Start(); Pause(150);
                            recording.Mark("prepare and close batch");
                            // The foreground decision is injected only in this
                            // isolated fixture: background tool launches cannot
                            // reliably claim Windows foreground input permission.
                            CloseForRecording(batch);
                            Check(IsAbove(new WindowInteropHelper(match).Handle, behind.Handle), "二级弹窗关闭后父窗口已完成交接：" + state + " " + repeat);
                            Pause(repeat == 0 ? 40 : 160);
                            recording.Mark("prepare and close match");
                            CloseForRecording(match);
                            Check(IsAbove(new WindowInteropHelper(main).Handle, behind.Handle), "一级弹窗关闭后主窗口已在浅色应用之上：" + state + " " + repeat);
                            Pause(600);
                            recording.Stop();
                            string name = state + "-" + repeat; recording.Save(Path.Combine(output, name));
                            Check(recording.Frames.Count >= 30, "实际屏幕连续录帧数量：" + name + " " + recording.Frames.Count + " FPS=" + (recording.Frames.Count * 1000 / recording.Frames.Last().Ms).ToString("F1"));
                            Check(recording.Frames.All(x => x.WhiteFraction < .30), "窗口关闭过程无大面积白帧：" + name + " 最大白色占比=" + recording.Frames.Max(x => x.WhiteFraction).ToString("P2"));
                        }
                        Check(main.IsVisible && IsWindowVisible(new WindowInteropHelper(main).Handle) && mainClosing == 0, "依次关闭后主窗口保持显示且没有触发关闭：" + state + " " + repeat);
                        }
                    }
                }
                report.Add("PASS: " + report.Count(x => x.StartsWith("PASS ")) + " dialog-frame checks"); return 0;
            }
            catch (Exception error) { report.Add("FAIL: " + error); return 1; }
            finally
            {
                if (controller != null) { controller.ReleaseWindow(); controller.Dispose(); }
                app.Shutdown(); Paths.Root = original;
                File.WriteAllLines(Path.Combine(output, "report.txt"), report);
                File.WriteAllText(Path.Combine(original, "tests", "output", "dialog-frames-latest.txt"), output);
            }
        }
        static void CloseCaption(Window window)
        { Children<Button>(window).Single(x => (x.Tag as string) == "close").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
        static void CloseForRecording(DialogWindow window)
        {
            bool foreground = Ui.IsForeground(window);
            if (!foreground && !Ui.PrepareDialogClose(window, true)) throw new Exception("隔离前台交接准备失败");
            CloseCaption(window);
            if (foreground && !window.OwnerPreparedForClose) throw new Exception("实际前台关闭未走销毁前交接路径");
            report.Add("INFO close path=" + (foreground ? "native foreground / OnClosing" : "injected isolated foreground") + " " + window.GetType().Name);
        }
        sealed class CloseProbe : DialogWindow
        {
            public bool Prepared;
            public CloseProbe() : base("关闭策略隔离测试", 780, 560) { }
            protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
            { base.OnClosing(e); Prepared = OwnerPreparedForClose; }
        }
        static void TestClosePolicy(Window main)
        {
            var probe = new CloseProbe { Owner = main }; probe.Show(); ShowWindow(new WindowInteropHelper(probe).Handle, 5); Pause(100);
            System.ComponentModel.CancelEventHandler cancel = (s, e) => e.Cancel = true; probe.Closing += cancel; probe.Close();
            Check(probe.IsVisible && !probe.Prepared, "取消关闭不提前改变父窗口");
            Check(!Ui.PrepareDialogClose(probe, false), "后台关闭不抢占其他应用前台或提升窗口");
            Check(Ui.PrepareDialogClose(probe, true) && probe.IsVisible, "父窗口在子 HWND 仍可见时完成前台关闭准备");
            probe.Closing -= cancel; probe.Close();
            Pause(100);
        }
        static void TestHistoryClose()
        {
            string saved = Paths.Root;
            try
            {
                Paths.Root = Path.Combine(saved, "history-" + Guid.NewGuid().ToString("N"));
                var input = new HistoryInput("danmu", "关闭保存测试"); input.CommitSearch();
                string file = Path.Combine(Paths.Data, "search-history.json");
                Check(SearchHistory.List("danmu").SequenceEqual(new[] { "关闭保存测试" }), "搜索历史仍正常写入隔离配置");
                var before = File.GetLastWriteTimeUtc(file);
                // A reader denying replacement models antivirus/indexer locks.
                using (var reader = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                { input.Commit(); input.Dispose(); }
                Check(File.GetLastWriteTimeUtc(file) == before, "关闭弹窗不重复替换已保存的历史，即使文件正被只读占用");
            }
            finally { Paths.Root = saved; }
        }
        static void ArrangeInterveningWindow(Window main, Window first, Window second, IntPtr light)
        {
            const uint flags = 0x1 | 0x2 | 0x10 | 0x200;
            SetWindowPos(light, new IntPtr(-1), 0, 0, 0, 0, flags);
            SetWindowPos(new WindowInteropHelper(main).Handle, light, 0, 0, 0, 0, flags);
            SetWindowPos(new WindowInteropHelper(first).Handle, new IntPtr(-1), 0, 0, 0, 0, flags);
            SetWindowPos(new WindowInteropHelper(second).Handle, new IntPtr(-1), 0, 0, 0, 0, flags);
        }
        static bool IsAbove(IntPtr first, IntPtr second)
        { for (int count = 0; first != IntPtr.Zero && count < 2000; count++, first = GetWindow(first, 2)) if (first == second) return count > 0; return false; }
        static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
        { if (root is T) yield return (T)root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var item in Children<T>(VisualTreeHelper.GetChild(root, i))) yield return item; }
        static void Pause(int milliseconds)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        static void Check(bool passed, string text) { if (!passed) throw new Exception(text); report.Add("PASS " + text); }
        sealed class Frame : IDisposable
        {
            public Bitmap Image; public double Ms, WhiteFraction;
            public void Dispose() { Image.Dispose(); }
        }
        sealed class Recording : IDisposable
        {
            readonly Rectangle bounds; readonly Stopwatch watch = new Stopwatch(); readonly List<string> events = new List<string>();
            Thread thread; volatile bool stopping; Exception failure;
            public readonly List<Frame> Frames = new List<Frame>();
            public Recording(Rectangle bounds) { this.bounds = bounds; }
            public void Start() { watch.Start(); thread = new Thread(Capture) { IsBackground = true }; thread.Start(); }
            public void Mark(string text) { events.Add(watch.Elapsed.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "," + text); }
            void Capture()
            {
                try
                {
                    using (var raw = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                    using (var graphics = Graphics.FromImage(raw))
                    {
                        while (!stopping)
                        {
                            foreach (var point in new[] { new System.Drawing.Point(bounds.Left + 4, bounds.Top + 4), new System.Drawing.Point(bounds.Right - 4, bounds.Bottom - 4), new System.Drawing.Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2) })
                            {
                                uint screenProcess; GetWindowThreadProcessId(WindowFromPoint(point), out screenProcess);
                                if (screenProcess != (uint)Process.GetCurrentProcess().Id) throw new InvalidOperationException("测试区域被其他应用遮挡；不录制。process=" + screenProcess);
                            }
                            double start = watch.Elapsed.TotalMilliseconds;
                            graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                            var image = raw.Clone(new Rectangle(0, 0, raw.Width, raw.Height), PixelFormat.Format32bppArgb);
                            int white = 0, total = 0;
                            for (int y = 12; y < image.Height; y += 12) for (int x = 12; x < image.Width; x += 12)
                            { var pixel = image.GetPixel(x, y); total++; if (pixel.R > 225 && pixel.G > 225 && pixel.B > 225) white++; }
                            Frames.Add(new Frame { Image = image, Ms = start, WhiteFraction = (double)white / total });
                            int wait = (int)(10 - (watch.Elapsed.TotalMilliseconds - start)); if (wait > 0) Thread.Sleep(wait);
                        }
                    }
                }
                catch (Exception error) { failure = error; }
            }
            public void Stop() { stopping = true; thread.Join(); if (failure != null) throw new Exception("屏幕录制失败", failure); }
            public void Save(string path)
            {
                Directory.CreateDirectory(path); var rows = new List<string> { "frame,ms,white_fraction" };
                for (int i = 0; i < Frames.Count; i++) { var frame = Frames[i]; frame.Image.Save(Path.Combine(path, i.ToString("D4") + ".png"), ImageFormat.Png); rows.Add(i + "," + frame.Ms.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "," + frame.WhiteFraction.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)); }
                File.WriteAllLines(Path.Combine(path, "frames.csv"), rows); File.WriteAllLines(Path.Combine(path, "events.csv"), events);
            }
            public void Dispose() { if (thread != null && thread.IsAlive) { stopping = true; thread.Join(); } foreach (var frame in Frames) frame.Dispose(); }
        }
    }
}
