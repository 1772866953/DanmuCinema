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
        Grid animatedSurface;
        readonly Image backdrop = new Image { IsHitTestVisible = false, Stretch = Stretch.Fill };
        bool fadeClosing, finishClose, alreadyClosed;
        bool? pendingResult;
        internal void CloseImmediately() { finishClose = true; Close(); }
        internal bool OwnerPreparedForClose { get; private set; }
        internal bool CloseTransitionSuppressed { get; private set; }
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (!e.Cancel && !finishClose && IsVisible && SystemParameters.ClientAreaAnimation)
            {
                e.Cancel = true;
                if (!fadeClosing)
                {
                    fadeClosing = true; pendingResult = DialogResult;
                    backdrop.Source = DialogBackdrop.Capture(this);
                    // A close during the first transparent frame may finish the
                    // fade synchronously. Always unwind Closing before closing again.
                    SurfaceMotion.FadeOut(animatedSurface, () => Dispatcher.BeginInvoke(new Action(() => { if (alreadyClosed) return; finishClose = true; if (pendingResult.HasValue) DialogResult = pendingResult; if (!alreadyClosed) Close(); })));
                }
                return;
            }
            // Run after cancellation checks while this HWND is still alive.
            if (!e.Cancel)
            {
                // Stop the outgoing DWM snapshot before owner activation can
                // change this dialog's non-client/activation rendering.
                CloseTransitionSuppressed = Ui.SuppressDialogCloseTransition(this);
                OwnerPreparedForClose = Ui.PrepareDialogClose(this);
            }
        }
        public DialogWindow(string title, double width, double height)
        {
            Title = title; Width = width; Height = height; MinWidth = 760; MinHeight = 540; WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
            Style = (Style)Application.Current.FindResource(typeof(Window));
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 48, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(14), UseAeroCaptionButtons = false });
            Background = (Brush)Ui.Resource("DialogCanvas");
            ShowInTaskbar = false;
            var outer = new Grid { Background = Background }; outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) }); outer.RowDefinitions.Add(new RowDefinition());
            var atmosphere = Ui.Atmosphere(); Grid.SetRowSpan(atmosphere, 2); outer.Children.Add(atmosphere);
            var header = new Grid { Background = (Brush)Ui.Resource("DialogHeader") }; header.Children.Add(new TextBlock { Text = title, Margin = new Thickness(22, 0, 60, 0), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
            var accent = Ui.AccentLine(); accent.Margin = new Thickness(22, 0, 0, 0); accent.VerticalAlignment = VerticalAlignment.Bottom; header.Children.Add(accent);
            var close = Ui.Button("", Close); Ui.ConfigureCaption(close, "close"); close.HorizontalAlignment = HorizontalAlignment.Right; WindowChrome.SetIsHitTestVisibleInChrome(close, true); header.Children.Add(close); outer.Children.Add(header);
            Body = new Grid { Margin = new Thickness(22) }; var viewport = new LayoutViewport(Body, 340, height < 400 ? 240 : 420); Grid.SetRow(viewport, 1); outer.Children.Add(viewport);
            UiScale.Attach(this, outer, 48);
            var roundedContent = Ui.RoundedContent(outer, 13);
            var frame = new SmoothBorder { Style = (Style)Ui.Resource("DialogOutline"), Child = roundedContent, Background = Background };
            animatedSurface = new Grid(); animatedSurface.Children.Add(frame);
            var layers = new Grid { Background = Background }; layers.Children.Add(backdrop); layers.Children.Add(animatedSurface); Content = layers;
            // The antialiased title contour must blend with title colour, including
            // pixels between WPF's curve and DWM's native mask. A canvas-coloured
            // underlay leaves a dark crescent even when both radii are the same.
            Action fillCorners = () =>
            {
                // The native window's outer frame is outside the scaled body.
                // Keep its visible outline as thick as the scaled floating pane.
                frame.BorderThickness = new Thickness(UiScale.Current / 100.0);
                roundedContent.Tag = Math.Max(0, frame.CornerRadius.TopLeft - frame.BorderThickness.Left);
                if (roundedContent.ActualWidth > 0 && roundedContent.ActualHeight > 0) roundedContent.Clip = SmoothBorder.Rounded(new Rect(roundedContent.RenderSize), new CornerRadius((double)roundedContent.Tag));
                if (layers.ActualHeight <= 0) return;
                double edge = Math.Min(1, (48 * UiScale.Current / 100.0 + 1) / layers.ActualHeight);
                var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                var titleColour = ((SolidColorBrush)Ui.Resource("DialogHeader")).Color;
                var bodyColour = ((SolidColorBrush)Ui.Resource("DialogCanvas")).Color;
                fill.GradientStops.Add(new GradientStop(titleColour, 0)); fill.GradientStops.Add(new GradientStop(titleColour, edge));
                fill.GradientStops.Add(new GradientStop(bodyColour, edge)); fill.GradientStops.Add(new GradientStop(bodyColour, 1)); fill.Freeze();
                layers.Background = fill; frame.Background = fill;
            };
            layers.SizeChanged += (s, e) => fillCorners(); outer.SizeChanged += (s, e) => fillCorners();
            SourceInitialized += (s, e) =>
            {
                Ui.EnableWindowTransitions(this);
                double radius = WindowCorners.Attach(this);
                frame.CornerRadius = new CornerRadius(radius); roundedContent.Tag = radius - 1;
                WindowChrome.GetWindowChrome(this).CornerRadius = new CornerRadius(radius);
                Ui.SuppressDialogCloseTransition(this);
            };
            // Arm before Show; run only once the view has completed its first layout.
            SurfaceMotion.FadeIn(animatedSurface, () => { if (!fadeClosing) backdrop.Source = null; });
            Loaded += (s, e) => { if (Owner != null) Icon = Owner.Icon; if (SystemParameters.ClientAreaAnimation) backdrop.Source = DialogBackdrop.Capture(this); Ui.AnimateAccent(accent); };
            Window parent = null; bool returnFocus = false;
            Closing += (s, e) => { parent = Owner; returnFocus = Ui.IsForeground(this); };
            Closed += (s, e) =>
            {
                alreadyClosed = true;
                UiScale.Detach(this);
                Ui.ReleaseVisualTree(animatedSurface); Body.Children.Clear();
                animatedSurface.Children.Clear(); animatedSurface = null; Content = null;
                backdrop.Source = null;
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
            MinWidth = 460; MinHeight = 250; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.Manual; ShowInTaskbar = false;
            var accent = Ui.AccentLine();
            var heading = Ui.Text(title, "Heading"); heading.Margin = new Thickness(0, 12, 0, 8);
            var content = Ui.Text(message); content.LineHeight = 25; content.Margin = new Thickness(0, 0, 0, 22);
            var scroll = new ScrollViewer { Content = content, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var accept = Ui.Button(confirm ? acceptLabel : "知道了", () => DialogResult = true, true);
            accept.IsDefault = !confirm;
            var actions = Ui.Row(); actions.HorizontalAlignment = HorizontalAlignment.Right;
            if (confirm) { var cancel = Ui.Button(cancelLabel, () => DialogResult = false); cancel.IsCancel = true; actions.Children.Add(cancel); Loaded += (s, e) => cancel.Focus(); }
            else { accept.IsCancel = true; Loaded += (s, e) => accept.Focus(); }
            actions.Children.Add(accept);
            var layout = new DockPanel(); var intro = Ui.Stack(accent, heading); DockPanel.SetDock(intro, Dock.Top); layout.Children.Add(intro); DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions); layout.Children.Add(scroll); Body.Children.Add(layout);
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
            var only = Ui.Check("客户端搜索默认只看动漫", settings.AnimeOnly);
            var dandan = Ui.Check("弹弹play 官方 API", settings.EnableDandan);
            dandan.ToolTip = "通过官方接口识别视频、查找作品并下载弹幕；有效缓存优先使用。";
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
    // Window adapters retain compatibility for diagnostic/legacy entry points.
    // Production matching mounts these same views in the main window workspace.
    public sealed class MatchWindow : DialogWindow
    {
        public MatchWindow(DesktopController controller, ShellWindow shell, MatchState state, bool autoSearch)
            : base("选择弹幕来源", 1080, 790)
        {
            var view = new MatchView(controller, state, autoSearch, () => shell.ShowBatch(this));
            Body.Children.Add(view); Closed += (s, e) => view.Dispose();
        }
    }
    public sealed class BatchWindow : DialogWindow
    {
        public BatchWindow(DesktopController controller) : base("全部下载 · " + controller.BatchTitle, 1080, 680)
        {
            var view = new BatchView(controller); Body.Children.Add(view); Closed += (s, e) => view.Dispose();
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
}
