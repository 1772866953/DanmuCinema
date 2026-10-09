using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace DanmuCinema.Desktop
{
    public sealed class FloatingPane : Grid, IDisposable
    {
        readonly List<Tuple<Thumb, DragDeltaEventHandler>> handles = new List<Tuple<Thumb, DragDeltaEventHandler>>();
        readonly SmoothBorder surface;
        FrameworkElement area;
        bool placed, disposed;
        double left, top;
        public Rect Bounds { get { return new Rect(left, top, Math.Max(0, Width), Math.Max(0, Height)); } }
        public FloatingPane(UIElement content, Rect? saved)
        {
            HorizontalAlignment = HorizontalAlignment.Left; VerticalAlignment = VerticalAlignment.Top;
            Width = saved.HasValue ? saved.Value.Width : 1040; Height = saved.HasValue ? saved.Value.Height : 760;
            left = saved.HasValue ? saved.Value.X : 0; top = saved.HasValue ? saved.Value.Y : 0; placed = saved.HasValue;
            surface = new SmoothBorder { Style = (Style)Ui.Resource("DialogOutline"), Background = (Brush)Ui.Resource("DialogSurface"), Child = Ui.RoundedContent(content, 13) }; Children.Add(surface);
            AddEdge("left", Cursors.SizeWE, HorizontalAlignment.Left, VerticalAlignment.Stretch, 7, Double.NaN);
            AddEdge("right", Cursors.SizeWE, HorizontalAlignment.Right, VerticalAlignment.Stretch, 7, Double.NaN);
            AddEdge("top", Cursors.SizeNS, HorizontalAlignment.Stretch, VerticalAlignment.Top, Double.NaN, 7);
            AddEdge("bottom", Cursors.SizeNS, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, Double.NaN, 7);
            AddEdge("top-left", Cursors.SizeNWSE, HorizontalAlignment.Left, VerticalAlignment.Top, 12, 12);
            AddEdge("top-right", Cursors.SizeNESW, HorizontalAlignment.Right, VerticalAlignment.Top, 12, 12);
            AddEdge("bottom-left", Cursors.SizeNESW, HorizontalAlignment.Left, VerticalAlignment.Bottom, 12, 12);
            AddEdge("bottom-right", Cursors.SizeNWSE, HorizontalAlignment.Right, VerticalAlignment.Bottom, 12, 12);
            Loaded += PaneLoaded;
        }
        Thumb Handle(string edge, Cursor cursor)
        {
            var thumb = new Thumb { Tag = edge, Cursor = cursor, Focusable = false, Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='Transparent'/></ControlTemplate>") };
            DragDeltaEventHandler move = (s, e) => { if (edge == "move") MoveBy(e.HorizontalChange, e.VerticalChange); else ResizeBy(edge, e.HorizontalChange, e.VerticalChange); e.Handled = true; };
            thumb.DragDelta += move; handles.Add(Tuple.Create(thumb, move)); return thumb;
        }
        void AddEdge(string edge, Cursor cursor, HorizontalAlignment horizontal, VerticalAlignment vertical, double width, double height)
        { var thumb = Handle(edge, cursor); thumb.HorizontalAlignment = horizontal; thumb.VerticalAlignment = vertical; thumb.Width = width; thumb.Height = height; Children.Add(thumb); }
        public Thumb DragHandle() { var handle = Handle("move", Cursors.SizeAll); handle.ToolTip = "拖动移动页面；拖动边缘或四角调整大小"; return handle; }
        void PaneLoaded(object sender, RoutedEventArgs e)
        {
            if (disposed) return; area = Parent as FrameworkElement;
            if (area != null) area.SizeChanged += AreaSized;
            if (!placed && area != null) { Width = Math.Min(1040, area.ActualWidth); Height = area.ActualHeight; left = (area.ActualWidth - Width) / 2; top = 0; placed = true; }
            Clamp();
        }
        void AreaSized(object sender, SizeChangedEventArgs e) { Clamp(); }
        void Clamp()
        {
            if (area == null || area.ActualWidth <= 0 || area.ActualHeight <= 0) return;
            double minWidth = Math.Min(680, area.ActualWidth), minHeight = Math.Min(540, area.ActualHeight);
            Width = Math.Max(minWidth, Math.Min(Width, area.ActualWidth)); Height = Math.Max(minHeight, Math.Min(Height, area.ActualHeight));
            left = Math.Max(0, Math.Min(left, area.ActualWidth - Width)); top = Math.Max(0, Math.Min(top, area.ActualHeight - Height)); Margin = new Thickness(left, top, 0, 0);
        }
        internal void MoveBy(double x, double y) { left += x; top += y; Clamp(); }
        internal void ResizeBy(string edge, double x, double y)
        {
            if (area == null) return;
            double right = left + Width, bottom = top + Height;
            if (edge.Contains("left")) left = Math.Max(0, Math.Min(left + x, right - Math.Min(680, area.ActualWidth)));
            if (edge.Contains("right")) right = Math.Min(area.ActualWidth, Math.Max(right + x, left + Math.Min(680, area.ActualWidth)));
            if (edge.Contains("top")) top = Math.Max(0, Math.Min(top + y, bottom - Math.Min(540, area.ActualHeight)));
            if (edge.Contains("bottom")) bottom = Math.Min(area.ActualHeight, Math.Max(bottom + y, top + Math.Min(540, area.ActualHeight)));
            Width = right - left; Height = bottom - top; Clamp();
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; Loaded -= PaneLoaded; if (area != null) area.SizeChanged -= AreaSized; area = null;
            foreach (var handle in handles) handle.Item1.DragDelta -= handle.Item2; handles.Clear();
            Ui.ReleaseVisualTree(this); surface.Child = null; Children.Clear();
        }
    }
}
