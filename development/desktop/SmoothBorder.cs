using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DanmuCinema.Desktop
{
    // Filled vector contours avoid the aliased one-pixel pen at rounded focus edges.
    public sealed class SmoothBorder : Border
    {
        Size cachedSize;
        CornerRadius cachedRadius;
        Thickness cachedThickness;
        Geometry outside, ring;
        public SmoothBorder() { SnapsToDevicePixels = false; RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified); }
        protected override void OnRender(DrawingContext drawing)
        {
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            if (outside == null || cachedSize != RenderSize || !cachedRadius.Equals(CornerRadius) || !cachedThickness.Equals(BorderThickness))
            {
                cachedSize = RenderSize; cachedRadius = CornerRadius; cachedThickness = BorderThickness;
                outside = Rounded(new Rect(RenderSize), CornerRadius);
                var t = BorderThickness; double width = Math.Max(0, ActualWidth - t.Left - t.Right), height = Math.Max(0, ActualHeight - t.Top - t.Bottom);
                if (width > 0 && height > 0)
                {
                    var innerRadius = new CornerRadius(Math.Max(0, CornerRadius.TopLeft - Math.Max(t.Left, t.Top)), Math.Max(0, CornerRadius.TopRight - Math.Max(t.Top, t.Right)), Math.Max(0, CornerRadius.BottomRight - Math.Max(t.Right, t.Bottom)), Math.Max(0, CornerRadius.BottomLeft - Math.Max(t.Left, t.Bottom)));
                    var group = new GeometryGroup { FillRule = FillRule.EvenOdd }; group.Children.Add(outside); group.Children.Add(Rounded(new Rect(t.Left, t.Top, width, height), innerRadius)); group.Freeze(); ring = group;
                }
                else ring = outside;
            }
            if (Background != null) drawing.DrawGeometry(Background, null, outside);
            if (BorderBrush != null && BorderThickness != new Thickness(0)) drawing.DrawGeometry(BorderBrush, null, ring);
        }
        internal static Geometry Rounded(Rect rect, CornerRadius radius)
        {
            double tl = radius.TopLeft, tr = radius.TopRight, br = radius.BottomRight, bl = radius.BottomLeft;
            double scale = Math.Min(1, Math.Min(Math.Min(rect.Width / Math.Max(1, tl + tr), rect.Width / Math.Max(1, bl + br)), Math.Min(rect.Height / Math.Max(1, tl + bl), rect.Height / Math.Max(1, tr + br))));
            tl *= scale; tr *= scale; br *= scale; bl *= scale;
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(new Point(rect.Left + tl, rect.Top), true, true);
                path.LineTo(new Point(rect.Right - tr, rect.Top), true, false); Arc(path, new Point(rect.Right, rect.Top + tr), tr);
                path.LineTo(new Point(rect.Right, rect.Bottom - br), true, false); Arc(path, new Point(rect.Right - br, rect.Bottom), br);
                path.LineTo(new Point(rect.Left + bl, rect.Bottom), true, false); Arc(path, new Point(rect.Left, rect.Bottom - bl), bl);
                path.LineTo(new Point(rect.Left, rect.Top + tl), true, false); Arc(path, new Point(rect.Left + tl, rect.Top), tl);
            }
            geometry.Freeze(); return geometry;
        }
        static void Arc(StreamGeometryContext path, Point end, double radius)
        { if (radius <= 0) path.LineTo(end, true, false); else path.ArcTo(end, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false); }
    }
}
