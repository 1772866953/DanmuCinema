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

    public class HistorySearchBox : UserControl
    {
        readonly TextBox editor;
        readonly Button arrow;
        readonly Timer rememberTimer;
        readonly string scope;
        readonly ToolStripDropDown popup;
        readonly HistoryList list;
        readonly Button clear;
        readonly Label historyHeading;
        bool choosing, deletedCurrent;
        public event Action SearchChosen;
        public HistorySearchBox(string scope, bool rememberTyping = false)
        {
            this.scope = scope;
            Height = 32; MinimumSize = new Size(140, 28);
            editor = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
            arrow = new Button { Text = "▾", Dock = DockStyle.Right, Width = 30, FlatStyle = FlatStyle.Flat, TabStop = false, AccessibleName = "搜索历史" };
            Controls.Add(editor); Controls.Add(arrow);
            var panel = new Panel { Size = new Size(350, 300), BackColor = Color.White, Padding = new Padding(4) };
            var heading = new Label { Text = "搜索历史", Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleLeft };
            historyHeading = heading;
            clear = new Button { Text = "清空全部历史", Dock = DockStyle.Bottom, Height = 34, FlatStyle = FlatStyle.Flat };
            list = new HistoryList { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
            panel.Controls.Add(list); panel.Controls.Add(clear); panel.Controls.Add(heading);
            popup = new ToolStripDropDown { Padding = Padding.Empty, AutoSize = true };
            popup.Items.Add(new ToolStripControlHost(panel) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = panel.Size });
            rememberTimer = new Timer { Interval = 1000 };
            rememberTimer.Tick += (s, e) => { rememberTimer.Stop(); Commit(); };
            editor.TextChanged += (s, e) => { deletedCurrent = false; OnTextChanged(EventArgs.Empty); if (rememberTyping && !choosing) { rememberTimer.Stop(); rememberTimer.Start(); } };
            editor.KeyDown += (s, e) =>
            {
                if ((e.Alt && e.KeyCode == Keys.Down) || e.KeyCode == Keys.F4) { ShowHistory(); e.SuppressKeyPress = true; return; }
                if (e.KeyCode == Keys.Enter) Commit(true);
                OnKeyDown(e);
            };
            editor.Leave += (s, e) => { if (rememberTyping && !choosing) Commit(); };
            arrow.Click += (s, e) => { if (popup.Visible) popup.Close(); else ShowHistory(); };
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
            SizeChanged += (s, e) => { Height = Math.Max(28, editor.PreferredHeight); };
        }
        public override string Text { get { return editor == null ? "" : editor.Text; } set { if (editor != null) editor.Text = value ?? ""; } }
        public void Clear() { rememberTimer.Stop(); choosing = true; try { editor.Clear(); } finally { choosing = false; } }
        public void Commit(bool force = false) { if (IsDisposed || Disposing) return; rememberTimer.Stop(); if (!deletedCurrent || force) { SearchHistory.Add(scope, Text); deletedCurrent = false; } }
        internal HistoryList HistoryListControl { get { return list; } }
        internal Button ClearHistoryButton { get { return clear; } }
        void ReloadHistory()
        {
            list.Items.Clear(); list.Items.AddRange(SearchHistory.List(scope)); clear.Enabled = list.Items.Count > 0;
            historyHeading.Text = list.Items.Count == 0 ? "搜索历史（暂无记录）" : "搜索历史";
            list.Invalidate();
        }
        public void ShowHistory()
        {
            PrepareHistory(); popup.Show(this, new Point(0, Height)); list.Focus();
        }
        internal void PrepareHistory()
        {
            rememberTimer.Stop(); ReloadHistory();
            var host = (ToolStripControlHost)popup.Items[0];
            int fixedHeight = historyHeading.Height + clear.Height + 8;
            host.Size = host.Control.Size = new Size(Math.Max(Width, 300), fixedHeight + Math.Min(6, Math.Max(1, list.Items.Count)) * list.ItemHeight);
        }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (editor != null) Height = Math.Max(28, editor.PreferredHeight); }
        protected override void Dispose(bool disposing)
        { if (disposing) { rememberTimer.Dispose(); popup.Dispose(); } base.Dispose(disposing); }

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
