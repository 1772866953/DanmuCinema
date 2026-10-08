using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace DanmuCinema.Desktop
{
    public sealed class TaskRow : INotifyPropertyChanged
    {
        public readonly DownloadTask Task;
        public TaskRow(DownloadTask task) { Task = task; }
        public string Name { get { return Task.Name; } }
        public string Filename { get { return Task.Filename; } }
        public string Origin { get { return Task.Origin; } }
        public string Status { get { return Task.Status; } }
        public string Detail { get { return Task.Detail; } }
        public bool Retryable { get { return Task.Retryable; } }
        public Brush Color { get { return Ui.StatusBrush(Status); } }
        public event PropertyChangedEventHandler PropertyChanged;
        public void Notify() { var handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(null)); }
    }
    public sealed class TasksPage : Grid, IDisposable
    {
        readonly DesktopController controller;
        readonly DataGrid grid;
        readonly TextBlock status, empty;
        readonly Button pause, resume, selected, all, stopBatch, cancel;
        bool disposed;
        readonly ObservableCollection<TaskRow> taskRows = new ObservableCollection<TaskRow>();
        public TasksPage(DesktopController controller)
        {
            this.controller = controller; RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            selected = Ui.Button("为已选影片准备", () => controller.StartPreparation(controller.Library.SelectedItems), true);
            selected.ToolTip = "为媒体库已勾选的影片补齐缺少的 XML，先用缓存，再按 hash 和文件名识别；已有弹幕保留。";
            all = Ui.Button("补齐媒体库弹幕", () => controller.StartPreparation(controller.Library.Entries.Select(x => x.Item)));
            all.ToolTip = "仅处理缺少 XML 的影片；不覆盖已有弹幕，不自动启用任务。";
            pause = Ui.Button("暂停准备", controller.PausePreparation);
            pause.ToolTip = "暂停自动补齐队列，保留已下载的弹幕与未完成队列。";
            resume = Ui.Button("继续准备", () => controller.StartPreparation(new System.Collections.Generic.Dictionary<string, object>[0], true));
            resume.ToolTip = "继续上次暂停的自动补齐队列，跳过已完成的影片。";
            stopBatch = Ui.Button("停止并保留弹幕", controller.CancelBatch); stopBatch.ToolTip = "停止手动整季下载；保留已保存的 XML，可以从下载预览继续。";
            cancel = Ui.Button("取消并删除弹幕", async () => await controller.CancelCurrentDownloads()); cancel.Foreground = (Brush)Ui.Resource("StatusError");
            cancel.ToolTip = "取消当前这批下载，删除本次任务所有影片的同名 XML，包括已下载和等待下载的影片。";
            var interval = Ui.Combo(new[] { "1 秒", "3 秒", "5 秒", "10 秒", "30 秒" }, 0, 100); int[] seconds = { 1, 3, 5, 10, 30 }; interval.SelectedIndex = Math.Max(0, Array.IndexOf(seconds, controller.Settings.DownloadIntervalSeconds));
            interval.SelectionChanged += (s, e) => { if (interval.SelectedIndex < 0) return; int old = controller.Settings.DownloadIntervalSeconds; try { controller.Settings.DownloadIntervalSeconds = seconds[interval.SelectedIndex]; SettingsStore.Save(controller.Settings); } catch { controller.Settings.DownloadIntervalSeconds = old; } };
            var label = Ui.Text("影片间隔"); label.Margin = new Thickness(8, 0, 8, 10); interval.ToolTip = "整季下载和提前准备处理两个影片之间的间隔。所有查询仍优先使用缓存。";
            var preview = Ui.Button("批量下载预览", () => controller.Window.ShowBatch()); preview.ToolTip = "查看当前手动下载批次的集数对应关系、勾选范围和进度。";
            var header = Ui.Stack(Ui.Row(selected, all, pause, resume, label, interval, preview, stopBatch, cancel), Ui.Text("自动准备只补齐缺少的弹幕；停止保留文件，取消则清理当前整批影片的同名 XML。", "Note")); Children.Add(header);
            grid = new DataGrid { IsReadOnly = true, RowHeight = 64, ItemsSource = taskRows };
            grid.Columns.Add(new DataGridTemplateColumn { Header = "影片", Width = new DataGridLength(2, DataGridLengthUnitType.Star), MinWidth = 200, CellTemplate = Template("<StackPanel ToolTip='{Binding Filename}'><TextBlock Text='{Binding Name}' TextWrapping='NoWrap' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding Filename}' Foreground='{DynamicResource Muted}' FontSize='11' Margin='0,4,0,0' TextWrapping='NoWrap' TextTrimming='CharacterEllipsis'/></StackPanel>") });
            grid.Columns.Add(new DataGridTextColumn { Header = "任务", Binding = new Binding("Origin"), Width = 90 });
            grid.Columns.Add(new DataGridTemplateColumn { Header = "状态", Width = 115, CellTemplate = Template("<TextBlock Text='{Binding Status}' Foreground='{Binding Color}'/>") });
            grid.Columns.Add(new DataGridTextColumn { Header = "说明", Binding = new Binding("Detail"), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star), MinWidth = 140 });
            grid.Columns.Add(new DataGridTemplateColumn { Header = "操作", Width = 135, CellTemplate = Template("<StackPanel Orientation='Horizontal'><Button Content='重试' Tag='retry' IsEnabled='{Binding Retryable}' Style='{DynamicResource TextAction}'/><Button Content='匹配' Tag='match' Style='{DynamicResource TextAction}'/></StackPanel>") });
            grid.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) => { var button = e.OriginalSource as Button; var row = button == null ? null : button.DataContext as TaskRow; if (row == null) return; if ((string)button.Tag == "retry") { SurfaceMotion.FadeIn(Ui.Ancestor<DataGridRow>(button)); controller.RetryTask(row.Task); } else controller.Window.Match(row.Task.Item, DanmuMatchScope.Single, new[] { row.Task.Item }); e.Handled = true; }));
            var body = new Grid(); body.Children.Add(grid); empty = Ui.Text("暂无任务\n选择影片准备弹幕，或在 iPad 播放时自动准备。", "Note"); empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center; body.Children.Add(empty); Grid.SetRow(body, 1); Children.Add(body);
            status = Ui.Text("", "Note"); Grid.SetRow(status, 2); Children.Add(status);
            controller.Changed += Render; Render();
        }
        static DataTemplate Template(string content) { return (DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" + content + "</DataTemplate>"); }
        void Render()
        {
            if (disposed) return;
            var jobs = controller.DownloadTasks.ToArray();
            foreach (var removed in taskRows.Where(x => !jobs.Contains(x.Task)).ToArray()) taskRows.Remove(removed);
            foreach (var job in jobs) if (!taskRows.Any(x => Object.ReferenceEquals(x.Task, job))) taskRows.Add(new TaskRow(job));
            foreach (var row in taskRows) row.Notify();
            bool idle = !controller.Busy && !controller.Loading && !controller.BatchRunning && !controller.CancellingDownloads;
            selected.IsEnabled = idle && controller.Library.SelectedItems.Length > 0; all.IsEnabled = idle && controller.Library.Entries.Length > 0;
            pause.IsEnabled = controller.Preparing; resume.IsEnabled = idle && controller.PreparationPaused;
            stopBatch.IsEnabled = controller.BatchRunning && !controller.Preparing;
            cancel.IsEnabled = controller.CanCancelDownloads;
            empty.Visibility = jobs.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            status.Text = controller.PreparationStatus ?? "单集、整季和自动播放准备的任务集中显示于此。";
        }
        public void Dispose() { if (disposed) return; disposed = true; controller.Changed -= Render; grid.ItemsSource = null; Ui.ReleaseVisualTree(this); Children.Clear(); }
    }
    public sealed partial class ShellWindow
    {
        FrameworkElement BuildTasks() { var view = new TasksPage(controller); pageResources.Add(view); return view; }
    }
}
