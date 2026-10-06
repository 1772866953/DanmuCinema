using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DanmuCinema
{
    public class MediaGrid : DataGridView
    {
        readonly LibrarySelection selection;
        readonly Timer dragTimer;
        bool synchronizing, potentialDrag, dragging, additive, checkboxPressed, danmuPressed;
        int anchor;
        Point origin, pointer;
        string[] dragPrevious;
        public event Action SelectionUpdated;
        public MediaGrid(LibrarySelection selection)
        {
            this.selection = selection;
            DoubleBuffered = true; MultiSelect = true; SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            AllowUserToAddRows = AllowUserToDeleteRows = AllowUserToResizeRows = false;
            RowHeadersVisible = false; AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            Columns.Add(new DataGridViewCheckBoxColumn { Name = "selected", HeaderText = "选", Width = 44, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, SortMode = DataGridViewColumnSortMode.NotSortable });
            Columns.Add("name", "文件 / 影片"); Columns["name"].FillWeight = 32;
            Columns.Add("type", "类型"); Columns["type"].FillWeight = 7;
            Columns.Add("modified", "修改日期"); Columns["modified"].FillWeight = 18;
            Columns.Add("size", "大小"); Columns["size"].FillWeight = 10;
            Columns.Add("bitrate", "平均码率"); Columns["bitrate"].FillWeight = 13;
            Columns.Add(new DataGridViewButtonColumn { Name = "danmu", HeaderText = "弹幕", FillWeight = 20, FlatStyle = FlatStyle.Flat, SortMode = DataGridViewColumnSortMode.NotSortable });
            foreach (DataGridViewColumn column in Columns)
            {
                column.ReadOnly = true;
                if (column.Name != "selected" && column.Name != "danmu") column.SortMode = DataGridViewColumnSortMode.Programmatic;
            }
            ColumnHeaderMouseClick += (s, e) => { if (e.ColumnIndex == 0) ToggleVisible(); };
            CellValueChanged += (s, e) =>
            {
                if (synchronizing || e.RowIndex < 0 || e.ColumnIndex != 0) return;
                var row = Rows[e.RowIndex]; var entry = row.Tag as LibraryEntry;
                if (entry == null) return;
                selection.Set(entry.Key, Convert.ToBoolean(row.Cells[0].Value)); SyncRows();
            };
            dragTimer = new Timer { Interval = 100 };
            dragTimer.Tick += (s, e) =>
            {
                if (!dragging || Rows.Count == 0) return;
                int first = FirstDisplayedScrollingRowIndex;
                if (first < 0) return;
                if (pointer.Y < ColumnHeadersHeight + 15 && first > 0) FirstDisplayedScrollingRowIndex = first - 1;
                else if (pointer.Y > ClientSize.Height - 20 && first < Rows.Count - 1) FirstDisplayedScrollingRowIndex = first + 1;
                UpdateDrag();
            };
        }
        string[] VisibleKeys { get { return Rows.Cast<DataGridViewRow>().Where(x => x.Tag is LibraryEntry).Select(x => ((LibraryEntry)x.Tag).Key).ToArray(); } }
        public void SetEntries(LibraryEntry[] entries)
        {
            int previousTop = FirstDisplayedScrollingRowIndex;
            string topKey = previousTop >= 0 && Rows[previousTop].Tag is LibraryEntry ? ((LibraryEntry)Rows[previousTop].Tag).Key : null;
            string currentKey = CurrentRow != null && CurrentRow.Tag is LibraryEntry ? ((LibraryEntry)CurrentRow.Tag).Key : null;
            int currentColumn = CurrentCell == null ? 1 : CurrentCell.ColumnIndex;
            synchronizing = true; SuspendLayout();
            try
            {
                Rows.Clear();
                foreach (var entry in entries)
                {
                    int index = Rows.Add(selection.Contains(entry.Key), entry.Name,
                        entry.Type == "Episode" ? "剧集" : entry.Type == "Movie" ? "电影" : "视频",
                        entry.ModifiedUtc.HasValue ? entry.ModifiedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—",
                        entry.Size.HasValue ? (entry.Size.Value / 1e9).ToString("0.00") + " GB" : "—",
                        entry.Bitrate > 0 ? (entry.Bitrate / 1e6).ToString("0.0") + " Mbps" : "—", entry.DanmuLabel);
                    var row = Rows[index]; row.Tag = entry;
                    row.Cells["name"].ToolTipText = MediaNames.EpisodeLabel(entry.Item) + Environment.NewLine + Json.Text(entry.Item, "Path");
                    row.Cells["danmu"].ToolTipText = entry.HasXml ? "已有同名 XML。点击可重新选择单集、整季或已选影片的弹幕来源。" : "尚无同名 XML。点击搜索并选择弹幕来源。";
                }
                var current = Rows.Cast<DataGridViewRow>().FirstOrDefault(x => ((LibraryEntry)x.Tag).Key == currentKey);
                if (current != null) CurrentCell = current.Cells[currentColumn];
                var top = Rows.Cast<DataGridViewRow>().FirstOrDefault(x => ((LibraryEntry)x.Tag).Key == topKey);
                if (top != null) FirstDisplayedScrollingRowIndex = top.Index;
                ClearSelection();
                foreach (DataGridViewRow row in Rows) row.Selected = selection.Contains(((LibraryEntry)row.Tag).Key);
            }
            finally { ResumeLayout(); synchronizing = false; Changed(); }
        }
        public void SelectVisible(bool selected)
        {
            foreach (string key in VisibleKeys) selection.Set(key, selected);
            SyncRows();
        }
        public void ClearChecked() { selection.Clear(); SyncRows(); }
        void ToggleVisible() { SelectVisible(!VisibleKeys.All(selection.Contains)); }
        void Changed() { var handler = SelectionUpdated; if (handler != null) handler(); }
        void SyncRows()
        {
            synchronizing = true;
            try
            {
                foreach (DataGridViewRow row in Rows)
                {
                    var entry = row.Tag as LibraryEntry; if (entry == null) continue;
                    bool chosen = selection.Contains(entry.Key);
                    row.Selected = chosen; row.Cells[0].Value = chosen;
                }
            }
            finally { synchronizing = false; Changed(); }
        }
        protected override void OnSelectionChanged(EventArgs e)
        {
            base.OnSelectionChanged(e);
            if (selection == null || synchronizing || dragging || Rows.Cast<DataGridViewRow>().Any(x => x.Tag == null)) return;
            selection.ReplaceVisible(VisibleKeys, SelectedRows.Cast<DataGridViewRow>().Select(x => ((LibraryEntry)x.Tag).Key));
            SyncRows();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            checkboxPressed = danmuPressed = false;
            var hit = HitTest(e.X, e.Y);
            if (e.Button == MouseButtons.Left && hit.RowIndex >= 0 && hit.ColumnIndex == 0)
            {
                checkboxPressed = true;
                synchronizing = true;
                try { Focus(); CurrentCell = Rows[hit.RowIndex].Cells[0]; } finally { synchronizing = false; }
                var entry = Rows[hit.RowIndex].Tag as LibraryEntry;
                if (entry != null) { selection.Set(entry.Key, !selection.Contains(entry.Key)); SyncRows(); }
                return;
            }
            if (e.Button == MouseButtons.Left && hit.ColumnIndex == Columns["danmu"].Index)
            {
                danmuPressed = true;
                synchronizing = true;
                try { base.OnMouseDown(e); } finally { synchronizing = false; SyncRows(); }
                return;
            }
            dragPrevious = selection.Keys; additive = (ModifierKeys & Keys.Control) != 0;
            potentialDrag = e.Button == MouseButtons.Left && e.Y > ColumnHeadersHeight && e.X > Columns[0].Width && Rows.Count > 0;
            anchor = hit.RowIndex < 0 ? Rows.Count - 1 : hit.RowIndex; origin = pointer = e.Location;
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            pointer = e.Location;
            if (potentialDrag && e.Button == MouseButtons.Left && (Math.Abs(e.X - origin.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - origin.Y) > SystemInformation.DragSize.Height))
            { dragging = true; Capture = true; dragTimer.Start(); }
            if (dragging) { UpdateDrag(); Invalidate(); return; }
            base.OnMouseMove(e);
        }
        void UpdateDrag()
        {
            var keys = VisibleKeys;
            int end = HitTest(Math.Max(Columns[0].Width + 1, Math.Min(ClientSize.Width - 20, pointer.X)), pointer.Y).RowIndex;
            if (end < 0) end = pointer.Y <= ColumnHeadersHeight ? Math.Max(0, FirstDisplayedScrollingRowIndex) : Math.Min(Rows.Count - 1, Math.Max(0, FirstDisplayedScrollingRowIndex) + Math.Max(0, DisplayedRowCount(false) - 1));
            selection.ReplaceVisible(keys, LibrarySelection.DragRange(keys, anchor, end, dragPrevious, additive));
            SyncRows();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (checkboxPressed) { checkboxPressed = false; SyncRows(); return; }
            bool wasDragging = dragging;
            potentialDrag = dragging = false; dragTimer.Stop(); Capture = false; Invalidate();
            synchronizing = wasDragging || danmuPressed;
            try { base.OnMouseUp(e); } finally { synchronizing = false; if (wasDragging || danmuPressed) SyncRows(); danmuPressed = false; }
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture) { potentialDrag = dragging = false; if (dragTimer != null) dragTimer.Stop(); Invalidate(); }
            base.OnMouseCaptureChanged(e);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A) { SelectVisible(true); e.Handled = e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Space && CurrentRow != null && CurrentRow.Tag is LibraryEntry)
            { var entry = (LibraryEntry)CurrentRow.Tag; selection.Set(entry.Key, !selection.Contains(entry.Key)); SyncRows(); e.Handled = e.SuppressKeyPress = true; return; }
            base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!dragging) return;
            var rectangle = Rectangle.FromLTRB(Math.Min(origin.X, pointer.X), Math.Min(origin.Y, pointer.Y), Math.Max(origin.X, pointer.X), Math.Max(origin.Y, pointer.Y));
            rectangle.Intersect(new Rectangle(0, ColumnHeadersHeight, ClientSize.Width, Math.Max(0, ClientSize.Height - ColumnHeadersHeight)));
            using (var brush = new SolidBrush(Color.FromArgb(35, 19, 128, 112))) e.Graphics.FillRectangle(brush, rectangle);
            using (var pen = new Pen(Color.FromArgb(19, 128, 112))) e.Graphics.DrawRectangle(pen, rectangle);
        }
        protected override void Dispose(bool disposing) { if (disposing && dragTimer != null) dragTimer.Dispose(); base.Dispose(disposing); }
    }
}
