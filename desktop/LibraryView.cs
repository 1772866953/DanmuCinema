using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DanmuCinema.Desktop
{
    public sealed class LibraryRow : INotifyPropertyChanged
    {
        public readonly LibraryEntry Entry;
        readonly LibrarySelection selection;
        readonly DesktopController controller;
        public event PropertyChangedEventHandler PropertyChanged;
        public event Action Changed;
        public LibraryRow(LibraryEntry entry, LibrarySelection selection, DesktopController controller = null) { Entry = entry; this.selection = selection; this.controller = controller; }
        public string Name { get { return (Entry.IsFolder ? "▸  " : "") + Entry.Name; } }
        public string DisplayName { get { var clean = Entry.IsFolder ? SmartMatching.CleanTitle(Entry.Name) : MediaPresentation.Title(Entry.Item); return String.IsNullOrWhiteSpace(clean) ? Entry.Name : clean; } }
        public string Filename { get { return Entry.IsFolder ? Entry.Members.Length + " 集 / 个影片 · " + Entry.Members.Count(x => x.HasXml) + " 个弹幕已就绪" : Entry.Name; } }
        public string State { get { if (Entry.IsFolder) return "打开文件夹"; var task = controller == null ? null : controller.FindTask(Entry.Item); return task == null ? Entry.HasXml ? "已就绪" : "待匹配" : task.Status.StartsWith("已保存") || task.Status == "保留已有 XML" || task.Status == "已就绪" ? Entry.HasXml ? "已就绪" : "待匹配" : task.Status; } }
        public Brush StateColor { get { return Ui.StatusBrush(State); } }
        public string Source { get { return Entry.IsFolder ? "打开文件夹" : Entry.SourceLabel == "" ? Entry.HasXml ? "本地 XML  ▾" : "选择来源  ▾" : Entry.SourceLabel + "  ▾"; } }
        public ImageSource Cover { get { return cover; } }
        ImageSource cover;
        public bool IsFolder { get { return Entry.IsFolder; } }
        public void SetCover(ImageSource value) { cover = value; var handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs("Cover")); }
        public string Type { get { return Entry.IsFolder ? "文件夹" : Entry.Type == "Episode" ? "剧集" : Entry.Type == "Movie" ? "电影" : "视频"; } }
        public string Modified { get { return Entry.ModifiedUtc.HasValue ? Entry.ModifiedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—"; } }
        public string Size { get { return Entry.Size.HasValue ? (Entry.Size.Value / 1e6).ToString("N2") + " MB" : "—"; } }
        public string Bitrate { get { return Entry.Bitrate > 0 ? (Entry.Bitrate / 1e6).ToString("0.0") + " Mbps" : "—"; } }
        public string Danmu { get { return Entry.IsFolder ? "打开文件夹 · " + Entry.Members.Length + " 个影片" : Entry.DanmuLabel; } }
        public string Hint { get { return Entry.IsFolder ? Entry.FolderPath : MediaNames.EpisodeLabel(Entry.Item) + "\n" + Json.Text(Entry.Item, "Path"); } }
        public bool? Selected
        {
            get { int count = Entry.SelectionKeys.Count(selection.Contains); return count == 0 ? false : count == Entry.SelectionKeys.Length ? (bool?)true : null; }
            set { foreach (var key in Entry.SelectionKeys) selection.Set(key, value == true); Notify(); var handler = Changed; if (handler != null) handler(); }
        }
        public void Notify() { var handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(null)); }
    }
    public sealed partial class ShellWindow
    {
        DataGrid grid;
        LibraryRow[] rows;
        CheckBox all;
        HistoryInput librarySearch;
        ContextMenu libraryMenu;
        WrapPanel bulkActions;
        Canvas selectionCanvas;
        Rectangle selectionBox;
        bool dragging, potentialDrag, dragAdditive;
        int anchor;
        Point dragOrigin;
        string[] dragPrevious;
        FrameworkElement BuildLibrary()
        {
            var panel = new Grid(); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition()); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            librarySearch = new HistoryInput("library", session.Filter, true); pageResources.Add(librarySearch);
            librarySearch.Editor.TextChanged += (s, e) => { session.Filter = librarySearch.Editor.Text; session.Navigation.UpdateFilter(session.Filter); ApplyLibrary(); };
            librarySearch.Width = 250;
            var sort = Ui.Combo(new[] { "名称", "修改日期", "大小", "类型", "平均码率" }, session.Sort, 115); var order = Ui.Combo(new[] { "升序", "降序" }, session.Order, 85);
            sort.SelectionChanged += (s, e) => { session.Sort = sort.SelectedIndex; ApplyLibrary(); }; order.SelectionChanged += (s, e) => { session.Order = order.SelectedIndex; ApplyLibrary(); };
            location = Ui.Text("", "Note"); count = Ui.Text("", "Note");
            var breadcrumb = new DockPanel();
            DockPanel.SetDock(count, Dock.Right); breadcrumb.Children.Add(count); breadcrumb.Children.Add(location);
            location.TextWrapping = TextWrapping.NoWrap; location.TextTrimming = TextTrimming.CharacterEllipsis;
            var clear = Ui.Button("清除", () => librarySearch.Editor.Clear()); clear.Style = (Style)Ui.Resource("TextAction");
            var scan = Command("扫描媒体库", controller.ScanLibrary);
            var sourcesButton = Ui.Button("管理接口", () => Track(new SourcesWindow(controller)));
            var details = Ui.Check("完整列信息", session.LibraryDetails); details.Margin = new Thickness(4, 0, 0, 10);
            details.Click += (s, e) => { session.LibraryDetails = details.IsChecked == true; FitLibraryColumns(); };
            var search = Ui.Button("搜索弹幕", () => Match(null, DanmuMatchScope.Single, new Dictionary<string, object>[0]));
            bulkActions = Ui.Row(Ui.Button("匹配已选影片", () => { var selected = controller.Library.SelectedItems; Match(selected.FirstOrDefault(), selected.Length > 1 ? DanmuMatchScope.Selection : DanmuMatchScope.Single, selected); }, true),
                Ui.Button("提前准备弹幕", () => { var selected = controller.Library.SelectedItems; Navigate("tasks"); controller.StartPreparation(selected); }), Command("刷新选中弹幕", controller.RefreshDanmu));
            var selectedMore = Ui.Button("已选操作", () => { }); selectedMore.Click += (s, e) => ShowLibraryActions(selectedMore, true); bulkActions.Children.Add(selectedMore);
            bulkActions.Children.Add(Ui.Button("取消选择", () => { controller.Library.Selection.Clear(); SyncSelection(); }));
            var top = Ui.Stack(Ui.Row(librarySearch, clear, sort, order, Command("刷新", controller.LoadLibrary), search, scan, sourcesButton, details), breadcrumb, bulkActions);
            panel.Children.Add(top);
            grid = new DataGrid { IsReadOnly = false, RowHeight = 66 }; System.Windows.Automation.AutomationProperties.SetName(grid, "媒体库文件列表");
            grid.ContextMenu = new ContextMenu();
            grid.ContextMenuOpening += (s, e) =>
            {
                var visual = Ui.Ancestor<DataGridRow>(e.OriginalSource as DependencyObject);
                var row = visual == null ? (e.CursorLeft < 0 ? grid.SelectedItem as LibraryRow : null) : visual.Item as LibraryRow;
                e.Handled = true; if (row == null) return;
                if (row.Selected != true) { controller.Library.Selection.ReplaceVisible(rows.SelectMany(x => x.Entry.SelectionKeys), row.Entry.SelectionKeys); SyncSelection(); }
                if (libraryMenu != null) { libraryMenu.IsOpen = false; libraryMenu.Items.Clear(); }
                libraryMenu = CreateLibraryContextMenu(row.Entry); libraryMenu.PlacementTarget = visual == null ? (UIElement)grid : visual; libraryMenu.IsOpen = true;
            };
            all = Ui.Check("", false); all.Margin = new Thickness(0); all.ToolTip = "全选 / 取消选择当前列表";
            all.Click += (s, e) => { if (!synchronizing && rows != null) { bool chosen = all.IsChecked == true; foreach (var row in rows) foreach (string key in row.Entry.SelectionKeys) controller.Library.Selection.Set(key, chosen); SyncSelection(); } };
            var checkTemplate = (DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><CheckBox IsChecked='{Binding Selected, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}' Margin='0' HorizontalAlignment='Center' ToolTip='选择影片'/></DataTemplate>");
            grid.Columns.Add(new DataGridTemplateColumn { Header = all, CellTemplate = checkTemplate, Width = 44, CanUserSort = false });
            var nameTemplate = (DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><DockPanel ToolTip='{Binding Hint}'><Border Width='36' Height='50' CornerRadius='6' Background='#293A56' Margin='0,0,12,0'><Border.Clip><RectangleGeometry Rect='0,0,36,50' RadiusX='6' RadiusY='6'/></Border.Clip><Border.Style><Style TargetType='Border'><Setter Property='Visibility' Value='Collapsed'/><Style.Triggers><DataTrigger Binding='{Binding IsFolder}' Value='True'><Setter Property='Visibility' Value='Visible'/></DataTrigger></Style.Triggers></Style></Border.Style><Grid><TextBlock Text='▸' HorizontalAlignment='Center' Foreground='{DynamicResource Accent}'/><Image Source='{Binding Cover}' Stretch='UniformToFill'/></Grid></Border><StackPanel VerticalAlignment='Center'><TextBlock Text='{Binding DisplayName}' FontWeight='SemiBold' TextWrapping='NoWrap' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding Filename}' Foreground='{DynamicResource Muted}' FontSize='11' Margin='0,4,0,0' TextWrapping='NoWrap' TextTrimming='CharacterEllipsis'/></StackPanel></DockPanel></DataTemplate>");
            grid.Columns.Add(new DataGridTemplateColumn { Header = "作品 / 原始文件", CellTemplate = nameTemplate, SortMemberPath = "Name", Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 240 });
            string[] headers = { "类型", "修改日期", "大小", "平均码率" }, bindings = { "Type", "Modified", "Size", "Bitrate" };
            double[] widths = { 64, 142, 110, 92 };
            var textStyle = new Style(typeof(TextBlock)); textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap)); textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis)); textStyle.Setters.Add(new Setter(TextBlock.ToolTipProperty, new Binding("Hint")));
            for (int i = 0; i < headers.Length; i++) grid.Columns.Add(new DataGridTextColumn { Header = headers[i], Binding = new Binding(bindings[i]), SortMemberPath = bindings[i], Width = new DataGridLength(widths[i]), MinWidth = widths[i], IsReadOnly = true, ElementStyle = textStyle });
            var danmuTemplate = (DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel VerticalAlignment='Center'><TextBlock Text='{Binding State}' Foreground='{Binding StateColor}' Margin='8,0,0,1' FontSize='12'><TextBlock.Style><Style TargetType='TextBlock'><Style.Triggers><DataTrigger Binding='{Binding IsFolder}' Value='True'><Setter Property='Visibility' Value='Collapsed'/></DataTrigger></Style.Triggers></Style></TextBlock.Style></TextBlock><Button Content='{Binding Source}' Padding='8,3' Margin='0' MinHeight='24' ToolTip='打开文件夹，或重新选择弹幕来源'><Button.Style><Style TargetType='Button' BasedOn='{StaticResource TextAction}'><Setter Property='HorizontalContentAlignment' Value='Left'/><Style.Triggers><DataTrigger Binding='{Binding IsFolder}' Value='True'><Setter Property='HorizontalContentAlignment' Value='Center'/></DataTrigger></Style.Triggers></Style></Button.Style></Button></StackPanel></DataTemplate>");
            grid.Columns.Add(new DataGridTemplateColumn { Header = "弹幕", CellTemplate = danmuTemplate, Width = 210, CanUserSort = false });
            grid.SizeChanged += (s, e) => FitLibraryColumns();
            grid.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) => { var button = e.OriginalSource as Button; var row = button == null ? null : button.DataContext as LibraryRow; if (row == null) return; if (row.Entry.IsFolder) OpenFolder(row.Entry); else ShowDanmuMenu(button, row.Entry); e.Handled = true; }));
            grid.SelectionChanged += (s, e) =>
            {
                if (synchronizing || rows == null || dragging) return;
                controller.Library.Selection.ReplaceVisible(rows.SelectMany(x => x.Entry.SelectionKeys), grid.SelectedItems.Cast<LibraryRow>().SelectMany(x => x.Entry.SelectionKeys)); SyncSelection();
            };
            grid.Sorting += (s, e) =>
            {
                e.Handled = true; string[] names = { "Name", "Modified", "Size", "Type", "Bitrate" }; int index = Array.IndexOf(names, e.Column.SortMemberPath);
                if (index < 0) return;
                if (session.Sort == index) { session.Order = 1 - session.Order; order.SelectedIndex = session.Order; }
                else { session.Order = index == 1 ? 1 : 0; session.Sort = index; order.SelectedIndex = session.Order; sort.SelectedIndex = index; }
                ApplyLibrary();
            };
            grid.PreviewMouseLeftButtonDown += GridDown; grid.PreviewMouseMove += GridMove; grid.PreviewMouseLeftButtonUp += GridUp;
            grid.PreviewKeyDown += (s, e) => { if (e.Key != Key.Space) return; var check = Ui.Ancestor<CheckBox>(e.OriginalSource as DependencyObject); var data = check == null ? null : check.DataContext as LibraryRow; if (data != null) { data.Selected = data.Selected != true; e.Handled = true; } };
            grid.MouseDoubleClick += (s, e) => { var row = Ui.Ancestor<DataGridRow>(e.OriginalSource as DependencyObject); if (row == null || Ui.Ancestor<Button>(e.OriginalSource as DependencyObject) != null || Ui.Ancestor<CheckBox>(e.OriginalSource as DependencyObject) != null) return; var entry = ((LibraryRow)row.Item).Entry; if (entry.IsFolder) OpenFolder(entry); else DesktopController.Open(controller.LocalUrl + "/web/#!/details?id=" + Uri.EscapeDataString(Json.Text(entry.Item, "Id"))); };
            var overlay = new Grid(); overlay.Children.Add(grid); selectionCanvas = new Canvas { IsHitTestVisible = false }; selectionBox = new Rectangle { Fill = Ui.Brush("#209DABFF"), Stroke = Ui.Brush("#709DABFF"), StrokeThickness = 1, RadiusX = 4, RadiusY = 4, Visibility = Visibility.Collapsed }; selectionCanvas.Children.Add(selectionBox); overlay.Children.Add(selectionCanvas);
            var border = Ui.TableSurface(overlay); Grid.SetRow(border, 1); panel.Children.Add(border);
            var hint = Ui.Text("点击文件夹进入列表；支持复选框、全选、Ctrl / Shift 多选与拖动圈选。右键可删除文件夹、视频或弹幕。鼠标侧键和 Alt + 方向键可前进 / 后退。", "Note"); hint.FontSize = 11; Grid.SetRow(hint, 2); panel.Children.Add(hint);
            grid.Loaded += (s, e) => { double offset; var viewer = Ui.Child<ScrollViewer>(grid); if (viewer != null && session.ScrollOffsets.TryGetValue("library-grid", out offset)) viewer.ScrollToVerticalOffset(offset); };
            ApplyLibrary(); return panel;
        }
        void ApplyLibrary()
        {
            if (grid == null) return;
            renderedEntries = controller.Library.Entries;
            var view = controller.Library.Browse(session.Navigation.Current.Directory, session.Filter, (LibrarySort)session.Sort, session.Order == 1, controller.Settings.MediaFolder);
            rows = view.Select(x => new LibraryRow(x, controller.Library.Selection, controller)).ToArray();
            foreach (var row in rows) row.Changed += SyncSelection;
            var viewer = Ui.Child<ScrollViewer>(grid); double offset = viewer == null ? 0 : viewer.VerticalOffset;
            synchronizing = true; try { grid.ItemsSource = rows; } finally { synchronizing = false; }
            if (viewer != null) viewer.ScrollToVerticalOffset(offset);
            location.Text = session.Navigation.Current.Directory == null ? "媒体库文件夹" : session.Navigation.Current.Directory == "" ? "未分类影片" : session.Navigation.Current.Directory;
            location.ToolTip = location.Text;
            string[] names = { "Name", "Modified", "Size", "Type", "Bitrate" }; foreach (var column in grid.Columns) column.SortDirection = column.SortMemberPath == names[session.Sort] ? (session.Order == 0 ? ListSortDirection.Ascending : ListSortDirection.Descending) : (ListSortDirection?)null;
            SyncSelection();
            LoadFolderCovers();
        }
        void SyncSelection()
        {
            if (grid == null || rows == null || synchronizing) return;
            synchronizing = true;
            try
            {
                // Incremental updates preserve focus, keyboard anchors and scroll position.
                foreach (var row in rows)
                {
                    row.Notify(); bool selected = row.Selected == true; bool exists = grid.SelectedItems.Contains(row);
                    if (selected && !exists) grid.SelectedItems.Add(row); else if (!selected && exists) grid.SelectedItems.Remove(row);
                }
                var keys = rows.SelectMany(x => x.Entry.SelectionKeys).Distinct().ToArray(); int chosen = keys.Count(controller.Library.Selection.Contains);
                all.IsChecked = chosen == 0 ? false : chosen == keys.Length ? (bool?)true : null;
                count.Text = "共 " + controller.Library.Entries.Length + " 个影片 · 当前 " + rows.Length + (session.Navigation.Current.Directory == null ? " 个文件夹" : " 个影片") + " · 已选 " + controller.Library.Selection.Count + " 个影片";
                if (bulkActions != null) SurfaceMotion.SetVisible(bulkActions, controller.Library.Selection.Count > 0);
            }
            finally { synchronizing = false; }
        }
        void OpenFolder(LibraryEntry entry) { if (!entry.IsFolder || controller.Loading || controller.Busy) return; session.Navigation.UpdateFilter(session.Filter); if (session.Navigation.Visit(entry.FolderPath, session.Filter)) { session.ScrollOffsets["library-grid"] = 0; ApplyLibrary(); } }
        void NavigateHistory(bool forward)
        {
            if (session.Page != "library" || controller.Busy || controller.Loading) return;
            session.Navigation.UpdateFilter(session.Filter); if (forward ? session.Navigation.Forward() : session.Navigation.Back()) RestoreLocation();
        }
        void RestoreLocation() { session.Filter = session.Navigation.Current.Filter; librarySearch.RestoreText(session.Filter); ApplyLibrary(); }
        void ShowDanmuMenu(Button button, LibraryEntry entry)
        {
            var menu = new ContextMenu { PlacementTarget = button, Style = (Style)Ui.Resource("MediaMenu") }; var selected = controller.Library.SelectedItems;
            Action<string, DanmuMatchScope, bool> add = (title, scope, enabled) => { var item = new MenuItem { Header = title, Style = (Style)Ui.Resource("MediaMenuItem"), IsEnabled = enabled }; item.Click += (s, e) => Match(scope == DanmuMatchScope.Selection ? selected.FirstOrDefault() : entry.Item, scope, selected); menu.Items.Add(item); };
            add("重新选择单集弹幕来源…", DanmuMatchScope.Single, true); add("重新选择整季弹幕来源…", DanmuMatchScope.Season, entry.Type != "Movie"); add("重新选择已选 " + selected.Length + " 个影片的来源…", DanmuMatchScope.Selection, selected.Length > 1);
            menu.IsOpen = true;
        }
        void ShowLibraryActions(Button target, bool selectedOnly)
        {
            if (libraryMenu != null) { libraryMenu.IsOpen = false; libraryMenu.Items.Clear(); }
            libraryMenu = new ContextMenu { PlacementTarget = target, Style = (Style)Ui.Resource("MediaMenu") };
            Action<string, Action> add = (label, action) => { var item = new MenuItem { Header = label, Style = (Style)Ui.Resource("MediaMenuItem") }; item.Click += (s, e) => action(); libraryMenu.Items.Add(item); };
            if (selectedOnly)
            {
                add("导出 XML", async () => await controller.Execute(ExportXml)); add("影片详情", async () => await controller.Execute(() => { OpenDetails(); return Done(); }));
                add("复制原始文件名", () => Clipboard.SetText(String.Join(Environment.NewLine, controller.Library.SelectedItems.Select(x => System.IO.Path.GetFileName(Json.Text(x, "Path"))))));
            }
            libraryMenu.IsOpen = true;
        }
        void FitLibraryColumns()
        {
            if (grid == null || grid.Columns.Count < 7) return;
            bool detail = session.LibraryDetails || grid.ActualWidth >= 1050;
            grid.Columns[2].Visibility = grid.Columns[5].Visibility = detail ? Visibility.Visible : Visibility.Collapsed;
            grid.Columns[3].MinWidth = grid.ActualWidth < 850 && !session.LibraryDetails ? 132 : 142;
            grid.Columns[3].Width = grid.ActualWidth < 850 && !session.LibraryDetails ? 132 : 142;
            grid.Columns[4].Width = 110; grid.Columns[6].Width = grid.ActualWidth < 850 && !session.LibraryDetails ? 185 : 210;
        }
        internal ContextMenu CreateLibraryContextMenu(LibraryEntry entry)
        {
            var menu = new ContextMenu { Style = (Style)Ui.Resource("MediaMenu") };
            var selected = rows == null ? new[] { entry } : rows.Where(x => x.Selected == true).Select(x => x.Entry).ToArray();
            var targets = entry.SelectionKeys.All(controller.Library.Selection.Contains) && selected.Length > 0 ? selected : new[] { entry };
            foreach (MediaDeleteKind kind in Enum.GetValues(typeof(MediaDeleteKind)))
            {
                var action = kind; string title = kind == MediaDeleteKind.Folder ? "删除整个文件夹…" : kind == MediaDeleteKind.Video ? "删除视频…" : "删除弹幕…";
                var item = new MenuItem { Header = title, Style = (Style)Ui.Resource("MediaMenuItem"), IsEnabled = !controller.Busy && !controller.Loading && !controller.BatchRunning };
                item.Click += async (s, e) => await controller.Execute(async () =>
                {
                    var plan = await Task.Run(() => MediaDeletion.Plan(targets, action, controller.Settings.MediaFolder));
                    if (controller.Window == null || !AlertWindow.Show(controller.Window.View, plan.Title, plan.Confirmation, true, "永久删除", "取消")) return;
                    await controller.DeleteMedia(plan);
                });
                menu.Items.Add(item);
            }
            return menu;
        }
        void GridDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            if (Ui.Ancestor<CheckBox>(source) != null) { e.Handled = true; var check = Ui.Ancestor<CheckBox>(source); var data = check.DataContext as LibraryRow; if (data != null) data.Selected = data.Selected != true; else { bool select = all.IsChecked != true; foreach (var row in rows) foreach (var key in row.Entry.SelectionKeys) controller.Library.Selection.Set(key, select); SyncSelection(); } return; }
            if (Ui.Ancestor<Button>(source) != null || Ui.Ancestor<System.Windows.Controls.Primitives.DataGridColumnHeader>(source) != null || Ui.Ancestor<System.Windows.Controls.Primitives.ScrollBar>(source) != null) return;
            var rowElement = Ui.Ancestor<DataGridRow>(source); anchor = rowElement == null ? -1 : Array.IndexOf(rows, (LibraryRow)rowElement.Item);
            dragOrigin = e.GetPosition(grid); dragPrevious = controller.Library.Selection.Keys; dragAdditive = (Keyboard.Modifiers & ModifierKeys.Control) != 0; potentialDrag = anchor >= 0;
            var cell = Ui.Ancestor<DataGridCell>(source);
            if (rowElement != null && ((LibraryRow)rowElement.Item).Entry.IsFolder && cell != null && cell.Column.DisplayIndex == 1) e.Handled = true;
        }
        void GridMove(object sender, MouseEventArgs e)
        {
            if (!potentialDrag || e.LeftButton != MouseButtonState.Pressed || rows == null) return;
            var point = e.GetPosition(grid);
            if (!dragging && (point - dragOrigin).Length < 6) return;
            dragging = true; grid.CaptureMouse(); selectionBox.Visibility = Visibility.Visible;
            Canvas.SetLeft(selectionBox, Math.Min(point.X, dragOrigin.X)); Canvas.SetTop(selectionBox, Math.Min(point.Y, dragOrigin.Y)); selectionBox.Width = Math.Abs(point.X - dragOrigin.X); selectionBox.Height = Math.Abs(point.Y - dragOrigin.Y);
            var row = Ui.Ancestor<DataGridRow>(grid.InputHitTest(point) as DependencyObject); int end = row == null ? anchor : Array.IndexOf(rows, (LibraryRow)row.Item);
            var viewer = Ui.Child<ScrollViewer>(grid); if (viewer != null) { if (point.Y < 58) viewer.LineUp(); else if (point.Y > grid.ActualHeight - 24) viewer.LineDown(); }
            var selectedRows = LibrarySelection.DragRange(rows.Select(x => x.Entry.Key).ToArray(), anchor, end, new string[0], false).ToArray();
            var selected = rows.Where(x => selectedRows.Contains(x.Entry.Key)).SelectMany(x => x.Entry.SelectionKeys);
            if (dragAdditive) selected = selected.Concat(dragPrevious);
            controller.Library.Selection.ReplaceVisible(rows.SelectMany(x => x.Entry.SelectionKeys), selected); SyncSelection(); e.Handled = true;
        }
        void GridUp(object sender, MouseButtonEventArgs e)
        {
            bool wasDragging = dragging; potentialDrag = dragging = false; if (grid.IsMouseCaptured) grid.ReleaseMouseCapture(); selectionBox.Visibility = Visibility.Collapsed;
            if (wasDragging) { e.Handled = true; return; }
            var cell = Ui.Ancestor<DataGridCell>(e.OriginalSource as DependencyObject); var row = Ui.Ancestor<DataGridRow>(e.OriginalSource as DependencyObject);
            if (cell != null && cell.Column.DisplayIndex == 1 && row != null && ((LibraryRow)row.Item).Entry.IsFolder) OpenFolder(((LibraryRow)row.Item).Entry);
        }
        Dictionary<string, object> SingleItem() { var selected = controller.Library.SelectedItems; if (selected.Length != 1) throw new InvalidOperationException("请只选择一个影片；多选后可批量匹配或刷新弹幕。"); return selected[0]; }
        void OpenDetails() { DesktopController.Open(controller.LocalUrl + "/web/#!/details?id=" + Uri.EscapeDataString(Json.Text(SingleItem(), "Id"))); }
        async Task ExportXml()
        {
            var item = SingleItem(); string path = System.IO.Path.ChangeExtension(Json.Text(item, "Path"), ".xml");
            if (!File.Exists(path)) throw new InvalidOperationException("此影片尚无弹幕 XML，请先选择来源并下载。");
            string content = await Task.Run(() => File.ReadAllText(path)); int comments = DanmuCatalog.ParseXml(content).GetElementsByTagName("d").Count;
            string name = Json.Text(item, "Name"); foreach (char character in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(character, '_');
            var dialog = new Microsoft.Win32.SaveFileDialog { Title = "弹幕共 " + comments + " 条", Filter = "XML 弹幕|*.xml", FileName = name + ".xml", InitialDirectory = Paths.Data };
            if (dialog.ShowDialog(View) == true) { File.WriteAllText(dialog.FileName, content, new System.Text.UTF8Encoding(false)); Log.Write("已导出 " + comments + " 条弹幕。"); }
        }
    }
}
