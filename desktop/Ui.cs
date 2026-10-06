using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DanmuCinema.Desktop
{
    public static class Ui
    {
        public static Brush Brush(string color) { var value = (SolidColorBrush)new BrushConverter().ConvertFromString(color); value.Freeze(); return value; }
        public static object Resource(string name) { return Application.Current.FindResource(name); }
        public static T Load<T>(string name)
        {
            using (var stream = typeof(Ui).Assembly.GetManifestResourceStream("DanmuCinema.Desktop." + name)) return (T)XamlReader.Load(stream);
        }
        public static void InstallTheme(Application application) { application.Resources.MergedDictionaries.Add(Load<ResourceDictionary>("Theme.xaml")); }
        public static TextBlock Text(string text, string style = null)
        { var value = new TextBlock { Text = text }; if (style != null) value.Style = (Style)Resource(style); return value; }
        public static Button Button(string text, Action action, bool primary = false)
        {
            var button = new Button { Content = text }; AutomationProperties.SetName(button, text);
            if (primary) button.Style = (Style)Resource("Primary");
            button.Click += (s, e) => action(); return button;
        }
        public static WrapPanel Row(params UIElement[] children)
        { var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center }; foreach (var child in children) row.Children.Add(child); return row; }
        public static StackPanel Stack(params UIElement[] children)
        { var stack = new StackPanel(); foreach (var child in children) stack.Children.Add(child); return stack; }
        public static Border Card(string title, params UIElement[] children)
        {
            var stack = Stack(Text(title, "Heading")); foreach (var child in children) stack.Children.Add(child);
            return new Border { Background = Brushes.White, BorderBrush = Brush("#E4EBF2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(22, 20, 22, 18), Margin = new Thickness(0, 0, 0, 18), Child = stack };
        }
        public static ComboBox Combo(IEnumerable<string> values, int selected, double width = 160)
        { var combo = new ComboBox { Width = width, ItemsSource = values.ToArray(), SelectedIndex = selected }; return combo; }
        public static TextBox Input(string value, double width = 260)
        { return new TextBox { Text = value ?? "", Width = width }; }
        public static CheckBox Check(string text, bool value)
        { var result = new CheckBox { Content = Text(text), IsChecked = value }; AutomationProperties.SetName(result, text); return result; }
        public static TextBlock Label(string text) { return new TextBlock { Text = text, Width = 100, Margin = new Thickness(0, 0, 10, 10), Foreground = (Brush)Resource("Muted") }; }
        public static void Animate(FrameworkElement element)
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            var transform = element.RenderTransform as TranslateTransform;
            if (transform == null) { transform = new TranslateTransform(); element.RenderTransform = transform; }
            var duration = TimeSpan.FromMilliseconds(220);
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { FillBehavior = FillBehavior.Stop });
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }
        public static void StopAnimations(DependencyObject root)
        {
            var element = root as UIElement;
            if (element != null)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                var transform = element.RenderTransform as TranslateTransform;
                if (transform != null) transform.BeginAnimation(TranslateTransform.YProperty, null);
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) StopAnimations(VisualTreeHelper.GetChild(root, i));
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
        readonly StackPanel list;
        readonly DispatcherTimer remember;
        readonly string scope;
        bool suppressed = true, disposed, restoring;
        public event Action Chosen;
        public HistoryInput(string scope, string text, bool rememberTyping = false)
        {
            this.scope = scope; Width = 290; Margin = new Thickness(0, 0, 10, 10);
            Editor = Ui.Input(text); Editor.Width = Double.NaN; Editor.Margin = new Thickness(0); Editor.Padding = new Thickness(12, 9, 38, 9); Children.Add(Editor);
            var arrow = Ui.Button("⌄", Toggle); arrow.Width = 32; arrow.HorizontalAlignment = HorizontalAlignment.Right; arrow.Margin = new Thickness(0, 2, 3, 2); arrow.Padding = new Thickness(0); arrow.MinHeight = 32; arrow.Background = Brushes.Transparent; arrow.BorderThickness = new Thickness(0); arrow.ToolTip = "搜索历史"; Children.Add(arrow);
            list = new StackPanel();
            popup = new Popup { PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
            var body = Ui.Stack(Ui.Row(Ui.Text("搜索历史"), Ui.Button("清空历史", () => { SearchHistory.Clear(scope); suppressed = true; BuildHistory(); })), new ScrollViewer { Content = list, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            popup.Child = new Border { Width = Width, Child = body, Background = Brushes.White, BorderBrush = Ui.Brush("#DDE6F0"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(0, 4, 0, 0) };
            remember = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) }; remember.Tick += RememberTick;
            Editor.PreviewMouseLeftButtonDown += (s, e) => { if (!popup.IsOpen) OpenHistory(); };
            Editor.TextChanged += (s, e) => { suppressed = restoring; if (rememberTyping && !restoring) { remember.Stop(); remember.Start(); } };
            Editor.LostKeyboardFocus += (s, e) => { if (rememberTyping && !popup.IsOpen) Commit(); };
            Editor.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) popup.IsOpen = false; if (e.Key == Key.Enter) { popup.IsOpen = false; Commit(); } if (e.Key == Key.Down && !popup.IsOpen) { OpenHistory(); e.Handled = true; } };
        }
        public void Commit() { remember.Stop(); if (!suppressed && !disposed) SearchHistory.Add(scope, Editor.Text); }
        public void CommitSearch() { suppressed = false; Commit(); }
        public void RestoreText(string text)
        { remember.Stop(); restoring = true; try { Editor.Text = text ?? ""; suppressed = true; } finally { restoring = false; } }
        void RememberTick(object sender, EventArgs e) { remember.Stop(); Commit(); }
        void Toggle() { if (popup.IsOpen) popup.IsOpen = false; else OpenHistory(); }
        void OpenHistory() { BuildHistory(); popup.IsOpen = true; }
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
        public void Dispose() { if (disposed) return; Commit(); disposed = true; remember.Stop(); remember.Tick -= RememberTick; popup.IsOpen = false; popup.Child = null; popup.PlacementTarget = null; list.Children.Clear(); Chosen = null; }
    }
}
