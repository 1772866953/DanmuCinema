using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DanmuCinema.Desktop
{
    // Render the affected controls without starting services or calling APIs.
    public static class ComboBorderTests
    {
        public static int Run()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var report = new List<string>();
            var output = Path.Combine(Paths.Root, "tests", "output", "combo-borders"); Directory.CreateDirectory(output);
            try
            {
                Ui.InstallTheme(app);
                foreach (double dpi in new[] { 96.0, 120.0, 144.0, 192.0 })
                {
                    var root = new StackPanel { Background = (Brush)Ui.Resource("Canvas"), UseLayoutRounding = true, SnapsToDevicePixels = true, Width = 360 };
                    var toolbar = Ui.Row(Ui.Button("清除", () => { }), Ui.Combo(new[] { "名称", "修改日期" }, 0, 115), Ui.Combo(new[] { "升序", "降序" }, 0, 85));
                    root.Children.Add(toolbar); root.Children.Add(Ui.Row(Ui.Text("影片间隔"), Ui.Combo(new[] { "1 秒", "3 秒" }, 0, 100)));
                    root.Measure(new Size(360, 200)); root.Arrange(new Rect(0, 0, 360, root.DesiredSize.Height)); root.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi / 96), (int)Math.Ceiling(root.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32); bitmap.Render(root);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(output, "combo-" + dpi + ".png"))) encoder.Save(file);
                    foreach (var combo in toolbar.Children.OfType<ComboBox>().Concat(((WrapPanel)root.Children[1]).Children.OfType<ComboBox>()))
                    {
                        var border = Ui.Child<SmoothBorder>(combo);
                        DependencyObject ancestor = border;
                        while (ancestor != null)
                        {
                            var element = ancestor as FrameworkElement;
                            if (element != null)
                            {
                                var clip = LayoutInformation.GetLayoutClip(element);
                                var bounds = border.TransformToAncestor(element).TransformBounds(new Rect(border.RenderSize));
                                if (clip != null && !clip.Bounds.Contains(bounds)) throw new Exception(combo.Text + "的边框被" + element.GetType().Name + "裁切");
                            }
                            if (ancestor == root) break; ancestor = VisualTreeHelper.GetParent(ancestor);
                        }
                        var surface = new RenderTargetBitmap((int)Math.Ceiling(border.ActualWidth * dpi / 96), (int)Math.Ceiling(border.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32); surface.Render(border);
                        var pixels = new byte[surface.PixelWidth * surface.PixelHeight * 4]; surface.CopyPixels(pixels, surface.PixelWidth * 4, 0); int middle = surface.PixelHeight / 2;
                        Func<int, int> green = x => pixels[(middle * surface.PixelWidth + x) * 4 + 1];
                        if (Math.Max(green(0), green(1)) < 50 || Math.Max(green(surface.PixelWidth - 1), green(surface.PixelWidth - 2)) < 50) throw new Exception(combo.Text + "左右边框像素不完整");
                        report.Add("PASS " + combo.Text + "：无布局裁切，左右边框像素完整，渲染缩放 " + dpi / 96 * 100 + "%");
                    }
                }
                report.Add("PASS: 12 dropdown border checks; no real API or media operations."); return 0;
            }
            catch (Exception error) { report.Add("FAIL " + error); return 1; }
            finally { File.WriteAllLines(Path.Combine(output, "report.txt"), report); app.Shutdown(); }
        }
    }
}
