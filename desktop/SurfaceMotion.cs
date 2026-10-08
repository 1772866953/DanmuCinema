using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DanmuCinema.Desktop
{
    public static class SurfaceMotion
    {
        public static readonly DependencyProperty DropdownRevealProperty = DependencyProperty.RegisterAttached("DropdownReveal", typeof(bool), typeof(SurfaceMotion), new PropertyMetadata(false, RevealChanged));
        static readonly DependencyProperty ShownProperty = DependencyProperty.RegisterAttached("Shown", typeof(bool?), typeof(SurfaceMotion));
        static readonly DependencyProperty RevealProperty = DependencyProperty.RegisterAttached("Reveal", typeof(Reveal), typeof(SurfaceMotion));
        public const int EnterMilliseconds = 150, ExitMilliseconds = 120;
        sealed class Reveal { public RoutedEventHandler Loaded; public DispatcherOperation Pending; }
        public static void Cancel(FrameworkElement element)
        {
            var reveal = (Reveal)element.GetValue(RevealProperty);
            if (reveal != null) { if (reveal.Loaded != null) element.Loaded -= reveal.Loaded; if (reveal.Pending != null) reveal.Pending.Abort(); element.ClearValue(RevealProperty); }
            element.BeginAnimation(UIElement.OpacityProperty, null);
        }
        public static bool GetDropdownReveal(DependencyObject target) { return (bool)target.GetValue(DropdownRevealProperty); }
        public static void SetDropdownReveal(DependencyObject target, bool value) { target.SetValue(DropdownRevealProperty, value); }
        static void RevealChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        { var element = target as FrameworkElement; if (element == null) return; if ((bool)e.NewValue) element.Loaded += RevealLoaded; else element.Loaded -= RevealLoaded; }
        static void RevealLoaded(object sender, RoutedEventArgs e) { if (Object.ReferenceEquals(sender, e.OriginalSource)) FadeIn((FrameworkElement)sender); }
        public static void FadeIn(FrameworkElement element, Action complete = null)
        {
            Cancel(element); element.Opacity = 1; element.IsHitTestVisible = true;
            if (!SystemParameters.ClientAreaAnimation) { if (complete != null) complete(); return; }
            element.Opacity = 0;
            var reveal = new Reveal(); element.SetValue(RevealProperty, reveal);
            Action start = () =>
            {
                // Loaded priority follows the first layout/render. Expensive view
                // construction must not consume the visible animation's duration.
                reveal.Pending = element.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (!Object.ReferenceEquals(element.GetValue(RevealProperty), reveal)) return;
                    element.ClearValue(RevealProperty); element.Opacity = 1;
                    if (element.IsVisible)
                    {
                        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(EnterMilliseconds)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                        if (complete != null) animation.Completed += (s, e) => complete();
                        element.BeginAnimation(UIElement.OpacityProperty, animation);
                    }
                    else if (complete != null) complete();
                }));
            };
            if (element.IsLoaded) start();
            else { reveal.Loaded = (s, e) => { if (!Object.ReferenceEquals(e.OriginalSource, element)) return; element.Loaded -= reveal.Loaded; reveal.Loaded = null; start(); }; element.Loaded += reveal.Loaded; }
        }
        public static void FadeOut(FrameworkElement element, Action complete)
        {
            double opacity = element.Opacity; Cancel(element); element.Opacity = opacity;
            if (!SystemParameters.ClientAreaAnimation || !element.IsVisible || opacity <= 0) { complete(); return; }
            element.IsHitTestVisible = false;
            var animation = new DoubleAnimation(opacity, 0, TimeSpan.FromMilliseconds(ExitMilliseconds)) { FillBehavior = FillBehavior.HoldEnd };
            animation.Completed += (s, e) => complete(); element.BeginAnimation(UIElement.OpacityProperty, animation);
        }
        public static void SetVisible(FrameworkElement element, bool show)
        {
            var old = (bool?)element.GetValue(ShownProperty); if (old == show) return; element.SetValue(ShownProperty, show);
            element.BeginAnimation(FrameworkElement.MaxHeightProperty, null); Cancel(element);
            if (!element.IsLoaded || !SystemParameters.ClientAreaAnimation) { element.MaxHeight = Double.PositiveInfinity; element.Opacity = 1; element.Visibility = show ? Visibility.Visible : Visibility.Collapsed; return; }
            element.ClipToBounds = true;
            if (show)
            {
                element.Visibility = Visibility.Visible; var parent = element.Parent as FrameworkElement;
                element.Measure(new Size(parent == null || parent.ActualWidth <= 0 ? 1200 : parent.ActualWidth, Double.PositiveInfinity));
                double height = Math.Max(1, element.DesiredSize.Height); element.MaxHeight = Double.PositiveInfinity;
                element.BeginAnimation(FrameworkElement.MaxHeightProperty, new DoubleAnimation(0, height, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }); FadeIn(element);
            }
            else
            {
                var animation = new DoubleAnimation(element.ActualHeight, 0, TimeSpan.FromMilliseconds(ExitMilliseconds)) { FillBehavior = FillBehavior.HoldEnd };
                element.BeginAnimation(FrameworkElement.MaxHeightProperty, animation);
                FadeOut(element, () => { if ((bool?)element.GetValue(ShownProperty) == false) { element.Visibility = Visibility.Collapsed; element.BeginAnimation(FrameworkElement.MaxHeightProperty, null); element.MaxHeight = Double.PositiveInfinity; } });
            }
        }
    }
}
