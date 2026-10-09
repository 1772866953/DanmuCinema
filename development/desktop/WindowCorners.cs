using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DanmuCinema.Desktop
{
    internal static class WindowCorners
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
        public static double Attach(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle; int rounded = 2;
            // DWMWA_BORDER_COLOR prevents Windows' active/inactive grey outline
            // from covering the application's shared highlighted border colour.
            Action highlight = () =>
            {
                var colour = ((SolidColorBrush)Ui.Resource("DialogLine")).Color;
                int nativeColour = colour.R | colour.G << 8 | colour.B << 16;
                try { DwmSetWindowAttribute(handle, 34, ref nativeColour, sizeof(int)); } catch (DllNotFoundException) { }
            };
            EventHandler activation = (s, e) => highlight();
            highlight(); window.Activated += activation; window.Deactivated += activation;
            window.Closed += (s, e) => { window.Activated -= activation; window.Deactivated -= activation; };
            // DWM_ROUND uses an 8-DIP contour. A separate 14-DIP content mask
            // exposes an opaque crescent between the two different curves.
            try { if (DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int)) == 0) return 8; } catch (DllNotFoundException) { }
            // Windows 10 lacks DWM's corner preference. Use a native region,
            // retaining the opaque HWND and avoiding layered-window transparency.
            Action update = () =>
            {
                var source = PresentationSource.FromVisual(window) as HwndSource; if (source == null || window.ActualWidth <= 0) return;
                if (window.WindowState == WindowState.Maximized) { SetWindowRgn(handle, IntPtr.Zero, true); return; }
                var dpi = source.CompositionTarget.TransformToDevice;
                var region = CreateRoundRectRgn(0, 0, (int)Math.Ceiling(window.ActualWidth * dpi.M11) + 1, (int)Math.Ceiling(window.ActualHeight * dpi.M22) + 1, (int)Math.Round(28 * dpi.M11), (int)Math.Round(28 * dpi.M22));
                if (region != IntPtr.Zero && SetWindowRgn(handle, region, true) == 0) DeleteObject(region);
            };
            SizeChangedEventHandler size = (s, e) => update(); EventHandler state = (s, e) => update();
            window.SizeChanged += size; window.StateChanged += state; window.Closed += (s, e) => { window.SizeChanged -= size; window.StateChanged -= state; }; update();
            return 14;
        }
    }
}
