using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DanmuCinema.Desktop
{
    public static class DialogOutlineTests
    {
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        public static int Run()
        {
            string original = Paths.Root, output = Path.Combine(Paths.TestOutputFor(original), "dialog-outline"); Directory.CreateDirectory(output);
            Paths.Root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Paths.Root);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; DesktopController controller = null; var report = new List<string>();
            try
            {
                System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext()); Ui.InstallTheme(app);
                var settings = new AppSettings { EnableDandan = false, EnableAnimeko = false, EnableBahamut = false, EnableExistingDanmu = false }; controller = new DesktopController(app, settings, false); controller.ShowWindow(); var main = controller.Window.View;
                controller.BatchPlan = new List<BatchEntry>();
                var outline = (Style)Ui.Resource("DialogOutline"); var colour = ((SolidColorBrush)Ui.Resource("DialogLine")).Color;
                foreach (int percent in new[] { 100, 120 })
                {
                    UiScale.Change(controller, percent); Pause(80);
                    var dialogs = new DialogWindow[] { new SourcesWindow(controller), new AlertWindow("确认", "仅验证边框，不执行操作。", true), new MatchWindow(controller, controller.Window, new MatchState(), false), new BatchWindow(controller) };
                    foreach (var dialog in dialogs)
                    {
                        string name = dialog.GetType().Name; controller.Window.Track(dialog); Pause(240); dialog.UpdateLayout(); var frame = Children<SmoothBorder>((DependencyObject)dialog.Content).First();
                        Check(frame.Style == outline && ((SolidColorBrush)frame.BorderBrush).Color == colour && Math.Abs(frame.BorderThickness.Left - percent / 100.0) < .001, name + " " + percent + "% 使用共同高亮外框及同比例线宽", report);
                        int borderColour; int result = DwmGetWindowAttribute(new WindowInteropHelper(dialog).Handle, 34, out borderColour, sizeof(int));
                        if (result == 0) Check(borderColour == (colour.R | colour.G << 8 | colour.B << 16), name + " 系统外沿也使用高亮颜色", report);
                        main.Activate(); Pause(50);
                        if (result == 0) { DwmGetWindowAttribute(new WindowInteropHelper(dialog).Handle, 34, out borderColour, sizeof(int)); Check(borderColour == (colour.R | colour.G << 8 | colour.B << 16), name + " 失去焦点不变成灰色外沿", report); }
                        foreach (int dpi in new[] { 96, 144, 192 })
                        {
                            var root = (FrameworkElement)dialog.Content; var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi / 96), (int)Math.Ceiling(root.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32); bitmap.Render(root);
                            byte[] pixels = new byte[4]; bitmap.CopyPixels(new Int32Rect(bitmap.PixelWidth / 2, 0, 1, 1), pixels, 4, 0);
                            Check(Math.Abs(pixels[0] - colour.B) <= 1 && Math.Abs(pixels[1] - colour.G) <= 1 && Math.Abs(pixels[2] - colour.R) <= 1, name + " " + dpi + "DPI 外框可见且颜色一致", report);
                            if (name == "SourcesWindow" && dpi == 96) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(output, "sources-" + percent + ".png"))) encoder.Save(file); }
                        }
                        dialog.Close(); Pause(220);
                    }
                    using (var pane = new FloatingPane(Ui.Text("下载预览"), new Rect(0, 0, 500, 300)))
                    { var border = Children<SmoothBorder>(pane).First(); Check(border.Style == outline && ((SolidColorBrush)border.BorderBrush).Color == colour && border.BorderThickness.Left == 1, percent + "% 来源选择与下载预览浮层共享同一外框样式", report); }
                }
                report.Add("PASS: " + report.Count + " focused outline checks; no API or service operations."); return 0;
            }
            catch (Exception error) { report.Add("FAIL " + error); return 1; }
            finally { if (controller != null) { controller.ReleaseWindow(); controller.Dispose(); } app.Shutdown(); Paths.Root = original; File.WriteAllLines(Path.Combine(output, "report.txt"), report); }
        }
        static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject { if (root == null) yield break; if (root is T) yield return (T)root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Children<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
        static void Check(bool condition, string message, List<string> report) { if (!condition) throw new Exception(message); report.Add("PASS " + message); }
        static void Pause(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
    }
}
