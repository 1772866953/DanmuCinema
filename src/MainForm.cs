using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace DanmuCinema
{
    public sealed partial class MainForm : Form
    {
        readonly AppSettings settings;
        readonly ServiceManager services;
        readonly DanmuGateway gateway;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer timer;
        readonly EventWaitHandle showSignal;
        readonly bool startHidden, forceStart;
        readonly Color ink = Color.FromArgb(28, 39, 56), muted = Color.FromArgb(108, 120, 137), accent = Color.FromArgb(19, 128, 112);
        readonly Dictionary<string, Panel> pages = new Dictionary<string, Panel>();
        readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        Panel body;
        Label title, subtitle, footer, serverState, danmuState, pluginState, sessionState;
        TextBox serverAddress, danmuAddress, logs, mediaFolder, libraryName, userName, password;
        HistorySearchBox search;
        readonly LibraryMouseNavigation libraryMouseNavigation;
        NumericUpDown port, danmuPort;
        ComboBox network, libraryType, closeBehavior;
        CheckBox autoStart, servicesOnLaunch, original;
        MediaGrid library;
        bool busy, checking, exitRequested, finalClose, closing;
        string selectedPage = "overview";
        Icon appIcon;

        public MainForm(AppSettings settings, bool startHidden, bool forceStart)
        {
            this.settings = settings; this.startHidden = startHidden; this.forceStart = forceStart;
            services = new ServiceManager(settings);
            gateway = new DanmuGateway(settings);
            scheduler = new Scheduler(new SystemClock());
            Text = "弹幕影院 · DanmuCinema";
            Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = Color.FromArgb(241, 244, 248);
            ForeColor = ink;
            ClientSize = new Size(1120, 770);
            MinimumSize = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            appIcon = MakeIcon(); Icon = appIcon;
            BuildLayout();
            libraryMouseNavigation = new LibraryMouseNavigation(this, () => selectedPage == "library" && !busy && !libraryLoading && !closing, NavigateLibraryHistory);
            tray = new NotifyIcon { Icon = appIcon, Text = "弹幕影院：服务已停止", Visible = true };
            tray.DoubleClick += (s, e) => RestoreWindow();
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开控制窗口", null, (s, e) => RestoreWindow());
            menu.Items.Add("启动服务", null, async (s, e) => await Execute(StartAll));
            menu.Items.Add("停止服务", null, async (s, e) => await Execute(StopAll));
            menu.Items.Add("打开媒体库", null, (s, e) => OpenBrowser(LocalUrl + "/web/"));
            traySchedule = menu.Items.Add("当前没有定时任务", null, (s, e) => { RestoreWindow(); Navigate("schedule"); });
            trayCancelSchedule = menu.Items.Add("取消定时", null, (s, e) => CancelSchedule());
            trayCancelSchedule.Enabled = false;
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出并停止服务", null, (s, e) => { exitRequested = true; Close(); });
            tray.ContextMenuStrip = menu;
            showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DanmuCinema-Show-" + Program.StableId(Paths.Root));
            Log.Added += AppendLog;
            if (File.Exists(Paths.LogPath)) logs.Text = String.Join(Environment.NewLine, File.ReadLines(Paths.LogPath).Reverse().Take(160).Reverse()) + Environment.NewLine;
            timer = new System.Windows.Forms.Timer { Interval = 4000 };
            timer.Tick += async (s, e) => { if (showSignal.WaitOne(0)) RestoreWindow(); await UpdateStatus(); };
            scheduleTimer = new System.Windows.Forms.Timer { Interval = 250 };
            scheduleTimer.Tick += async (s, e) => await TickSchedule();
            KeyPreview = true;
            KeyDown += (s, e) => { if (selectedPage == "library" && e.Alt && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)) { NavigateLibraryHistory(e.KeyCode == Keys.Right); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Escape && scheduler.Active) { CancelSchedule(); e.SuppressKeyPress = true; } };
            Shown += async (s, e) =>
            {
                if (startHidden) HideToTray();
                timer.Start();
                scheduleTimer.Start();
                if (forceStart || settings.StartServicesOnLaunch || services.OwnsProcess) await Execute(StartAll);
                else await UpdateStatus();
            };
            FormClosing += OnClosing;
            UpdateAddresses();
        }
        string LocalUrl { get { return "http://127.0.0.1:" + settings.Port; } }
        string ChosenIp { get { return network.SelectedItem == null ? "电脑局域网IP" : network.SelectedItem.ToString(); } }

        void BuildLayout()
        {
            var sidebar = new Panel { Dock = DockStyle.Left, Width = 182, BackColor = ink, Padding = new Padding(16, 24, 16, 16) };
            var brand = new Label { Text = "DanmuCinema", ForeColor = Color.White, Font = new Font("Segoe UI", 14, FontStyle.Bold), Dock = DockStyle.Top, Height = 48 };
            var brandHint = new Label { Text = "你的家庭弹幕影院", ForeColor = Color.FromArgb(155, 174, 193), Dock = DockStyle.Top, Height = 50, Padding = new Padding(1, 6, 0, 0) };
            var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 20, 0, 0) };
            AddNavigation(nav, "overview", "01   服务总览");
            AddNavigation(nav, "library", "02   影片与弹幕");
            AddNavigation(nav, "connect", "03   连接 iPad");
            AddNavigation(nav, "setup", "04   首次设置");
            AddNavigation(nav, "settings", "05   启动与偏好");
            AddNavigation(nav, "schedule", "06   定时任务");
            AddNavigation(nav, "logs", "07   运行日志");
            var version = new Label { Dock = DockStyle.Bottom, Height = 72, ForeColor = Color.FromArgb(155, 174, 193), Text = "DanmuCinema v1.2\r\nJellyfin + Danmu", Font = new Font("Segoe UI", 9), Padding = new Padding(0, 16, 0, 0) };
            sidebar.Controls.Add(nav); sidebar.Controls.Add(version); sidebar.Controls.Add(brandHint); sidebar.Controls.Add(brand);
            var workspace = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 22, 28, 14) };
            var header = new Panel { Dock = DockStyle.Top, Height = 95 };
            title = new Label { Dock = DockStyle.Top, Height = 47, Font = new Font("Microsoft YaHei UI", 24, FontStyle.Bold), Text = "服务总览" };
            subtitle = new Label { Dock = DockStyle.Fill, ForeColor = muted, Text = "在电脑管理媒体，在 iPad 原画播放。", Padding = new Padding(2, 4, 0, 0) };
            header.Controls.Add(subtitle); header.Controls.Add(title);
            footer = new Label { Dock = DockStyle.Bottom, Height = 32, ForeColor = muted, Text = "就绪", Padding = new Padding(0, 10, 0, 0), Font = new Font("Microsoft YaHei UI", 9) };
            body = new Panel { Dock = DockStyle.Fill };
            workspace.Controls.Add(body); workspace.Controls.Add(footer); workspace.Controls.Add(header);
            Controls.Add(workspace); Controls.Add(sidebar);
            BuildOverview(); BuildLibrary(); BuildConnect(); BuildSetup(); BuildSettings(); BuildSchedule(); BuildLogs();
            Navigate("overview");
        }
        void AddNavigation(FlowLayoutPanel parent, string key, string text)
        {
            var button = new Button { Text = text, Size = new Size(150, 46), FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(185, 200, 215), BackColor = ink, Margin = new Padding(0, 0, 0, 9), Cursor = Cursors.Hand, Padding = new Padding(6, 0, 0, 0) };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (s, e) => Navigate(key);
            navigation[key] = button; parent.Controls.Add(button);
        }
        Panel Page(string key)
        {
            var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Visible = false };
            pages[key] = panel; body.Controls.Add(panel); return panel;
        }
        TableLayoutPanel Stack(Panel page)
        {
            var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(0, 0, 3, 12) };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.Controls.Add(stack); return stack;
        }
        void Add(TableLayoutPanel stack, Control child)
        {
            int row = stack.RowCount++;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            child.Dock = DockStyle.Fill; child.Margin = new Padding(0, 0, 0, 16); stack.Controls.Add(child, 0, row);
        }
        Panel Card(string caption, int height)
        {
            var panel = new Panel { BackColor = Color.White, Height = height, Padding = new Padding(20, 15, 20, 16) };
            var heading = new Label { Text = caption, Dock = DockStyle.Top, Height = 33, Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold), ForeColor = ink };
            panel.Controls.Add(heading); return panel;
        }
        Label TextLabel(string text, int height)
        {
            return new Label { Text = text, Height = height, Dock = DockStyle.Top, ForeColor = muted, Padding = new Padding(0, 4, 0, 0) };
        }
        Button ActionButton(string text, Func<Task> action, bool primary)
        {
            var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(112, 38), Height = 38, FlatStyle = FlatStyle.Flat, BackColor = primary ? accent : Color.FromArgb(238, 243, 247), ForeColor = primary ? Color.White : ink, Padding = new Padding(12, 3, 12, 3), Margin = new Padding(0, 0, 10, 8), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderSize = 0;
            button.Click += async (s, e) => await Execute(action);
            return button;
        }
        FlowLayoutPanel Actions(params Control[] controls)
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            panel.Controls.AddRange(controls); return panel;
        }
        static Task Completed() { return Task.FromResult(0); }
        TextBox AddressBox()
        {
            return new TextBox { ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(246, 248, 251), ForeColor = ink, Font = new Font("Consolas", 12), Dock = DockStyle.Top, Height = 32 };
        }
        void BuildOverview()
        {
            var stack = Stack(Page("overview"));
            var card = Card("运行状态", 203);
            var states = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(0, 6, 0, 0) };
            states.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128)); states.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            serverState = StateRow(states, 0, "视频服务器", "尚未启动");
            danmuState = StateRow(states, 1, "弹幕接口", "尚未启动");
            pluginState = StateRow(states, 2, "弹幕插件", "启动并登录后检测");
            sessionState = StateRow(states, 3, "播放会话", "暂无播放");
            card.Controls.Add(states); states.BringToFront(); Add(stack, card);
            Add(stack, Actions(ActionButton("启动服务", StartAll, true), ActionButton("停止服务", StopAll, false), ActionButton("打开媒体库", () => { OpenBrowser(LocalUrl + "/web/"); return Completed(); }, false), ActionButton("扫描媒体库", ScanLibrary, false)));
            var guide = Card("第一次使用", 147);
            var instructions = TextLabel("1  到「首次设置」创建管理员账号、添加视频目录。\r\n2  到「连接 iPad」复制服务器地址，添加 Jellyfin 连接。\r\n3  影片入库后自动匹配弹幕；匹配不准时可以搜索修正。", 86);
            guide.Controls.Add(instructions); instructions.BringToFront(); Add(stack, guide);
            Add(stack, TextLabel("视频通过 Jellyfin 直接传输。弹幕接口只传弹幕，不转发蓝光视频。\r\n托盘运行时服务继续工作；在托盘菜单选择「退出并停止服务」即可完全退出。", 66));
        }
        Label StateRow(TableLayoutPanel panel, int row, string name, string value)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            panel.Controls.Add(new Label { Text = name, Dock = DockStyle.Fill, ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            var label = new Label { Text = value, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold) };
            panel.Controls.Add(label, 1, row); return label;
        }
        void BuildConnect()
        {
            var stack = Stack(Page("connect"));
            network = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            network.Items.AddRange(NetworkInfo.Addresses()); if (network.Items.Count > 0) network.SelectedIndex = 0;
            network.SelectedIndexChanged += (s, e) => UpdateAddresses();
            Add(stack, Actions(new Label { Text = "电脑局域网地址", Width = 128, Height = 32, TextAlign = ContentAlignment.MiddleLeft }, network, ActionButton("刷新地址", () => { network.Items.Clear(); network.Items.AddRange(NetworkInfo.Addresses()); if (network.Items.Count > 0) network.SelectedIndex = 0; UpdateAddresses(); return Completed(); }, false)));
            var video = Card("视频服务器地址", 130);
            serverAddress = AddressBox();
            var videoInner = new Panel { Dock = DockStyle.Fill };
            videoInner.Controls.Add(Actions(ActionButton("复制地址", () => { Clipboard.SetText(serverAddress.Text); return Completed(); }, false)));
            videoInner.Controls.Add(serverAddress); video.Controls.Add(videoInner); videoInner.BringToFront(); Add(stack, video);
            var danmu = Card("自定义弹幕 API", 130);
            danmuAddress = AddressBox();
            var danmuInner = new Panel { Dock = DockStyle.Fill };
            danmuInner.Controls.Add(Actions(ActionButton("复制弹幕地址", () => { Clipboard.SetText(danmuAddress.Text); return Completed(); }, false)));
            danmuInner.Controls.Add(danmuAddress); danmu.Controls.Add(danmuInner); danmuInner.BringToFront(); Add(stack, danmu);
            Add(stack, TextLabel("SenPlayer / Filebar：添加服务器 → Jellyfin → 输入上面的地址和你创建的账号。\r\nSenPlayer：设置 → 弹幕设置 → 自定义弹幕 API，填入第二个地址。\r\nFilebar：优先使用媒体服务器弹幕；也可在自定义弹幕服务器中尝试第二个地址。\r\n首次匹配可能需要搜索并选集，具体自动加载行为以 iPad 上的版本为准。", 113));
            Add(stack, Actions(ActionButton("配置局域网防火墙", ConfigureFirewall, true), ActionButton("打开连接说明", () => { OpenFile(Path.Combine(Paths.Root, "README.md")); return Completed(); }, false)));
            Add(stack, TextLabel("电脑与 iPad 应连接同一路由器。防火墙规则仅允许专用网络的同一子网。\r\n如果地址有多个，选择电脑实际连接路由器的地址；避免 VPN / 虚拟网卡地址。", 60));
        }
        void BuildSetup()
        {
            var stack = Stack(Page("setup"));
            Add(stack, Actions(ActionButton("安装 / 修复运行组件", InstallComponents, true), ActionButton("启动服务器", StartAll, false), ActionButton("打开 Jellyfin 设置", () => { OpenBrowser(LocalUrl + "/web/#!/dashboard"); return Completed(); }, false)));
            userName = new TextBox { Text = settings.AdminName, Width = 170, Margin = new Padding(0, 4, 18, 8) };
            password = new TextBox { Width = 230, UseSystemPasswordChar = true, Margin = new Padding(0, 4, 12, 8) };
            var showPassword = new CheckBox { Text = "显示密码", AutoSize = true, Margin = new Padding(0, 6, 0, 8) };
            showPassword.CheckedChanged += (s, e) => password.UseSystemPasswordChar = !showPassword.Checked;
            Add(stack, SetupCard("创建或连接管理员账号",
                Actions(new Label { Text = "账号", Width = 50, Height = 32 }, userName, new Label { Text = "密码", Width = 50, Height = 32 }, password, showPassword),
                Actions(ActionButton("初始化 / 登录", InitializeAccount, true)),
                new Label { Text = "新服务器将创建该账号；已初始化的服务器会登录验证。密码至少 8 位。", AutoSize = true, ForeColor = muted }));
            mediaFolder = new TextBox { Text = settings.MediaFolder, Width = 470, Margin = new Padding(0, 4, 10, 8) };
            libraryName = new TextBox { Text = settings.LibraryName, Width = 185, Margin = new Padding(0, 4, 14, 8) };
            libraryType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155, Margin = new Padding(0, 4, 0, 8) };
            libraryType.Items.AddRange(new object[] { "电影", "电视剧 / 动画" }); libraryType.SelectedIndex = settings.LibraryType == "tvshows" ? 1 : 0;
            Add(stack, SetupCard("添加视频目录",
                Actions(mediaFolder, ActionButton("选择目录", () => { using (var dialog = new FolderBrowserDialog { SelectedPath = mediaFolder.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) mediaFolder.Text = dialog.SelectedPath; return Completed(); }, false)),
                Actions(new Label { Text = "媒体库名", Width = 76, Height = 32 }, libraryName, new Label { Text = "类型", Width = 45, Height = 32 }, libraryType, ActionButton("添加媒体库", AddMediaLibrary, true)),
                new Label { Text = "电影和剧集建议使用不同目录，可多次添加。弹幕插件会在视频旁保存 XML。", AutoSize = true, ForeColor = muted }));
            Add(stack, TextLabel("安装包来自 Jellyfin 官方和开源 Danmu 插件，版本已固定并校验。\r\n服务器账号是 iPad 的登录账号。控制台保存加密登录凭证，不保存你的密码。", 66));
        }
        TableLayoutPanel SetupCard(string caption, params Control[] controls)
        {
            var card = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Color.White, Padding = new Padding(20, 15, 20, 0) };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Add(card, new Label { Text = caption, AutoSize = true, Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold), ForeColor = ink });
            foreach (var control in controls) Add(card, control);
            card.SizeChanged += (s, e) =>
            {
                foreach (var note in controls.OfType<Label>())
                {
                    int width = Math.Max(100, card.ClientSize.Width - card.Padding.Horizontal - note.Margin.Horizontal);
                    if (note.MaximumSize.Width != width) note.MaximumSize = new Size(width, 0);
                }
            };
            return card;
        }
        void BuildSettings()
        {
            var stack = Stack(Page("settings"));
            var startup = Card("启动与关闭", 252);
            var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            autoStart = new CheckBox { Text = "开机启动（登录 Windows 后自动启动服务并留在托盘）", AutoSize = true, Checked = AutoStart.Enabled, Margin = new Padding(0, 8, 0, 10) };
            servicesOnLaunch = new CheckBox { Text = "手动打开程序时，同时启动服务", AutoSize = true, Checked = settings.StartServicesOnLaunch, Margin = new Padding(0, 0, 0, 12) };
            closeBehavior = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 290, Margin = new Padding(0, 3, 0, 10) };
            closeBehavior.Items.AddRange(new object[] { "缩小到右下角托盘，服务继续运行", "停止视频与弹幕服务，然后退出" }); closeBehavior.SelectedIndex = settings.CloseToTray ? 0 : 1;
            controls.Controls.Add(autoStart); controls.Controls.Add(servicesOnLaunch);
            controls.Controls.Add(Actions(new Label { Text = "关闭窗口时", Width = 110, Height = 34, TextAlign = ContentAlignment.MiddleLeft }, closeBehavior));
            controls.Controls.Add(new Label { Text = "最小化按钮仍最小化到任务栏。托盘菜单的「退出并停止服务」始终完全退出。\r\n开机启动在当前账号登录后生效；未登录 Windows 时不会启动。", AutoSize = true, ForeColor = muted });
            startup.Controls.Add(controls); controls.BringToFront(); Add(stack, startup);
            var playback = Card("播放与连接", 170);
            original = new CheckBox { Text = "保留原画：禁止当前播放账号的视频转码（允许重新封装与音频转换）", Checked = settings.PreferOriginal, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
            port = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = settings.Port, Width = 105 };
            danmuPort = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = settings.DanmuPort, Width = 105 };
            var playbackInner = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            playbackInner.Controls.Add(original);
            playbackInner.Controls.Add(Actions(new Label { Text = "视频端口", Width = 90, Height = 32 }, port, new Label { Text = "弹幕端口", Width = 90, Height = 32, Margin = new Padding(20, 0, 0, 0) }, danmuPort));
            playbackInner.Controls.Add(new Label { Text = "更改端口前请停止服务；更改后需更新 iPad 地址和防火墙规则。", AutoSize = true, ForeColor = muted });
            playback.Controls.Add(playbackInner); playbackInner.BringToFront(); Add(stack, playback);
            Add(stack, Actions(ActionButton("保存设置", SaveSettings, true), ActionButton("打开数据目录", () => { OpenFile(Paths.Data); return Completed(); }, false)));
        }
        void BuildLogs()
        {
            var page = Page("logs");
            logs = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = Color.FromArgb(24, 34, 47), ForeColor = Color.FromArgb(205, 225, 229), BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10) };
            var controls = Actions(ActionButton("打开服务器日志", () => { Directory.CreateDirectory(Path.Combine(Paths.Data, "server-logs")); OpenFile(Path.Combine(Paths.Data, "server-logs")); return Completed(); }, false), ActionButton("打开控制台日志", () => { if (File.Exists(Paths.LogPath)) OpenFile(Paths.LogPath); return Completed(); }, false)); controls.Dock = DockStyle.Bottom;
            page.Controls.Add(logs); page.Controls.Add(controls);
        }
        void Navigate(string key)
        {
            selectedPage = key;
            foreach (var pair in pages) pair.Value.Visible = pair.Key == key;
            foreach (var pair in navigation) { pair.Value.BackColor = pair.Key == key ? accent : ink; pair.Value.ForeColor = pair.Key == key ? Color.White : Color.FromArgb(185, 200, 215); }
            var headings = new Dictionary<string, string> { { "overview", "服务总览" }, { "library", "影片与弹幕" }, { "connect", "连接 iPad" }, { "setup", "首次设置" }, { "settings", "启动与偏好" }, { "schedule", "定时任务" }, { "logs", "运行日志" } };
            title.Text = headings[key];
            if (key == "schedule") { subtitle.Text = "设置倒计时或指定时间，托盘中继续运行。"; return; }
            subtitle.Text = key == "settings" ? "让启动、后台运行和退出按你的习惯工作。" : key == "library" ? "浏览和筛选媒体库，批量选择影片，重新匹配弹幕来源。" : key == "connect" ? "复制地址，在播放器中添加你的电脑。" : key == "setup" ? "一次设置账号与媒体库，之后直接启动即可。" : "在电脑管理媒体，在 iPad 原画播放。";
        }
        async Task Execute(Func<Task> action)
        {
            if (busy || libraryLoading || closing || scheduler.State == ScheduleState.Executing) return;
            busy = true; UseWaitCursor = true; footer.Text = "正在处理，请稍候…";
            try { await action(); if (footer.Text == "正在处理，请稍候…") footer.Text = "操作完成"; }
            catch (Exception e) { Log.Write(e.Message); footer.Text = e.Message; if (Visible) MessageBox.Show(this, e.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); else Notify("需要处理", e.Message); }
            finally { busy = false; UseWaitCursor = false; }
            await UpdateStatus();
        }
        async Task StartAll()
        {
            await services.Start();
            try { gateway.Start(); }
            catch (Exception e) { throw new InvalidOperationException("视频服务已启动，但弹幕端口启动失败：" + e.Message); }
            if (!String.IsNullOrEmpty(services.Api.Token) && !String.IsNullOrEmpty(settings.UserId)) await services.Api.SetOriginalPolicy(settings.PreferOriginal);
            UpdateAddresses();
        }
        async Task StopAll() { await gateway.Stop(); await services.Stop(); }
        async Task InitializeAccount()
        {
            if (!services.OwnsProcess) await StartAll();
            await services.Api.Initialize(userName.Text.Trim(), password.Text);
            password.Clear();
            await services.Api.SetOriginalPolicy(settings.PreferOriginal);
            Log.Write("管理员账号已连接。现在可以添加视频目录。");
        }
        async Task AddMediaLibrary()
        {
            await services.Api.AddLibrary(mediaFolder.Text.Trim(), libraryName.Text.Trim(), libraryType.SelectedIndex == 1 ? "tvshows" : "movies");
            settings.MediaFolder = mediaFolder.Text.Trim(); settings.LibraryName = libraryName.Text.Trim(); settings.LibraryType = libraryType.SelectedIndex == 1 ? "tvshows" : "movies";
            SettingsStore.Save(settings);
        }
        async Task ScanLibrary() { await services.Api.Request("POST", "Library/Refresh", new { }, true); Log.Write("已提交媒体库扫描，弹幕将在影片识别后匹配。"); }
        Task EditSources()
        {
            using (var dialog = new SourcesDialog(settings)) dialog.ShowDialog(this);
            return Completed();
        }
        static string SafeFileName(string name) { foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_'); return name; }
        Task OpenItemDetails() { OpenBrowser(LocalUrl + "/web/#!/details?id=" + Uri.EscapeDataString(Json.Text(SelectedItem(), "Id"))); return Completed(); }
        async Task SaveSettings()
        {
            int nextPort = (int)port.Value, nextDanmuPort = (int)danmuPort.Value;
            if ((services.OwnsProcess || gateway.Running) && (nextPort != settings.Port || nextDanmuPort != settings.DanmuPort)) throw new InvalidOperationException("请先停止服务，再修改端口。");
            if (nextPort == nextDanmuPort) throw new InvalidOperationException("视频与弹幕端口不能相同。");
            if (services.OwnsProcess && !String.IsNullOrEmpty(services.Api.Token)) await services.Api.SetOriginalPolicy(original.Checked);
            var previous = Json.Read<AppSettings>(Json.Write(settings));
            bool previousAutoStart = AutoStart.Enabled;
            try
            {
                settings.Port = nextPort; settings.DanmuPort = nextDanmuPort; settings.CloseToTray = closeBehavior.SelectedIndex == 0;
                settings.StartServicesOnLaunch = servicesOnLaunch.Checked; settings.PreferOriginal = original.Checked;
                SettingsStore.Save(settings);
                AutoStart.Set(autoStart.Checked);
            }
            catch
            {
                settings.Port = previous.Port; settings.DanmuPort = previous.DanmuPort; settings.CloseToTray = previous.CloseToTray;
                settings.StartServicesOnLaunch = previous.StartServicesOnLaunch; settings.PreferOriginal = previous.PreferOriginal;
                SettingsStore.Save(settings);
                try { AutoStart.Set(previousAutoStart); } catch { }
                throw;
            }
            UpdateAddresses(); Log.Write("设置已保存；关闭窗口行为立即生效。");
        }
        async Task InstallComponents()
        {
            if (services.OwnsProcess || gateway.Running) throw new InvalidOperationException("请先停止服务，再安装或修复运行组件。");
            string script = Path.Combine(Paths.Root, "scripts", "install-components.ps1");
            var start = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File " + AutoStart.Quote(script)) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
            using (var process = new Process { StartInfo = start })
            {
                process.OutputDataReceived += (s, e) => { if (!String.IsNullOrWhiteSpace(e.Data)) Log.Write(e.Data); };
                process.Start(); process.BeginOutputReadLine();
                var errors = process.StandardError.ReadToEndAsync();
                await Task.Run(() => process.WaitForExit());
                string error = await errors;
                if (process.ExitCode != 0) throw new InvalidOperationException("组件安装失败：" + error);
            }
        }
        async Task ConfigureFirewall()
        {
            string script = Path.Combine(Paths.Root, "scripts", "configure-firewall.ps1");
            var start = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File " + AutoStart.Quote(script) + " -MediaPort " + settings.Port + " -DanmuPort " + settings.DanmuPort) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using (var process = Process.Start(start))
            {
                await Task.Run(() => process.WaitForExit());
                if (process.ExitCode != 0) throw new InvalidOperationException("防火墙配置未完成。请允许 Windows 管理员权限，并将家庭网络设为专用网络。");
            }
            Log.Write("已配置专用网络局域网访问规则。");
        }
        async Task UpdateStatus()
        {
            if (checking || closing || services.Transitioning) return;
            checking = true;
            try
            {
                if (!services.OwnsProcess)
                {
                    serverState.Text = services.DesiredRunning ? "异常停止，请查看日志后重新启动" : (Paths.FindServer() == null ? "组件未安装" : "已停止");
                    serverState.ForeColor = muted; pluginState.Text = "启动并登录后检测"; sessionState.Text = "暂无播放";
                    if (gateway.Running) await gateway.Stop();
                }
                else
                {
                    var info = await services.Api.PublicInfo();
                    serverState.Text = "运行中 · Jellyfin " + Json.Text(info, "Version") + " · HTTP " + settings.Port; serverState.ForeColor = accent;
                    if (String.IsNullOrEmpty(services.Api.Token)) { pluginState.Text = "请先初始化 / 登录管理员"; sessionState.Text = "尚未登录"; }
                    else
                    {
                        try
                        {
                            var plugins = await services.Api.Plugins();
                            var danmu = plugins.Cast<Dictionary<string, object>>().FirstOrDefault(x => Json.Text(x, "Name").IndexOf("Danmu", StringComparison.OrdinalIgnoreCase) >= 0);
                            pluginState.Text = danmu == null ? "未加载，停止服务后修复组件" : Json.Text(danmu, "Status") + " · " + Json.Text(danmu, "Version");
                            var playing = (await services.Api.Sessions()).Cast<Dictionary<string, object>>().Where(x => Json.Child(x, "NowPlayingItem") != null).ToArray();
                            sessionState.Text = playing.Length == 0 ? "暂无播放" : String.Join("；", playing.Select(x => Json.Text(x, "Client") + " · " + Json.Text(Json.Child(x, "NowPlayingItem"), "Name") + " · " + Json.Text(Json.Child(x, "PlayState"), "PlayMethod")));
                        }
                        catch { pluginState.Text = "登录已失效或接口异常，请重新登录"; }
                    }
                }
                danmuState.Text = gateway.Running ? "运行中 · " + settings.DanmuPort : "已停止";
                danmuState.ForeColor = gateway.Running ? accent : muted;
                tray.Text = services.OwnsProcess ? "弹幕影院：视频与弹幕后台运行中" : "弹幕影院：服务已停止";
            }
            catch { serverState.Text = "服务正在启动或暂时无响应"; }
            finally { checking = false; }
            await AutoLoadLibrary();
        }
        void UpdateAddresses()
        {
            if (serverAddress == null || danmuAddress == null || network == null) return;
            serverAddress.Text = "http://" + ChosenIp + ":" + settings.Port;
            danmuAddress.Text = "http://" + ChosenIp + ":" + settings.DanmuPort + "/" + gateway.Key;
        }
        void AppendLog(string line)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    if (logs.TextLength > 160000) logs.Text = logs.Text.Substring(logs.TextLength - 80000);
                    logs.AppendText(line + Environment.NewLine);
                    if (busy) footer.Text = line.Length > 110 ? line.Substring(0, 110) : line;
                }));
            }
            catch (InvalidOperationException) { }
        }
        void RestoreWindow() { Show(); ShowInTaskbar = true; WindowState = FormWindowState.Normal; Activate(); }
        void HideToTray() { Hide(); ShowInTaskbar = false; }
        void Notify(string titleText, string message) { tray.BalloonTipTitle = titleText; tray.BalloonTipText = message; tray.ShowBalloonTip(4000); }
        async void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (finalClose) return;
            if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
            {
                timer.Stop(); scheduleTimer.Stop(); CancelSchedule(); return;
            }
            e.Cancel = true;
            if (closing) return;
            if (!exitRequested && settings.CloseToTray) { HideToTray(); return; }
            if (busy || libraryLoading) { footer.Text = "当前操作尚未完成，请稍后再退出。"; return; }
            if (scheduler.State == ScheduleState.Executing) { footer.Text = "定时操作正在执行，请稍后退出。"; return; }
            if (scheduler.Active)
            {
                // Do not dispatch a power action through the exit confirmation dialog.
                scheduleTimer.Stop();
                if (MessageBox.Show(this, "当前定时尚未完成。退出将取消任务，确定退出？", "退出弹幕影院", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    scheduler.OnResume(); scheduleWarningShown = false; scheduleTimer.Start();
                    exitRequested = false; return;
                }
            }
            CancelSchedule();
            closing = true; timer.Stop(); footer.Text = "正在停止服务并退出…";
            try { await StopAll(); finalClose = true; tray.Visible = false; Close(); }
            catch (Exception error) { closing = false; exitRequested = false; timer.Start(); MessageBox.Show(this, "停止服务失败：" + error.Message, "退出未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        static void OpenBrowser(string url) { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        static void OpenFile(string path) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        static Icon MakeIcon()
        {
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("DanmuCinema.AppIcon"))
                if (stream != null) using (var embedded = new Icon(stream, new Size(64, 64))) return (Icon)embedded.Clone();
            using (var bitmap = new Bitmap(64, 64))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                using (var brush = new SolidBrush(Color.FromArgb(19, 128, 112))) graphics.FillEllipse(brush, 2, 2, 60, 60);
                graphics.FillPolygon(Brushes.White, new[] { new Point(26, 19), new Point(26, 45), new Point(46, 32) });
                IntPtr handle = bitmap.GetHicon();
                try { using (var originalIcon = Icon.FromHandle(handle)) return (Icon)originalIcon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (libraryMouseNavigation != null) libraryMouseNavigation.Dispose();
                Log.Added -= AppendLog;
                scheduler.Cancel(); ReleaseScheduleAwake(); scheduleTimer.Dispose();
                timer.Dispose(); tray.Dispose(); showSignal.Dispose(); services.Dispose(); gateway.Dispose(); appIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
