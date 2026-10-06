using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DanmuCinema
{
    public sealed partial class MainForm
    {
        readonly MediaLibrary mediaLibrary = new MediaLibrary();
        ComboBox librarySort, libraryOrder;
        Label librarySummary;
        bool libraryLoading, libraryNeedsRefresh = true;
        DateTime libraryLastAttempt, libraryLastRefresh;
        string libraryNotice = "启动服务并登录后，自动显示媒体库文件。";
        void BuildLibrary()
        {
            var page = Page("library"); page.AutoScroll = false;
            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            search = new TextBox { Width = 245, Margin = new Padding(0, 4, 10, 8) };
            search.TextChanged += (s, e) => ApplyLibraryView();
            search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyLibraryView(); } };
            Add(top, Actions(new Label { Text = "筛选文件", Width = 78, Height = 32, TextAlign = ContentAlignment.MiddleLeft }, search,
                ActionButton("清除筛选", () => { search.Clear(); return Completed(); }, false),
                ActionButton("刷新列表", LoadLibrary, true), ActionButton("扫描媒体库", ScanLibrary, false)));
            librarySort = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
            librarySort.Items.AddRange(new object[] { "名称", "修改日期", "大小", "类型", "平均码率" }); librarySort.SelectedIndex = 0;
            libraryOrder = new ComboBox { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
            libraryOrder.Items.AddRange(new object[] { "升序", "降序" }); libraryOrder.SelectedIndex = 0;
            librarySort.SelectedIndexChanged += (s, e) => ApplyLibraryView(); libraryOrder.SelectedIndexChanged += (s, e) => ApplyLibraryView();
            Add(top, Actions(new Label { Text = "排序", Width = 45, Height = 32, TextAlign = ContentAlignment.MiddleLeft }, librarySort, libraryOrder,
                ActionButton("全选当前列表", () => { library.SelectVisible(true); return Completed(); }, false),
                ActionButton("取消全部选择", () => { library.ClearChecked(); return Completed(); }, false)));
            Add(top, Actions(ActionButton("选择弹幕来源", MatchDanmu, false), ActionButton("管理接口", EditSources, false),
                ActionButton("刷新选中弹幕", RefreshDanmu, false), ActionButton("导出 XML", InspectDanmu, false), ActionButton("影片详情", OpenItemDetails, false)));
            librarySummary = TextLabel(libraryNotice, 30); Add(top, librarySummary);
            foreach (Control row in top.Controls) row.Margin = new Padding(0, 0, 0, 6);
            library = new MediaGrid(mediaLibrary.Selection) { Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false, ColumnHeadersHeight = 40, RowTemplate = { Height = 38 } };
            library.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(229, 237, 243);
            library.ColumnHeadersDefaultCellStyle.ForeColor = ink;
            library.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 240, 236); library.DefaultCellStyle.SelectionForeColor = ink;
            library.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            library.SelectionUpdated += UpdateLibrarySummary;
            library.ColumnHeaderMouseClick += (s, e) =>
            {
                string[] columns = { "name", "modified", "size", "type", "bitrate" };
                int index = Array.IndexOf(columns, library.Columns[e.ColumnIndex].Name);
                if (index < 0) return;
                if (librarySort.SelectedIndex == index) libraryOrder.SelectedIndex = 1 - libraryOrder.SelectedIndex;
                else { libraryOrder.SelectedIndex = index == 1 ? 1 : 0; librarySort.SelectedIndex = index; }
            };
            library.CellContentClick += (s, e) => { if (e.RowIndex >= 0 && library.Columns[e.ColumnIndex].Name == "danmu") ShowDanmuOptions(e.RowIndex); };
            library.CellDoubleClick += async (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex > 0 && library.Columns[e.ColumnIndex].Name != "danmu") await Execute(OpenItemDetails); };
            var hint = TextLabel("输入名称或路径即可筛选；点击列标题也可排序。勾选、Ctrl / Shift 或拖动圈选可多选。\r\n点击每行「弹幕」选择单集、整季或已选影片的来源。筛选与排序保留勾选项。", 64); hint.Dock = DockStyle.Bottom;
            page.Controls.Add(library); page.Controls.Add(hint); page.Controls.Add(top);
        }
        void ApplyLibraryView()
        {
            if (library == null || librarySort == null || libraryOrder == null) return;
            var entries = mediaLibrary.View(search.Text, (LibrarySort)librarySort.SelectedIndex, libraryOrder.SelectedIndex == 1);
            library.SetEntries(entries);
            string[] names = { "name", "modified", "size", "type", "bitrate" };
            foreach (DataGridViewColumn column in library.Columns) column.HeaderCell.SortGlyphDirection = SortOrder.None;
            library.Columns[names[librarySort.SelectedIndex]].HeaderCell.SortGlyphDirection = libraryOrder.SelectedIndex == 1 ? SortOrder.Descending : SortOrder.Ascending;
            UpdateLibrarySummary();
        }
        void UpdateLibrarySummary()
        {
            if (librarySummary == null || library == null) return;
            int shownSelected = library.Rows.Cast<DataGridViewRow>().Count(x => x.Tag is LibraryEntry && mediaLibrary.Selection.Contains(((LibraryEntry)x.Tag).Key));
            librarySummary.Text = "共 " + mediaLibrary.Entries.Length + " 个 · 当前显示 " + library.Rows.Count + " 个 · 已选 " + mediaLibrary.Selection.Count + " 个（当前列表 " + shownSelected + " 个）" + (libraryNotice == "" ? "" : " · " + libraryNotice);
        }
        async Task AutoLoadLibrary()
        {
            if (closing || IsDisposed || busy || libraryLoading) return;
            if (!services.OwnsProcess || String.IsNullOrEmpty(services.Api.Token))
            {
                if (mediaLibrary.Entries.Length == 0) { libraryNotice = "启动服务并登录后，自动显示媒体库文件。"; UpdateLibrarySummary(); }
                return;
            }
            bool due = libraryNeedsRefresh || (selectedPage == "library" && (DateTime.UtcNow - libraryLastRefresh).TotalSeconds >= 30);
            if (!due || (DateTime.UtcNow - libraryLastAttempt).TotalSeconds < 10) return;
            try { await LoadLibrary(); }
            catch { if (!IsDisposed) { libraryNotice = "暂时无法读取媒体库，请登录或点击刷新列表重试。"; UpdateLibrarySummary(); } }
        }
        async Task LoadLibrary()
        {
            if (libraryLoading) return;
            libraryLoading = true; libraryLastAttempt = DateTime.UtcNow; libraryNotice = "正在读取媒体库…"; UpdateLibrarySummary();
            try
            {
                var items = (await services.Api.Items("")).Cast<Dictionary<string, object>>().ToArray();
                // File size / last-write metadata is read off the GUI thread, never video content.
                var entries = await Task.Run(() => MediaLibrary.Build(items));
                if (IsDisposed || closing) return;
                mediaLibrary.ReplaceEntries(entries);
                libraryNeedsRefresh = false; libraryLastRefresh = DateTime.UtcNow;
                libraryNotice = items.Length == 0 ? "媒体库暂无文件，请扫描媒体库。" : ""; ApplyLibraryView();
                Log.Write("媒体库已加载，共 " + mediaLibrary.Entries.Length + " 个视频。");
            }
            catch { if (!IsDisposed) { libraryNotice = "读取失败，请登录或点击刷新列表重试。"; UpdateLibrarySummary(); } throw; }
            finally { libraryLoading = false; }
        }
        async Task RefreshLibraryMetadata()
        {
            var items = mediaLibrary.Entries.Select(x => x.Item).ToArray();
            libraryLoading = true;
            try { var entries = await Task.Run(() => MediaLibrary.Build(items)); if (!IsDisposed && !closing) { mediaLibrary.ReplaceEntries(entries); ApplyLibraryView(); } }
            finally { libraryLoading = false; }
        }
        Dictionary<string, object> SelectedItem()
        {
            var selected = mediaLibrary.SelectedItems;
            if (selected.Length != 1) throw new InvalidOperationException("此操作用于单个影片，请只选择一行；多选后可批量选择来源或刷新弹幕。");
            return selected[0];
        }
        async Task RefreshDanmu()
        {
            var selected = mediaLibrary.SelectedItems;
            if (selected.Length == 0) throw new InvalidOperationException("请先选择要刷新弹幕的影片。");
            int success = 0, failed = 0;
            foreach (var item in selected)
            {
                try { await services.Api.Request("GET", "api/danmu/" + Json.Text(item, "Id") + "/refresh", null, true); success++; }
                catch { failed++; }
            }
            await RefreshLibraryMetadata();
            Log.Write("刷新弹幕完成：成功 " + success + " 个，失败 " + failed + " 个。");
            footer.Text = "刷新弹幕完成：成功 " + success + " 个，失败 " + failed + " 个。";
        }
        Task MatchDanmu()
        {
            var selected = mediaLibrary.SelectedItems;
            return OpenDanmuMatch(selected.FirstOrDefault(), selected.Length > 1 ? DanmuMatchScope.Selection : DanmuMatchScope.Single, selected);
        }
        async Task OpenDanmuMatch(Dictionary<string, object> item, DanmuMatchScope scope, Dictionary<string, object>[] selected = null)
        {
            if (scope == DanmuMatchScope.Selection) MediaLibrary.RequireSameSeason(selected ?? mediaLibrary.SelectedItems);
            using (var dialog = new MatchDialog(gateway.Catalog, item, search.Text.Trim(), settings.AnimeOnly, selected, scope)) dialog.ShowDialog(this);
            await RefreshLibraryMetadata();
        }
        void ShowDanmuOptions(int rowIndex)
        {
            var item = ((LibraryEntry)library.Rows[rowIndex].Tag).Item;
            var selected = mediaLibrary.SelectedItems;
            var menu = new ContextMenuStrip();
            menu.Items.Add("重新选择单集弹幕来源…", null, async (s, e) => await Execute(() => OpenDanmuMatch(item, DanmuMatchScope.Single)));
            var season = menu.Items.Add("重新选择整季弹幕来源…", null, async (s, e) => await Execute(() => OpenDanmuMatch(item, DanmuMatchScope.Season)));
            season.Enabled = Json.Text(item, "Type") != "Movie";
            var group = menu.Items.Add("重新选择已选 " + selected.Length + " 个影片的来源…", null, async (s, e) => await Execute(() => OpenDanmuMatch(selected.FirstOrDefault(), DanmuMatchScope.Selection, selected)));
            group.Enabled = selected.Length > 1;
            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
            var rectangle = library.GetCellDisplayRectangle(library.Columns["danmu"].Index, rowIndex, true);
            menu.Show(library, new Point(rectangle.Left, rectangle.Bottom));
        }
        async Task InspectDanmu()
        {
            var selected = SelectedItem(); string path = Path.ChangeExtension(Json.Text(selected, "Path"), ".xml");
            if (!File.Exists(path)) throw new InvalidOperationException("该视频尚无弹幕 XML，请在「弹幕」列选择来源并下载。");
            string content = await Task.Run(() => File.ReadAllText(path));
            int count = DanmuCatalog.ParseXml(content).GetElementsByTagName("d").Count;
            using (var dialog = new SaveFileDialog { Title = "弹幕共 " + count + " 条，选择 XML 导出位置", Filter = "XML 弹幕|*.xml", FileName = SafeFileName(Json.Text(selected, "Name")) + ".xml", InitialDirectory = Paths.Data })
                if (dialog.ShowDialog(this) == DialogResult.OK) { File.WriteAllText(dialog.FileName, content, new System.Text.UTF8Encoding(false)); Log.Write("已导出 " + count + " 条弹幕。"); }
        }
    }
}
