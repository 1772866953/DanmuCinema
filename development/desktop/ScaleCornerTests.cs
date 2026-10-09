using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DanmuCinema.Desktop
{
    // Focused checks for first-open placement, the two zoom choices and corner contours.
    public static class ScaleCornerTests
    {
        [StructLayout(LayoutKind.Sequential)] struct RectI { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RectI rect);
        static readonly List<string> report = new List<string>();
        static string output;
        public static int Run()
        {
            string original = Paths.Root; output = Path.Combine(Paths.TestOutputFor(original), "scale-corners"); Directory.CreateDirectory(output);
            Paths.Root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Paths.Root);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; DesktopController controller = null;
            try
            {
                System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext()); Ui.InstallTheme(app);
                var settings = new AppSettings { MediaFolder = Path.Combine(Paths.Root, "videos"), EnableDandan = false, EnableAnimeko = false, EnableBahamut = false, EnableExistingDanmu = false }; Directory.CreateDirectory(settings.MediaFolder);
                Directory.CreateDirectory(Paths.Data);
                foreach (int old in new[] { 125, 150, 175, 200 })
                {
                    settings.UiScalePercent = old; File.WriteAllText(Paths.SettingsFile, Json.Write(settings));
                    Check(SettingsStore.Load().UiScalePercent == 120, "旧配置 " + old + "% 安全迁移到120%");
                }
                settings.UiScalePercent = 100; SettingsStore.Save(settings);
                Check(Json.Read<AppSettings>("{}").UiScalePercent == 100, "未设置比例的旧配置默认100%");
                settings.UiScalePercent = 110; bool invalid = false; try { settings.Validate(); } catch (ArgumentException) { invalid = true; } Check(invalid, "无效比例仍拒绝"); settings.UiScalePercent = 100;
                controller = new DesktopController(app, settings, false); controller.ShowWindow(); var main = controller.Window.View;
                main.Width = 1200; main.Height = 820; controller.Window.Navigate("library"); Pause(200);
                SearchHistory.Add("library", "测试");
                CheckHistory(main, "冷启动100%首次");
                UiScale.Change(controller, 120); main.UpdateLayout(); CheckHistory(main, "切换120%首次"); CheckHistory(main, "120%再次打开");
                controller.Window.Navigate("settings"); Pause(80);
                var choice = Children<ComboBox>(main).Single(x => AutomationProperties.GetName(x) == "界面缩放");
                Check(choice.Items.Count == 2 && choice.SelectedIndex == 1 && choice.Items[1].ToString().Contains("120"), "设置仅显示100%和120%两档");
                CheckCombo(choice, "120%标准下拉首次");
                choice.SelectedIndex = 0; main.UpdateLayout(); CheckCombo(choice, "改回100%标准下拉首次");
                choice.SelectedIndex = 1; main.UpdateLayout(); CheckCombo(choice, "切换120%同一下拉首次");
                CheckDetachedPopups(choice, main);
                controller.ReleaseWindow(); controller.ShowWindow(); main = controller.Window.View; Pause(160); controller.Window.Navigate("library"); Pause(100);
                Check(SettingsStore.Load().UiScalePercent == 120 && UiScale.Current == 120, "托盘恢复记忆120%且持久化有效");
                CheckHistory(main, "托盘恢复120%首次"); CheckHistory(main, "托盘恢复再次打开");
                foreach (int percent in new[] { 100, 120 })
                {
                    UiScale.Change(controller, percent); Pause(80);
                    foreach (int dpi in new[] { 96, 144, 192 }) CheckTableCorners(percent, dpi);
                    var dialog = new SourcesWindow(controller); controller.Window.Track(dialog); Pause(250);
                    var surface = (Grid)typeof(DialogWindow).GetField("animatedSurface", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
                    var frame = Children<SmoothBorder>(surface).First(); var content = (Grid)frame.Child;
                    double radius = System.Windows.Shell.WindowChrome.GetWindowChrome(dialog).CornerRadius.TopLeft;
                    Check((radius == 8 || radius == 14) && frame.CornerRadius.TopLeft == radius && (double)content.Tag == radius - percent / 100.0 && content.Clip != null && ((Grid)dialog.Content).Clip == null, percent + "% 原生弹窗边框与系统圆角一致，只裁切内容");
                    Check(dialog.Opacity == 1 && frame.Clip == null, percent + "% 弹窗保留不透明窗口及完整抗锯齿边线");
                    Save(dialog, Path.Combine(output, "dialog-" + percent + ".png"), 96);
                    dialog.Close(); Pause(240);
                }
                report.Add("PASS: " + report.Count(x => x.StartsWith("PASS ")) + " focused checks. No real API, download, power or service operations."); return 0;
            }
            catch (Exception e) { report.Add("FAIL " + e); return 1; }
            finally { if (controller != null) { controller.ReleaseWindow(); controller.Dispose(); } app.Shutdown(); Paths.Root = original; File.WriteAllLines(Path.Combine(output, "report.txt"), report); }
        }
        static void CheckHistory(Window main, string label)
        {
            var input = Children<HistoryInput>(main).Single(); var popup = (Popup)typeof(HistoryInput).GetField("popup", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input);
            input.Editor.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            CheckPlacement(popup, input, label);
            popup.IsOpen = false; Pause(30);
        }
        static void CheckCombo(ComboBox choice, string label)
        {
            choice.ApplyTemplate(); choice.IsDropDownOpen = true; var popup = (Popup)choice.Template.FindName("PART_Popup", choice);
            CheckPlacement(popup, choice, label); choice.IsDropDownOpen = false; Pause(30);
        }
        static void CheckPlacement(Popup popup, FrameworkElement target, string label)
        {
            RectI first = new RectI(); bool seen = false;
            foreach (int delay in new[] { 1, 15, 35, 70, 100 })
            {
                Pause(delay); var source = PresentationSource.FromVisual(popup.Child) as HwndSource; RectI bounds;
                Check(source != null && GetWindowRect(source.Handle, out bounds), label + " 弹层HWND可读 " + delay + "ms"); GetWindowRect(source.Handle, out bounds);
                var bottom = target.PointToScreen(new Point(0, target.ActualHeight));
                var visible = ((FrameworkElement)popup.Child).PointToScreen(new Point(0, 0));
                report.Add("INFO " + label + " " + delay + "ms HWND " + bounds.Left + "," + bounds.Top + "," + bounds.Right + "," + bounds.Bottom + " child " + visible + " target " + bottom + " size " + target.RenderSize);
                // PopupAnimation reserves a small transparent margin on the HWND.
                // Test that margin separately from the visible surface below.
                Check(bounds.Top >= bottom.Y - 7 && Math.Abs(bounds.Left - bottom.X) < 2, label + " 定位在输入框下方 " + delay + "ms（" + bounds.Top + "/" + bottom.Y.ToString("F1") + "）");
                if (seen) Check(bounds.Left == first.Left && bounds.Top == first.Top && bounds.Right == first.Right && bounds.Bottom == first.Bottom, label + " 动画期间边界稳定 " + delay + "ms");
                else { first = bounds; seen = true; }
            }
            var child = (FrameworkElement)popup.Child; var dpi = PresentationSource.FromVisual(target).CompositionTarget.TransformToDevice.M11;
            var origin = child.PointToScreen(new Point(0, 0)); var edge = target.PointToScreen(new Point(0, target.ActualHeight));
            Check(origin.Y >= edge.Y - .5 && Math.Abs(origin.X - edge.X) < 2, label + " 可见表面完全在输入框下方");
            double physical = child.PointToScreen(new Point(child.ActualWidth, 0)).X - child.PointToScreen(new Point(0, 0)).X;
            Check(Math.Abs(physical / child.ActualWidth / dpi - UiScale.Current / 100.0) < 0.02, label + " 弹层按当前矢量比例显示");
            Check(Math.Abs(first.Right - first.Left - physical) < 2, label + " 首帧窗口宽度即为最终宽度");
        }
        static void CheckDetachedPopups(FrameworkElement target, Window main)
        {
            var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom }; menu.Items.Add(new MenuItem { Header = "测试菜单" }); menu.ApplyTemplate();
            Check(((ScaleTransform)menu.LayoutTransform).ScaleX == 1.2, "右键菜单在打开前已有正确缩放"); menu.IsOpen = true; Pause(180); menu.IsOpen = false;
            var tip = new ToolTip { PlacementTarget = target, Content = "测试提示", Placement = PlacementMode.Bottom }; tip.ApplyTemplate();
            Check(((ScaleTransform)tip.LayoutTransform).ScaleX == 1.2, "提示在打开前已有正确缩放"); tip.IsOpen = true; Pause(100); tip.IsOpen = false;
        }
        static void CheckTableCorners(int percent, int dpi)
        {
            var background = (Brush)Ui.Resource("Canvas"); var header = Ui.Brush("#223149");
            var table = (SmoothBorder)Ui.TableSurface(new Grid { Background = header }); table.Width = 200; table.Height = 100; table.LayoutTransform = new ScaleTransform(percent / 100.0, percent / 100.0);
            var host = new Grid { Background = background, Width = 200 * percent / 100.0, Height = 100 * percent / 100.0 }; host.Children.Add(table); host.Measure(new Size(host.Width, host.Height)); host.Arrange(new Rect(0, 0, host.Width, host.Height)); host.UpdateLayout();
            var clip = (Grid)table.Child;
            Check(clip.Clip != null && !clip.Clip.FillContains(new Point(0, 0)), percent + "% " + dpi + "DPI 内容自动裁切圆角");
            var actual = Render(host, dpi); byte[] pixels = Pixels(actual);
            Check(pixels[0] == 0x27 && pixels[1] == 0x18 && pixels[2] == 0x10 && table.Clip == null, percent + "% " + dpi + "DPI 圆角外保持底色且边线不重复裁切");
            double scale = percent / 100.0 * dpi / 96.0; int samples = 0; double difference = 0;
            var reference = new SmoothBorder { Width = 200, Height = 100, CornerRadius = new CornerRadius(12), Background = header, BorderBrush = (Brush)Ui.Resource("Line"), BorderThickness = new Thickness(1), LayoutTransform = new ScaleTransform(percent / 100.0, percent / 100.0), UseLayoutRounding = false };
            var refHost = new Grid { Width = host.Width, Height = host.Height, Background = background }; refHost.Children.Add(reference); refHost.Measure(new Size(host.Width, host.Height)); refHost.Arrange(new Rect(0, 0, host.Width, host.Height)); refHost.UpdateLayout(); var expected = Pixels(Render(refHost, dpi));
            for (int y = 0; y < 12 * scale; y++) for (int x = 0; x < 12 * scale; x++)
            {
                double dx = (x + .5) / scale - 12, dy = (y + .5) / scale - 12, distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < 11.15 || distance > 11.85) continue;
                int index = (y * actual.PixelWidth + x) * 4; for (int c = 0; c < 3; c++) difference += Math.Abs(pixels[index + c] - expected[index + c]); samples += 3;
            }
            Check(samples > 0 && difference / samples < 12, percent + "% " + dpi + "DPI 表头未盖住圆角边线（平均色差 " + (difference / Math.Max(1, samples)).ToString("F2") + "）");
            Save(host, Path.Combine(output, "corner-" + percent + "-" + dpi + ".png"), dpi);
        }
        static byte[] Pixels(BitmapSource bitmap) { var data = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(data, bitmap.PixelWidth * 4, 0); return data; }
        static RenderTargetBitmap Render(FrameworkElement view, int dpi) { var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * dpi / 96), (int)Math.Ceiling(view.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32); bitmap.Render(view); return bitmap; }
        static void Save(FrameworkElement view, string path, int dpi) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Render(view, dpi))); using (var file = File.Create(path)) encoder.Save(file); }
        static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject { if (root == null) yield break; if (root is T) yield return (T)root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Children<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
        static void Check(bool condition, string label) { if (!condition) throw new Exception(label); report.Add("PASS " + label); }
        static void Pause(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
    }
}
