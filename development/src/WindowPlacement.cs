using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DanmuCinema
{
    public sealed class WindowPlacement
    {
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Maximized { get; set; }
        public Rectangle Bounds { get { return new Rectangle(Left, Top, Width, Height); } }
        public bool Valid { get { return Width > 0 && Height > 0 && Width <= 100000 && Height <= 100000 && Math.Abs((long)Left) <= 1000000 && Math.Abs((long)Top) <= 1000000; } }

        public void Capture(Rectangle normalBounds, FormWindowState state)
        {
            // Minimize and tray transitions must not replace the last usable placement.
            if (state == FormWindowState.Minimized || normalBounds.Width <= 0 || normalBounds.Height <= 0) return;
            Left = normalBounds.Left; Top = normalBounds.Top;
            Width = normalBounds.Width; Height = normalBounds.Height;
            Maximized = state == FormWindowState.Maximized;
        }

        public Rectangle Fit(Rectangle[] workingAreas, Size minimum)
        {
            if (workingAreas == null || workingAreas.Length == 0) return Bounds;
            // Prefer the monitor containing the old window; recover safely if it was disconnected.
            var area = workingAreas.OrderByDescending(x => IntersectionArea(x, Bounds))
                .ThenBy(x => Distance(x, Bounds)).First();
            int width = Math.Max(minimum.Width, Math.Min(Width, area.Width));
            int height = Math.Max(minimum.Height, Math.Min(Height, area.Height));
            int left = Math.Max(area.Left, Math.Min(Left, area.Right - width));
            int top = Math.Max(area.Top, Math.Min(Top, area.Bottom - height));
            return new Rectangle(left, top, width, height);
        }
        static long IntersectionArea(Rectangle a, Rectangle b)
        { var intersection = Rectangle.Intersect(a, b); return (long)Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height); }
        static double Distance(Rectangle a, Rectangle b)
        { double x = (double)a.Left + a.Width / 2.0 - b.Left - b.Width / 2.0, y = (double)a.Top + a.Height / 2.0 - b.Top - b.Height / 2.0; return x * x + y * y; }
    }

    public static class WindowPlacementStore
    {
        public static string FilePath { get { return Path.Combine(Paths.Data, "window-state.json"); } }
        public static WindowPlacement Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var placement = Json.Read<WindowPlacement>(File.ReadAllText(FilePath));
                return placement != null && placement.Valid ? placement : null;
            }
            catch (Exception) { return null; }
        }
        public static void Save(WindowPlacement placement)
        {
            if (placement == null || !placement.Valid) return;
            Directory.CreateDirectory(Paths.Data);
            // Persist only geometry; never couple frequent resizing to credentials/settings saves.
            SettingsStore.AtomicWrite(FilePath, Json.Write(new { placement.Left, placement.Top, placement.Width, placement.Height, placement.Maximized }), false);
        }
    }

    public sealed partial class MainForm
    {
        WindowPlacement placement;
        System.Windows.Forms.Timer placementTimer;
        bool placementReady, changingVisibility, hideQueued, startupShown, startupStarted;

        void InitializeWindowMemory()
        {
            placement = WindowPlacementStore.Load() ?? new WindowPlacement();
            placementTimer = new System.Windows.Forms.Timer { Interval = 700 };
            placementTimer.Tick += (s, e) => SaveWindowPlacement();
            Load += (s, e) =>
            {
                if (placementReady) return;
                changingVisibility = true;
                try
                {
                    if (placement.Valid)
                    {
                        StartPosition = FormStartPosition.Manual;
                        Bounds = placement.Fit(Screen.AllScreens.Select(x => x.WorkingArea).ToArray(), MinimumSize);
                    }
                    else placement.Capture(Bounds, WindowState);
                }
                finally { changingVisibility = false; placementReady = true; }
            };
            Resize += (s, e) => RememberWindowPlacement();
            LocationChanged += (s, e) => RememberWindowPlacement();
            ResizeEnd += (s, e) => SaveWindowPlacement();
        }
        void RememberWindowPlacement()
        {
            if (!placementReady || !startupShown || changingVisibility || !Visible || WindowState == FormWindowState.Minimized) return;
            placement.Capture(WindowState == FormWindowState.Normal ? Bounds : RestoreBounds, WindowState);
            placementTimer.Stop(); placementTimer.Start();
        }
        void ApplyStartupWindowState()
        {
            // Native Show finishes after Shown. Apply from the queued callback and
            // ignore all geometry events until that initial Show has completed.
            changingVisibility = true;
            try
            {
                WindowState = FormWindowState.Normal;
                if (placement.Valid) Bounds = placement.Fit(Screen.AllScreens.Select(x => x.WorkingArea).ToArray(), MinimumSize);
                WindowState = placement.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
            }
            finally { changingVisibility = false; }
        }
        void SaveWindowPlacement()
        {
            if (!placementReady) return;
            RememberWindowPlacement(); placementTimer.Stop();
            try { WindowPlacementStore.Save(placement); }
            catch (Exception error) { Log.Write("窗口状态保存失败：" + error.Message); }
        }
        void RestoreWindow()
        {
            hideQueued = false;
            if (Visible && WindowState != FormWindowState.Minimized) { Activate(); return; }
            changingVisibility = true;
            try
            {
                // Change taskbar membership before showing: it can recreate the native handle.
                ShowInTaskbar = true;
                WindowState = FormWindowState.Normal;
                if (placement.Valid) Bounds = placement.Fit(Screen.AllScreens.Select(x => x.WorkingArea).ToArray(), MinimumSize);
                Show();
                // Show can restore the handle's cached native state. Set the final
                // placement afterwards so maximized startup/tray restore stays maximized.
                WindowState = FormWindowState.Normal;
                if (placement.Valid) Bounds = placement.Fit(Screen.AllScreens.Select(x => x.WorkingArea).ToArray(), MinimumSize);
                WindowState = placement.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
                Activate();
            }
            finally { changingVisibility = false; }
        }
        void HideToTray()
        {
            SaveWindowPlacement();
            changingVisibility = true;
            try { ShowInTaskbar = false; Hide(); }
            finally { changingVisibility = false; }
        }
        void QueueHideToTray()
        {
            if (hideQueued) return;
            hideQueued = true;
            // Wait until WinForms finishes cancelling the close. Recreating a maximized
            // handle inside FormClosing can otherwise show the window again.
            BeginInvoke(new Action(() =>
            {
                if (!hideQueued || IsDisposed || closing || finalClose) return;
                hideQueued = false; HideToTray();
            }));
        }
    }
}
