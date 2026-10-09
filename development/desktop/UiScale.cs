using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace DanmuCinema.Desktop
{
    // LayoutTransform remeasures vector controls/glyphs at the requested scale.
    // Never scale a cached bitmap, or animate the text through fractional zooms.
    public static class UiScale
    {
        public static readonly DependencyProperty PopupScaleProperty = DependencyProperty.RegisterAttached("PopupScale", typeof(bool), typeof(UiScale), new PropertyMetadata(false, PopupChanged));
        public static bool GetPopupScale(DependencyObject target) { return (bool)target.GetValue(PopupScaleProperty); }
        public static void SetPopupScale(DependencyObject target, bool value) { target.SetValue(PopupScaleProperty, value); }
        static void PopupChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            var element = target as FrameworkElement;
            if (element == null) return;
            if ((bool)e.NewValue) ScalePopup(element);
            else { element.ClearValue(FrameworkElement.LayoutTransformProperty); element.ClearValue(TextOptions.TextFormattingModeProperty); }
        }
        public static void ScalePopup(FrameworkElement element)
        {
            // Resolve before the Popup's first measure/placement, never in Opened.
            element.SetResourceReference(FrameworkElement.LayoutTransformProperty, "PopupScaleTransform");
            element.SetResourceReference(TextOptions.TextFormattingModeProperty, "PopupTextFormatting");
            TextOptions.SetTextRenderingMode(element, TextRenderingMode.ClearType);
        }
        static readonly DependencyProperty RootProperty = DependencyProperty.RegisterAttached("Root", typeof(FrameworkElement), typeof(UiScale));
        static readonly DependencyProperty CaptionProperty = DependencyProperty.RegisterAttached("Caption", typeof(double), typeof(UiScale));
        public static int Current { get { var value = Application.Current.Resources["UiScalePercent"]; return value is int ? (int)value : 100; } }
        public static void Initialize(int percent)
        {
            Application.Current.Resources["UiScalePercent"] = percent;
            var transform = new ScaleTransform(percent / 100.0, percent / 100.0); transform.Freeze();
            Application.Current.Resources["PopupScaleTransform"] = transform;
            Application.Current.Resources["PopupTextFormatting"] = percent == 100 ? TextFormattingMode.Display : TextFormattingMode.Ideal;
        }
        public static void Attach(Window window, FrameworkElement root, double caption)
        {
            if (window is DialogWindow && Current > 100)
            { window.Width = Math.Min(SystemParameters.WorkArea.Width - 32, window.Width * Current / 100.0); window.Height = Math.Min(SystemParameters.WorkArea.Height - 32, window.Height * Current / 100.0); }
            window.SetValue(RootProperty, root); window.SetValue(CaptionProperty, caption); Apply(window, Current);
        }
        static void Apply(Window window, int percent)
        {
            var root = window.GetValue(RootProperty) as FrameworkElement; if (root == null) return;
            var transform = new ScaleTransform(percent / 100.0, percent / 100.0); transform.Freeze(); root.LayoutTransform = transform;
            root.CacheMode = null; TextOptions.SetTextFormattingMode(root, percent == 100 ? TextFormattingMode.Display : TextFormattingMode.Ideal); TextOptions.SetTextRenderingMode(root, TextRenderingMode.ClearType);
            var chrome = WindowChrome.GetWindowChrome(window); if (chrome != null) chrome.CaptionHeight = (double)window.GetValue(CaptionProperty) * percent / 100.0;
        }
        public static void Detach(Window window) { window.ClearValue(RootProperty); window.ClearValue(CaptionProperty); }
        public static void Change(DesktopController controller, int percent)
        {
            if (percent != 100 && percent != 120) throw new ArgumentException("请选择有效的界面缩放比例。");
            if (controller.Settings.UiScalePercent == percent && Current == percent) return;
            int previous = controller.Settings.UiScalePercent; controller.Settings.UiScalePercent = percent;
            try { SettingsStore.Save(controller.Settings); } catch { controller.Settings.UiScalePercent = previous; throw; }
            Initialize(percent);
            foreach (Window window in Application.Current.Windows.Cast<Window>().ToArray()) Apply(window, percent);
        }
    }

    // Keep star-sized tables under finite measure constraints even at high zoom.
    // Small viewports scroll a complete logical layout instead of clipping rows,
    // buttons or forcing a virtualized table to measure at infinite height.
    public sealed class LayoutViewport : ScrollViewer
    {
        readonly Grid panel;
        readonly double minimumWidth, minimumHeight;
        public LayoutViewport(UIElement child, double minimumWidth, double minimumHeight)
        {
            this.minimumWidth = minimumWidth; this.minimumHeight = minimumHeight;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            CanContentScroll = false; Focusable = false;
            UseLayoutRounding = false;
            panel = new Grid { Width = minimumWidth, Height = minimumHeight, UseLayoutRounding = false }; panel.Children.Add(child); Content = panel;
            Loaded += (s, e) => Fit(); SizeChanged += (s, e) => Fit(); LayoutUpdated += (s, e) => Fit(); ScrollChanged += (s, e) => { if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) Fit(); };
        }
        void Fit()
        {
            double width = ViewportWidth > 0 ? ViewportWidth : ActualWidth > 0 ? ActualWidth : minimumWidth;
            double scrollingMinimum; bool scrolls = TryScrollingMinimum(panel.Children[0], out scrollingMinimum);
            double height = Math.Max(scrolls ? scrollingMinimum : Math.Max(minimumHeight, ContentMinimumHeight(panel.Children[0])), ViewportHeight > 0 ? ViewportHeight : ActualHeight);
            if (!Double.IsInfinity(width) && Math.Abs(panel.Width - width) > 0.1) panel.Width = width;
            if (!Double.IsInfinity(height) && Math.Abs(panel.Height - height) > 0.1) panel.Height = height;
        }
        static bool TryScrollingMinimum(UIElement element, out double minimum)
        {
            minimum = 0;
            if (element is ScrollViewer) return true;
            var content = element as ContentControl; if (content != null && content.Content is UIElement) return TryScrollingMinimum((UIElement)content.Content, out minimum);
            var grid = element as Grid;
            if (grid != null && grid.RowDefinitions.Count == 0 && grid.Children.Count == 1)
            { bool result = TryScrollingMinimum(grid.Children[0], out minimum); minimum += grid.Margin.Top + grid.Margin.Bottom; return result; }
            var dock = element as DockPanel;
            if (dock != null && dock.Children.Count > 0 && (dock.Children[dock.Children.Count - 1] is ScrollViewer || dock.Children[dock.Children.Count - 1] is TextBox))
            { minimum = 120 + dock.Children.Cast<UIElement>().Take(dock.Children.Count - 1).Where(child => DockPanel.GetDock(child) == Dock.Top || DockPanel.GetDock(child) == Dock.Bottom).Sum(child => child.DesiredSize.Height); return true; }
            return false;
        }
        static double ContentMinimumHeight(UIElement element)
        {
            var content = element as ContentControl; if (content != null && content.Content is UIElement) return ContentMinimumHeight((UIElement)content.Content);
            var grid = element as Grid; if (grid == null) return 0;
            if (grid.RowDefinitions.Count == 0 && grid.Children.Count == 1) return ContentMinimumHeight(grid.Children[0]);
            if (!grid.RowDefinitions.Any(row => row.Height.IsStar)) return 0;
            return grid.RowDefinitions.Sum(row => row.Height.IsStar ? Math.Max(180, row.MinHeight) : row.ActualHeight) + grid.Margin.Top + grid.Margin.Bottom;
        }
    }

    public sealed class AdaptiveWrapPanel : WrapPanel
    {
        static readonly DependencyProperty MaximumProperty = DependencyProperty.RegisterAttached("Maximum", typeof(double?), typeof(AdaptiveWrapPanel));
        static readonly DependencyProperty MinimumProperty = DependencyProperty.RegisterAttached("Minimum", typeof(double?), typeof(AdaptiveWrapPanel));
        protected override Size MeasureOverride(Size available)
        {
            if (!Double.IsInfinity(available.Width) && available.Width > 0)
                foreach (FrameworkElement child in Children.OfType<FrameworkElement>())
                {
                    if (child.GetValue(MaximumProperty) == null) { child.SetValue(MaximumProperty, (double?)child.MaxWidth); child.SetValue(MinimumProperty, (double?)child.MinWidth); }
                    double maximum = Math.Min((double?)child.GetValue(MaximumProperty) ?? Double.PositiveInfinity, Math.Max(40, available.Width - child.Margin.Left - child.Margin.Right));
                    double minimum = Math.Min((double?)child.GetValue(MinimumProperty) ?? 0, maximum);
                    if (child.MinWidth != minimum) child.MinWidth = minimum;
                    if (child.MaxWidth != maximum) child.MaxWidth = maximum;
                }
            return base.MeasureOverride(available);
        }
    }
}
