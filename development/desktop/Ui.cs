using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace DanmuCinema.Desktop
{
    public static class Ui
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr SetActiveWindow(IntPtr window);
        public static bool IsForeground(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            return handle == IntPtr.Zero ? window.IsActive : handle == GetForegroundWindow();
        }
        public static void RestoreDialogOwner(Window owner, bool foreground)
        {
            if (owner == null || !owner.IsVisible || owner.WindowState == WindowState.Minimized) return;
            var handle = new WindowInteropHelper(owner).Handle;
            // WPF/DWM retain the parent's composed surface while its child closes.
            // Showing an already visible HWND and synchronously repainting its
            // non-client frame discards that smooth handover and can flash white.
            // Repair only an actual native/logical visibility mismatch.
            if (handle != IntPtr.Zero && !IsWindowVisible(handle))
                SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x1 | 0x2 | 0x4 | 0x10 | 0x40);
            // Only the closure of a foreground child returns focus to its direct
            // parent. Closing background tasks must never interrupt another app.
            // IsActive can lag native focus during owned-window destruction.
            // Consult the HWND here so an inactive-looking handover is repaired.
            if (foreground && handle != GetForegroundWindow()) owner.Activate();
        }
        public static bool EnableWindowTransitions(Window window)
        {
            var source = PresentationSource.FromVisual(window) as HwndSource;
            if (source != null) NativeWindowSurface.Attach(window, source, (window.Background as SolidColorBrush ?? (SolidColorBrush)Resource("Canvas")).Color);
            // Keep the system's animation policy; only remove our window's opt-out.
            if (!SystemParameters.MinimizeAnimation) return true;
            try { int disabled = 0; return DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 3, ref disabled, sizeof(int)) == 0; } catch (DllNotFoundException) { return false; }
        }
        internal static bool PrepareDialogClose(Window window)
        { return PrepareDialogClose(window, IsForeground(window)); }
        internal static bool SuppressDialogCloseTransition(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return false;
            // A dying dialog must not animate a second snapshot after its owner
            // becomes active. Only this HWND's final close transition is removed;
            // live windows retain their normal maximize/restore/open animations.
            try { int disabled = 1; return DwmSetWindowAttribute(handle, 3, ref disabled, sizeof(int)) == 0; }
            catch (DllNotFoundException) { return false; }
        }
        internal static bool PrepareDialogClose(Window window, bool foreground)
        {
            var owner = window.Owner;
            if (owner == null || !owner.IsVisible || owner.WindowState == WindowState.Minimized || !foreground) return false;
            var handle = new WindowInteropHelper(owner).Handle;
            if (handle == IntPtr.Zero || !IsWindowVisible(handle)) return false;
            // Destruction selects Windows' last active top-level HWND. That can
            // be another app between the owned dialog and its inactive owner.
            // Activate the direct owner while the dialog still covers it, before
            // WM_DESTROY/DWM can expose that intervening app for a frame.
            // Z-order is repaired before destruction even if Windows refuses
            // foreground activation (e.g. during an interleaved focus change).
            bool raised = SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x200);
            // SetForegroundWindow can be denied; the GUI thread must still
            // nominate its owner as active before DestroyWindow selects a fallback.
            SetActiveWindow(handle);
            owner.Activate(); return raised;
        }
        public static Brush Brush(string color) { var value = (SolidColorBrush)new BrushConverter().ConvertFromString(color); value.Freeze(); return value; }
        public static Brush StatusBrush(string status)
        {
            string key = status == "已就绪" || (status ?? "").StartsWith("已保存") || status == "保留已有 XML" ? "ServiceRunning" : (status ?? "").Contains("失败") || (status ?? "").Contains("超时") ? "StatusError" : status == "待匹配" || status == "等待准备" || status == "已暂停" || status == "待确认" || status == "暂无弹幕" ? "StatusWaiting" : "Accent";
            return (Brush)Resource(key);
        }
        public static object Resource(string name) { return Application.Current.FindResource(name); }
        public static T Load<T>(string name)
        {
            using (var stream = typeof(Ui).Assembly.GetManifestResourceStream("DanmuCinema.Desktop." + name))
            using (var reader = new System.IO.StreamReader(stream))
                return (T)XamlReader.Parse(reader.ReadToEnd().Replace("assembly=DanmuCinema\"", "assembly=" + typeof(Ui).Assembly.GetName().Name + "\""));
        }
        public static void InstallTheme(Application application) { UiScale.Initialize(100); application.Resources.MergedDictionaries.Add(Load<ResourceDictionary>("Theme.xaml")); var logo = Load<DrawingImage>("AppLogo.xaml"); logo.Freeze(); application.Resources["AppLogo"] = logo; }
        public static Border TableSurface(UIElement content)
        {
            return new SmoothBorder { UseLayoutRounding = false, Child = RoundedContent(content, 11), CornerRadius = new CornerRadius(12), Background = (Brush)Resource("Surface"), BorderBrush = (Brush)Resource("Line"), BorderThickness = new Thickness(1) };
        }
        internal static Grid RoundedContent(UIElement content, double radius)
        {
            // Clip only the inset content, not the already antialiased border ring.
            // Square table headers must never paint over that ring at the corners.
            var clip = new Grid { Tag = radius, UseLayoutRounding = false }; clip.Children.Add(content);
            clip.SizeChanged += (s, e) => clip.Clip = SmoothBorder.Rounded(new Rect(clip.RenderSize), new CornerRadius((double)clip.Tag));
            return clip;
        }
        public static TextBlock Text(string text, string style = null)
        { var value = new TextBlock { Text = text }; if (style != null) value.Style = (Style)Resource(style); return value; }
        static readonly DependencyProperty ButtonActionProperty = DependencyProperty.RegisterAttached("ButtonAction", typeof(Action), typeof(Ui));
        static void InvokeButtonAction(object sender, RoutedEventArgs e)
        { var action = ((Button)sender).GetValue(ButtonActionProperty) as Action; if (action != null) action(); }
        public static Button Button(string text, Action action, bool primary = false)
        {
            var button = new Button { Content = text }; AutomationProperties.SetName(button, text);
            if (primary) button.Style = (Style)Resource("Primary");
            UiHints.Apply(button, text); button.SetValue(ButtonActionProperty, action); button.Click += InvokeButtonAction; return button;
        }
        public static WrapPanel Row(params UIElement[] children)
        { var row = new AdaptiveWrapPanel { VerticalAlignment = VerticalAlignment.Center }; foreach (var child in children) row.Children.Add(child); return row; }
        public static StackPanel Stack(params UIElement[] children)
        { var stack = new StackPanel(); foreach (var child in children) stack.Children.Add(child); return stack; }
        public static Border Card(string title, params UIElement[] children)
        {
            var stack = Stack(Text(title, "Heading")); foreach (var child in children) stack.Children.Add(child);
            return new Border { Background = (Brush)Resource("Surface"), BorderBrush = (Brush)Resource("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(22, 20, 22, 18), Margin = new Thickness(0, 0, 0, 18), Child = stack };
        }
        public static ComboBox Combo(IEnumerable<string> values, int selected, double width = 160)
        { var combo = new ComboBox { Width = width, ItemsSource = values.ToArray(), SelectedIndex = selected }; return combo; }
        public static TextBox Input(string value, double width = 260)
        { return new TextBox { Text = value ?? "", Width = width }; }
        public static CheckBox Check(string text, bool value)
        { var result = new CheckBox { Content = Text(text), IsChecked = value }; AutomationProperties.SetName(result, text); UiHints.Apply(result, text); return result; }
        public static TextBlock Label(string text) { return new TextBlock { Text = text, Width = 100, Margin = new Thickness(0, 0, 10, 10), Foreground = (Brush)Resource("Muted") }; }
        public static void Animate(FrameworkElement element)
        {
            // Never animate the text's opacity or position. Transparent intermediate
            // buffers and fractional translations change WPF glyph antialiasing.
            element.BeginAnimation(UIElement.OpacityProperty, null);
            var transform = element.RenderTransform as TranslateTransform;
            if (transform != null) { transform.BeginAnimation(TranslateTransform.YProperty, null); transform.Y = 0; }
            element.ClearValue(TextOptions.TextFormattingModeProperty);
            TextOptions.SetTextRenderingMode(element, TextRenderingMode.ClearType);
        }
        public static Border AccentLine()
        { return new Border { Height = 3, Width = 64, CornerRadius = new CornerRadius(2), Background = (Brush)Resource("AccentGradient"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), IsHitTestVisible = false }; }
        public static void AnimateAccent(Border line)
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            line.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(28, 64, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }
        public static void AnimateSurface(Border surface)
        {
            var background = surface.Background as SolidColorBrush;
            if (background == null || !SystemParameters.ClientAreaAnimation) return;
            var color = background.Color; var brush = new SolidColorBrush(color); surface.Background = brush;
            var start = Color.FromArgb(color.A, (byte)Math.Min(255, color.R + 6), (byte)Math.Min(255, color.G + 6), (byte)Math.Min(255, color.B + 8));
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(start, color, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
        }
        public static System.Windows.Shapes.Path CaptionGlyph(string kind)
        {
            string data = kind == "minimize" ? "M 1,7 L 13,7" : kind == "maximize" ? "M 2,2 L 12,2 12,12 2,12 Z" : kind == "restore" ? "M 5,2 L 12,2 12,9 M 2,5 L 9,5 9,12 2,12 Z" : "M 2,2 L 12,12 M 12,2 L 2,12";
            var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(data), Width = 14, Height = 14, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) }); return path;
        }
        public static void ConfigureCaption(Button button, string kind)
        { button.Content = CaptionGlyph(kind); button.Style = (Style)Resource("Caption"); button.Tag = kind; AutomationProperties.SetName(button, kind == "minimize" ? "最小化" : kind == "maximize" || kind == "restore" ? "最大化 / 还原" : "关闭"); }
        public static Grid Atmosphere()
        {
            var background = new Grid { IsHitTestVisible = false, ClipToBounds = true };
            // Soft radial colors preserve the glow without allocating an effect render
            // target on each owned window activation or resize.
            var blue = new RadialGradientBrush(Color.FromArgb(36, 77, 101, 159), Colors.Transparent); blue.Freeze();
            var teal = new RadialGradientBrush(Color.FromArgb(24, 58, 159, 173), Colors.Transparent); teal.Freeze();
            background.Children.Add(new System.Windows.Shapes.Ellipse { Width = 500, Height = 380, Fill = blue, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -160, -130, 0) });
            background.Children.Add(new System.Windows.Shapes.Ellipse { Width = 410, Height = 380, Fill = teal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-140, 0, 0, -150) }); return background;
        }
        public static void ReleaseVisualTree(DependencyObject root)
        {
            // UI Automation clients can temporarily retain a disposed control.
            // Its action must no longer keep the window/workspace alive.
            var button = root as Button;
            if (button != null) { button.ClearValue(ButtonActionProperty); button.Click -= InvokeButtonAction; }
            var framework = root as FrameworkElement;
            if (framework != null) { SurfaceMotion.Cancel(framework); framework.BeginAnimation(FrameworkElement.WidthProperty, null); framework.BeginAnimation(FrameworkElement.MaxHeightProperty, null); }
            var border = root as Border;
            var background = border == null ? null : border.Background as SolidColorBrush;
            if (background != null && !background.IsFrozen) background.BeginAnimation(SolidColorBrush.ColorProperty, null);
            var element = root as UIElement;
            if (element != null)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                var transform = element.RenderTransform as TranslateTransform;
                if (transform != null) transform.BeginAnimation(TranslateTransform.YProperty, null);
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) ReleaseVisualTree(VisualTreeHelper.GetChild(root, i));
        }
        public static T Ancestor<T>(DependencyObject element) where T : DependencyObject
        { while (element != null && !(element is T)) element = element is Visual || element is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element); return element as T; }
        public static T Child<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T) return (T)root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var found = Child<T>(VisualTreeHelper.GetChild(root, i)); if (found != null) return found; } return null;
        }
    }
    public sealed class HistoryInput : Grid, IDisposable
    {
        public readonly TextBox Editor;
        readonly Popup popup;
        readonly SmoothBorder historySurface;
        readonly StackPanel list;
        readonly DispatcherTimer remember;
        readonly string scope;
        Window owner;
        bool suppressed = true, disposed, restoring;
        public event Action Chosen;
        public HistoryInput(string scope, string text, bool rememberTyping = false)
        {
            this.scope = scope; Width = 290; Margin = new Thickness(0, 0, 10, 10);
            Editor = Ui.Input(text); Editor.Width = Double.NaN; Editor.Margin = new Thickness(0); Editor.Padding = new Thickness(12, 9, 38, 9); Children.Add(Editor);
            var arrow = Ui.Button("", Toggle); arrow.Focusable = false; AutomationProperties.SetName(arrow, "搜索历史"); arrow.Content = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 1,1 L 5,5 9,1"), Stroke = (Brush)Ui.Resource("Muted"), StrokeThickness = 2, Width = 10, Height = 6, Stretch = Stretch.Uniform, IsHitTestVisible = false }; arrow.Width = 32; arrow.HorizontalAlignment = HorizontalAlignment.Right; arrow.Margin = new Thickness(0, 2, 3, 2); arrow.Padding = new Thickness(0); arrow.MinHeight = 32; arrow.Background = Brushes.Transparent; arrow.BorderThickness = new Thickness(0); arrow.ToolTip = "搜索历史"; Children.Add(arrow);
            list = new StackPanel();
            // StaysOpen=false captures the mouse and interrupts the editor's first click.
            // Outside clicks are observed on the owner without consuming that click.
            popup = new Popup { PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = true, AllowsTransparency = true, PopupAnimation = PopupAnimation.Slide, Focusable = false };
            var clear = Ui.Button("清空历史", () => { SearchHistory.Clear(scope); suppressed = true; BuildHistory(); }); clear.Style = (Style)Ui.Resource("TextAction");
            var heading = new Grid { Margin = new Thickness(2, 0, 2, 8) }; heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); heading.Children.Add(Ui.Text("搜索历史")); Grid.SetColumn(clear, 1); heading.Children.Add(clear);
            var body = Ui.Stack(heading, new ScrollViewer { Content = list, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var surface = new SmoothBorder { Width = Width, Child = body, Background = (Brush)Ui.Resource("Surface"), BorderBrush = (Brush)Ui.Resource("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(0, 4, 0, 0) };
            historySurface = surface; UiScale.ScalePopup(surface);
            TextElement.SetForeground(surface, (Brush)Ui.Resource("Ink")); popup.Child = surface;
            remember = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) }; remember.Tick += RememberTick;
            Editor.PreviewMouseLeftButtonDown += (s, e) => { if (!popup.IsOpen) OpenHistory(); };
            Loaded += AttachOwner; Unloaded += DetachOwner;
            Editor.TextChanged += (s, e) => { suppressed = restoring; if (rememberTyping && !restoring) { remember.Stop(); remember.Start(); } };
            Editor.LostKeyboardFocus += (s, e) => { if (rememberTyping && !popup.IsOpen) Commit(); };
            Editor.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) popup.IsOpen = false; if (e.Key == Key.Enter) { popup.IsOpen = false; Commit(); } if (e.Key == Key.Down && !popup.IsOpen) { OpenHistory(); e.Handled = true; } };
        }
        public void Commit() { remember.Stop(); if (!suppressed && !disposed) { SearchHistory.Add(scope, Editor.Text); suppressed = true; } }
        public void CommitSearch() { suppressed = false; Commit(); }
        public void RestoreText(string text)
        { remember.Stop(); restoring = true; try { Editor.Text = text ?? ""; suppressed = true; } finally { restoring = false; } }
        void RememberTick(object sender, EventArgs e) { remember.Stop(); Commit(); }
        void Toggle() { Editor.Focus(); if (popup.IsOpen) popup.IsOpen = false; else OpenHistory(); }
        void OpenHistory()
        {
            if (disposed) return;
            BuildHistory(); UpdateLayout();
            historySurface.Width = ActualWidth > 0 ? ActualWidth : Width;
            // Measure the final content at the final scale before Windows positions
            // the popup HWND. Its very first open must use the same bounds as later opens.
            historySurface.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            popup.IsOpen = true;
        }
        public bool IsHistoryOpen { get { return popup.IsOpen; } }
        void AttachOwner(object sender, RoutedEventArgs e)
        { if (disposed || owner != null) return; owner = Window.GetWindow(this); if (owner != null) { owner.PreviewMouseDown += OutsideClick; owner.Deactivated += OwnerDeactivated; owner.LocationChanged += OwnerMoved; } }
        void DetachOwner(object sender, RoutedEventArgs e)
        { popup.IsOpen = false; if (owner == null) return; owner.PreviewMouseDown -= OutsideClick; owner.Deactivated -= OwnerDeactivated; owner.LocationChanged -= OwnerMoved; owner = null; }
        void OutsideClick(object sender, MouseButtonEventArgs e)
        { if (popup.IsOpen && !Object.ReferenceEquals(Ui.Ancestor<HistoryInput>(e.OriginalSource as DependencyObject), this)) popup.IsOpen = false; }
        void OwnerDeactivated(object sender, EventArgs e) { popup.IsOpen = false; }
        void OwnerMoved(object sender, EventArgs e) { popup.IsOpen = false; }
        void BuildHistory()
        {
            list.Children.Clear(); var terms = SearchHistory.List(scope);
            if (terms.Length == 0) list.Children.Add(Ui.Text("暂无历史记录", "Note"));
            foreach (var term in terms)
            {
                string value = term; var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var choose = Ui.Button(value, () => { Editor.Text = value; Commit(); popup.IsOpen = false; Editor.Focus(); Editor.CaretIndex = Editor.Text.Length; var handler = Chosen; if (handler != null) handler(); });
                choose.HorizontalContentAlignment = HorizontalAlignment.Left; choose.Content = new TextBlock { Text = value, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis }; choose.Margin = new Thickness(0, 0, 3, 3); choose.Background = Brushes.Transparent; choose.BorderThickness = new Thickness(0);
                var remove = Ui.Button("×", () => { remember.Stop(); SearchHistory.Remove(scope, value); if (String.Equals(value, Editor.Text.Trim(), StringComparison.OrdinalIgnoreCase)) suppressed = true; BuildHistory(); }); remove.Width = 30; remove.Padding = new Thickness(0); remove.Margin = new Thickness(0, 0, 0, 3); remove.Background = Brushes.Transparent; remove.BorderThickness = new Thickness(0); remove.ToolTip = "删除此条历史";
                Grid.SetColumn(remove, 1); row.Children.Add(choose); row.Children.Add(remove); list.Children.Add(row);
            }
        }
        public void Dispose() { if (disposed) return; Commit(); disposed = true; DetachOwner(this, null); Loaded -= AttachOwner; Unloaded -= DetachOwner; remember.Stop(); remember.Tick -= RememberTick; popup.IsOpen = false; popup.Child = null; popup.PlacementTarget = null; list.Children.Clear(); Chosen = null; }
    }
}
