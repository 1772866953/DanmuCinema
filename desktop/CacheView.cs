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
            var cachePage = new CachePage(controller.Gateway.Catalog.Cache, View);
            pageResources.Add(cachePage); return cachePage;
        }
    }
    // Disk reads and deletion run off the dispatcher. Releasing the page drops its controls.
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
        public CachePage(DandanApiCache cache, Window owner)
        {
            this.cache = cache; this.owner = owner;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            summary = Ui.Text("正在读取本地缓存…", "Note");
            filter = Ui.Combo(new[] { "全部缓存", "文件特征", "文件识别", "弹幕", "搜索 / 作品详情" }, 0, 200);
            retention = Ui.Combo(CacheRetention.Labels, Array.IndexOf(CacheRetention.Months, cache.RetentionMonths), 150);
            System.Windows.Automation.AutomationProperties.SetName(filter, "缓存类型");
            System.Windows.Automation.AutomationProperties.SetName(retention, "统一缓存有效期");
            listing = new DataGrid { IsReadOnly = true, SelectionMode = DataGridSelectionMode.Extended };
            string[] labels = { "类型", "内容", "大小", "保存时间", "有效期" }, fields = { "TypeLabel", "Label", "SizeLabel", "CreatedLabel", "ExpiresLabel" };
            for (int i = 0; i < fields.Length; i++) listing.Columns.Add(new DataGridTextColumn { Header = labels[i], Binding = new Binding(fields[i]), Width = new DataGridLength(i == 1 ? 2.4 : i >= 3 ? 1.4 : 1, DataGridLengthUnitType.Star), MinWidth = i == 1 ? 160 : 100 });
            toolbar = Ui.Row(filter, Ui.Button("刷新", async () => await Refresh(null)), Ui.Button("删除选中", async () =>
            {
                var keys = listing.SelectedItems.Cast<ApiCacheEntry>().Select(x => x.Key).ToArray();
                await Refresh(() => cache.Remove(keys));
            }), Ui.Button("清理过期", async () => await Refresh(() => cache.Clear(true))), Ui.Button("清空全部缓存", async () => await Refresh(() => cache.Clear(false))));
            var policy = Ui.Row(Ui.Label("统一有效期"), retention, Ui.Button("应用有效期", async () =>
            {
                int months = CacheRetention.Months[retention.SelectedIndex]; await Refresh(() => cache.SetRetention(months));
            }));
            Children.Add(Ui.Stack(policy, toolbar, summary)); Grid.SetRow(listing, 1); Children.Add(listing);
            var note = Ui.Text("首次成功请求后保存，后续先读取本地缓存。所有类型共用有效期，按保存时间计算，修改后也适用于已有缓存。“长期”不因时间过期，仍受容量上限约束。删除缓存不会删除视频旁的 XML；缓存过期或被删除后，下次相关请求才重新联网。", "Note"); Grid.SetRow(note, 2); Children.Add(note);
            filter.SelectionChanged += FilterChanged; Loaded += InitialLoad;
        }
        async void InitialLoad(object sender, RoutedEventArgs e) { Loaded -= InitialLoad; await Refresh(null); }
        void FilterChanged(object sender, SelectionChangedEventArgs e) { ApplyFilter(); }
        void ApplyFilter()
        { listing.ItemsSource = entries.Where(x => filter.SelectedIndex == 0 || x.TypeLabel == (string)filter.SelectedItem).ToArray(); }
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
            entries = new ApiCacheEntry[0]; listing.ItemsSource = null; Children.Clear();
        }
    }
}
