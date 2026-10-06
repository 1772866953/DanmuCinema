using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace DanmuCinema
{
    public sealed class MatchDialog : Form
    {
        readonly DanmuCatalog catalog;
        readonly Dictionary<string, object> item;
        readonly HistorySearchBox keyword;
        readonly ListBox sources, episodes;
        readonly Label status;
        readonly Button search, fetch, save, saveAll, saveSelected;
        readonly CheckBox animeOnly, smart;
        readonly ComboBox service;
        readonly TabControl scopeTabs;
        readonly TableLayoutPanel layout;
        readonly List<WrappedLabel> scopeDescriptions = new List<WrappedLabel>();
        readonly List<Button> scopeCommands = new List<Button>();
        bool fittingLayout;
        readonly Dictionary<string, object>[] selectedLocal;
        bool busy;
        class Choice
        {
            public Dictionary<string, object> Data;
            public string Label;
            public override string ToString() { return Label; }
        }
        class ServiceChoice
        {
            public string Id, Name;
            public override string ToString() { return Name; }
        }
        public MatchDialog(DanmuCatalog catalog, Dictionary<string, object> item, string initialSearch, bool preferAnime, IEnumerable<Dictionary<string, object>> selectedItems = null, DanmuMatchScope scope = DanmuMatchScope.Single)
        {
            this.catalog = catalog; this.item = item;
            selectedLocal = (selectedItems ?? new Dictionary<string, object>[0]).GroupBy(LibraryEntry.Identity, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToArray();
            Text = item == null ? "在线弹幕搜索" : "匹配弹幕 · " + Json.Text(item, "Name");
            Font = new Font("Microsoft YaHei UI", 10); ClientSize = new Size(1000, 700);
            MinimumSize = new Size(960, 600); StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(241, 244, 248); Padding = new Padding(20);
            var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout = outer; outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize)); outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 160)); outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var input = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            input.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            service = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
            service.Items.Add(new ServiceChoice { Id = null, Name = "全部已启用接口（联合搜索）" });
            foreach (var source in catalog.SourceChoices()) service.Items.Add(new ServiceChoice { Id = Json.Text(source, "Id"), Name = Json.Text(source, "Name") });
            service.SelectedIndex = 0;
            fetch = new Button { Text = "读取选中作品的集数", AutoSize = true, Height = 34 };
            fetch.Click += async (s, e) => await Execute(GetEpisodes);
            input.Controls.Add(Flow(new Label { Text = "接口服务", Width = 85, Height = 32, TextAlign = ContentAlignment.MiddleLeft }, service, fetch), 0, 0);
            keyword = new HistorySearchBox("danmu") { Width = 350, Text = item == null ? initialSearch : MediaNames.SearchTitle(item), Margin = new Padding(0, 5, 12, 5) };
            keyword.SearchChosen += async () => await Execute(Search);
            search = new Button { Text = "搜索在线弹幕", AutoSize = true, Height = 34 };
            search.Click += async (s, e) => await Execute(Search);
            keyword.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Execute(Search); } };
            animeOnly = new CheckBox { Text = "只看动漫", AutoSize = true, Checked = item != null && Json.Text(item, "Type") == "Movie" ? false : preferAnime, Margin = new Padding(12, 8, 0, 0) };
            smart = new CheckBox { Text = "智能搜索匹配", AutoSize = true, Checked = true, Margin = new Padding(12, 8, 0, 0) };
            input.Controls.Add(Flow(keyword, search, animeOnly, smart), 0, 1); outer.Controls.Add(input, 0, 0);
            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); columns.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            columns.Controls.Add(new Label { Text = "1  选择作品和弹幕来源", Dock = DockStyle.Fill }, 0, 0);
            columns.Controls.Add(new Label { Text = "2  选择对应集数", Dock = DockStyle.Fill }, 1, 0);
            sources = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
            episodes = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
            sources.DoubleClick += async (s, e) => await Execute(GetEpisodes);
            sources.SelectedIndexChanged += (s, e) => episodes.Items.Clear();
            columns.Controls.Add(sources, 0, 1); columns.Controls.Add(episodes, 1, 1); outer.Controls.Add(columns, 0, 1);
            scopeTabs = new TabControl { Dock = DockStyle.Fill };
            save = new Button { Text = "下载并关联到此影片", AutoSize = true, Height = 34, BackColor = Color.FromArgb(19, 128, 112), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            saveAll = new Button { Text = "预览并替换整季弹幕", AutoSize = true, Height = 34, Enabled = item != null };
            saveSelected = new Button { Text = "预览并替换已选影片弹幕", AutoSize = true, Height = 34, Enabled = selectedLocal.Length > 1 };
            save.Click += async (s, e) => await Execute(Download);
            saveAll.Click += async (s, e) => await Execute(() => DownloadGroup(false));
            saveSelected.Click += async (s, e) => await Execute(() => DownloadGroup(true));
            AddScope("单集", save, "仅关联当前影片。已有同名 XML 将直接覆盖。", DanmuMatchScope.Single);
            if (item != null && Json.Text(item, "Type") != "Movie") AddScope("整个季度", saveAll, "按集号匹配本地整季文件；预览确认后下载，重复或无法识别的集数会跳过。", DanmuMatchScope.Season);
            if (selectedLocal.Length > 1) AddScope("已选 " + selectedLocal.Length + " 个影片", saveSelected, "只更新勾选的同番同季影片，不扩展到整个季度；下载前可再次核对。", DanmuMatchScope.Selection);
            foreach (TabPage page in scopeTabs.TabPages) if ((DanmuMatchScope)page.Tag == scope) scopeTabs.SelectedTab = page;
            outer.Controls.Add(scopeTabs, 0, 2);
            status = new WrappedLabel { Name = "matchStatus", ForeColor = Color.FromArgb(100, 116, 131), Text = item == null ? "可直接搜索在线弹幕。关联前请回到本地列表选择视频。" : "本地：" + Json.Text(item, "SeriesName") + " · " + Json.Text(item, "Name") + "\r\n请核对季度、年份和集数。搜索词已去掉末尾 S 季号，结果可能包含其他季度。", Padding = new Padding(0, 10, 0, 0) };
            save.Enabled = item != null;
            outer.Controls.Add(status, 0, 3); Controls.Add(outer);
            outer.SizeChanged += (s, e) => FitScopeHeight();
            scopeTabs.SizeChanged += (s, e) => FitScopeHeight();
            FitScopeHeight();
            service.SelectedIndexChanged += async (s, e) => { if (!busy && Visible && !String.IsNullOrWhiteSpace(keyword.Text)) await Execute(Search); };
            Shown += async (s, e) => { if (!String.IsNullOrWhiteSpace(keyword.Text)) await Execute(Search); };
            FormClosing += (s, e) => { if (busy) { e.Cancel = true; status.Text = "正在执行请求，请稍后关闭。"; } };
        }
        static FlowLayoutPanel Flow(params Control[] controls)
        {
            var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
            flow.Controls.AddRange(controls); return flow;
        }
        void AddScope(string caption, Button command, string explanation, DanmuMatchScope scope)
        {
            var page = new TabPage(caption) { Tag = scope, Padding = new Padding(10), BackColor = Color.White, AutoScroll = true };
            var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2 };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            command.MinimumSize = new Size(0, 36);
            content.Controls.Add(Flow(command), 0, 0);
            var label = new WrappedLabel { Name = "scopeDescription", Text = explanation, ForeColor = Color.FromArgb(100, 116, 131) };
            content.Controls.Add(label, 0, 1); scopeDescriptions.Add(label); scopeCommands.Add(command);
            label.FontChanged += (s, e) => FitScopeHeight(); command.SizeChanged += (s, e) => FitScopeHeight();
            page.Controls.Add(content); scopeTabs.TabPages.Add(page);
        }
        void FitScopeHeight()
        {
            if (fittingLayout || layout == null || scopeTabs == null || scopeDescriptions.Count == 0) return;
            fittingLayout = true;
            try
            {
                int width = Math.Max(100, scopeTabs.Width - 48), height = 0;
                for (int i = 0; i < scopeDescriptions.Count; i++)
                    height = Math.Max(height, Math.Max(scopeCommands[i].Height, scopeCommands[i].GetPreferredSize(Size.Empty).Height) + scopeDescriptions[i].GetPreferredSize(new Size(width, 0)).Height);
                layout.RowStyles[2].Height = height + Font.Height + 54;
            }
            finally { fittingLayout = false; }
        }
        async Task Execute(Func<Task> action)
        {
            if (busy) return; busy = true; keyword.Enabled = search.Enabled = fetch.Enabled = save.Enabled = saveAll.Enabled = saveSelected.Enabled = service.Enabled = scopeTabs.Enabled = sources.Enabled = episodes.Enabled = animeOnly.Enabled = smart.Enabled = false; UseWaitCursor = true;
            try { await action(); }
            catch (Exception e) { status.Text = e.Message; }
            finally { busy = false; keyword.Enabled = search.Enabled = fetch.Enabled = service.Enabled = scopeTabs.Enabled = sources.Enabled = episodes.Enabled = animeOnly.Enabled = smart.Enabled = true; save.Enabled = item != null; saveAll.Enabled = item != null && Json.Text(item, "Type") != "Movie"; saveSelected.Enabled = selectedLocal.Length > 1; UseWaitCursor = false; }
        }
        async Task Search()
        {
            if (String.IsNullOrWhiteSpace(keyword.Text)) throw new InvalidOperationException("请输入作品名。");
            keyword.Commit(true);
            status.Text = "正在查询在线弹幕来源…";
            int season = item == null ? SmartMatching.SeasonTitle(keyword.Text) : SmartMatching.Season(item);
            sources.Items.Clear(); episodes.Items.Clear();
            var merged = await catalog.Search(keyword.Text.Trim(), animeOnly.Checked, smart.Checked, season, ((ServiceChoice)service.SelectedItem).Id);
            var results = merged.Items;
            sources.Items.Clear(); episodes.Items.Clear();
            foreach (Dictionary<string, object> data in results)
                sources.Items.Add(new Choice { Data = data, Label = (smart.Checked ? "[" + Math.Round(SmartMatching.Score(keyword.Text, data, season)) + "%] " : "") + Json.Text(data, "Name") + " · " + Json.Text(data, "Year") + " · " + Json.Text(data, "Site") + (Json.Text(data, "Provider") == "animeko" ? "（目录候选）" : "") });
            status.Text = "显示 " + results.Length + " 个候选" + (merged.HiddenCount > 0 ? "，已隐藏 " + merged.HiddenCount + " 个非动漫候选" : "") + "。请核对季度和集数。\r\n" + merged.Summary;
            if (smart.Checked && results.Length > 0)
            {
                var candidate = (Dictionary<string, object>)results[0];
                if (SmartMatching.Score(keyword.Text, candidate, season) >= 75 && (season == 0 || SmartMatching.SeasonTitle(Json.Text(candidate, "Name")) == season))
                {
                    sources.SelectedIndex = 0; await GetEpisodes();
                    status.Text = "已自动推荐作品并读取集数，请核对后下载。\r\n" + merged.Summary;
                }
            }
        }
        async Task GetEpisodes()
        {
            var selected = sources.SelectedItem as Choice;
            if (selected == null) throw new InvalidOperationException("请先选择作品。");
            var results = await catalog.Episodes(selected.Data);
            episodes.Items.Clear();
            foreach (Dictionary<string, object> data in results) episodes.Items.Add(new Choice { Data = data, Label = Json.Text(data, "Number") + "  " + Json.Text(data, "Title") });
            if (results.Length == 1) episodes.SelectedIndex = 0;
            int localNumber = item == null ? 0 : SmartMatching.Episode(item);
            if (localNumber > 0)
            {
                var matching = episodes.Items.Cast<Choice>().Where(x => SmartMatching.RemoteNumber(x.Data) == localNumber).ToArray();
                if (matching.Length == 1) episodes.SelectedItem = matching[0];
            }
            status.Text = "读取到 " + results.Length + " 集。请选择与本地视频对应的一集。";
        }
        async Task DownloadGroup(bool selectedOnly)
        {
            if (item == null) throw new InvalidOperationException("请先选择本地番剧视频。");
            var source = sources.SelectedItem as Choice;
            if (source == null) throw new InvalidOperationException("请先匹配并选择正确季度的作品。");
            if (Json.Text(item, "Type") == "Movie") throw new InvalidOperationException("全部下载用于番剧；电影请使用单集下载。");
            if (episodes.Items.Count == 0) await GetEpisodes();
            status.Text = "正在读取本地本季文件并按集号匹配…";
            if (selectedOnly) MediaLibrary.RequireSameSeason(selectedLocal);
            var local = selectedOnly ? selectedLocal : await catalog.LocalSeason(item);
            var plan = BatchMatching.Plan(local, episodes.Items.Cast<Choice>().Select(x => x.Data));
            if (plan.Count == 0) throw new InvalidOperationException("没有找到同季本地视频。");
            using (var dialog = new BatchDialog(catalog, plan, (selectedOnly ? "已选影片 · " : "整季 · ") + Json.Text(source.Data, "Name") + " · " + Json.Text(source.Data, "Site"), false)) dialog.ShowDialog(this);
            status.Text = "批量下载窗口已关闭，已保存的弹幕可在播放器重新打开影片后读取。";
        }
        async Task Download()
        {
            if (item == null) throw new InvalidOperationException("请先在本地影片列表选择要关联的视频。");
            var source = sources.SelectedItem as Choice; var episode = episodes.SelectedItem as Choice;
            if (source == null || episode == null) throw new InvalidOperationException("请先选择作品和对应集数。");
            string video = Json.Text(item, "Path");
            if (!File.Exists(video)) throw new InvalidOperationException("本地视频文件不存在；当前功能仅关联本地视频。");
            string content = await catalog.Download(episode.Data);
            var xml = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(content), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) xml.Load(reader);
            int count = xml.GetElementsByTagName("d").Count;
            if (count == 0) throw new InvalidOperationException("这集没有可用弹幕，已有文件未更改。");
            SettingsStore.AtomicWrite(Path.ChangeExtension(video, ".xml"), content, false);
            catalog.RecordAssociation(item, episode.Data);
            Log.Write("已手动关联 " + count + " 条弹幕到选中影片。");
            status.Text = "已保存 " + count + " 条弹幕。重新打开影片即可读取；不同剪辑版本可在播放器调整弹幕时间偏移。";
        }
    }
}
