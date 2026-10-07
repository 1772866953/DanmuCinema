using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DanmuCinema.Desktop
{
    // WPF content and the HWND erase surface are separate. Protect the latter
    // too, including frames exposed during activation, close and chrome updates.
    internal sealed class NativeWindowSurface : IDisposable
    {
        static readonly DependencyProperty SurfaceProperty = DependencyProperty.RegisterAttached("Surface", typeof(NativeWindowSurface), typeof(NativeWindowSurface));
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] static extern int FillRect(IntPtr dc, ref Rect rect, IntPtr brush);
        [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint color);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
        Window window;
        HwndSource source;
        readonly Color color;
        IntPtr brush;
        DispatcherOperation pending;
        public static void Attach(Window window, HwndSource source, Color color)
        {
            var current = window.GetValue(SurfaceProperty) as NativeWindowSurface;
            if (current != null) { current.KeepOpaque(); return; }
            window.SetValue(SurfaceProperty, new NativeWindowSurface(window, source, color));
        }
        NativeWindowSurface(Window window, HwndSource source, Color color)
        {
            this.window = window; this.source = source; this.color = color;
            brush = CreateSolidBrush((uint)(color.R | color.G << 8 | color.B << 16));
            if (brush == IntPtr.Zero) throw new InvalidOperationException("无法创建窗口背景画刷。");
            source.AddHook(Hook); source.Disposed += SourceDisposed;
            window.Loaded += UpdateSurface; window.Activated += UpdateSurface; window.StateChanged += UpdateSurface;
            KeepOpaque();
        }
        void UpdateSurface(object sender, EventArgs e) { KeepOpaque(); QueueUpdate(); }
        void KeepOpaque()
        {
            if (source != null && !source.IsDisposed && source.CompositionTarget != null && source.CompositionTarget.BackgroundColor != color)
                source.CompositionTarget.BackgroundColor = color;
        }
        void QueueUpdate()
        {
            if (source == null || pending != null) return;
            pending = source.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => { pending = null; KeepOpaque(); }));
        }
        IntPtr Hook(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (message == 0x14) // WM_ERASEBKGND: never fall back to a white HWND brush.
            {
                KeepOpaque(); Rect rect;
                if (wparam != IntPtr.Zero && GetClientRect(hwnd, out rect) && FillRect(wparam, ref rect, brush) != 0)
                { handled = true; return new IntPtr(1); }
            }
            // Chrome/theme transitions can overwrite HwndTarget.BackgroundColor.
            // Reassert after that message unwinds; no invalidation or idle timer.
            if (message == 0xF || message == 0x6 || message == 0x85) KeepOpaque();
            if (message == 0x47 || message == 0x31A || message == 0x320 || message == 0x2E0) QueueUpdate();
            return IntPtr.Zero;
        }
        void SourceDisposed(object sender, EventArgs e) { Dispose(); }
        public void Dispose()
        {
            if (source == null) return;
            if (pending != null) { pending.Abort(); pending = null; }
            source.Disposed -= SourceDisposed;
            if (!source.IsDisposed) source.RemoveHook(Hook);
            window.Loaded -= UpdateSurface; window.Activated -= UpdateSurface; window.StateChanged -= UpdateSurface;
            window.ClearValue(SurfaceProperty); window = null; source = null;
            if (brush != IntPtr.Zero) { DeleteObject(brush); brush = IntPtr.Zero; }
        }
    }
}
