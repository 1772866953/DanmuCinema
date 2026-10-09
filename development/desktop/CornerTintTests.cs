using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DanmuCinema.Desktop
{
    public static class CornerTintTests
    {
        public static void Run(List<string> report, string output)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Ui.InstallTheme(app);
            try
            {
                foreach (int percent in new[] { 100, 120 }) foreach (int dpi in new[] { 96, 144, 192 })
                {
                    UiScale.Initialize(percent); var dialog = new DialogWindow("圆角配色检查", 780, 550); dialog.Show(); Pause(240); dialog.UpdateLayout();
                    var layers = (Grid)dialog.Content; var brush = layers.Background as LinearGradientBrush;
                    if (brush == null || brush.GradientStops[0].Color != ((SolidColorBrush)Ui.Resource("DialogHeader")).Color) throw new Exception("弹窗标题圆角底色不一致");
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth * dpi / 96), (int)Math.Ceiling(dialog.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32); bitmap.Render(layers);
                    byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); var title = ((SolidColorBrush)Ui.Resource("DialogHeader")).Color;
                    foreach (int x in new[] { 0, bitmap.PixelWidth - 1 })
                    { int i = x * 4; if (pixels[i] != title.B || pixels[i + 1] != title.G || pixels[i + 2] != title.R) throw new Exception("原生轮廓外的标题角残留异色色块"); }
                    report.Add("PASS " + percent + "% " + dpi + "DPI 标题两侧角落底色一致，保留不透明窗口");
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(Path.Combine(output, "title-corner-" + percent + "-" + dpi + ".png"))) encoder.Save(stream);
                    dialog.Close(); Pause(200);
                }
            }
            finally { app.Shutdown(); }
        }
        static void Pause(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
    }
}
