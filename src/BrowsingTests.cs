using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace DanmuCinema
{
    public static class BrowsingTests
    {
        static Dictionary<string, object> Video(int id, string path, long size = 2000000)
        {
            return new Dictionary<string, object> { { "Id", id.ToString() }, { "Path", path }, { "Name", Path.GetFileNameWithoutExtension(path) }, { "Type", "Episode" }, { "SeriesName", "测试番剧" },
                { "DateLastSaved", "2026-10-01T12:00:00Z" }, { "MediaSources", new object[] { new Dictionary<string, object> { { "Size", size } } } } };
        }
        sealed class Grid : MediaGrid
        {
            public Grid(LibrarySelection selection) : base(selection) { }
            public void Header() { OnColumnHeaderMouseClick(new DataGridViewCellMouseEventArgs(0, -1, 12, 12, new MouseEventArgs(MouseButtons.Left, 1, 12, 12, 0))); }
            public void Down(Point p) { OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0)); }
            public void Up(Point p) { OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0)); }
            public void Content(int column, int row) { OnCellContentClick(new DataGridViewCellEventArgs(column, row)); }
        }
        sealed class HistoryList : HistorySearchBox.HistoryList
        {
            public void Down(Point p) { OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0)); }
            public void Up(Point p) { OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0)); }
        }
        sealed class HistoryBox : HistorySearchBox
        {
            public int OpenCount;
            public bool FocusHistory;
            public HistoryBox() : base("history-click") { }
            public override void ShowHistory(bool focusHistory = true) { OpenCount++; FocusHistory = focusHistory; PrepareHistory(); }
        }
        public static void Run(List<string> report)
        {
            string root = Path.Combine(Paths.Root, "browse-fixture"), first = Path.Combine(root, "Show2"), second = Path.Combine(root, "Show10");
            var model = new MediaLibrary();
            model.Replace(new[] { Video(1, Path.Combine(first, "Gaikotsu (10).mkv")), Video(2, Path.Combine(first, "Gaikotsu (2).mkv")), Video(3, Path.Combine(first, "Gaikotsu (1).mkv")), Video(4, Path.Combine(second, "Other (1).mkv")) });
            SelfTests.Assert(model.View("Gaikotsu", LibrarySort.Name, false).Select(x => x.Name).SequenceEqual(new[] { "Gaikotsu (1).mkv", "Gaikotsu (2).mkv", "Gaikotsu (10).mkv" }), "名称自然排序按 1、2、10 排列，不再使用字符数字顺序", report);
            SelfTests.Assert(model.View("Gaikotsu", LibrarySort.Name, true).Select(x => x.Name).SequenceEqual(new[] { "Gaikotsu (10).mkv", "Gaikotsu (2).mkv", "Gaikotsu (1).mkv" }), "名称自然降序按 10、2、1 排列", report);
            SelfTests.Assert(NaturalNames.Instance.Compare("Ep0002", "Ep10") < 0 && NaturalNames.Instance.Compare("Ep9999999999999999999999999", "Ep10000000000000000000000000") < 0 && NaturalNames.Instance.Compare("S2E3", "S2E11") < 0, "名称数字排序支持前导零、多段数字及超过整数范围的编号", report);
            var folders = model.Browse(null, "", LibrarySort.Name, false);
            SelfTests.Assert(folders.Length == 2 && folders.All(x => x.IsFolder) && folders.Select(x => x.Name).SequenceEqual(new[] { "Show2", "Show10" }) && folders[0].Members.Length == 3, "默认只展示实际媒体目录，文件夹名称同样自然排序", report);
            SelfTests.Assert(model.Browse(first, "", LibrarySort.Name, false).Length == 3 && model.Browse(first, "(2)", LibrarySort.Name, false).Single().Name == "Gaikotsu (2).mkv", "进入文件夹列出完整文件，搜索只筛选当前目录", report);
            SelfTests.Assert(model.Browse(null, "(2)", LibrarySort.Name, false).Single().SelectionKeys.SequenceEqual(new[] { "id:2" }), "根目录筛选显示匹配文件所在目录，文件夹勾选只包含匹配影片", report);
            var duplicate = new MediaLibrary(); duplicate.Replace(new[] { Video(1, Path.Combine(first, "a.mkv")), Video(2, Path.Combine(root, "another", "Show2", "b.mkv")) });
            SelfTests.Assert(duplicate.Browse(null, "", LibrarySort.Name, false).Length == 2, "不同路径的同名文件夹不会合并", report);
            var nested = new MediaLibrary(); nested.Replace(new[] { Video(1, Path.Combine(first, "a.mkv")), Video(2, Path.Combine(first, "SPs", "special.mkv")), Video(3, Path.Combine(second, "season", "b.mkv")) });
            SelfTests.Assert(nested.Browse(null, "", LibrarySort.Name, false, root).Select(x => x.Name).SequenceEqual(new[] { "Show2", "Show10" }) && nested.Browse(first, "", LibrarySort.Name, false, root).Any(x => x.IsFolder && x.Name == "SPs"), "根层只显示直属动漫文件夹，SPs 和季度文件夹保留真实层级", report);
            SelfTests.Assert(nested.Browse(null, "special", LibrarySort.Name, false, root).Single().Name == "Show2", "搜索子文件时仍保留父动漫层级", report);
            SelfTests.Assert(Enumerable.Range(1, 12).Select(i => SmartMatching.Episode(new Dictionary<string, object> { { "Path", "Hyakkano - 25 (" + i + ").mkv" }, { "IndexNumber", 25 } })).SequenceEqual(Enumerable.Range(1, 12)), "括号集号优先于错误的固定第25集媒体元数据，完整12集可分别匹配", report);
            SelfTests.Assert(SmartMatching.Episode(new Dictionary<string, object> { { "Path", "番剧 SP (1).mkv" }, { "IndexNumber", 1 } }) == 0, "特别篇即使有括号和元数据集号也不混入普通整季匹配", report);
            SearchHistory.Add("history-test", "first"); SearchHistory.Add("history-test", "second"); SearchHistory.Add("history-test", "FIRST");
            SelfTests.Assert(SearchHistory.List("history-test").SequenceEqual(new[] { "FIRST", "second" }), "搜索历史持久化、去重且最近使用排在顶部", report);
            SearchHistory.Add("history-other", "other"); SearchHistory.Remove("history-test", "first");
            SelfTests.Assert(SearchHistory.List("history-test").SequenceEqual(new[] { "second" }) && SearchHistory.List("history-other").Length == 1, "逐条删除历史只修改对应范围", report);
            SearchHistory.Clear("history-test");
            SelfTests.Assert(SearchHistory.List("history-test").Length == 0 && SearchHistory.List("history-other").Length == 1 && !File.Exists(Path.Combine(Paths.Data, "search-history.json.bak")), "清空历史持久保存，无备份残留已删除的搜索词", report);
            Exception failure = null;
            var thread = new Thread(() => { try { GridChecks(model, report); HistoryChecks(report); NavigationChecks(report); LayoutChecks(model, report); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (failure != null) throw new Exception("浏览与布局控件测试失败", failure);
        }
        static void GridChecks(MediaLibrary model, List<string> report)
        {
            using (var grid = new Grid(model.Selection) { Size = new Size(1200, 400) })
            {
                IntPtr handle = grid.Handle; grid.SetEntries(model.Browse(null, "", LibrarySort.Name, false));
                SelfTests.Assert(grid.VisibleState == CheckState.Unchecked && grid.Columns[0].HeaderCell.ToolTipText.Contains("全选"), "列表表头提供全选复选框，初始未选中", report);
                var title = grid.GetCellDisplayRectangle(grid.Columns["name"].Index, 0, true); var titlePoint = new Point(title.Left + 12, title.Top + title.Height / 2);
                grid.Down(titlePoint); grid.Up(titlePoint);
                SelfTests.Assert(model.Selection.Count == 0, "点击目录名称浏览时不会意外勾选整季影片", report);
                grid.Header(); SelfTests.Assert(model.SelectedItems.Length == 4 && grid.VisibleState == CheckState.Checked, "表头全选文件夹对应的全部影片", report);
                grid.Header(); SelfTests.Assert(model.Selection.Count == 0 && grid.VisibleState == CheckState.Unchecked, "再次点击全选复选框取消当前列表选择", report);
                model.Selection.Set("id:2", true); grid.SetEntries(model.Browse(null, "", LibrarySort.Name, false));
                SelfTests.Assert(grid.VisibleState == CheckState.Indeterminate && (CheckState)grid.Rows[0].Cells[0].Value == CheckState.Indeterminate, "部分影片已选时，文件夹及全选框显示部分选中状态", report);
                grid.DataError += (s, e) => { throw new Exception("列表渲染失败", e.Exception); };
                using (var bitmap = new Bitmap(grid.Width, grid.Height)) grid.DrawToBitmap(bitmap, grid.ClientRectangle);
                SelfTests.Assert(true, "文件夹复选框和全选表头可正常渲染，部分选中状态不会触发格式错误", report);
                var rect = grid.GetCellDisplayRectangle(0, 0, true); var point = new Point(rect.Left + 12, rect.Top + rect.Height / 2);
                grid.Down(point); grid.Up(point);
                SelfTests.Assert(model.SelectedItems.Length == 3 && !model.Selection.Contains("id:4"), "勾选部分选中的文件夹补全选择，不混入其他目录", report);
                grid.FolderOpened += folder => grid.SetEntries(model.Browse(folder.FolderPath, "", LibrarySort.Name, false));
                grid.Content(grid.Columns["danmu"].Index, 0);
                SelfTests.Assert(grid.Rows.Count == 3 && grid.Rows.Cast<DataGridViewRow>().All(x => x.Selected) && model.Selection.Count == 3, "进入文件夹保留对应影片勾选，不把目录作为影片", report);
                string size = Convert.ToString(grid.Rows[0].Cells["size"].Value);
                SelfTests.Assert(size.EndsWith(" MB") && Double.Parse(size.Replace(" MB", ""), CultureInfo.CurrentCulture) == 2, "大小统一使用 MB 显示，数值换算正确", report);
            }
        }
        static void Invoke(Control control, string method, object args)
        { control.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new[] { args }); }
        static void HistoryChecks(List<string> report)
        {
            using (var list = new HistoryList { Size = new Size(320, 150) })
            {
                IntPtr handle = list.Handle; list.Items.AddRange(new object[] { "first", "second" });
                string removed = null, chosen = null; list.Removed += value => removed = value; list.Chosen += value => chosen = value;
                list.Down(new Point(305, 16)); list.Up(new Point(305, 16));
                SelfTests.Assert(removed == "first" && chosen == null, "历史条目右侧叉号只删除，不触发选择或搜索", report);
                list.Down(new Point(20, 50)); list.Up(new Point(20, 50));
                SelfTests.Assert(chosen == "second", "点击历史正文复用对应搜索词", report);
            }
            SearchHistory.Add("history-control", "first"); SearchHistory.Add("history-control", "second");
            using (var box = new HistorySearchBox("history-control") { Text = "second" })
            {
                box.PrepareHistory(); var list = box.HistoryListControl; list.SelectedIndex = 0;
                Invoke(list, "OnKeyDown", new KeyEventArgs(Keys.Delete)); box.Commit();
                SelfTests.Assert(SearchHistory.List("history-control").SequenceEqual(new[] { "first" }), "删除当前查询后不会因焦点变化重新写回历史", report);
                box.Commit(true); box.PrepareHistory(); Invoke(box.ClearHistoryButton, "OnClick", EventArgs.Empty); box.Commit();
                SelfTests.Assert(SearchHistory.List("history-control").Length == 0 && list.Items.Count == 0, "下拉框清空全部历史，即时更新且不会自动恢复", report);
                box.Text = "new-search"; box.Commit();
                SelfTests.Assert(SearchHistory.List("history-control").SequenceEqual(new[] { "new-search" }), "删除历史后，输入新查询仍可正常保存", report);
            }
            using (var box = new HistoryBox { Size = new Size(350, 36), Text = "原始搜索" })
            {
                Invoke(box.EditorControl, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 10, 0));
                SelfTests.Assert(box.OpenCount == 1 && !box.FocusHistory && box.Text == "原始搜索", "点击文本框自动展开历史，保留输入焦点和原有文本", report);
                Invoke(box.EditorControl, "OnMouseDown", new MouseEventArgs(MouseButtons.Right, 1, 20, 10, 0));
                SelfTests.Assert(box.OpenCount == 1, "文本框右键保留编辑菜单，不强制展开历史", report);
                Invoke(box.EditorControl, "OnKeyDown", new KeyEventArgs(Keys.Alt | Keys.Down));
                SelfTests.Assert(box.OpenCount == 2 && box.FocusHistory, "键盘展开历史时可聚焦记录列表", report);
                Invoke(box.ArrowControl, "OnClick", EventArgs.Empty);
                SelfTests.Assert(box.OpenCount == 3 && !box.FocusHistory && box.ArrowControl.Text == "" && box.ArrowControl.FlatAppearance.BorderSize == 0 && box.EditorControl.BorderStyle == BorderStyle.None, "下拉按钮统一边框，使用绘制箭头且保留编辑焦点", report);
                box.RestoreText("恢复的筛选词"); box.Commit();
                SelfTests.Assert(!SearchHistory.List("history-click").Contains("恢复的筛选词"), "导航恢复筛选词不会自动写回已删除的搜索历史", report);
                foreach (float size in new[] { 10f, 15f, 20f })
                {
                    box.Font = new Font("Microsoft YaHei UI", size); box.PerformLayout();
                    SelfTests.Assert(box.EditorControl.Bottom < box.Height && box.EditorControl.Right < box.ArrowControl.Left && box.ArrowControl.Bottom < box.Height, "统一搜索框在 " + size + "pt 字体下无重叠或裁切", report);
                    using (var bitmap = new Bitmap(box.Width, box.Height)) box.DrawToBitmap(bitmap, box.ClientRectangle);
                }
            }
        }
        static void NavigationChecks(List<string> report)
        {
            var history = new LibraryNavigation();
            SelfTests.Assert(!history.Back() && !history.Forward() && history.Current.Directory == null, "浏览历史初始位于文件夹列表，边界前后退不改变内容", report);
            history.UpdateFilter("root-filter"); history.Visit("season-a", "root-filter"); history.UpdateFilter("episode-2");
            SelfTests.Assert(history.Back() && history.Current.Directory == null && history.Current.Filter == "root-filter" && history.CanForward, "后退恢复先前目录及其筛选词", report);
            SelfTests.Assert(history.Forward() && history.Current.Directory == "season-a" && history.Current.Filter == "episode-2", "前进恢复进入的目录及最新筛选词", report);
            history.Visit(null, "");
            SelfTests.Assert(history.Back() && history.Current.Directory == "season-a", "返回文件夹按钮的导航也进入浏览历史", report);
            history.Visit("season-b", "new-filter");
            SelfTests.Assert(!history.CanForward && !history.Visit("season-b", "new-filter"), "后退后进入新目录清除旧前进分支，重复进入不新增历史", report);
            using (var form = new Form()) using (var editor = new TextBox()) using (var other = new Form())
            {
                form.Controls.Add(editor); IntPtr parentHandle = form.Handle, childHandle = editor.Handle, otherHandle = other.Handle;
                int back = 0, forward = 0; bool enabled = true;
                using (var filter = new LibraryMouseNavigation(form, () => enabled, next => { if (next) forward++; else back++; }))
                {
                    var down = Message.Create(childHandle, 0x20B, new IntPtr(1 << 16), IntPtr.Zero);
                    var up = Message.Create(childHandle, 0x20C, new IntPtr(1 << 16), IntPtr.Zero);
                    SelfTests.Assert(filter.PreFilterMessage(ref down) && back == 0 && filter.PreFilterMessage(ref up) && back == 1, "鼠标侧键后退在释放时只执行一次，并拦截控件默认点击", report);
                    up = Message.Create(childHandle, 0x20C, new IntPtr(2 << 16), IntPtr.Zero);
                    SelfTests.Assert(filter.PreFilterMessage(ref up) && forward == 1, "鼠标第二侧键触发前进，文本框等子控件也支持", report);
                    var command = Message.Create(parentHandle, 0x319, IntPtr.Zero, new IntPtr(1 << 16));
                    SelfTests.Assert(filter.PreFilterMessage(ref command) && back == 2, "兼容鼠标驱动发送的浏览器后退命令", report);
                    enabled = false;
                    SelfTests.Assert(!filter.PreFilterMessage(ref up) && forward == 1, "离开影片界面或操作忙碌时不触发目录导航", report);
                    enabled = true; up = Message.Create(otherHandle, 0x20C, new IntPtr(2 << 16), IntPtr.Zero);
                    SelfTests.Assert(!filter.PreFilterMessage(ref up) && forward == 1, "其他窗口及匹配弹窗不会误触发主窗口前进后退", report);
                }
            }
        }
        static IEnumerable<Control> Children(Control control)
        { foreach (Control child in control.Controls) { yield return child; foreach (var nested in Children(child)) yield return nested; } }
        static void LayoutTree(Control control)
        { control.PerformLayout(); foreach (Control child in control.Controls) LayoutTree(child); }
        static void LayoutChecks(MediaLibrary model, List<string> report)
        {
            var settings = new AppSettings { EnableExistingDanmu = false, EnableAnimeko = false, EnableBahamut = false, EnableDandan = false };
            using (var api = new JellyfinApi(settings)) using (var catalog = new DanmuCatalog(settings, api))
            using (var dialog = new MatchDialog(catalog, model.Entries[0].Item, "", true, model.SelectedItems, DanmuMatchScope.Season))
            {
                foreach (float fontSize in new[] { 10f, 15f, 20f })
                {
                    dialog.Font = new Font("Microsoft YaHei UI", fontSize); dialog.ClientSize = new Size(1000, 700);
                    var tabs = Children(dialog).OfType<TabControl>().Single(); tabs.SelectedIndex = 1;
                    IntPtr dialogHandle = dialog.Handle; foreach (var control in Children(dialog)) { IntPtr handle = control.Handle; }
                    for (int i = 0; i < 4; i++) LayoutTree(dialog);
                    var label = Children(tabs.SelectedTab).OfType<WrappedLabel>().Single();
                    SelfTests.Assert(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height && label.Parent.Top + label.Parent.Height <= tabs.SelectedTab.ClientSize.Height,
                        "匹配选项说明在 " + fontSize + "pt 字体下完整显示且不超出页签", report);
                    var status = Children(dialog).OfType<WrappedLabel>().Single(x => x.Name == "matchStatus");
                    status.Text = "本地影片与来源说明 " + String.Concat(Enumerable.Repeat("较长提示会自动换行。", 15));
                    for (int i = 0; i < 4; i++) LayoutTree(dialog);
                    SelfTests.Assert(status.Height >= status.GetPreferredSize(new Size(status.Width, 0)).Height && status.Bottom <= status.Parent.ClientSize.Height, "长状态提示在 " + fontSize + "pt 字体下自动换行完整显示", report);
                }
            }
        }
    }
}
