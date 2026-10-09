using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DanmuCinema.Desktop
{
    internal static class DialogBackdrop
    {
        // Blend the complete dialog into the owner's last composed frame while
        // keeping the HWND opaque. Window.Opacity would revive native flash issues.
        public static BitmapSource Capture(Window dialog)
        {
            var owner = dialog.Owner;
            if (owner == null || !owner.IsVisible || dialog.ActualWidth <= 0 || dialog.ActualHeight <= 0) return null;
            try
            {
                var start = owner.PointFromScreen(dialog.PointToScreen(new Point(0, 0)));
                var end = owner.PointFromScreen(dialog.PointToScreen(new Point(dialog.ActualWidth, dialog.ActualHeight)));
                var source = PresentationSource.FromVisual(dialog) as HwndSource;
                if (source == null || source.CompositionTarget == null) return null;
                var scale = source.CompositionTarget.TransformToDevice;
                int width = (int)Math.Ceiling(dialog.ActualWidth * scale.M11), height = (int)Math.Ceiling(dialog.ActualHeight * scale.M22);
                if (width <= 0 || height <= 0 || (long)width * height > 16000000) return null;
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    var area = new Rect(0, 0, dialog.ActualWidth, dialog.ActualHeight);
                    drawing.DrawRectangle((Brush)Ui.Resource("Canvas"), null, area);
                    var brush = new VisualBrush(owner) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(start, end), Stretch = Stretch.Fill };
                    drawing.DrawRectangle(brush, null, area);
                }
                var image = new RenderTargetBitmap(width, height, 96 * scale.M11, 96 * scale.M22, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
            }
            catch (InvalidOperationException) { return null; }
            catch (ArgumentException) { return null; }
        }
    }
}
