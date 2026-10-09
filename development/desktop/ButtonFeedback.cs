using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace DanmuCinema.Desktop
{
    // Animate background overlays only. Glyphs never move, scale or fade.
    public static class ButtonFeedback
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(ButtonFeedback), new PropertyMetadata(false, Changed));
        public static bool GetEnabled(DependencyObject target) { return (bool)target.GetValue(EnabledProperty); }
        public static void SetEnabled(DependencyObject target, bool value) { target.SetValue(EnabledProperty, value); }
        static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            var button = target as Button; if (button == null) return;
            if ((bool)e.NewValue) { button.MouseEnter += Enter; button.MouseLeave += Leave; button.PreviewMouseLeftButtonDown += Down; button.PreviewMouseLeftButtonUp += Up; button.LostMouseCapture += Up; button.IsEnabledChanged += EnabledChanged; }
            else { button.MouseEnter -= Enter; button.MouseLeave -= Leave; button.PreviewMouseLeftButtonDown -= Down; button.PreviewMouseLeftButtonUp -= Up; button.LostMouseCapture -= Up; button.IsEnabledChanged -= EnabledChanged; }
        }
        static void Paint(Button button, string name, double to, int duration)
        {
            if (button.Template == null) return; button.ApplyTemplate(); var layer = button.Template.FindName(name, button) as FrameworkElement; if (layer == null) return;
            double from = layer.Opacity; layer.BeginAnimation(UIElement.OpacityProperty, null); layer.Opacity = to;
            if (SystemParameters.ClientAreaAnimation) layer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(duration)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        static void Enter(object sender, MouseEventArgs e) { var button = (Button)sender; if (!button.IsEnabled) return; Paint(button, "hover", 1, 140); if (button.IsPressed) Paint(button, "pressed", 1, 60); }
        static void Leave(object sender, MouseEventArgs e) { Paint((Button)sender, "hover", 0, 180); Paint((Button)sender, "pressed", 0, 160); }
        static void Down(object sender, MouseButtonEventArgs e) { if (((Button)sender).IsEnabled) Paint((Button)sender, "pressed", 1, 60); }
        static void Up(object sender, MouseEventArgs e) { Paint((Button)sender, "pressed", 0, 160); }
        static void EnabledChanged(object sender, DependencyPropertyChangedEventArgs e) { if (!(bool)e.NewValue) { Paint((Button)sender, "hover", 0, 140); Paint((Button)sender, "pressed", 0, 160); } }
    }
}
