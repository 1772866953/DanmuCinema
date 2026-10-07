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
    public class DialogWindow : Window
    {
        protected readonly Grid Body;
        public DialogWindow(string title, double width, double height)
        {
            Title = title; Width = width; Height = height; MinWidth = 760; MinHeight = 540; WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
            Style = (Style)Application.Current.FindResource(typeof(Window));
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 48, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
            ShowInTaskbar = false;
            var outer = new Grid { Background = (Brush)Ui.Resource("Canvas") }; outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) }); outer.RowDefinitions.Add(new RowDefinition());
            var atmosphere = Ui.Atmosphere(); Grid.SetRowSpan(atmosphere, 2); outer.Children.Add(atmosphere);
            var header = new Grid { Background = (Brush)Ui.Resource("Surface") }; header.Children.Add(new TextBlock { Text = title, Margin = new Thickness(22, 0, 60, 0), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
            var accent = Ui.AccentLine(); accent.Margin = new Thickness(22, 0, 0, 0); accent.VerticalAlignment = VerticalAlignment.Bottom; header.Children.Add(accent);
            var close = Ui.Button("", Close); Ui.ConfigureCaption(close, "close"); close.HorizontalAlignment = HorizontalAlignment.Right; WindowChrome.SetIsHitTestVisibleInChrome(close, true); header.Children.Add(close); outer.Children.Add(header);
            Body = new Grid { Margin = new Thickness(22) }; Grid.SetRow(Body, 1); outer.Children.Add(Body);
            Content = new Border { Child = outer, BorderBrush = (Brush)Ui.Resource("Line"), BorderThickness = new Thickness(1) };
            SourceInitialized += (s, e) => Ui.EnableWindowTransitions(this);
            Loaded += (s, e) => { if (Owner != null) Icon = Owner.Icon; Ui.Animate(Body); Ui.AnimateAccent(accent); };
            Window parent = null; bool returnFocus = false;
            Closing += (s, e) => { parent = Owner; returnFocus = Ui.IsForeground(this); };
            Closed += (s, e) =>
            {
                Ui.StopAnimations(Body); Body.Children.Clear(); Content = null;
                // Wait for native destruction/owned-window activation to unwind.
                // Capture the direct parent before WPF detaches ownership.
                Window target = parent; bool activate = returnFocus; parent = null;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => Ui.RestoreDialogOwner(target, activate)));
            };
        }
    }
    public sealed class AlertWindow : DialogWindow
    {
        public AlertWindow(string title, string message, bool confirm, string acceptLabel = "确定退出", string cancelLabel = "继续运行") : base(title, 560, 320)
        {
            MinWidth = 460; MinHeight = 250; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.Height; ShowInTaskbar = false;
            var accent = Ui.AccentLine();
            var heading = Ui.Text(title, "Heading"); heading.Margin = new Thickness(0, 12, 0, 8);
            var content = Ui.Text(message); content.LineHeight = 25; content.Margin = new Thickness(0, 0, 0, 22);
            var scroll = new ScrollViewer { Content = content, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var accept = Ui.Button(confirm ? acceptLabel : "知道了", () => DialogResult = true, true);
            accept.IsDefault = !confirm;
            var actions = Ui.Row(); actions.HorizontalAlignment = HorizontalAlignment.Right;
            if (confirm) { var cancel = Ui.Button(cancelLabel, () => DialogResult = false); cancel.IsCancel = true; actions.Children.Add(cancel); Loaded += (s, e) => cancel.Focus(); }
            else { accept.IsCancel = true; Loaded += (s, e) => accept.Focus(); }
            actions.Children.Add(accept); Body.Children.Add(Ui.Stack(accent, heading, scroll, actions));
            Loaded += (s, e) => Ui.AnimateAccent(accent);
        }
        public static bool Show(Window owner, string title, string message, bool confirm, string acceptLabel = "确定退出", string cancelLabel = "继续运行")
        {
            // Keep validation above the active child dialog, and tie its lifetime to it.
            var active = Application.Current.Windows.OfType<Window>().LastOrDefault(x => x.IsActive && x.IsVisible);
            var dialog = new AlertWindow(title, message, confirm, acceptLabel, cancelLabel); dialog.Owner = active ?? owner;
            return dialog.ShowDialog() == true;
        }
    }
    public sealed class SourcesWindow : DialogWindow
    {
        public SourcesWindow(DesktopController controller) : base("弹幕来源 · 联合搜索", 880, 680)
        {
            var settings = controller.Settings;
            var animeko = Ui.Check("Animeko 公益弹幕（Bangumi 动漫目录）", settings.EnableAnimeko);
            var bahamut = Ui.Check("巴哈姆特动画疯（繁简体名称搜索）", settings.EnableBahamut);
            var existing = Ui.Check("保留现有平台来源（B 站、爱奇艺、优酷等）", settings.EnableExistingDanmu);
            var only = Ui.Check("iPad 搜索默认只看动漫", settings.AnimeOnly);
            var dandan = Ui.Check("弹弹play 官方 API", settings.EnableDandan);
            var custom = Ui.Input(SettingsStore.Unprotect(settings.EncryptedAdditionalApis), Double.NaN); custom.AcceptsReturn = true; custom.TextWrapping = TextWrapping.NoWrap; custom.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; custom.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; custom.Height = 125;
            var status = Ui.Text("", "Note");
            var save = Ui.Button("保存并立即启用", () =>
            {
                try
                {
                    if (controller.Busy || controller.BatchRunning) throw new InvalidOperationException("请等待当前任务完成后修改接口。");
                    DanmuCatalog.ValidateAdditionalApis(custom.Text);
                    var before = Json.Read<AppSettings>(Json.Write(settings));
                    try { settings.EnableAnimeko = animeko.IsChecked == true; settings.EnableBahamut = bahamut.IsChecked == true; settings.EnableExistingDanmu = existing.IsChecked == true; settings.EnableDandan = dandan.IsChecked == true; settings.AnimeOnly = only.IsChecked == true; settings.EncryptedAdditionalApis = SettingsStore.Protect(custom.Text.Trim()); SettingsStore.Save(settings); }
                    catch { settings.EnableAnimeko = before.EnableAnimeko; settings.EnableBahamut = before.EnableBahamut; settings.EnableExistingDanmu = before.EnableExistingDanmu; settings.EnableDandan = before.EnableDandan; settings.AnimeOnly = before.AnimeOnly; settings.EncryptedAdditionalApis = before.EncryptedAdditionalApis; throw; }
                    Log.Write("弹幕来源已保存，下次搜索立即使用新来源。"); Close();
                }
                catch (Exception e) { status.Text = e.Message; }
            }, true);
            var dock = new DockPanel(); var footer = Ui.Stack(status, Ui.Row(save)); DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
            dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Ui.Stack(Ui.Text("勾选的来源会同时查询；单个来源失败不会影响其他结果。", "Note"),
                Ui.Card("接口服务", animeko, bahamut, existing, dandan, only),
                Ui.Card("自定义兼容 API", Ui.Text("最多 5 个，每行：来源名称|API 根地址", "Note"), custom, Ui.Text("兼容 /api/v2/search/anime、/bangumi/{id}、/comment/{id}。配置内容使用 Windows 加密保存。", "Note"))) }); Body.Children.Add(dock);
        }
    }
    public sealed class Choice
    {
        public Dictionary<string, object> Data { get; set; }
        public string Label { get; set; }
        public override string ToString() { return Label; }
    }
    public sealed class MatchWindow : DialogWindow
    {
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
        object[] renderedSources, renderedEpisodes;
        bool synchronizing, closed;
        public MatchWindow(DesktopController controller, ShellWindow shell, MatchState state, bool autoSearch) : base("选择弹幕来源" + (state.Item == null ? "" : " · " + Json.Text(state.Item, "Name")), 1080, 790)
        {
            this.controller = controller; this.state = state;
            if (state.Selected == null) state.Selected = new Dictionary<string, object>[0];
            Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Body.RowDefinitions.Add(new RowDefinition()); Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var choices = new List<Choice> { new Choice { Label = "全部已启用接口（联合搜索）", Data = new Dictionary<string, object> { { "Id", "" } } } };
            choices.AddRange(controller.Gateway.Catalog.SourceChoices().Select(x => new Choice { Data = x, Label = Json.Text(x, "Name") }));
            service = new ComboBox { Width = 300, ItemsSource = choices, SelectedIndex = Math.Max(0, choices.FindIndex(x => Json.Text(x.Data, "Id") == (state.ServiceId ?? ""))) };
            keyword = new HistoryInput("danmu", state.Keyword); keyword.Chosen += SearchClicked;
            anime = Ui.Check("只看动漫", state.AnimeOnly); smart = Ui.Check("智能搜索匹配", state.Smart);
            keyword.Editor.TextChanged += (s, e) => state.Keyword = keyword.Editor.Text;
            keyword.Editor.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) { SearchClicked(); e.Handled = true; } };
            anime.Checked += (s, e) => state.AnimeOnly = true; anime.Unchecked += (s, e) => state.AnimeOnly = false;
            smart.Checked += (s, e) => state.Smart = true; smart.Unchecked += (s, e) => state.Smart = false;
            input = Ui.Stack(Ui.Row(Ui.Label("接口服务"), service, Command("识别本地文件", Identify), Command("读取作品集数", GetEpisodes)), Ui.Row(keyword, Command("搜索在线弹幕", Search, true), anime, smart)); Body.Children.Add(input);
            sources = new ListBox(); episodes = new ListBox();
            var columns = new Grid { Margin = new Thickness(0, 6, 0, 16) }; columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); columns.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new DockPanel(); var leftLabel = Ui.Text("01  选择作品和来源", "Heading"); DockPanel.SetDock(leftLabel, Dock.Top); left.Children.Add(leftLabel); left.Children.Add(sources);
            var right = new DockPanel(); var rightLabel = Ui.Text("02  选择对应集数", "Heading"); DockPanel.SetDock(rightLabel, Dock.Top); right.Children.Add(rightLabel); right.Children.Add(episodes);
            columns.Children.Add(left); Grid.SetColumn(right, 2); columns.Children.Add(right); Grid.SetRow(columns, 1); Body.Children.Add(columns);
            var itemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding Label}' TextWrapping='Wrap'/></DataTemplate>"); sources.ItemTemplate = episodes.ItemTemplate = itemTemplate;
            sources.SelectionChanged += async (s, e) => { if (synchronizing) return; state.SourceIndex = sources.SelectedIndex; state.Episodes = new object[0]; state.EpisodeIndex = -1; Render(); if (state.SourceIndex >= 0) await Work(GetEpisodes); };
            episodes.SelectionChanged += (s, e) => { if (!synchronizing) state.EpisodeIndex = episodes.SelectedIndex; };
            scopes = new TabControl { Margin = new Thickness(0, 0, 0, 4) };
            AddScope("单集", DanmuMatchScope.Single, Command("下载并关联单集", Download, true), "只更新当前影片，已有同名 XML 将直接覆盖。");
            if (state.Item == null || Json.Text(state.Item, "Type") != "Movie") AddScope("整个季度", DanmuMatchScope.Season, Command("预览整季并全部下载", () => DownloadGroup(false), true), "按集号匹配本地整季文件。先预览，无法识别或重复的集数会跳过。");
            if (state.Selected.Length > 1) AddScope("已选 " + state.Selected.Length + " 个影片", DanmuMatchScope.Selection, Command("预览已选影片并下载", () => DownloadGroup(true), true), "只更新已勾选的同番同季影片。");
            foreach (TabItem tab in scopes.Items) if ((DanmuMatchScope)tab.Tag == state.Scope) scopes.SelectedItem = tab;
            if (state.Item == null)
            {
                var targets = controller.Library.Entries.Where(x => x.Type != "Movie").GroupBy(MediaLibrary.DirectoryOf, StringComparer.OrdinalIgnoreCase).Select(x => new Choice { Label = x.Key, Data = x.First().Item }).ToArray();
                localTarget = new ComboBox { ItemsSource = targets, SelectedIndex = -1, MinWidth = 350, MaxWidth = 760 };
                localTarget.SelectionChanged += (s, e) => { var choice = localTarget.SelectedItem as Choice; state.Item = choice == null ? null : choice.Data; };
                input.Children.Add(Ui.Row(Ui.Label("本地季度"), localTarget)); scopes.SelectedIndex = 1; state.Scope = DanmuMatchScope.Season;
            }
            scopes.SelectionChanged += (s, e) => { var tab = scopes.SelectedItem as TabItem; if (tab != null) { state.Scope = (DanmuMatchScope)tab.Tag; Ui.Animate((FrameworkElement)tab.Content); } };
            Grid.SetRow(scopes, 2); Body.Children.Add(scopes); status = Ui.Text(state.Status, "Note"); Grid.SetRow(status, 3); Body.Children.Add(status);
            service.SelectionChanged += async (s, e) => { state.ServiceId = service.SelectedItem == null ? null : Json.Text(((Choice)service.SelectedItem).Data, "Id"); if (!closed && IsVisible && !controller.Busy && !String.IsNullOrWhiteSpace(state.Keyword)) await Work(Search); };
            controller.Changed += Render;
            Closed += (s, e) => { closed = true; controller.Changed -= Render; keyword.Dispose(); sources.ItemsSource = episodes.ItemsSource = null; input.Children.Clear(); scopes.Items.Clear(); commands.Clear(); if (!controller.ReleasingWindow) state.Open = false; };
            Render();
            if (autoSearch) Loaded += async (s, e) => { if (state.Item != null && (String.IsNullOrEmpty(state.ServiceId) || state.ServiceId == "dandan")) await Work(IdentifyThenSearch); else if (!String.IsNullOrWhiteSpace(state.Keyword)) await Work(Search); };
        }
        Button Command(string text, Func<Task> action, bool primary = false) { var button = Ui.Button(text, async () => await Work(action), primary); commands.Add(button); return button; }
        async void SearchClicked() { await Work(Search); }
        async Task Work(Func<Task> action) { if (controller.Busy || controller.BatchRunning || closed) return; keyword.CommitSearch(); await controller.Execute(action); }
        void AddScope(string name, DanmuMatchScope scope, Button command, string hint) { scopes.Items.Add(new TabItem { Header = name, Tag = scope, Content = Ui.Stack(Ui.Row(command), Ui.Text(hint, "Note")), Padding = new Thickness(12, 8, 12, 8) }); }
        void Render()
        {
            if (closed) return;
            bool enabled = !controller.Busy && !controller.Loading && !controller.BatchRunning;
            input.IsEnabled = sources.IsEnabled = episodes.IsEnabled = enabled; foreach (var button in commands) button.IsEnabled = enabled;
            synchronizing = true;
            try
            {
                if (!Object.ReferenceEquals(renderedSources, state.Sources)) { sources.ItemsSource = MakeSources(); renderedSources = state.Sources; }
                if (!Object.ReferenceEquals(renderedEpisodes, state.Episodes)) { episodes.ItemsSource = MakeEpisodes(); renderedEpisodes = state.Episodes; }
                sources.SelectedIndex = state.SourceIndex; episodes.SelectedIndex = state.EpisodeIndex;
            }
            finally { synchronizing = false; }
            status.Text = state.Status;
        }
        Choice[] MakeSources()
        {
            int season = state.Item == null ? SmartMatching.SeasonTitle(state.Keyword) : SmartMatching.Season(state.Item);
            return state.Sources.OfType<Dictionary<string, object>>().Select(data => new Choice { Data = data, Label = (state.Smart ? "[" + Math.Round(SmartMatching.Score(state.Keyword, data, season)) + "%] " : "") + Json.Text(data, "Name") + " · " + Json.Text(data, "Year") + " · " + Json.Text(data, "Site") + (Json.Text(data, "Provider") == "animeko" ? "（目录候选）" : "") }).ToArray();
        }
        Choice[] MakeEpisodes() { return state.Episodes.OfType<Dictionary<string, object>>().Select(data => new Choice { Data = data, Label = Json.Text(data, "Number") + "  " + Json.Text(data, "Title") }).ToArray(); }
        async Task Search()
        {
            if (String.IsNullOrWhiteSpace(state.Keyword)) throw new InvalidOperationException("请输入作品名。");
            state.Sources = state.Episodes = new object[0]; state.SourceIndex = state.EpisodeIndex = -1; state.Status = "正在联合查询在线弹幕来源…"; controller.Publish();
            int season = state.Item == null ? SmartMatching.SeasonTitle(state.Keyword) : SmartMatching.Season(state.Item);
            var result = await controller.Gateway.Catalog.Search(state.Keyword.Trim(), state.AnimeOnly, state.Smart, season, String.IsNullOrEmpty(state.ServiceId) ? null : state.ServiceId);
            state.Sources = result.Items; state.Status = "显示 " + result.Items.Length + " 个候选" + (result.HiddenCount > 0 ? "，隐藏 " + result.HiddenCount + " 个非动漫候选" : "") + "。请核对季度和集数。\n" + result.Summary; controller.Publish();
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
            var episode = (Dictionary<string, object>)state.Episodes[state.EpisodeIndex]; string video = Json.Text(state.Item, "Path");
            if (!File.Exists(video)) throw new InvalidOperationException("本地视频文件不存在。");
            string content = await controller.Gateway.Catalog.Download(episode); int count = DanmuCatalog.ParseXml(content).GetElementsByTagName("d").Count;
            if (count == 0) throw new InvalidOperationException("这集没有可用弹幕，已有文件未更改。");
            SettingsStore.AtomicWrite(Path.ChangeExtension(video, ".xml"), content, false); controller.Gateway.Catalog.RecordAssociation(state.Item, episode);
            state.Status = "已保存 " + count + " 条弹幕。重新打开影片即可读取；时间偏差可在播放器调整。"; Log.Write(state.Status); await controller.RefreshMetadata();
        }
        async Task DownloadGroup(bool selectedOnly)
        {
            if (state.Item == null || Json.Text(state.Item, "Type") == "Movie") throw new InvalidOperationException("整季下载用于本地番剧视频。");
            var source = Source(); if (state.Episodes.Length == 0) await GetEpisodes();
            if (selectedOnly) MediaLibrary.RequireSameSeason(state.Selected);
            var local = selectedOnly ? state.Selected : controller.Library.Entries.Select(x => x.Item).Where(x => SmartMatching.SameSeason(state.Item, x)).ToArray();
            var plan = BatchMatching.Plan(local, state.Episodes.Cast<Dictionary<string, object>>()); if (plan.Count == 0) throw new InvalidOperationException("没有找到同季本地视频。");
            controller.BatchPlan = plan; controller.BatchKeep = false; controller.BatchTitle = (selectedOnly ? "已选影片 · " : "整季 · ") + Json.Text(source, "Name") + " · " + Json.Text(source, "Site"); controller.BatchStatus = "可匹配 " + plan.Count(x => x.Selected) + " 集，共 " + plan.Count + " 个本地文件。";
            if (controller.Window != null) controller.Window.ShowBatch(this);
        }
    }
    public sealed class BatchRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void Notify() { var handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(null)); }
        public BatchEntry Entry;
        public bool Selected { get { return Entry.Selected; } set { Entry.Selected = Entry.Remote != null && value; Notify(); } }
        public string Number { get { return Entry.Number > 0 ? Entry.Number.ToString() : "?"; } }
        public string Local { get { return Path.GetFileName(Json.Text(Entry.Local, "Path")); } }
        public string Remote { get { return Entry.Remote == null ? "—" : Json.Text(Entry.Remote, "Number") + " " + Json.Text(Entry.Remote, "Title"); } }
        public string Status { get { return Entry.Status; } }
    }
    public sealed class BatchWindow : DialogWindow
    {
        readonly DesktopController controller;
        readonly DataGrid grid;
        readonly TextBlock status;
        readonly CheckBox keep;
        readonly Button start, stop;
        List<BatchEntry> renderedPlan;
        bool closed;
        public BatchWindow(DesktopController controller) : base("全部下载 · " + controller.BatchTitle, 1080, 680)
        {
            this.controller = controller;
            Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Body.RowDefinitions.Add(new RowDefinition()); Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Body.Children.Add(Ui.Text("请核对下表。按集号关联本地同季文件；无法判断或重复的集数会跳过。", "Note"));
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
            var actions = Ui.Row(keep, start, stop); actions.Margin = new Thickness(0, 16, 0, 0); Grid.SetRow(actions, 2); Body.Children.Add(actions);
            status = Ui.Text(controller.BatchStatus, "Note"); Grid.SetRow(status, 3); Body.Children.Add(status);
            controller.Changed += Render; Closed += (s, e) => { closed = true; controller.Changed -= Render; grid.ItemsSource = null; };
            Render();
        }
        void Render() { if (closed) return; if (!Object.ReferenceEquals(renderedPlan, controller.BatchPlan)) { renderedPlan = controller.BatchPlan; grid.ItemsSource = renderedPlan.Select(x => new BatchRow { Entry = x }).ToArray(); Title = "全部下载 · " + controller.BatchTitle; } grid.IsEnabled = keep.IsEnabled = start.IsEnabled = !controller.BatchRunning && !controller.Busy; stop.IsEnabled = controller.BatchRunning; status.Text = controller.BatchStatus; foreach (BatchRow row in grid.Items) row.Notify(); }
    }
}
