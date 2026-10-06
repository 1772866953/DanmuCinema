using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace DanMuLAN
{
    public sealed class MatchDialog : Form
    {
        readonly DanmuCatalog catalog;
        readonly Dictionary<string, object> item;
        readonly TextBox keyword;
        readonly ListBox sources, episodes;
        readonly Label status;
        readonly Button search, fetch, save;
        readonly CheckBox animeOnly;
        bool busy;
        class Choice
        {
            public Dictionary<string, object> Data;
            public string Label;
            public override string ToString() { return Label; }
        }
        public MatchDialog(DanmuCatalog catalog, Dictionary<string, object> item, string initialSearch, bool preferAnime)
        {
            this.catalog = catalog; this.item = item;
            Text = item == null ? "在线弹幕搜索" : "匹配弹幕 · " + Json.Text(item, "Name");
            Font = new Font("Microsoft YaHei UI", 10); ClientSize = new Size(880, 640);
            MinimumSize = new Size(740, 520); StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(241, 244, 248); Padding = new Padding(20);
            var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            var input = new FlowLayoutPanel { Dock = DockStyle.Fill };
            keyword = new TextBox { Width = 420, Text = item == null ? initialSearch : MediaNames.SearchTitle(item), Margin = new Padding(0, 5, 12, 5) };
            search = new Button { Text = "搜索在线弹幕", AutoSize = true, Height = 34 };
            search.Click += async (s, e) => await Execute(Search);
            keyword.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Execute(Search); } };
            input.Controls.Add(keyword); input.Controls.Add(search); outer.Controls.Add(input, 0, 0);
            animeOnly = new CheckBox { Text = "只看动漫", AutoSize = true, Checked = item != null && Json.Text(item, "Type") == "Movie" ? false : preferAnime, Margin = new Padding(12, 8, 0, 0) };
            input.Controls.Add(animeOnly);
            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); columns.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            columns.Controls.Add(new Label { Text = "1  选择作品和弹幕来源", Dock = DockStyle.Fill }, 0, 0);
            columns.Controls.Add(new Label { Text = "2  选择对应集数", Dock = DockStyle.Fill }, 1, 0);
            sources = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
            episodes = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
            sources.DoubleClick += async (s, e) => await Execute(GetEpisodes);
            sources.SelectedIndexChanged += (s, e) => episodes.Items.Clear();
            columns.Controls.Add(sources, 0, 1); columns.Controls.Add(episodes, 1, 1); outer.Controls.Add(columns, 0, 1);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
            fetch = new Button { Text = "读取选中作品的集数", AutoSize = true, Height = 34 };
            save = new Button { Text = "下载并关联到此影片", AutoSize = true, Height = 34, BackColor = Color.FromArgb(19, 128, 112), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            fetch.Click += async (s, e) => await Execute(GetEpisodes); save.Click += async (s, e) => await Execute(Download);
            actions.Controls.Add(fetch); actions.Controls.Add(save); outer.Controls.Add(actions, 0, 2);
            status = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(100, 116, 131), Text = item == null ? "可直接搜索在线弹幕。关联前请回到本地列表选择视频。" : "本地：" + Json.Text(item, "SeriesName") + " · " + Json.Text(item, "Name") + "\r\n请核对季度、年份和集数。搜索词已去掉末尾 S 季号，结果可能包含其他季度。", Padding = new Padding(0, 10, 0, 0) };
            save.Enabled = item != null;
            outer.Controls.Add(status, 0, 3); Controls.Add(outer);
            FormClosing += (s, e) => { if (busy) { e.Cancel = true; status.Text = "正在执行请求，请稍后关闭。"; } };
        }
        async Task Execute(Func<Task> action)
        {
            if (busy) return; busy = true; search.Enabled = fetch.Enabled = save.Enabled = sources.Enabled = episodes.Enabled = animeOnly.Enabled = false; UseWaitCursor = true;
            try { await action(); }
            catch (Exception e) { status.Text = e.Message; }
            finally { busy = false; search.Enabled = fetch.Enabled = sources.Enabled = episodes.Enabled = animeOnly.Enabled = true; save.Enabled = item != null; UseWaitCursor = false; }
        }
        async Task Search()
        {
            if (String.IsNullOrWhiteSpace(keyword.Text)) throw new InvalidOperationException("请输入作品名。");
            status.Text = "正在查询在线弹幕来源…";
            var merged = await catalog.Search(keyword.Text.Trim(), animeOnly.Checked);
            var results = merged.Items;
            sources.Items.Clear(); episodes.Items.Clear();
            foreach (Dictionary<string, object> data in results)
                sources.Items.Add(new Choice { Data = data, Label = Json.Text(data, "Name") + " · " + Json.Text(data, "Year") + " · " + Json.Text(data, "Site") + (Json.Text(data, "Provider") == "animeko" ? "（目录候选，弹幕量需下载确认）" : "") });
            status.Text = "显示 " + results.Length + " 个候选" + (merged.HiddenCount > 0 ? "，已隐藏 " + merged.HiddenCount + " 个非动漫候选" : "") + "。请核对季度和集数。\r\n" + merged.Summary;
        }
        async Task GetEpisodes()
        {
            var selected = sources.SelectedItem as Choice;
            if (selected == null) throw new InvalidOperationException("请先选择作品。");
            var results = await catalog.Episodes(selected.Data);
            episodes.Items.Clear();
            foreach (Dictionary<string, object> data in results) episodes.Items.Add(new Choice { Data = data, Label = Json.Text(data, "Number") + "  " + Json.Text(data, "Title") });
            if (results.Length == 1) episodes.SelectedIndex = 0;
            status.Text = "读取到 " + results.Length + " 集。请选择与本地视频对应的一集。";
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
            SettingsStore.AtomicWrite(Path.ChangeExtension(video, ".xml"), content);
            Log.Write("已手动关联 " + count + " 条弹幕到选中影片。");
            status.Text = "已保存 " + count + " 条弹幕。重新打开影片即可读取；不同剪辑版本可在播放器调整弹幕时间偏移。";
        }
    }
}
