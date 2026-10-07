using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shell;
using System.ComponentModel;

namespace DanmuCinema.Desktop
{
    public sealed class MatchView : Grid, IDisposable
    {
        Grid Body { get { return this; } }
        readonly Action showPreview;
        readonly DesktopController controller;
        readonly MatchState state;
        readonly HistoryInput keyword;
        readonly ComboBox service;
        readonly ComboBox localTarget;
        readonly CheckBox anime, smart;
        readonly ListBox sources, episodes;
        readonly TextBlock status;
        readonly TabControl scopes;
        readonly StackPanel input;
        readonly List<Button> commands = new List<Button>();
        readonly List<Action> unhook = new List<Action>();
        void Subscribe(UIElement control, RoutedEvent routedEvent, Delegate handler)
        { control.AddHandler(routedEvent, handler); unhook.Add(() => control.RemoveHandler(routedEvent, handler)); }
        object[] renderedSources, renderedEpisodes;
        bool synchronizing, closed, working;
        public MatchView(DesktopController controller, MatchState state, bool autoSearch, Action showPreview)
        {
            this.controller = controller; this.state = state; this.showPreview = showPreview;
            if (state.Selected == null) state.Selected = new Dictionary<string, object>[0];
            Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Body.RowDefinitions.Add(new RowDefinition()); Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var choices = new List<Choice> { new Choice { Label = "全部已启用接口（联合搜索）", Data = new Dictionary<string, object> { { "Id", "" } } } };
            choices.AddRange(controller.Gateway.Catalog.SourceChoices().Select(x => new Choice { Data = x, Label = Json.Text(x, "Name") }));
            service = new ComboBox { Width = 300, ItemsSource = choices, SelectedIndex = Math.Max(0, choices.FindIndex(x => Json.Text(x.Data, "Id") == (state.ServiceId ?? ""))) };
            keyword = new HistoryInput("danmu", state.Keyword); keyword.Chosen += SearchClicked;
            anime = Ui.Check("只看动漫", state.AnimeOnly); smart = Ui.Check("智能搜索匹配", state.Smart);
            Subscribe(keyword.Editor, TextBox.TextChangedEvent, new TextChangedEventHandler((s, e) => state.Keyword = keyword.Editor.Text));
            Subscribe(keyword.Editor, UIElement.KeyDownEvent, new System.Windows.Input.KeyEventHandler((s, e) => { if (e.Key == System.Windows.Input.Key.Enter) { SearchClicked(); e.Handled = true; } }));
            Subscribe(anime, CheckBox.CheckedEvent, new RoutedEventHandler((s, e) => state.AnimeOnly = true)); Subscribe(anime, CheckBox.UncheckedEvent, new RoutedEventHandler((s, e) => state.AnimeOnly = false));
            Subscribe(smart, CheckBox.CheckedEvent, new RoutedEventHandler((s, e) => state.Smart = true)); Subscribe(smart, CheckBox.UncheckedEvent, new RoutedEventHandler((s, e) => state.Smart = false));
            input = Ui.Stack(Ui.Row(Ui.Label("接口服务"), service, Command("识别本地文件", Identify), Command("读取作品集数", GetEpisodes)), Ui.Row(keyword, Command("搜索在线弹幕", Search, true), anime, smart)); Body.Children.Add(input);
            var targetHint = Ui.Text(state.Item == null ? "搜索来源后，选择需要关联的本地季度。" : "本地影片 · " + MediaPresentation.Title(state.Item) + (state.Selected.Length > 1 ? " · 已选 " + state.Selected.Length + " 个影片" : ""));
            targetHint.Foreground = (Brush)Ui.Resource("Muted"); targetHint.Margin = new Thickness(0, 0, 0, 12); targetHint.TextWrapping = TextWrapping.NoWrap; targetHint.TextTrimming = TextTrimming.CharacterEllipsis; targetHint.ToolTip = state.Item == null ? null : Json.Text(state.Item, "Path"); input.Children.Insert(0, targetHint);
            sources = new ListBox(); episodes = new ListBox();
            var columns = new Grid { Margin = new Thickness(0, 6, 0, 16) }; columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); columns.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new DockPanel(); var leftLabel = Ui.Text("01  选择作品和来源", "Heading"); DockPanel.SetDock(leftLabel, Dock.Top); left.Children.Add(leftLabel); left.Children.Add(sources);
            var right = new DockPanel(); var rightLabel = Ui.Text("02  选择对应集数", "Heading"); DockPanel.SetDock(rightLabel, Dock.Top); right.Children.Add(rightLabel); right.Children.Add(episodes);
            columns.Children.Add(left); Grid.SetColumn(right, 2); columns.Children.Add(right); Grid.SetRow(columns, 1); Body.Children.Add(columns);
            var itemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding Label}' TextWrapping='Wrap'/></DataTemplate>"); sources.ItemTemplate = episodes.ItemTemplate = itemTemplate;
            Subscribe(sources, ListBox.SelectionChangedEvent, new SelectionChangedEventHandler(async (s, e) => { if (synchronizing) return; state.SourceIndex = sources.SelectedIndex; state.Episodes = new object[0]; state.EpisodeIndex = -1; Render(); if (state.SourceIndex >= 0) await Work(GetEpisodes); }));
            Subscribe(episodes, ListBox.SelectionChangedEvent, new SelectionChangedEventHandler((s, e) => { if (!synchronizing) state.EpisodeIndex = episodes.SelectedIndex; }));
            scopes = new TabControl { Margin = new Thickness(0, 0, 0, 4) };
            AddScope("单集", DanmuMatchScope.Single, Command("下载并关联单集", Download, true), "只更新当前影片，已有同名 XML 将直接覆盖。");
            if (state.Item == null || Json.Text(state.Item, "Type") != "Movie") AddScope("整个季度", DanmuMatchScope.Season, Command("预览整季并全部下载", () => DownloadGroup(false), true), "按集号匹配本地整季文件。先预览，无法识别或重复的集数会跳过。");
            if (state.Selected.Length > 1) AddScope("已选 " + state.Selected.Length + " 个影片", DanmuMatchScope.Selection, Command("预览已选影片并下载", () => DownloadGroup(true), true), "只更新已勾选的同番同季影片。");
            foreach (TabItem tab in scopes.Items) if ((DanmuMatchScope)tab.Tag == state.Scope) scopes.SelectedItem = tab;
            if (state.Item == null)
            {
                var targets = controller.Library.Entries.Where(x => x.Type != "Movie").GroupBy(MediaLibrary.DirectoryOf, StringComparer.OrdinalIgnoreCase).Select(x => new Choice { Label = x.Key, Data = x.First().Item }).ToArray();
                localTarget = new ComboBox { ItemsSource = targets, SelectedIndex = -1, MinWidth = 350, MaxWidth = 760 };
                Subscribe(localTarget, ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler((s, e) => { var choice = localTarget.SelectedItem as Choice; state.Item = choice == null ? null : choice.Data; }));
                input.Children.Add(Ui.Row(Ui.Label("本地季度"), localTarget)); scopes.SelectedIndex = 1; state.Scope = DanmuMatchScope.Season;
            }
            Subscribe(scopes, TabControl.SelectionChangedEvent, new SelectionChangedEventHandler((s, e) => { var tab = scopes.SelectedItem as TabItem; if (tab != null) { state.Scope = (DanmuMatchScope)tab.Tag; Ui.Animate((FrameworkElement)tab.Content); } }));
            Grid.SetRow(scopes, 2); Body.Children.Add(scopes); status = Ui.Text(state.Status, "Note"); Grid.SetRow(status, 3); Body.Children.Add(status);
            Subscribe(service, ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(async (s, e) => { state.ServiceId = service.SelectedItem == null ? null : Json.Text(((Choice)service.SelectedItem).Data, "Id"); if (!closed && IsVisible && !controller.Busy && !String.IsNullOrWhiteSpace(state.Keyword)) await Work(Search); }));
            controller.Changed += Render;

