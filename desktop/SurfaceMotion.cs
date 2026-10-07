using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace DanmuCinema.Desktop
{
    public static class SurfaceMotion
    {
        public static readonly DependencyProperty DropdownRevealProperty = DependencyProperty.RegisterAttached("DropdownReveal", typeof(bool), typeof(SurfaceMotion), new PropertyMetadata(false, RevealChanged));
        static readonly DependencyProperty ShownProperty = DependencyProperty.RegisterAttached("Shown", typeof(bool?), typeof(SurfaceMotion));
        public static bool GetDropdownReveal(DependencyObject target) { return (bool)target.GetValue(DropdownRevealProperty); }
        public static void SetDropdownReveal(DependencyObject target, bool value) { target.SetValue(DropdownRevealProperty, value); }
        static void RevealChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        { var element = target as FrameworkElement; if (element == null) return; if ((bool)e.NewValue) element.Loaded += RevealLoaded; else element.Loaded -= RevealLoaded; }
        static void RevealLoaded(object sender, RoutedEventArgs e) { if (Object.ReferenceEquals(sender, e.OriginalSource)) FadeIn((FrameworkElement)sender); }
        public static void FadeIn(FrameworkElement element)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = 1;
            if (!SystemParameters.ClientAreaAnimation) return;
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        public static void FadeOut(FrameworkElement element, Action complete)
        {
            if (!SystemParameters.ClientAreaAnimation || !element.IsVisible) { complete(); return; }
            var animation = new DoubleAnimation(element.Opacity, 0, TimeSpan.FromMilliseconds(120)) { FillBehavior = FillBehavior.HoldEnd };
            animation.Completed += (s, e) => complete(); element.BeginAnimation(UIElement.OpacityProperty, animation);
        }
        public static void SetVisible(FrameworkElement element, bool show)
        {
            var old = (bool?)element.GetValue(ShownProperty); if (old == show) return; element.SetValue(ShownProperty, show);
            element.BeginAnimation(FrameworkElement.MaxHeightProperty, null); element.BeginAnimation(UIElement.OpacityProperty, null);
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
                var animation = new DoubleAnimation(element.ActualHeight, 0, TimeSpan.FromMilliseconds(120)) { FillBehavior = FillBehavior.HoldEnd };
                animation.Completed += (s, e) => { if ((bool?)element.GetValue(ShownProperty) == false) { element.Visibility = Visibility.Collapsed; element.BeginAnimation(FrameworkElement.MaxHeightProperty, null); element.MaxHeight = Double.PositiveInfinity; } };
                element.BeginAnimation(FrameworkElement.MaxHeightProperty, animation);
            }
        }
    }
}
