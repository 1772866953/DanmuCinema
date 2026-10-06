using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DanmuCinema
{
    public static class SearchHistory
    {
        static readonly object sync = new object();
        static string FilePath { get { return Path.Combine(Paths.Data, "search-history.json"); } }
        static Dictionary<string, string[]> Read()
        {
            try { return File.Exists(FilePath) ? Json.Read<Dictionary<string, string[]>>(File.ReadAllText(FilePath)) ?? new Dictionary<string, string[]>() : new Dictionary<string, string[]>(); }
            catch { Log.Write("搜索历史无法读取。"); return new Dictionary<string, string[]>(); }
        }
        public static string[] List(string scope)
        {
            lock (sync) { string[] rows; return Read().TryGetValue(scope, out rows) ? (rows ?? new string[0]).Where(x => !String.IsNullOrWhiteSpace(x)).ToArray() : new string[0]; }
        }
        static void Change(string scope, Func<string[], string[]> change)
        {
            lock (sync)
            {
                var all = Read(); string[] rows; all.TryGetValue(scope, out rows);
                all[scope] = change(rows ?? new string[0]);
                Directory.CreateDirectory(Paths.Data); SettingsStore.AtomicWrite(FilePath, Json.Write(all), false);
            }
        }
        public static void Add(string scope, string text)
        {
            text = (text ?? "").Trim(); if (text.Length == 0 || text.Length > 200) return;
            string term = text;
            Change(scope, rows => new[] { term }.Concat(rows.Where(x => !String.Equals(x, term, StringComparison.OrdinalIgnoreCase))).Take(100).ToArray());
        }
        public static void Remove(string scope, string text) { Change(scope, rows => rows.Where(x => !String.Equals(x, text, StringComparison.OrdinalIgnoreCase)).ToArray()); }
        public static void Clear(string scope) { Change(scope, rows => new string[0]); }
    }

    public class HistorySearchBox : UserControl, IMessageFilter
    {
        readonly TextBox editor;
        readonly HistoryArrow arrow;
        readonly Timer rememberTimer;
        readonly string scope;
        readonly ToolStripDropDown popup;
        readonly HistoryList list;
        readonly Button clear;
        readonly Label historyHeading;
        Form popupOwner;
        bool filtering;
        bool choosing, deletedCurrent;
        public event Action SearchChosen;
        public HistorySearchBox(string scope, bool rememberTyping = false)
        {
            this.scope = scope;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White; Height = 36; MinimumSize = new Size(140, 32);
            editor = new TextBox { BorderStyle = BorderStyle.None, BackColor = Color.White };
            arrow = new HistoryArrow();
            Controls.Add(editor); Controls.Add(arrow);
            var panel = new Panel { Size = new Size(350, 300), BackColor = Color.White, Padding = new Padding(4) };
            var heading = new Label { Text = "搜索历史", Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleLeft };
            historyHeading = heading;
            clear = new Button { Text = "清空全部历史", Dock = DockStyle.Bottom, Height = 34, FlatStyle = FlatStyle.Flat };
            clear.FlatAppearance.BorderSize = 0; clear.BackColor = Color.FromArgb(246, 250, 252); clear.ForeColor = Color.FromArgb(19, 128, 112);
            clear.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 246, 242);
            list = new HistoryList { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
            panel.Controls.Add(list); panel.Controls.Add(clear); panel.Controls.Add(heading);
            popup = new HistoryPopup { Padding = Padding.Empty, AutoSize = true, AutoClose = false };
            popup.Items.Add(new ToolStripControlHost(panel) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = panel.Size });
            rememberTimer = new Timer { Interval = 1000 };
            popup.Opened += (s, e) => { arrow.Expanded = true; if (!filtering) { Application.AddMessageFilter(this); filtering = true; } popupOwner = FindForm(); if (popupOwner != null) popupOwner.Deactivate += OwnerDeactivated; Invalidate(); };
            popup.Closed += (s, e) => { arrow.Expanded = false; RemovePopupFilter(); Invalidate(); };
            rememberTimer.Tick += (s, e) => { rememberTimer.Stop(); Commit(); };
            editor.TextChanged += (s, e) => { deletedCurrent = false; OnTextChanged(EventArgs.Empty); if (rememberTyping && !choosing) { rememberTimer.Stop(); rememberTimer.Start(); } };
            editor.KeyDown += (s, e) =>
            {
                if ((e.Alt && e.KeyCode == Keys.Down) || e.KeyCode == Keys.F4) { ShowHistory(); e.SuppressKeyPress = true; return; }
                if (e.KeyCode == Keys.Down && popup.Visible) { if (list.Items.Count > 0) list.SelectedIndex = 0; list.Focus(); e.SuppressKeyPress = true; return; }
                if (e.KeyCode == Keys.Enter) { popup.Close(); Commit(true); }
                OnKeyDown(e);
            };
            editor.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left && !choosing) ShowHistory(false); };
            editor.Enter += (s, e) => Invalidate();
            editor.Leave += (s, e) => { Invalidate(); if (rememberTyping && !choosing) Commit(); };
            arrow.Click += (s, e) => { if (popup.Visible) { popup.Close(); editor.Focus(); } else ShowHistory(false); };
            list.Chosen += term =>
            {
                choosing = true; rememberTimer.Stop();
                try { Text = term; popup.Close(); editor.Focus(); editor.SelectionStart = editor.TextLength; }
                finally { choosing = false; }
                Commit(); var handler = SearchChosen; if (handler != null) handler();
            };
            list.Removed += term => { rememberTimer.Stop(); if (String.Equals(term, Text.Trim(), StringComparison.OrdinalIgnoreCase)) deletedCurrent = true; SearchHistory.Remove(scope, term); ReloadHistory(); };
            clear.Click += (s, e) => { rememberTimer.Stop(); deletedCurrent = true; SearchHistory.Clear(scope); ReloadHistory(); };
            FontChanged += (s, e) => { panel.Font = Font; heading.Height = Math.Max(30, Font.Height + 12); clear.Height = Math.Max(34, Font.Height + 14); };
            SizeChanged += (s, e) => FitEditor();
            FitEditor();
        }
        public override string Text { get { return editor == null ? "" : editor.Text; } set { if (editor != null) editor.Text = value ?? ""; } }
        public void Clear() { rememberTimer.Stop(); choosing = true; try { editor.Clear(); } finally { choosing = false; } }
        public void RestoreText(string text) { rememberTimer.Stop(); choosing = true; try { Text = text; deletedCurrent = true; } finally { choosing = false; } }
        public void Commit(bool force = false) { if (IsDisposed || Disposing) return; rememberTimer.Stop(); if (!deletedCurrent || force) { SearchHistory.Add(scope, Text); deletedCurrent = false; } }
        internal HistoryList HistoryListControl { get { return list; } }
        internal Button ClearHistoryButton { get { return clear; } }
        void ReloadHistory()
        {
            list.Items.Clear(); list.Items.AddRange(SearchHistory.List(scope)); clear.Enabled = list.Items.Count > 0;
            historyHeading.Text = list.Items.Count == 0 ? "搜索历史（暂无记录）" : "搜索历史";
            list.Invalidate();
        }
        public virtual void ShowHistory(bool focusHistory = true)
        {
            if (!Enabled || IsDisposed || Disposing) return;
            if (!popup.Visible) { PrepareHistory(); popup.Show(this, new Point(0, Height + 3)); }
            if (focusHistory) list.Focus(); else editor.Focus();
        }
        internal TextBox EditorControl { get { return editor; } }
        internal Button ArrowControl { get { return arrow; } }
        internal void PrepareHistory()
        {
            rememberTimer.Stop(); ReloadHistory();
            var host = (ToolStripControlHost)popup.Items[0];
            int fixedHeight = historyHeading.Height + clear.Height + 8;
            host.Size = host.Control.Size = new Size(Math.Max(Width, 300), fixedHeight + Math.Min(6, Math.Max(1, list.Items.Count)) * list.ItemHeight);
        }
        void FitEditor()
        {
            if (editor == null) return;
            int padding = Math.Max(7, Font.Height / 3);
            int height = Math.Max(34, editor.PreferredHeight + padding * 2 + 2);
            if (Height != height) Height = height;
            int arrowWidth = Math.Max(32, Font.Height + 12);
            arrow.Bounds = new Rectangle(Math.Max(1, Width - arrowWidth - 3), 3, arrowWidth, Math.Max(1, Height - 6));
            editor.Bounds = new Rectangle(padding + 2, Math.Max(1, (Height - editor.PreferredHeight) / 2), Math.Max(1, Width - arrowWidth - padding - 9), editor.PreferredHeight);
            Invalidate();
        }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitEditor(); }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); FitEditor(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var border = ContainsFocus || (popup != null && popup.Visible) ? Color.FromArgb(19, 128, 112) : Color.FromArgb(194, 207, 219);
            using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 5))
            using (var pen = new Pen(border)) e.Graphics.DrawPath(pen, path);
        }
        static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath(); int d = radius * 2;
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90); path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        void OwnerDeactivated(object sender, EventArgs e) { if (!popup.ContainsFocus) popup.Close(); }
        void RemovePopupFilter()
        {
            if (filtering) { Application.RemoveMessageFilter(this); filtering = false; }
            if (popupOwner != null) { popupOwner.Deactivate -= OwnerDeactivated; popupOwner = null; }
        }
        public bool PreFilterMessage(ref Message message)
        {
            if (!popup.Visible) return false;
            if (message.Msg == 0x100 && message.WParam.ToInt32() == (int)Keys.Escape) { popup.Close(); editor.Focus(); return true; }
            if (message.Msg == 0x201 || message.Msg == 0x204 || message.Msg == 0x207 || message.Msg == 0x20B || message.Msg == 0xA1)
            {
                Point point = Control.MousePosition;
                if (!popup.Bounds.Contains(point) && !RectangleToScreen(ClientRectangle).Contains(point)) popup.Close();
            }
            return false;
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { RemovePopupFilter(); rememberTimer.Dispose(); popup.Dispose(); } base.Dispose(disposing); }
        sealed class HistoryPopup : ToolStripDropDown
        {
            protected override CreateParams CreateParams { get { var parameters = base.CreateParams; parameters.ExStyle |= 0x08000000; return parameters; } }
            protected override void WndProc(ref Message message) { if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; } base.WndProc(ref message); }
        }
        sealed class HistoryArrow : Button
        {
            bool hovered, expanded;
            public bool Expanded { set { expanded = value; Invalidate(); } }
            public HistoryArrow()
            {
                FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Color.White; TabStop = false;
                AccessibleName = "搜索历史"; Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
            protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(hovered || expanded ? Color.FromArgb(235, 246, 242) : Color.White);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float x = Width / 2f, y = Height / 2f, half = Math.Max(4, Font.Height / 5f), offset = expanded ? -1 : 1;
                using (var pen = new Pen(Enabled ? Color.FromArgb(76, 103, 117) : Color.Gray, 1.6f))
                { pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round; pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    e.Graphics.DrawLines(pen, new[] { new PointF(x - half, y - offset * half / 2), new PointF(x, y + offset * half / 2), new PointF(x + half, y - offset * half / 2) }); }
            }
        }

        public class HistoryList : ListBox
        {
            public event Action<string> Chosen, Removed;
            public HistoryList() { DrawMode = DrawMode.OwnerDrawFixed; IntegralHeight = false; ItemHeight = 34; }
            protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ItemHeight = Math.Max(34, Font.Height + 14); }
            protected override void OnDrawItem(DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                e.DrawBackground();
                var text = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, Math.Max(1, e.Bounds.Width - 48), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, Convert.ToString(Items[e.Index]), Font, text, e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                var cross = new Rectangle(e.Bounds.Right - 34, e.Bounds.Top, 30, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, "×", Font, cross, e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                e.DrawFocusRectangle();
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                int index = IndexFromPoint(e.Location);
                if (e.Button == MouseButtons.Left && index >= 0 && e.X >= ClientSize.Width - 34)
                { var handler = Removed; if (handler != null) handler(Convert.ToString(Items[index])); return; }
                base.OnMouseDown(e);
            }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e); int index = IndexFromPoint(e.Location);
                if (e.Button == MouseButtons.Left && index >= 0 && e.X < ClientSize.Width - 34)
                { var handler = Chosen; if (handler != null) handler(Convert.ToString(Items[index])); }
            }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (SelectedIndex >= 0 && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Delete))
                { var handler = e.KeyCode == Keys.Delete ? Removed : Chosen; if (handler != null) handler(Convert.ToString(SelectedItem)); e.SuppressKeyPress = true; return; }
                base.OnKeyDown(e);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Items.Count == 0) TextRenderer.DrawText(e.Graphics, "暂无搜索历史", Font, ClientRectangle, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
            }
        }
    }
}
