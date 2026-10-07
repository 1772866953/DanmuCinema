using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DanmuCinema.Desktop
{
    public sealed partial class ShellWindow
    {
        FrameworkElement BuildCache()
        {
            var cachePage = new CachePage(controller.Gateway.Catalog.Cache, View, session.CacheNavigation, session.CacheFilter);
            cachePage.FilterUpdated += value => session.CacheFilter = value;
            pageResources.Add(cachePage); return cachePage;
        }
    }
    // Disk reads and deletion run off the dispatcher. Releasing the page drops its controls.
    public sealed class CacheRow
    {
        public ApiCacheEntry[] Members;
        public bool Folder;
        public string Anime;
        public string TypeLabel { get { return Folder ? "文件夹" : Members[0].TypeLabel; } }
        public string Label { get { return Folder ? "▸  " + Anime + " · " + Members.Length + " 条" : Members[0].Label; } }
        public string SizeLabel { get { return (Members.Sum(x => x.Bytes) / 1e6).ToString("N2") + " MB"; } }
        public string CreatedLabel { get { return Members.Max(x => x.CreatedUtc).ToLocalTime().ToString("yyyy-MM-dd HH:mm"); } }
        public string ExpiresLabel { get { return Folder ? "" : Members[0].ExpiresLabel; } }
    }
    public sealed class CachePage : Grid, IDisposable
    {
        readonly DandanApiCache cache;
        Window owner;
        readonly TextBlock summary;
        readonly ComboBox filter;
        readonly ComboBox retention;
        readonly DataGrid listing;
        readonly WrapPanel toolbar;
        ApiCacheEntry[] entries = new ApiCacheEntry[0];
        bool active = true, busy;
        readonly LibraryNavigation navigation;
        string currentAnime { get { return navigation.Current.Directory; } }
        public event Action<int> FilterUpdated;
        readonly Button breadcrumb;
        ContextMenu activeMenu;
        public CachePage(DandanApiCache cache, Window owner, LibraryNavigation navigation = null, int filterIndex = 0)
        {
            this.cache = cache; this.owner = owner;
            this.navigation = navigation ?? new LibraryNavigation();
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            summary = Ui.Text("正在读取本地缓存…", "Note");
            filter = Ui.Combo(new[] { "全部缓存", "文件特征", "文件识别", "弹幕", "搜索 / 作品详情", "封面" }, filterIndex, 200);
            retention = Ui.Combo(CacheRetention.Labels, Array.IndexOf(CacheRetention.Months, cache.RetentionMonths), 150);
            System.Windows.Automation.AutomationProperties.SetName(filter, "缓存类型");
            System.Windows.Automation.AutomationProperties.SetName(retention, "统一缓存有效期");
            listing = new DataGrid { IsReadOnly = true, SelectionMode = DataGridSelectionMode.Extended };
            breadcrumb = Ui.Button("缓存文件夹", () => OpenAnime(null));
            breadcrumb.HorizontalAlignment = HorizontalAlignment.Left; breadcrumb.Padding = new Thickness(12, 6, 12, 6); breadcrumb.MinHeight = 30;
            listing.MouseDoubleClick += (s, e) => { var visual = Ui.Ancestor<DataGridRow>(e.OriginalSource as DependencyObject); var row = visual == null ? null : visual.Item as CacheRow; if (row != null && row.Folder) OpenAnime(row.Anime); };
            listing.ContextMenu = new ContextMenu();
            listing.ContextMenuOpening += (s, e) =>
            {
                e.Handled = true; var visual = Ui.Ancestor<DataGridRow>(e.OriginalSource as DependencyObject); var row = visual == null ? (e.CursorLeft < 0 ? listing.SelectedItem as CacheRow : null) : visual.Item as CacheRow; if (row == null) return;
                var menu = CreateContextMenu(row); menu.IsOpen = true;
            };
            string[] labels = { "类型", "内容", "大小", "保存时间", "有效期" }, fields = { "TypeLabel", "Label", "SizeLabel", "CreatedLabel", "ExpiresLabel" };
            for (int i = 0; i < fields.Length; i++) listing.Columns.Add(new DataGridTextColumn { Header = labels[i], Binding = new Binding(fields[i]), Width = new DataGridLength(i == 1 ? 2.4 : i >= 3 ? 1.4 : 1, DataGridLengthUnitType.Star), MinWidth = i == 1 ? 160 : 100 });
            toolbar = Ui.Row(filter, Ui.Button("刷新", async () => await Refresh(null)), Ui.Button("删除选中", async () =>
            {
                var keys = listing.SelectedItems.Cast<CacheRow>().SelectMany(x => x.Members).Select(x => x.Key).ToArray();
                await Refresh(() => cache.Remove(keys));
            }), Ui.Button("清理过期", async () => await Refresh(() => cache.Clear(true))), Ui.Button("清空全部缓存", async () => await Refresh(() => cache.Clear(false))));
            var policy = Ui.Row(Ui.Label("统一有效期"), retention, Ui.Button("应用有效期", async () =>
            {
                int months = CacheRetention.Months[retention.SelectedIndex]; await Refresh(() => cache.SetRetention(months));
            }));
            Children.Add(Ui.Stack(policy, toolbar, breadcrumb, summary)); Grid.SetRow(listing, 1); Children.Add(listing);
            var note = Ui.Text("首次成功请求后保存，后续先读取本地缓存。所有类型共用有效期，按保存时间计算，修改后也适用于已有缓存。“长期”不因时间过期，仍受容量上限约束。删除缓存不会删除视频旁的 XML；缓存过期或被删除后，下次相关请求才重新联网。", "Note"); Grid.SetRow(note, 2); Children.Add(note);
            filter.SelectionChanged += FilterChanged; Loaded += InitialLoad;
        }
        async void InitialLoad(object sender, RoutedEventArgs e) { Loaded -= InitialLoad; await Refresh(null); }
        internal void OpenAnime(string anime) { if (!active || busy) return; navigation.Visit(anime, ""); ApplyFilter(); }
        internal void NavigateHistory(bool forward)
        {
            if (!active || busy) return;
            if (forward ? navigation.Forward() : navigation.Back()) ApplyFilter();
        }
        internal ContextMenu CreateContextMenu(CacheRow row)
        {
            if (activeMenu != null) { activeMenu.IsOpen = false; activeMenu.Items.Clear(); activeMenu.PlacementTarget = null; }
            var menu = new ContextMenu { PlacementTarget = listing, Style = (Style)Ui.Resource("MediaMenu") };
            var remove = new MenuItem { Header = row.Folder ? "删除这个动漫的全部缓存" : "删除缓存", Style = (Style)Ui.Resource("MediaMenuItem") };
            remove.Click += async (sender, args) => { var keys = row.Members.Select(x => x.Key).ToArray(); await Refresh(() => { if (row.Folder) cache.RemoveAnime(row.Anime); else cache.Remove(keys); }); }; menu.Items.Add(remove); activeMenu = menu; return menu;
        }
        void FilterChanged(object sender, SelectionChangedEventArgs e) { ApplyFilter(); if (FilterUpdated != null) FilterUpdated(filter.SelectedIndex); }
        void ApplyFilter()
        {
            var shown = entries.Where(x => filter.SelectedIndex == 0 || x.TypeLabel == (string)filter.SelectedItem).ToArray();
            // A deleted history destination displays the root without creating a
            // new history visit, so back/forward never becomes stuck in a loop.
            bool root = currentAnime == null || !entries.Any(x => x.Anime == currentAnime);
            listing.ItemsSource = root ? shown.GroupBy(x => x.Anime).OrderBy(x => x.Key, NaturalNames.Instance).Select(x => new CacheRow { Folder = true, Anime = x.Key, Members = x.ToArray() }).ToArray() : shown.Where(x => x.Anime == currentAnime).Select(x => new CacheRow { Anime = x.Anime, Members = new[] { x } }).ToArray();
            breadcrumb.Content = root ? "缓存文件夹" : "缓存文件夹 / " + currentAnime;
        }
        async Task Refresh(Action change)
        {
            if (!active || busy) return;
            busy = true; IsEnabled = false;
            try
            {
                var snapshot = await Task.Run(() => { if (change != null) change(); return cache.Entries(); });
                if (!active) return;
                entries = snapshot;
                summary.Text = "共 " + entries.Length + " 条 · " + (entries.Sum(x => x.Bytes) / 1000000.0).ToString("N2") + " MB · " + entries.Count(x => x.ExpiresUtc <= DateTime.UtcNow) + " 条已过期";
                ApplyFilter();
            }
            catch (Exception e) { if (active) AlertWindow.Show(owner, "缓存操作未完成", e.Message, false); }
            finally { busy = false; if (active) IsEnabled = true; }
        }
        public void Dispose()
        {
            active = false; owner = null; Loaded -= InitialLoad; filter.SelectionChanged -= FilterChanged;
            FilterUpdated = null;
            if (activeMenu != null) { activeMenu.IsOpen = false; activeMenu.Items.Clear(); activeMenu.PlacementTarget = null; activeMenu = null; }
            entries = new ApiCacheEntry[0]; listing.ItemsSource = null; Children.Clear();
        }
    }
}
