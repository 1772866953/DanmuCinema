using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DanmuCinema.Desktop
{
    public static class UiPolishTests
    {
        static readonly List<string> report = new List<string>();
        static readonly FieldInfo Surface = typeof(DialogWindow).GetField("animatedSurface", BindingFlags.Instance | BindingFlags.NonPublic);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        public static int Run()
        {
            string original = Paths.Root, output = Path.Combine(Paths.TestOutputFor(original), "ui-polish"); Directory.CreateDirectory(output); Paths.Root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Paths.Root);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; DesktopController controller = null;
            try
            {
                System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext()); Ui.InstallTheme(app);
                var settings = new AppSettings { MediaFolder = Path.Combine(Paths.Root, "videos"), EnableDandan = false, EnableAnimeko = false, EnableBahamut = false, EnableExistingDanmu = false }; Directory.CreateDirectory(settings.MediaFolder);
                controller = new DesktopController(app, settings, false); controller.ShowWindow(); var main = controller.Window.View; main.Width = 1200; main.Height = 820;
                var file = new Dictionary<string, object> { { "Path", Path.Combine(settings.MediaFolder, "S01E01.mkv") }, { "Name", "界面测试" }, { "Id", "fixture" }, { "SeriesName", "界面测试" }, { "Type", "Episode" }, { "IndexNumber", 1 } }; File.WriteAllText(Json.Text(file, "Path"), "fixture"); controller.Library.Replace(new[] { file });
                foreach (int percent in new[] { 100, 120 })
                {
                    UiScale.Change(controller, percent); Pause(60);
                    foreach (string page in new[] { "overview", "library", "tasks", "connect", "setup", "settings", "schedule", "cache", "logs" })
                    {
                        controller.Window.Navigate(page); Pause(70); main.UpdateLayout();
                        var root = (FrameworkElement)main.FindName("ScaleRoot"); var scale = (ScaleTransform)root.LayoutTransform;
                        Check(scale.ScaleX == percent / 100.0 && root.CacheMode == null && main.Opacity == 1 && TextOptions.GetTextRenderingMode(root) == TextRenderingMode.ClearType, percent + "% " + page + "按布局重新绘制，无图片缓存或窗口透明缩放");
                        var viewport = Children<LayoutViewport>(main).First(); var panel = (Grid)viewport.Content;
                        Check(panel.ActualWidth >= 299 && panel.ActualHeight >= 119 && !Double.IsInfinity(panel.ActualHeight) && viewport.ScrollableWidth < 1, percent + "% " + page + "自适应有限布局，正文不横向裁切（" + panel.ActualWidth.ToString("F1") + "×" + panel.ActualHeight.ToString("F1") + "，横向溢出 " + viewport.ScrollableWidth.ToString("F1") + "）");
                        foreach (var grid in Children<DataGrid>(main)) Check(grid.ActualHeight > 40 && grid.ActualWidth > 200 && grid.EnableRowVirtualization, percent + "% " + page + "表格保留可见空间和行虚拟化");
                        if (page == "library")
                        {
                            var input = Children<HistoryInput>(main).Single(); input.Editor.Text = "界面";
                            input.Editor.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent }); Pause(180);
                            var history = (Popup)typeof(HistoryInput).GetField("popup", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input); var historySurface = (FrameworkElement)history.Child;
                            double historyPixels = historySurface.PointToScreen(new Point(historySurface.ActualWidth, 0)).X - historySurface.PointToScreen(new Point(0, 0)).X; double screenDpi = PresentationSource.FromVisual(main).CompositionTarget.TransformToDevice.M11;
                            Check(history.IsOpen && Math.Abs(historyPixels / historySurface.ActualWidth / screenDpi - percent / 100.0) < 0.05, "历史下拉框与界面缩放一致 " + percent); history.IsOpen = false;
                            var clear = Children<Button>(main).Single(x => AutomationProperties.GetName(x) == "清除"); Check(clear.Content is WrapPanel && clear.Style == Ui.Resource("CompactAction") && clear.ToolTip != null, "清除按钮使用图标与紧凑圆角，并有功能提示 " + percent);
                            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(input.Editor.Text == "" && !clear.IsEnabled, "清除筛选保留输入逻辑，空文本时禁用 " + percent);
                        }
                        if (page == "logs")
                        {
                            var log = Children<TextBox>(main).Single(); var row = Children<Button>(main).Single(x => (x.Content as string) == "打开服务器日志"); var origin = row.TranslatePoint(new Point(0, 0), log);
                            Check(origin.Y - log.ActualHeight >= 13, "日志下方按钮与文本边框间距至少14逻辑像素 " + percent);
                        }
                        if (percent == 100 && page == "logs" || percent == 120 && (page == "settings" || page == "library")) Save(main, Path.Combine(output, page + "-" + percent + ".png"));
                    }
                    controller.Window.Navigate("settings"); Pause(50); var choice = Children<ComboBox>(main).Single(x => AutomationProperties.GetName(x) == "界面缩放");
                    choice.IsDropDownOpen = true; Pause(180); var popup = choice.Template.FindName("PART_Popup", choice) as Popup;
                    Check(popup != null && popup.IsOpen && popup.Child.IsVisible, "缩放下拉框正常打开 " + percent);
                    var child = (FrameworkElement)popup.Child; var physical = child.PointToScreen(new Point(child.ActualWidth, 0)).X - child.PointToScreen(new Point(0, 0)).X; var dpi = PresentationSource.FromVisual(main).CompositionTarget.TransformToDevice.M11;
                    Check(Math.Abs(physical / child.ActualWidth / dpi - percent / 100.0) < 0.05, "标准下拉框与界面缩放一致 " + percent); choice.IsDropDownOpen = false;
                    var menu = new ContextMenu { PlacementTarget = choice, Placement = PlacementMode.Bottom }; menu.Items.Add(new MenuItem { Header = "界面测试菜单" }); menu.IsOpen = true; Pause(180); CheckPopup(menu, main, percent, "右键菜单"); menu.IsOpen = false;
                    var tip = new ToolTip { Content = Ui.Text("仅用于验证提示缩放"), PlacementTarget = choice, Placement = PlacementMode.Bottom }; tip.IsOpen = true; Pause(100); CheckPopup(tip, main, percent, "悬停提示"); tip.IsOpen = false;
                    var dialog = new SourcesWindow(controller); controller.Window.Track(dialog); Pause(240);
                    var surface = (Grid)Surface.GetValue(dialog); var border = Children<SmoothBorder>(surface).First();
                    Check((border.CornerRadius.TopLeft == 8 || border.CornerRadius.TopLeft == 14) && ((SolidColorBrush)dialog.Background).Color != ((SolidColorBrush)Ui.Resource("Canvas")).Color && dialog.Opacity == 1, "管理接口弹窗统一圆角和独立配色，保留不透明窗口 " + percent);
                    int corner; int result = DwmGetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(dialog).Handle, 33, out corner, sizeof(int)); if (result == 0) Check(corner == 2, "原生弹窗启用圆角 " + percent);
                    var nested = new DialogWindow("子弹窗测试", 780, 540); controller.Window.Track(nested, dialog); Pause(240); Check(((ScaleTransform)Children<LayoutViewport>(nested).Single().Parent.GetValue(FrameworkElement.LayoutTransformProperty)).ScaleX == percent / 100.0, "子弹窗同步当前缩放 " + percent);
                    nested.Close(); Pause(220); Check(dialog.IsVisible && main.IsVisible && surface.Opacity == 1, "子弹窗关闭保留父级并结束淡出 " + percent);
                    if (percent == 100 || percent == 120) Save(dialog, Path.Combine(output, "sources-" + percent + ".png"));
                    dialog.Close(); Pause(220); Check(main.IsVisible && main.Opacity == 1 && !dialog.IsVisible, "弹窗关闭后主界面保持不透明 " + percent);
                    var alert = new AlertWindow("缩放提示", String.Join("\n", Enumerable.Repeat("仅验证长提示文字和底部操作布局，不执行任何操作。", 12)), true, "模拟确认", "模拟取消") { Owner = main };
                    var alertTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) }; alertTimer.Tick += (s, e) => { alertTimer.Stop(); var cancel = Children<Button>(alert).Single(x => (x.Content as string) == "模拟取消"); var point = cancel.TranslatePoint(new Point(0, cancel.ActualHeight), alert); Check(cancel.IsVisible && point.Y <= alert.ActualHeight, "长文本提示底部操作可见 " + percent); cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
                    alertTimer.Start(); Check(alert.ShowDialog() == false && main.IsVisible, "提示弹窗正常淡出并保留取消结果 " + percent);
                    controller.Window.Navigate("library");
                    controller.Session.Match = new MatchState { Item = file, Selected = new[] { file }, Scope = DanmuMatchScope.Single, Keyword = "", Sources = new object[] { new Dictionary<string, object> { { "Id", "fixture" }, { "Name", "界面测试" } } } };
                    controller.Window.Match(file, DanmuMatchScope.Single, new[] { file }); Pause(240); var pane = Children<FloatingPane>(main).Single();
                    pane.MoveBy(15, 10); pane.ResizeBy("bottom-right", -20, -10); Pause(80);
                    Check(pane.Bounds.Width > 0 && pane.Bounds.Height > 0 && pane.Opacity == 1 && Children<LayoutViewport>(pane).Single().ScrollableWidth < 1, "匹配浮层可移动缩放，正文布局随比例适应 " + percent);
                    controller.BatchPlan = new List<BatchEntry> { new BatchEntry { Local = file, Selected = true, Number = 1, Status = "待确认" } }; controller.Window.ShowBatch(); Pause(300);
                    Check(Children<BatchView>(main).Single().IsVisible && Children<DataGrid>(pane).Single().ActualHeight > 40, "嵌套下载预览保留完整表格空间 " + percent);
                    var previewGrid = Children<DataGrid>(pane).Single(); var previewScroll = Ui.Child<ScrollViewer>(previewGrid); var previewBox = Children<CheckBox>(previewGrid).First(x => x.IsVisible); var previewCell = Ui.Ancestor<DataGridCell>(previewBox); var boxPoint = previewBox.TranslatePoint(new Point(0, 0), previewCell);
                    var gridPoint = previewBox.TranslatePoint(new Point(0, 0), previewGrid);
                    Check(previewScroll.HorizontalOffset == 0 && boxPoint.X >= -0.5 && boxPoint.X + previewBox.ActualWidth <= previewCell.ActualWidth + 0.5 && gridPoint.X >= 0 && gridPoint.X + previewBox.ActualWidth <= previewGrid.ActualWidth, "预览首列复选框完整且初始滚动位置正确 " + percent + "（单元格 " + previewCell.ActualWidth.ToString("F1") + "，位置 " + gridPoint.X.ToString("F1") + "）");
                    if (percent == 100 || percent == 120) Save(main, Path.Combine(output, "match-" + percent + ".png"));
                    controller.Window.RequestWorkspaceClose(); Pause(300); Check(controller.Window.WorkspaceVisible && Children<MatchView>(main).Single().IsVisible, "缩放后关闭子预览仅返回父页面 " + percent);
                    controller.Window.RequestWorkspaceClose(); Pause(220); Check(!controller.Window.WorkspaceVisible && main.IsVisible && main.Opacity == 1, "缩放后浮层淡出完成，不改变主窗口透明度 " + percent);
                }
                controller.Window.Navigate("setup"); UiScale.Change(controller, 100); Pause(120); var primary = Children<Button>(main).Single(x => (x.Content as string) == "安装 / 修复运行组件"); primary.ApplyTemplate();
                var hover = (FrameworkElement)primary.Template.FindName("hover", primary); var press = (FrameworkElement)primary.Template.FindName("pressed", primary); primary.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent }); Pause(35);
                Check(!SystemParameters.ClientAreaAnimation || hover.Opacity > 0 && hover.Opacity < 1, "蓝色按钮悬停有渐变中间帧"); Pause(180);
                primary.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent }); Pause(25); Check(!SystemParameters.ClientAreaAnimation || press.Opacity > 0 && press.Opacity < 1, "蓝色按钮按下有独立动画反馈");
                primary.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent }); primary.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent }); Pause(230);
                Check(hover.Opacity == 0 && press.Opacity == 0 && primary.Opacity == 1 && primary.RenderTransform == Transform.Identity, "按钮释放和移出后归位，文字不缩放或透明"); Check(primary.ToolTip != null, "安装运行组件提供功能提示");
                UiScale.Change(controller, 120); controller.ReleaseWindow(); controller.ShowWindow(); Pause(160); main = controller.Window.View;
                Check(SettingsStore.Load().UiScalePercent == 120 && ((ScaleTransform)((FrameworkElement)main.FindName("ScaleRoot")).LayoutTransform).ScaleX == 1.2, "缩放持久化并在托盘恢复的新窗口生效");
                controller.Window.Navigate("settings"); Pause(80); var scaleChoice = Children<ComboBox>(main).Single(x => AutomationProperties.GetName(x) == "界面缩放"); scaleChoice.SelectedIndex = 1; Pause(140);
                Check(settings.UiScalePercent == 120 && TextOptions.GetTextFormattingMode((DependencyObject)main.FindName("PageHost")) == TextFormattingMode.Ideal, "在当前页面直接改比例即重新排版，无需切页修复字体");
                main.Width = 1020; main.Height = 700; Pause(120);
                foreach (string smallPage in new[] { "library", "tasks", "settings", "schedule", "logs" }) { controller.Window.Navigate(smallPage); Pause(100); var small = Children<LayoutViewport>(main).First(); Check(small.ScrollableWidth < 1 && ((Grid)small.Content).ActualWidth > 180, "最小窗口120%布局仍可访问 " + smallPage); }
                Save(main, Path.Combine(output, "small-120.png"));
                controller.Window.Navigate("library"); Pause(120); var remembered = Children<LayoutViewport>(main).First(); remembered.ScrollToVerticalOffset(80); Pause(70); double expectedOffset = remembered.VerticalOffset;
                controller.Window.Navigate("logs"); Pause(70); controller.Window.Navigate("library"); Pause(120); Check(Math.Abs(remembered.VerticalOffset - expectedOffset) < 1, "高比例下外层滚动位置随页面记忆，切换后恢复");
                Check(Json.Read<AppSettings>("{}").UiScalePercent == 100, "旧配置缺少缩放字段时仍默认100%");
                var invalid = Json.Read<AppSettings>(Json.Write(settings)); invalid.UiScalePercent = 110; bool rejected = false; try { invalid.Validate(); } catch (ArgumentException) { rejected = true; } Check(rejected, "只接受两个合法缩放档位");
                report.Add("PASS: " + report.Count(x => x.StartsWith("PASS ")) + " current UI checks; no real API/power/download operations."); return 0;
            }
            catch (Exception error) { report.Add("FAIL " + error); return 1; }
            finally { if (controller != null) { controller.ReleaseWindow(); controller.Dispose(); } app.Shutdown(); Paths.Root = original; File.WriteAllLines(Path.Combine(output, "report.txt"), report); }
        }
        static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject { if (root == null) yield break; if (root is T) yield return (T)root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Children<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
        static void CheckPopup(FrameworkElement popup, Window main, int percent, string name) { double pixels = popup.PointToScreen(new Point(popup.ActualWidth, 0)).X - popup.PointToScreen(new Point(0, 0)).X; double dpi = PresentationSource.FromVisual(main).CompositionTarget.TransformToDevice.M11; Check(popup.IsVisible && Math.Abs(pixels / popup.ActualWidth / dpi - percent / 100.0) < 0.05, name + "与界面缩放一致 " + percent); }
        static void Check(bool condition, string name) { if (!condition) throw new Exception(name); report.Add("PASS " + name); }
        static void Pause(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        static void Save(FrameworkElement view, string path) { view.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(path)) encoder.Save(file); }
    }
}