            Render();
            bool searchedOnLoad = false; if (autoSearch) Subscribe(this, LoadedEvent, new RoutedEventHandler(async (s, e) => { if (searchedOnLoad || closed) return; searchedOnLoad = true; if (state.Item != null && (String.IsNullOrEmpty(state.ServiceId) || state.ServiceId == "dandan")) await Work(IdentifyThenSearch); else if (!String.IsNullOrWhiteSpace(state.Keyword)) await Work(Search); }));
        }
        Button Command(string text, Func<Task> action, bool primary = false) { var button = Ui.Button(text, async () => await Work(action), primary); commands.Add(button); return button; }
        async void SearchClicked() { await Work(Search); }
        async Task Work(Func<Task> action)
        {
            if (working || controller.Busy || controller.BatchRunning || closed) return;
            // Read the editor at command time, including a selected history row.
            state.Keyword = keyword.Editor.Text; keyword.CommitSearch(); working = true; Render();
            try
            {
                // A history popup can be clicked while an earlier library read
                // is finishing. Execute used to silently discard that search.
                while (controller.Loading && !closed) await Task.Delay(25);
                if (!closed) await controller.Execute(async () => { try { await action(); } catch (Exception error) { state.Status = error.Message; throw; } }, false);
            }
            finally { working = false; Render(); }
        }
        void AddScope(string name, DanmuMatchScope scope, Button command, string hint) { scopes.Items.Add(new TabItem { Header = name, Tag = scope, Content = Ui.Stack(Ui.Row(command), Ui.Text(hint, "Note")), Padding = new Thickness(12, 8, 12, 8) }); }
        void Render()
        {
            if (closed) return;
            // Unrelated background library/status updates do not change this
            // dialog's enabled state or tear down its existing search results.
            bool enabled = !working && !controller.Busy && !controller.BatchRunning;
            input.IsEnabled = sources.IsEnabled = episodes.IsEnabled = enabled; foreach (var button in commands) button.IsEnabled = enabled;
            synchronizing = true;
            try
            {
                if (!Object.ReferenceEquals(renderedSources, state.Sources)) { sources.ItemsSource = MakeSources(); renderedSources = state.Sources; }
                if (!Object.ReferenceEquals(renderedEpisodes, state.Episodes)) { episodes.ItemsSource = MakeEpisodes(); renderedEpisodes = state.Episodes; }
                if (sources.SelectedIndex != state.SourceIndex) sources.SelectedIndex = state.SourceIndex;
                if (episodes.SelectedIndex != state.EpisodeIndex) episodes.SelectedIndex = state.EpisodeIndex;
            }
            finally { synchronizing = false; }
            if (status.Text != state.Status) status.Text = state.Status;
        }
        Choice[] MakeSources()
        {
            int season = state.Item == null ? SmartMatching.SeasonTitle(state.Keyword) : SmartMatching.Season(state.Item);
            return state.Sources.OfType<Dictionary<string, object>>().Select(data => new Choice { Data = data, Label = Json.Text(data, "Name") + "\n" + Json.Text(data, "Site") + (String.IsNullOrEmpty(Json.Text(data, "Year")) ? "" : " · " + Json.Text(data, "Year")) + (state.Smart ? " · 名称相似度 " + Math.Round(SmartMatching.Score(state.Keyword, data, season)) + "%" : "") + (Json.Text(data, "Provider") == "animeko" ? "（目录候选）" : "") }).ToArray();
        }
        Choice[] MakeEpisodes() { return state.Episodes.OfType<Dictionary<string, object>>().Select(data => new Choice { Data = data, Label = Json.Text(data, "Number") + "  " + Json.Text(data, "Title") }).ToArray(); }
        async Task Search()
        {
            if (String.IsNullOrWhiteSpace(state.Keyword)) throw new InvalidOperationException("请输入作品名。");
            state.Status = "正在查询弹幕来源，优先读取本地缓存…"; controller.Publish();
            int season = state.Item == null ? SmartMatching.SeasonTitle(state.Keyword) : SmartMatching.Season(state.Item);
            var result = await controller.Gateway.Catalog.Search(state.Keyword.Trim(), state.AnimeOnly, state.Smart, season, String.IsNullOrEmpty(state.ServiceId) ? null : state.ServiceId);
            state.Sources = result.Items; state.Episodes = new object[0]; state.SourceIndex = state.EpisodeIndex = -1; state.Status = "显示 " + result.Items.Length + " 个候选" + (result.HiddenCount > 0 ? "，隐藏 " + result.HiddenCount + " 个非动漫候选" : "") + "。请核对季度和集数。\n" + result.Summary; controller.Publish();
            if (state.Smart && result.Items.Length > 0)
            {
                var candidate = (Dictionary<string, object>)result.Items[0];
                if (SmartMatching.Score(state.Keyword, candidate, season) >= 75 && (season == 0 || SmartMatching.SeasonTitle(Json.Text(candidate, "Name")) == season)) { state.SourceIndex = 0; await GetEpisodes(); state.Status = "已推荐作品并读取集数，请核对后下载。\n" + result.Summary; }
            }
        }
        async Task IdentifyThenSearch() { await Identify(); if (state.Sources.Length == 0 && !String.IsNullOrWhiteSpace(state.Keyword)) await Search(); }
        async Task Identify()
        {
            if (state.Item == null) throw new InvalidOperationException("请先选择本地影片。");
            state.Status = "先查本地缓存；需要时识别 hash，再匹配文件名…"; controller.Publish();
            var result = await controller.Gateway.Catalog.IdentifyFile(state.Item, CancellationToken.None);
            state.Sources = result.Animes.Cast<object>().ToArray(); state.SourceIndex = result.Recommended == null ? (result.Animes.Length == 1 ? 0 : -1) : Array.FindIndex(result.Animes, x => Json.Text(x, "Id") == Json.Text(result.Recommended, "AnimeId"));
            state.Episodes = result.Episodes.Where(x => state.SourceIndex >= 0 && Json.Text(x, "AnimeId") == Json.Text(result.Animes[state.SourceIndex], "Id")).Cast<object>().ToArray();
            state.EpisodeIndex = result.Recommended == null ? -1 : Array.FindIndex(state.Episodes, x => Json.Text((Dictionary<string, object>)x, "Id") == Json.Text(result.Recommended, "Id"));
            if (state.SourceIndex >= 0) await GetEpisodes();
            if (result.Recommended != null)
            {
                int identified = Array.FindIndex(state.Episodes, x => Json.Text((Dictionary<string, object>)x, "Id") == Json.Text(result.Recommended, "Id"));
                if (identified >= 0) { state.EpisodeIndex = identified; var full = (Dictionary<string, object>)state.Episodes[identified]; full["Shift"] = Json.Text(result.Recommended, "Shift"); full["MatchMethod"] = result.Method; }
            }
            state.Status = result.Status + " 请核对作品和集数后下载。"; controller.Publish();
        }
        Dictionary<string, object> Source()
        { if (state.SourceIndex < 0 || state.SourceIndex >= state.Sources.Length) throw new InvalidOperationException("请先选择作品。"); return (Dictionary<string, object>)state.Sources[state.SourceIndex]; }
        async Task GetEpisodes()
        {
            state.Episodes = await controller.Gateway.Catalog.Episodes(Source()); state.EpisodeIndex = state.Episodes.Length == 1 ? 0 : -1;
            if (localTarget != null && state.Item == null)
            {
                string title = Json.Text(Source(), "Name"); int sourceSeason = SmartMatching.SeasonTitle(title);
                var targets = localTarget.Items.Cast<Choice>().Where(x => SmartMatching.Episode(x.Data) > 0 && (sourceSeason == 0 || SmartMatching.Season(x.Data) == sourceSeason) && SmartMatching.Score(title, new Dictionary<string, object> { { "Name", MediaNames.SearchTitle(x.Data) } }, 0) >= 90).ToArray();
                if (targets.Length == 1) localTarget.SelectedItem = targets[0];
            }
            int number = state.Item == null ? 0 : SmartMatching.Episode(state.Item);
            var matches = state.Episodes.Select((x, i) => new { Data = (Dictionary<string, object>)x, Index = i }).Where(x => number > 0 && SmartMatching.RemoteNumber(x.Data) == number).ToArray(); if (matches.Length == 1) state.EpisodeIndex = matches[0].Index;
            state.Status = "读取到 " + state.Episodes.Length + " 集，请核对本地影片对应的集数。"; controller.Publish();
        }
        async Task Download()
        {
            if (state.Item == null) throw new InvalidOperationException("请先选择本地视频。"); Source();
            if (state.EpisodeIndex < 0 || state.EpisodeIndex >= state.Episodes.Length) throw new InvalidOperationException("请先选择对应集数。");
            var episode = (Dictionary<string, object>)state.Episodes[state.EpisodeIndex];
            await controller.DownloadSingle(state.Item, episode);
            state.Status = "弹幕已就绪。下载结果可在「下载任务」查看。"; Log.Write(state.Status);
        }
        async Task DownloadGroup(bool selectedOnly)
        {
            if (state.Item == null || Json.Text(state.Item, "Type") == "Movie") throw new InvalidOperationException("整季下载用于本地番剧视频。");
            var source = Source(); if (state.Episodes.Length == 0) await GetEpisodes();
            if (selectedOnly) MediaLibrary.RequireSameSeason(state.Selected);
            var local = selectedOnly ? state.Selected : controller.Library.Entries.Select(x => x.Item).Where(x => SmartMatching.SameSeason(state.Item, x)).ToArray();
            var plan = BatchMatching.Plan(local, state.Episodes.Cast<Dictionary<string, object>>()); if (plan.Count == 0) throw new InvalidOperationException("没有找到同季本地视频。");
            controller.BatchPlan = plan; controller.BatchKeep = false; controller.BatchTitle = (selectedOnly ? "已选影片 · " : "整季 · ") + Json.Text(source, "Name") + " · " + Json.Text(source, "Site"); controller.BatchStatus = "可匹配 " + plan.Count(x => x.Selected) + " 集，共 " + plan.Count + " 个本地文件。";
            if (!closed && controller.Window != null) showPreview();
        }
        public void Dispose()
        {
            if (closed) return; closed = true; controller.Changed -= Render; keyword.Dispose();
            foreach (var detach in unhook) detach(); unhook.Clear(); Ui.ReleaseVisualTree(this);
            foreach (TabItem tab in scopes.Items) tab.Content = null;
            sources.ItemsSource = episodes.ItemsSource = null; input.Children.Clear(); scopes.Items.Clear(); commands.Clear();
            Children.Clear(); if (!controller.ReleasingWindow) state.Open = false;
        }
    }
    public sealed class BatchView : Grid, IDisposable
    {
        Grid Body { get { return this; } }
        readonly DesktopController controller;
        readonly DataGrid grid;
        readonly TextBlock status;
        readonly CheckBox keep;
        readonly Button start, stop, retry, resume;
        readonly ProgressBar progress;
        readonly TextBlock heading;
        List<BatchEntry> renderedPlan;
        bool closed;
        public BatchView(DesktopController controller)
        {
            this.controller = controller;
            Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Body.RowDefinitions.Add(new RowDefinition()); Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            heading = Ui.Text(controller.BatchTitle + "\n请核对对应关系。无法判断或重复的集数会跳过。", "Note"); Body.Children.Add(heading);
            grid = new DataGrid { ItemsSource = controller.BatchPlan.Select(x => new BatchRow { Entry = x }).ToArray() };
            renderedPlan = controller.BatchPlan;
            grid.Columns.Add(new DataGridTemplateColumn { Header = "下载", CellTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><CheckBox IsChecked='{Binding Selected,Mode=TwoWay,UpdateSourceTrigger=PropertyChanged}' HorizontalAlignment='Center' Margin='0'/></DataTemplate>"), Width = 62 });
            string[] headers = { "集号", "本地文件", "在线集数", "状态" }, properties = { "Number", "Local", "Remote", "Status" }; double[] widths = { 60, 2, 1.6, 1.4 };
            for (int i = 0; i < headers.Length; i++) grid.Columns.Add(new DataGridTextColumn { Header = headers[i], Binding = new Binding(properties[i]), Width = new DataGridLength(widths[i], i == 0 ? DataGridLengthUnitType.Pixel : DataGridLengthUnitType.Star), IsReadOnly = true, MinWidth = i == 0 ? 55 : 130 });
            Grid.SetRow(grid, 1); Body.Children.Add(grid);
            keep = Ui.Check("保留已有 XML（取消勾选将直接覆盖）", controller.BatchKeep); keep.Checked += (s, e) => controller.BatchKeep = true; keep.Unchecked += (s, e) => controller.BatchKeep = false;
            // The view must not await the job: its async state machine would retain the
            // complete window during a long batch after the main window enters the tray.
            start = Ui.Button("开始全部下载", () => { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); controller.StartBatch(); }, true);
            stop = Ui.Button("停止下载", controller.CancelBatch);
            retry = Ui.Button("仅重试失败项", () => controller.RetryBatch(true)); resume = Ui.Button("继续未完成", () => controller.RetryBatch(false));
            var actions = Ui.Row(keep, start, stop, retry, resume); actions.Margin = new Thickness(0, 16, 0, 0); Grid.SetRow(actions, 2); Body.Children.Add(actions);
            status = Ui.Text(controller.BatchStatus, "Note"); progress = new ProgressBar { Height = 5, Maximum = 1 };
            var feedback = Ui.Stack(progress, status); Grid.SetRow(feedback, 3); Body.Children.Add(feedback);
            controller.Changed += Render;
            Render();
        }
        void Render()
        {
            if (closed) return; if (!Object.ReferenceEquals(renderedPlan, controller.BatchPlan)) { renderedPlan = controller.BatchPlan; grid.ItemsSource = renderedPlan.Select(x => new BatchRow { Entry = x }).ToArray(); }
            bool idle = !controller.BatchRunning && !controller.Busy;
            heading.Text = controller.BatchTitle + "\n请核对对应关系。无法判断或重复的集数会跳过。";
            grid.IsEnabled = keep.IsEnabled = start.IsEnabled = idle; stop.IsEnabled = controller.BatchRunning && !controller.Preparing;
            retry.IsEnabled = idle && renderedPlan.Any(x => x.Remote != null && (x.Status.Contains("失败") || x.Status.Contains("超时")));
            resume.IsEnabled = idle && renderedPlan.Any(x => x.Remote != null && !x.Status.StartsWith("已保存") && x.Status != "保留已有 XML");
            var selected = renderedPlan.Where(x => x.Selected && x.Remote != null).ToArray();
            progress.Value = selected.Length == 0 ? 0 : selected.Count(x => x.Status.StartsWith("已保存") || x.Status == "保留已有 XML" || x.Status.Contains("失败") || x.Status == "没有弹幕") / (double)selected.Length;
            status.Text = controller.BatchStatus; foreach (BatchRow row in grid.Items) row.Notify();
        }
        public void Dispose() { if (closed) return; closed = true; controller.Changed -= Render; grid.ItemsSource = null; Ui.ReleaseVisualTree(this); Children.Clear(); }
    }
}
