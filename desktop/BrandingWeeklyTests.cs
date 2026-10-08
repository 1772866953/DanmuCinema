using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DanmuCinema.Desktop
{
    public static class BrandingWeeklyTests
    {
        sealed class Clock : IClock
        {
            public DateTimeOffset Now { get; set; }
            public long Milliseconds { get; set; }
            public void Advance(int seconds) { Now += TimeSpan.FromSeconds(seconds); Milliseconds += seconds * 1000L; }
        }
        static readonly List<string> report = new List<string>();
        public static int Run()
        {
            string original = Paths.Root, output = Path.Combine(original, "tests", "output", "branding-weekly"); Directory.CreateDirectory(output);
            Paths.Root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Paths.Root);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; DesktopController controller = null;
            try
            {
                System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext()); Ui.InstallTheme(app);
                var monday = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
                for (int day = 0; day < 7; day++)
                {
                    var plan = new WeeklyPlan(1 << day, TimeSpan.FromHours(9), TimeZoneInfo.Utc); var next = plan.Next(monday);
                    Check(((int)next.DayOfWeek + 6) % 7 == day && next.Hour == 9 && next > monday, WeeklyPlan.DayNames[day] + "独立选择和共同时间正确");
                }
                var all = new WeeklyPlan(127, TimeSpan.FromHours(9), TimeZoneInfo.Utc);
                Check(all.Next(monday.AddHours(1)) == monday.AddDays(1).AddHours(1), "当天执行时间已过则选择下一天，不立即执行");
                var single = new WeeklyPlan(1, TimeSpan.FromHours(9), TimeZoneInfo.Utc);
                Check(single.Next(monday.AddHours(1)) == monday.AddDays(7).AddHours(1), "单日计划跨周且不会重复当天已执行的任务");
                Reject(() => new WeeklyPlan(0, TimeSpan.Zero, TimeZoneInfo.Utc), "未勾选日期被拒绝"); Reject(() => new WeeklyPlan(128, TimeSpan.Zero, TimeZoneInfo.Utc), "无效日期掩码被拒绝"); Reject(() => new WeeklyPlan(1, TimeSpan.FromDays(1), TimeZoneInfo.Utc), "24点及越界时间被拒绝");
                var clock = new Clock { Now = monday.AddMinutes(59).AddSeconds(40) }; var scheduler = new Scheduler(clock);
                scheduler.StartWeekly(1 | 4, TimeSpan.FromHours(9), PowerAction.Hibernate, TimeZoneInfo.Utc);
                Check(scheduler.Weekly && scheduler.Action == PowerAction.Hibernate && scheduler.Target == monday.AddHours(1), "每周计划关联所选动作和最近执行时间");
                bool dispatched = false; for (int i = 0; i < 4; i++) { clock.Advance(5); bool current = scheduler.Poll(); if (i < 3) Check(!current, "执行前保留取消阶段 " + i); dispatched |= current; }
                Check(dispatched && !scheduler.Poll(), "每次计划只分派一次，不实际执行电源动作"); scheduler.Finish(true);
                Check(scheduler.Active && scheduler.Weekly && scheduler.Target == monday.AddDays(2).AddHours(1), "成功后自动安排下一勾选日期"); scheduler.Cancel(); clock.Advance(7 * 86400);
                Check(!scheduler.Weekly && !scheduler.Active && !scheduler.Poll(), "取消每周计划同时取消后续执行");
                scheduler.StartWeekly(127, clock.Now.TimeOfDay + TimeSpan.FromSeconds(10), PowerAction.Lock, TimeZoneInfo.Utc); clock.Advance(5); scheduler.Poll(); clock.Advance(5); Check(scheduler.Poll(), "模拟失败计划到达分派状态"); scheduler.Finish(false);
                Check(!scheduler.Active && !scheduler.Weekly && scheduler.State == ScheduleState.Failed, "失败后停止计划，不循环触发错误");
                scheduler.StartWeekly(127, clock.Now.TimeOfDay + TimeSpan.FromSeconds(10), PowerAction.Lock, TimeZoneInfo.Utc); clock.Advance(120); Check(!scheduler.Poll() && scheduler.Remaining >= TimeSpan.FromSeconds(15), "错过执行时间时保留醒来后的取消机会"); scheduler.Cancel(); scheduler.StartDelay(TimeSpan.FromSeconds(30), PowerAction.Lock);
                Check(!scheduler.Weekly && scheduler.Remaining == TimeSpan.FromSeconds(30), "切回倒计时后不沿用每周规则"); scheduler.Cancel();
                var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
                var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday);
                var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2020, 1, 1), new DateTime(2030, 12, 31), TimeSpan.FromHours(1), daylightStart, daylightEnd);
                var zone = TimeZoneInfo.CreateCustomTimeZone("fixture-zone", TimeSpan.FromHours(-5), "fixture", "fixture", "fixture-summer", new[] { rule });
                Check(new WeeklyPlan(64, TimeSpan.FromMinutes(150), zone).Next(new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.FromHours(-5))).Day == 15, "夏令时不存在的时间跳过到下一选定日");
                Check(new WeeklyPlan(64, TimeSpan.FromMinutes(90), zone).Next(new DateTimeOffset(2026, 11, 1, 5, 45, 0, TimeSpan.Zero)).UtcDateTime.Hour == 6, "夏令时重复时间只选择较晚一次");
                using (var stream = typeof(Ui).Assembly.GetManifestResourceStream("DanmuCinema.AppIcon"))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    Check(decoder.Frames.Select(x => x.PixelWidth).SequenceEqual(new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 }), "图标包含十种尺寸，覆盖托盘、任务栏和高分辨率快捷方式");
                    foreach (var frame in decoder.Frames)
                    {
                        var expected = new Image { Source = (ImageSource)Ui.Resource("AppLogo"), Width = frame.PixelWidth, Height = frame.PixelHeight }; expected.Measure(new Size(frame.PixelWidth, frame.PixelHeight)); expected.Arrange(new Rect(0, 0, frame.PixelWidth, frame.PixelHeight));
                        var bitmap = new RenderTargetBitmap(frame.PixelWidth, frame.PixelHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(expected);
                        var decoded = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0); byte[] actual = new byte[frame.PixelWidth * frame.PixelHeight * 4], vector = new byte[actual.Length]; decoded.CopyPixels(actual, frame.PixelWidth * 4, 0);
                        // Compare both sides after the same PNG alpha conversion used by ICO frames.
                        using (var encoded = new MemoryStream()) { var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); png.Save(encoded); encoded.Position = 0; var roundTrip = BitmapDecoder.Create(encoded, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); new FormatConvertedBitmap(roundTrip.Frames[0], PixelFormats.Pbgra32, null, 0).CopyPixels(vector, frame.PixelWidth * 4, 0); }
                        Check(actual.SequenceEqual(vector), "内嵌图标与应用内矢量标志一致：" + frame.PixelWidth + "px");
                    }
                }
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(typeof(Ui).Assembly.Location)) using (var bitmap = icon.ToBitmap()) { var center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2); Check(center.R > 240 && center.G > 240 && center.B > 240, "可执行程序原生图标使用白色播放标志"); }
                var settings = new AppSettings { MediaFolder = Path.Combine(Paths.Root, "videos") }; Directory.CreateDirectory(settings.MediaFolder); controller = new DesktopController(app, settings, false); controller.ShowWindow(); var main = controller.Window.View;
                foreach (string page in new[] { "overview", "library", "tasks", "connect", "setup", "settings", "schedule", "cache", "logs" })
                {
                    controller.Window.Navigate(page); Pause(100);
                    var text = String.Join(" ", Children<TextBlock>(main).Select(x => x.Text).Concat(Children<Button>(main).Select(x => x.Content as string)));
                    Check(!System.Text.RegularExpressions.Regex.IsMatch(text, "iPad|SenPlayer|Filebar", System.Text.RegularExpressions.RegexOptions.IgnoreCase), "通用设备文案：" + page);
                    if (page == "library" || page == "tasks" || page == "cache") { var table = Children<DataGrid>(main).Single(); CheckRound(table, page); Save(main, Path.Combine(output, page + ".png")); }
                }
                controller.Window.Navigate("schedule"); Pause(100); var mode = Named<ComboBox>(main, "定时方式"); var action = Named<ComboBox>(main, "到时操作");
                Check(action.SelectedIndex == (int)PowerAction.Hibernate && (string)action.SelectedItem == "休眠", "到时操作默认选中休眠"); mode.SelectedIndex = 1; var time = Named<TextBox>(main, "定时执行时间"); time.Text = "02:35:40"; mode.SelectedIndex = 2; Pause(100);
                var days = Children<CheckBox>(main).Where(x => WeeklyPlan.DayNames.Contains(AutomationProperties.GetName(x))).ToArray();
                Check(days.Length == 7 && days.All(x => x.IsVisible && x.IsEnabled) && time.Text == "02:35:40", "七天选项可操作，并与指定时间共用时间输入");
                int tomorrow = ((int)DateTime.Now.AddDays(1).DayOfWeek + 6) % 7; foreach (var day in days) day.IsChecked = AutomationProperties.GetName(day) == WeeklyPlan.DayNames[tomorrow]; action.SelectedIndex = (int)PowerAction.StopServices;
                Children<Button>(main).Single(x => (x.Content as string) == "开始定时").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(controller.Scheduler.Weekly && controller.Scheduler.Target.LocalDateTime.TimeOfDay == TimeSpan.Parse("02:35:40") && controller.Scheduler.Action == PowerAction.StopServices, "界面将日期、时间和动作交给每周计划，目标在未来且不会实际执行");
                Save(main, Path.Combine(output, "schedule.png")); controller.CancelSchedule();
                var item = new Dictionary<string, object> { { "Path", Path.Combine(settings.MediaFolder, "fixture.mkv") }, { "Name", "fixture" }, { "Id", "fixture" } }; File.WriteAllText(Json.Text(item, "Path"), "fixture"); controller.BatchPlan = new List<BatchEntry> { new BatchEntry { Local = item, Number = 1, Status = "待确认" } }; controller.Window.ShowBatch(); Pause(200); CheckRound(Children<DataGrid>(Children<BatchView>(main).Single()).Single(), "批量预览");
                report.Add("PASS: " + report.Count(x => x.StartsWith("PASS ")) + " current branding/weekly checks; no real API or power actions."); return 0;
            }
            catch (Exception error) { report.Add("FAIL " + error); return 1; }
            finally { File.WriteAllLines(Path.Combine(output, "report.txt"), report); if (controller != null) { controller.ReleaseWindow(); controller.Dispose(); } app.Shutdown(); Paths.Root = original; }
        }
        static void CheckRound(DataGrid table, string name)
        {
            var surface = Ui.Ancestor<SmoothBorder>(VisualTreeHelper.GetParent(table)); Check(surface != null && surface.CornerRadius.TopLeft == 12 && surface.Clip != null && !surface.Clip.FillContains(new Point(0.2, 0.2)), name + "列表使用统一圆角裁切，表头不超出边界");
        }
        static T Named<T>(DependencyObject root, string name) where T : DependencyObject { return Children<T>(root).Single(x => AutomationProperties.GetName(x) == name); }
        static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject { if (root == null) yield break; if (root is T) yield return (T)root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Children<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
        static void Check(bool condition, string name) { if (!condition) throw new Exception(name); report.Add("PASS " + name); }
        static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Check(rejected, name); }
        static void Pause(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        static void Save(FrameworkElement view, string path) { view.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(path)) encoder.Save(file); }
    }
}
