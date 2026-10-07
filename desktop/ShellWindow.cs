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
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DanmuCinema.Desktop
{
    public sealed partial class ShellWindow
    {
        public readonly Window View;
        readonly DesktopController controller;
        readonly DesktopSession session;
        ContentControl host;
        readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        readonly List<IDisposable> pageResources = new List<IDisposable>();
        readonly DispatcherTimer placementTimer;
        readonly DispatcherTimer viewTimer;
        readonly List<Window> dialogs = new List<Window>();
        public bool HasOpenDialogs { get { return WorkspaceVisible || dialogs.Any(x => x.IsVisible); } }
        WindowPlacement placement;
        bool ready, releasing, synchronizing, closingQueued;
        FrameworkElement page;
        TextBlock server, danmu, plugin, playback, count, location, scheduleStatus, countdown, scheduleTarget, batchStatus;
        TextBox logs;
        StackPanel scheduleEditor;
        Button scheduleStart, scheduleCancel, batchResume;
        ProgressBar scheduleProgress;
        LibraryEntry[] renderedEntries;
        public ShellWindow(DesktopController controller)
        {
            this.controller = controller; session = controller.Session;
            View = Ui.Load<Window>("Shell.xaml"); host = (ContentControl)View.FindName("PageHost");
            var iconFile = Path.Combine(Paths.Root, "assets", "DanmuCinema.ico");
            if (File.Exists(iconFile)) View.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(iconFile));
            var nav = (StackPanel)View.FindName("Navigation");
            string[] keys = { "overview", "library", "tasks", "connect", "setup", "settings", "schedule", "cache", "logs" };
            string[] titles = { "服务总览", "影片与弹幕", "下载任务", "连接 iPad", "首次设置", "启动与偏好", "定时任务", "缓存管理", "运行日志" };
            string[] icons = { "\uE80F", "\uE8B7", "\uE896", "\uE8EA", "\uE713", "\uE115", "\uE823", "\uE8B7", "\uE9D9" };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i]; var button = Ui.Button(titles[i], () => Navigate(key)); button.Style = (Style)Ui.Resource("Navigation");
                button.Content = Ui.Row(new TextBlock { Text = icons[i], FontFamily = new FontFamily("Segoe MDL2 Assets"), Width = 30, FontSize = 15 }, Ui.Text(titles[i]));
                navigation[key] = button; nav.Children.Add(button);
            }
            var atmosphere = Ui.Atmosphere(); Grid.SetRowSpan(atmosphere, 4); ((Grid)View.FindName("WorkArea")).Children.Insert(0, atmosphere);
            Ui.ConfigureCaption((Button)View.FindName("Minimize"), "minimize"); Ui.ConfigureCaption((Button)View.FindName("Maximize"), "maximize"); Ui.ConfigureCaption((Button)View.FindName("Close"), "close");
            ((Button)View.FindName("Minimize")).Click += (s, e) => SystemCommands.MinimizeWindow(View);
            ((Button)View.FindName("Maximize")).Click += (s, e) => { if (View.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(View); else SystemCommands.MaximizeWindow(View); };
            ((Button)View.FindName("Close")).Click += (s, e) => View.Close();
            placement = WindowPlacementStore.Load();
            placementTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) }; placementTimer.Tick += PlacementTick;
            viewTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; viewTimer.Tick += ViewTick;
            View.SourceInitialized += SourceInitialized;
            View.SizeChanged += GeometryChanged; View.LocationChanged += LocationChanged; View.StateChanged += StateChanged;
            View.Closing += OnClosing; View.PreviewKeyDown += KeyDown; View.PreviewMouseUp += MouseUp;
            controller.Changed += Render; Log.Added += LogAdded;
            Navigate(session.Page); Render();
        }
        public void Show()
        {
            if (releasing) return;
            if (!View.IsVisible) View.Show();
            if (View.WindowState == WindowState.Minimized) View.WindowState = placement != null && placement.Maximized ? WindowState.Maximized : WindowState.Normal;
            View.Activate();
            if (session.Match != null && session.Match.Open && !WorkspaceVisible) OpenMatchWindow(false);
            if (controller.BatchRunning && !WorkspaceVisible) ShowBatch();
            if (session.Page == "schedule") viewTimer.Start();
        }
        void SourceInitialized(object sender, EventArgs e)
        {
            Ui.EnableWindowTransitions(View);
            var source = (HwndSource)PresentationSource.FromVisual(View);
            var target = source.CompositionTarget;
            if (placement != null && placement.Valid)
            {
                var scale = target.TransformToDevice;
                var bounds = placement.Fit(Forms.Screen.AllScreens.Select(x => x.WorkingArea).ToArray(), new System.Drawing.Size((int)(View.MinWidth * scale.M11), (int)(View.MinHeight * scale.M22)));
                View.WindowStartupLocation = WindowStartupLocation.Manual;
                View.Left = bounds.Left / scale.M11; View.Top = bounds.Top / scale.M22; View.Width = bounds.Width / scale.M11; View.Height = bounds.Height / scale.M22;
                if (placement.Maximized) View.WindowState = WindowState.Maximized;
            }
            source.AddHook(WindowHook);
            ready = true;
        }
        IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (message == 0x319 && (WorkspaceVisible || session.Page == "library" || session.Page == "cache"))
            {
                int command = (int)((lparam.ToInt64() >> 16) & 0x7ff);
                if (command == 1 || command == 2) { NavigatePageHistory(command == 2); handled = true; return new IntPtr(1); }
            }
            return IntPtr.Zero;
        }
        void GeometryChanged(object sender, SizeChangedEventArgs e) { RememberPlacement(); }
        void LocationChanged(object sender, EventArgs e) { RememberPlacement(); }
        void StateChanged(object sender, EventArgs e)
        {
            Ui.ConfigureCaption((Button)View.FindName("Maximize"), View.WindowState == WindowState.Maximized ? "restore" : "maximize");
            // Native minimize never replaces the last usable normal/maximized state.
            RememberPlacement();
            if (View.WindowState == WindowState.Minimized) viewTimer.Stop(); else if (session.Page == "schedule") viewTimer.Start();
        }
        void RememberPlacement()
        {
            if (!ready || releasing || !View.IsVisible || View.WindowState == WindowState.Minimized) return;
            var rect = View.WindowState == WindowState.Maximized ? View.RestoreBounds : new Rect(View.Left, View.Top, View.Width, View.Height);
            if (rect.IsEmpty || Double.IsNaN(rect.Width) || rect.Width <= 0) return;
            var source = (HwndSource)PresentationSource.FromVisual(View); var scale = source.CompositionTarget.TransformToDevice;
            if (placement == null) placement = new WindowPlacement();
            placement.Capture(new System.Drawing.Rectangle((int)Math.Round(rect.Left * scale.M11), (int)Math.Round(rect.Top * scale.M22), (int)Math.Round(rect.Width * scale.M11), (int)Math.Round(rect.Height * scale.M22)), View.WindowState == WindowState.Maximized ? Forms.FormWindowState.Maximized : Forms.FormWindowState.Normal);
            placementTimer.Stop(); placementTimer.Start();
        }
        void PlacementTick(object sender, EventArgs e) { SavePlacement(); }
        void SavePlacement() { placementTimer.Stop(); try { WindowPlacementStore.Save(placement); } catch (Exception e) { Log.Write("窗口状态保存失败：" + e.Message); } }
        void OnClosing(object sender, CancelEventArgs e)
        {
            if (releasing) return;
            e.Cancel = true;
            if (controller.Closing || closingQueued) return;
            RememberPlacement(); SavePlacement();
            // Close again only after the canceled native Closing event has unwound.
            // This avoids reentrant close/show transitions for maximized windows.
            closingQueued = true;
            View.Dispatcher.BeginInvoke(new Action(async () =>
            {
                closingQueued = false;
                if (releasing || controller.Closing) return;
                if (controller.Settings.CloseToTray) controller.ReleaseWindow();
                else await controller.Exit();
            }));
        }
        public void Release()
        {
            if (releasing) return;
            RememberPlacement(); SavePlacement(); SavePageState(); releasing = true;
            viewTimer.Stop(); viewTimer.Tick -= ViewTick; placementTimer.Stop(); placementTimer.Tick -= PlacementTick;
            controller.Changed -= Render; Log.Added -= LogAdded;
            foreach (var dialog in dialogs.AsEnumerable().Reverse().ToArray()) if (dialogs.Contains(dialog)) { var animated = dialog as DialogWindow; if (animated != null) animated.CloseImmediately(); else dialog.Close(); } dialogs.Clear();
            var source = (HwndSource)PresentationSource.FromVisual(View); if (source != null) source.RemoveHook(WindowHook);
            CloseWorkspace(true); ClearPage(); Ui.ReleaseVisualTree(View);
            Keyboard.ClearFocus(); FocusManager.SetFocusedElement(View, null);
            View.Closing -= OnClosing; View.SourceInitialized -= SourceInitialized; View.SizeChanged -= GeometryChanged; View.LocationChanged -= LocationChanged; View.StateChanged -= StateChanged; View.PreviewKeyDown -= KeyDown; View.PreviewMouseUp -= MouseUp;
            View.Close(); host.Content = null; View.Content = null; navigation.Clear();
            // Runtime XAML name scopes retain every named control even after Content is cleared.
            NameScope.SetNameScope(View, null); host = null;
            selectionCanvas = null; selectionBox = null;
        }
        void KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && WorkspaceVisible) { RequestWorkspaceClose(); e.Handled = true; }
            else if (e.Key == Key.Escape && controller.Scheduler.Active) { controller.CancelSchedule(); e.Handled = true; }
            else if (TryNavigateHistory(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers)) e.Handled = true;
        }
        internal bool TryNavigateHistory(Key key, ModifierKeys modifiers)
        {
            if (!(WorkspaceVisible || session.Page == "library" || session.Page == "cache") || (modifiers & ModifierKeys.Alt) == 0 || (key != Key.Left && key != Key.Right)) return false;
            NavigatePageHistory(key == Key.Right); return true;
        }
        void MouseUp(object sender, MouseButtonEventArgs e)
        { if ((WorkspaceVisible || session.Page == "library" || session.Page == "cache") && (e.ChangedButton == MouseButton.XButton1 || e.ChangedButton == MouseButton.XButton2)) { NavigatePageHistory(e.ChangedButton == MouseButton.XButton2); e.Handled = true; } }
        void NavigatePageHistory(bool forward)
        {
            if (WorkspaceVisible) { if (forward && controller.BatchPlan != null) MountBatch(); else if (!forward && session.Match != null) MountMatch(false); else if (!forward) CloseWorkspace(false); return; }
            var cachePage = page as CachePage; if (cachePage != null) cachePage.NavigateHistory(forward); else NavigateHistory(forward);
        }
        public void Navigate(string key)
        {
            if (!navigation.ContainsKey(key)) key = "overview";
            CloseWorkspace(false);
            SavePageState(); ClearPage(); session.Page = key;
            foreach (var pair in navigation) { pair.Value.Background = Ui.Brush(pair.Key == key ? "#344668" : "#0014243A"); pair.Value.Foreground = Ui.Brush(pair.Key == key ? "#FFFFFF" : "#AEBED2"); }
            var titles = new Dictionary<string, string> { { "overview", "服务总览" }, { "library", "影片与弹幕" }, { "tasks", "下载任务" }, { "connect", "连接 iPad" }, { "setup", "首次设置" }, { "settings", "启动与偏好" }, { "schedule", "定时任务" }, { "cache", "缓存管理" }, { "logs", "运行日志" } };
            ((TextBlock)View.FindName("Heading")).Text = titles[key];
            ((TextBlock)View.FindName("Subtitle")).Text = key == "tasks" ? "提前准备、查看进度，集中处理需要确认的影片。" : key == "library" ? "浏览媒体库，选择影片，为每一集找到合适的弹幕。" : key == "schedule" ? "倒计时或指定时间，托盘中继续运行。" : key == "connect" ? "连接你的电脑，在 iPad 上原画播放。" : key == "cache" ? "先读本地数据，减少官方接口请求。" : key == "settings" ? "让启动、播放和后台运行按你的习惯工作。" : key == "setup" ? "设置账号和媒体库，开启你的家庭影院。" : "在电脑管理媒体，在 iPad 原画播放。";
            page = key == "library" ? BuildLibrary() : key == "tasks" ? BuildTasks() : key == "overview" ? BuildOverview() : key == "connect" ? BuildConnect() : key == "setup" ? BuildSetup() : key == "settings" ? BuildSettings() : key == "schedule" ? BuildSchedule() : key == "cache" ? BuildCache() : BuildLogs();
            host.Content = page; Ui.Animate(host); Ui.AnimateAccent((Border)View.FindName("PageAccent")); Render();
            if (key == "schedule") viewTimer.Start(); else viewTimer.Stop();
            double offset;
            if (session.ScrollOffsets.TryGetValue(key, out offset)) page.Loaded += (s, e) => { var scroll = Ui.Child<ScrollViewer>((DependencyObject)s); if (scroll != null) scroll.ScrollToVerticalOffset(offset); };
            if (key == "library") { var ignored = controller.UpdateStatus(); }
        }
        void SavePageState()
        {
            var scroll = page == null ? null : Ui.Child<ScrollViewer>(page); if (scroll != null) session.ScrollOffsets[session.Page] = scroll.VerticalOffset;
            if (grid != null) { var viewer = Ui.Child<ScrollViewer>(grid); if (viewer != null) session.ScrollOffsets["library-grid"] = viewer.VerticalOffset; }
        }
        void ClearPage()
        {
            StopFolderCovers(); bulkActions = null;
            if (libraryMenu != null) { libraryMenu.IsOpen = false; libraryMenu.Items.Clear(); libraryMenu.PlacementTarget = null; libraryMenu.DataContext = null; libraryMenu = null; }
            foreach (var resource in pageResources) resource.Dispose(); pageResources.Clear();
            if (page != null) Ui.ReleaseVisualTree(page); page = null; host.Content = null; renderedEntries = null;
            server = danmu = plugin = playback = count = location = scheduleStatus = countdown = scheduleTarget = batchStatus = null;
            logs = null; grid = null; rows = null; all = null; librarySearch = null; scheduleEditor = null; scheduleStart = scheduleCancel = batchResume = null; scheduleProgress = null;
        }
        ScrollViewer Scroll(params UIElement[] children) { return new ScrollViewer { Content = Ui.Stack(children), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; }
        Button Command(string text, Func<Task> action, bool primary = false)
        { return Ui.Button(text, async () => { if (!controller.BatchRunning) await controller.Execute(action); }, primary); }
        Task Done() { return Task.FromResult(0); }
        TextBox DraftText(string key, string value, double width = 260)
        {
            object saved; var box = Ui.Input(session.Draft.TryGetValue(key, out saved) ? (string)saved : value, width);
            box.TextChanged += (s, e) => session.Draft[key] = box.Text; return box;
        }
        CheckBox DraftCheck(string key, string text, bool value)
        {
            object saved; var check = Ui.Check(text, session.Draft.TryGetValue(key, out saved) ? (bool)saved : value);
            check.Checked += (s, e) => session.Draft[key] = true; check.Unchecked += (s, e) => session.Draft[key] = false; return check;
        }
        ComboBox DraftCombo(string key, string[] values, int value, double width = 240)
        {
            object saved; var combo = Ui.Combo(values, session.Draft.TryGetValue(key, out saved) ? (int)saved : value, width);
            combo.SelectionChanged += (s, e) => session.Draft[key] = combo.SelectedIndex; return combo;
        }
        FrameworkElement BuildOverview()
        {
            server = Ui.Text(""); danmu = Ui.Text(""); plugin = Ui.Text(""); playback = Ui.Text("");
            var state = new Grid(); state.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); state.ColumnDefinitions.Add(new ColumnDefinition());
            string[] labels = { "视频服务器", "弹幕接口", "弹幕插件", "播放会话" }; TextBlock[] values = { server, danmu, plugin, playback };
            for (int i = 0; i < labels.Length; i++) { state.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); var label = Ui.Label(labels[i]); values[i].Margin = new Thickness(0, 0, 0, 16); Grid.SetRow(label, i); Grid.SetRow(values[i], i); Grid.SetColumn(values[i], 1); state.Children.Add(label); state.Children.Add(values[i]); }
            return Scroll(Ui.Card("服务状态", state, Ui.Row(Command("启动服务", controller.StartAll, true), Command("停止服务", controller.StopAll), Ui.Button("打开媒体库", () => DesktopController.Open(controller.LocalUrl + "/web/")), Command("扫描媒体库", controller.ScanLibrary))),
                Ui.Card("开始使用", Ui.Text("01   在「首次设置」创建账号、添加视频目录。\n02   在「连接 iPad」复制服务器地址。\n03   在「影片与弹幕」选择来源，匹配并下载弹幕。", "Note")),
                Ui.Text("视频由 Jellyfin 直接传输。关闭到托盘后，播放服务、下载和定时任务继续运行。", "Note"));
        }
        FrameworkElement BuildConnect()
        {
            var network = Ui.Combo(NetworkInfo.Addresses(), 0, 230); if (network.Items.Contains(session.Ip)) network.SelectedItem = session.Ip;
            var video = Ui.Input("", Double.NaN); video.IsReadOnly = true; video.FontFamily = new FontFamily("Consolas");
            var comments = Ui.Input("", Double.NaN); comments.IsReadOnly = true; comments.FontFamily = new FontFamily("Consolas");
            Action update = () => { session.Ip = network.SelectedItem as string ?? "电脑局域网IP"; video.Text = "http://" + session.Ip + ":" + controller.Settings.Port; comments.Text = "http://" + session.Ip + ":" + controller.Settings.DanmuPort + "/" + controller.Gateway.Key; };
            network.SelectionChanged += (s, e) => update(); update();
            return Scroll(Ui.Row(Ui.Label("局域网地址"), network, Ui.Button("刷新地址", () => { network.ItemsSource = NetworkInfo.Addresses(); network.SelectedIndex = 0; update(); })),
                Ui.Card("视频服务器地址", video, Ui.Row(Ui.Button("复制地址", () => Clipboard.SetText(video.Text)))),
                Ui.Card("自定义弹幕 API", comments, Ui.Row(Ui.Button("复制弹幕地址", () => Clipboard.SetText(comments.Text)))),
                Ui.Card("在 iPad 上连接", Ui.Text("SenPlayer / Filebar：添加服务器 → Jellyfin → 输入视频服务器地址和你创建的账号。\nSenPlayer：设置 → 弹幕设置 → 自定义弹幕 API，填入上面的弹幕地址。\nFilebar：优先使用媒体服务器弹幕，也可尝试自定义弹幕服务器。", "Note"), Ui.Row(Command("配置局域网防火墙", () => controller.RunScript("configure-firewall.ps1", true), true), Ui.Button("打开连接说明", () => DesktopController.Open(Path.Combine(Paths.Root, "README.md"))))),
                Ui.Text("电脑和 iPad 应连接同一路由器。多个地址时，请选择实际连接路由器的网卡地址。", "Note"));
        }
        FrameworkElement BuildSetup()
        {
            var name = DraftText("admin", controller.Settings.AdminName, 200); var password = new PasswordBox { Width = 230 }; System.Windows.Automation.AutomationProperties.SetName(password, "管理员密码");
            var folder = DraftText("folder", controller.Settings.MediaFolder, 420); var libraryName = DraftText("library-name", controller.Settings.LibraryName, 200);
            var type = DraftCombo("library-type", new[] { "电影", "电视剧 / 动画" }, controller.Settings.LibraryType == "tvshows" ? 1 : 0, 190);
            return Scroll(Ui.Row(Command("安装 / 修复运行组件", () => controller.RunScript("install-components.ps1", false), true), Command("启动服务器", controller.StartAll), Ui.Button("打开 Jellyfin 设置", () => DesktopController.Open(controller.LocalUrl + "/web/#!/dashboard"))),
                Ui.Card("管理员账号", Ui.Text("这是 iPad 连接服务器时使用的账号；密码不会保存到配置中。", "Note"), Ui.Row(Ui.Label("账号"), name, Ui.Label("密码"), password),
                    Ui.Row(Command("初始化 / 登录管理员", async () => { string user = name.Text.Trim(), secret = password.Password; if (!controller.Services.OwnsProcess) await controller.StartAll(); await controller.Services.Api.Initialize(user, secret); password.Clear(); await controller.Services.Api.SetOriginalPolicy(controller.Settings.PreferOriginal); }, true))),
                Ui.Card("视频目录", Ui.Row(folder, Ui.Button("选择目录", () => { using (var dialog = new Forms.FolderBrowserDialog { SelectedPath = folder.Text }) if (dialog.ShowDialog(new NativeOwner(View)) == Forms.DialogResult.OK) folder.Text = dialog.SelectedPath; })),
                    Ui.Row(Ui.Label("媒体库名"), libraryName, type), Ui.Row(Command("添加媒体库", async () => { string path = folder.Text.Trim(), title = libraryName.Text.Trim(), kind = type.SelectedIndex == 1 ? "tvshows" : "movies"; await controller.Services.Api.AddLibrary(path, title, kind); controller.Settings.MediaFolder = path; controller.Settings.LibraryName = title; controller.Settings.LibraryType = kind; SettingsStore.Save(controller.Settings); }, true)),
                    Ui.Text("电影和剧集建议使用不同目录，可多次添加。弹幕 XML 保存在视频旁边。", "Note")),
                Ui.Text("运行组件来自 Jellyfin 官方和开源弹幕插件，下载时校验固定版本。", "Note"));
        }
        FrameworkElement BuildSettings()
        {
            var auto = DraftCheck("auto-start", "开机启动：登录 Windows 后启动服务并留在托盘", AutoStart.Enabled);
            var launch = DraftCheck("launch", "手动打开程序时，同时启动服务", controller.Settings.StartServicesOnLaunch);
            var close = DraftCombo("close", new[] { "缩小到托盘，服务继续运行", "停止服务，然后退出" }, controller.Settings.CloseToTray ? 0 : 1, 330);
            var original = DraftCheck("original", "保留原画：禁止视频转码，允许重新封装与音频转换", controller.Settings.PreferOriginal);
            var port = DraftText("port", controller.Settings.Port.ToString(), 110); var danmuPort = DraftText("danmu-port", controller.Settings.DanmuPort.ToString(), 110);
            return Scroll(Ui.Card("启动与关闭", auto, launch, Ui.Row(Ui.Label("关闭窗口时"), close), Ui.Text("最小化按钮仍最小化到任务栏；托盘菜单的「退出并停止服务」完全退出。\n后台启动不创建主窗口；从托盘打开时恢复上次的大小和操作位置。", "Note")),
                Ui.Card("播放与连接", original, Ui.Row(Ui.Label("视频端口"), port, Ui.Label("弹幕端口"), danmuPort), Ui.Text("更改端口前请停止服务；修改后需同步更新 iPad 地址和防火墙规则。", "Note")),
                Ui.Row(Command("保存设置", async () => { int a, b; if (!Int32.TryParse(port.Text, out a) || !Int32.TryParse(danmuPort.Text, out b)) throw new ArgumentException("请输入有效端口。"); await controller.SavePreferences(a, b, close.SelectedIndex == 0, launch.IsChecked == true, original.IsChecked == true, auto.IsChecked == true); }, true), Ui.Button("打开数据目录", () => DesktopController.Open(Paths.Data))));
        }
        FrameworkElement BuildLogs()
        {
            logs = new TextBox { IsReadOnly = true, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Ui.Brush("#17273C"), Foreground = Ui.Brush("#CCE2E9"), FontFamily = new FontFamily("Consolas"), Padding = new Thickness(18), Margin = new Thickness(0) };
            if (File.Exists(Paths.LogPath)) logs.Text = String.Join(Environment.NewLine, File.ReadLines(Paths.LogPath).Reverse().Take(160).Reverse());
            var dock = new DockPanel(); var buttons = Ui.Row(Ui.Button("打开服务器日志", () => { var path = Path.Combine(Paths.Data, "server-logs"); Directory.CreateDirectory(path); DesktopController.Open(path); }), Ui.Button("打开控制台日志", () => { if (File.Exists(Paths.LogPath)) DesktopController.Open(Paths.LogPath); })); DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons); dock.Children.Add(logs); return dock;
        }
        FrameworkElement BuildSchedule()
        {
            countdown = new TextBlock { Text = "00:00:00", FontFamily = new FontFamily("Consolas"), FontSize = 40, Foreground = (Brush)Ui.Resource("Accent"), Margin = new Thickness(0, 0, 0, 6) };
            scheduleStatus = Ui.Text("", "Note"); scheduleTarget = Ui.Text("", "Note"); scheduleProgress = new ProgressBar { Height = 5, Maximum = 1, Foreground = (Brush)Ui.Resource("Accent"), Margin = new Thickness(0, 8, 0, 0) };
            scheduleStatus.Margin = scheduleTarget.Margin = new Thickness(0, 4, 0, 4);
            var mode = DraftCombo("timer-mode", new[] { "倒计时", "指定时间" }, 0, 190);
            var hours = DraftText("hours", "0", 80); var minutes = DraftText("minutes", "30", 80); var seconds = DraftText("seconds", "0", 80);
            var delayRow = Ui.Row(hours, Ui.Label("小时"), minutes, Ui.Label("分钟"), seconds, Ui.Label("秒"));
            var presets = Ui.Row(); foreach (int v in new[] { 15, 30, 60, 120 }) { int value = v; presets.Children.Add(Ui.Button(v < 60 ? v + " 分钟" : v / 60 + " 小时", () => { hours.Text = (value / 60).ToString(); minutes.Text = (value % 60).ToString(); seconds.Text = "0"; })); }
            var date = DraftText("date", DateTime.Now.AddMinutes(30).ToString("yyyy-MM-dd"), 150); var time = DraftText("time", DateTime.Now.AddMinutes(30).ToString("HH:mm:ss"), 130); var dateRow = Ui.Row(Ui.Label("执行日期"), date, Ui.Label("时间"), time);
            Action setMode = () => { delayRow.Visibility = presets.Visibility = mode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; dateRow.Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; }; mode.SelectionChanged += (s, e) => setMode(); setMode();
            var actions = DraftCombo("timer-action", Enum.GetValues(typeof(PowerAction)).Cast<PowerAction>().Select(PowerActions.Name).ToArray(), 0, 230); var description = Ui.Text(PowerActions.Description((PowerAction)actions.SelectedIndex), "Note"); actions.SelectionChanged += (s, e) => description.Text = PowerActions.Description((PowerAction)actions.SelectedIndex);
            var awake = DraftCheck("timer-awake", "定时期间阻止自动睡眠（屏幕仍可熄灭）", false);
            scheduleEditor = Ui.Stack(Ui.Row(Ui.Label("计时方式"), mode), delayRow, presets, dateRow, Ui.Row(Ui.Label("到时操作"), actions), description, awake);
            scheduleStart = Ui.Button("开始定时", () =>
            {
                try
                {
                    TimeSpan? delay = null; DateTime? target = null;
                    if (mode.SelectedIndex == 0) { int h, m, s; if (!Int32.TryParse(hours.Text, out h) || !Int32.TryParse(minutes.Text, out m) || !Int32.TryParse(seconds.Text, out s) || h < 0 || h > 720 || m < 0 || m > 59 || s < 0 || s > 59) throw new ArgumentException("请填写有效的小时、分钟和秒。"); delay = TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m) + TimeSpan.FromSeconds(s); }
                    else { DateTime local; if (!DateTime.TryParseExact(date.Text + " " + time.Text, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out local)) throw new ArgumentException("请使用 yyyy-MM-dd 和 HH:mm:ss 格式填写时间。"); target = local; }
                    controller.StartSchedule(delay, target, (PowerAction)actions.SelectedIndex, awake.IsChecked == true);
                }
                catch (Exception error) { AlertWindow.Show(View, "无法开始定时", error.Message, false); }
            }, true);
            scheduleCancel = Ui.Button("取消定时", controller.CancelSchedule);
            var dock = new DockPanel();
            var commands = Ui.Stack(Ui.Row(scheduleStart, scheduleCancel), Ui.Text("执行前 15 秒可取消或按 Esc；执行阶段开始后无法撤回。退出程序会取消任务。", "Note"));
            DockPanel.SetDock(commands, Dock.Bottom); dock.Children.Add(commands);
            dock.Children.Add(Scroll(Ui.Card("当前任务", countdown, scheduleStatus, scheduleTarget, scheduleProgress), Ui.Card("任务设置", scheduleEditor))); return dock;
        }
        void ViewTick(object sender, EventArgs e) { RenderSchedule(); }
        void RenderSchedule()
        {
            if (countdown == null) return; var scheduler = controller.Scheduler;
            bool locked = scheduler.Active || scheduler.State == ScheduleState.Executing;
            scheduleEditor.IsEnabled = scheduleStart.IsEnabled = !locked; scheduleCancel.IsEnabled = scheduler.Active;
            countdown.Text = Scheduler.FormatRemaining(scheduler.Remaining);
            scheduleStatus.Text = scheduler.Active ? PowerActions.Name(scheduler.Action) + (controller.Busy || controller.BatchRunning || controller.Loading ? " · 等待当前操作完成后提醒，仍可取消" : scheduler.State == ScheduleState.Warning ? " · 即将执行，仍可取消" : " · 定时进行中") : controller.ScheduleStatus;
            scheduleTarget.Text = scheduler.Active ? "预计执行 " + scheduler.Target.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "关闭到托盘后仍会继续计时。";
            scheduleProgress.Value = scheduler.Active ? Math.Max(0, Math.Min(1, 1 - scheduler.Remaining.TotalSeconds / Math.Max(1, controller.ScheduleInitial))) : scheduler.State == ScheduleState.Completed ? 1 : 0;
        }
        void Render()
        {
            if (releasing) return;
            ((TextBlock)View.FindName("Footer")).Text = controller.Preparing ? controller.PreparationStatus : controller.BatchRunning ? controller.BatchStatus : controller.Status;
            var videoColor = (Brush)Ui.Resource(controller.Services.OwnsProcess ? "ServiceRunning" : "ServiceStopped");
            var trayStatus = (TextBlock)View.FindName("TrayStatus");
            trayStatus.Text = controller.Services.OwnsProcess ? "● 视频服务运行中" : "○ 视频服务已停止";
            trayStatus.Foreground = videoColor;
            if (server != null)
            {
                server.Text = controller.ServerStatus; server.Foreground = videoColor;
                danmu.Text = controller.Gateway.Running ? "运行中 · HTTP " + controller.Settings.DanmuPort : "已停止";
                danmu.Foreground = (Brush)Ui.Resource(controller.Gateway.Running ? "ServiceRunning" : "ServiceStopped");
                plugin.Text = controller.PluginStatus; playback.Text = controller.SessionStatus;
            }
            if (batchStatus != null) { batchStatus.Text = controller.BatchPlan == null ? "暂无下载任务。到「影片与弹幕」匹配作品并选择集数。" : controller.BatchStatus; batchResume.IsEnabled = controller.BatchPlan != null; }
            if (grid != null) { if (!Object.ReferenceEquals(renderedEntries, controller.Library.Entries)) ApplyLibrary(); else SyncSelection(); }
            RenderSchedule();
        }
        void LogAdded(string line)
        {
            if (releasing) return;
            View.Dispatcher.BeginInvoke(new Action(() => { if (releasing || logs == null) return; if (logs.Text.Length > 160000) logs.Text = logs.Text.Substring(logs.Text.Length - 80000); logs.AppendText(Environment.NewLine + line); logs.ScrollToEnd(); }), DispatcherPriority.Background);
        }
        public void Track(Window dialog, Window owner = null)
        { dialog.Owner = owner ?? dialogs.LastOrDefault(x => x.IsActive && x.IsVisible) ?? View; dialog.ShowInTaskbar = false; dialogs.Add(dialog); dialog.Closed += (s, e) => dialogs.Remove(dialog); dialog.Show(); }
        public void ShowBatch() { ShowBatch(null); }
        public void ShowBatch(Window owner) { if (owner != null) { var existing = dialogs.OfType<BatchWindow>().FirstOrDefault(); if (existing != null) { existing.Activate(); return; } if (controller.BatchPlan != null) Track(new BatchWindow(controller), owner); } else if (controller.BatchPlan != null) MountBatch(); else Navigate("tasks"); }
        void OpenMatchWindow(bool autoSearch) { MountMatch(autoSearch); }
        public void Match(Dictionary<string, object> item, DanmuMatchScope scope, Dictionary<string, object>[] selected)
        {
            if (controller.Busy || controller.Loading || controller.BatchRunning) return;
            try
            {
                if (scope == DanmuMatchScope.Selection) MediaLibrary.RequireSameSeason(selected);
                if (WorkspaceVisible) { MountMatch(false); return; }
                bool reuse = session.Match != null && session.Match.Scope == scope && Json.Text(session.Match.Item, "Id") == Json.Text(item, "Id") && session.Match.Selected.Select(x => Json.Text(x, "Id")).SequenceEqual((selected ?? new Dictionary<string, object>[0]).Select(x => Json.Text(x, "Id")));
                if (reuse) { OpenMatchWindow(session.Match.Sources.Length == 0); return; }
                session.Match = new MatchState { Item = item, Selected = selected ?? new Dictionary<string, object>[0], Scope = scope, Keyword = item == null ? session.Filter : MediaNames.SearchTitle(item), AnimeOnly = item != null && Json.Text(item, "Type") == "Movie" ? false : controller.Settings.AnimeOnly };
                OpenMatchWindow(true);
            }
            catch (Exception e) { AlertWindow.Show(View, "无法匹配", e.Message, false); }
        }
        sealed class NativeOwner : Forms.IWin32Window
        { public NativeOwner(Window window) { Handle = new WindowInteropHelper(window).Handle; } public IntPtr Handle { get; private set; } }
    }
}
