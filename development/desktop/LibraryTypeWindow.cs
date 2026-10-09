using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DanmuCinema.Desktop
{
    public sealed class LibraryTypeWindow : DialogWindow
    {
        readonly DesktopController controller;
        readonly ComboBox libraries, mode;
        readonly TextBlock detail, status;
        readonly Button apply, refresh;
        readonly ProgressBar progress;
        bool closed, loading;
        public LibraryTypeWindow(DesktopController controller) : base("调整媒体库识别方式", 860, 590)
        {
            this.controller = controller;
            libraries = Ui.Combo(new string[0], -1, Double.NaN);
            mode = Ui.Combo(MediaAuto.Labels, 0, Double.NaN);
            libraries.ToolTip = "选择服务器中已经添加的媒体库。";
            mode.ToolTip = "自动识别允许电影、电视剧和动漫放在同一个媒体库；手动模式用于纠正分类。";
            detail = Ui.Text("正在读取媒体库…", "Note"); status = Ui.Text("", "Note");
            progress = new ProgressBar { Style = (Style)Ui.Resource("LibraryScanProgress"), Height = 5, Maximum = 100, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 12) };
            apply = Ui.Button("应用并重新扫描", async () =>
            {
                var choice = libraries.SelectedItem as Choice;
                if (choice == null || mode.SelectedIndex < 0) return;
                var selected = choice.Data; string target = MediaAuto.Modes[mode.SelectedIndex];
                await controller.Execute(() => controller.ChangeLibraryType(selected, target), false);
                if (!closed && !controller.Status.StartsWith("调整失败") && controller.Status.Contains("完成"))
                { string result = controller.Status; await Load(); if (!closed) status.Text = result; }
            }, true);
            apply.ToolTip = "保存此媒体库的识别方式，并自动扫描和更新列表。";
            refresh = Ui.Button("刷新媒体库列表", async () => await Load());
            libraries.SelectionChanged += (s,e) => SelectionChanged();
            var footer = Ui.Stack(progress, status, Ui.Row(apply, refresh, Ui.Button("关闭", Close)));
            var dock = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
            dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Ui.Stack(
                Ui.Card("已添加的媒体库", libraries, detail), Ui.Card("识别方式", mode,
                    Ui.Text("自动识别：电影按单部展示；电视剧和动漫按作品、季度、集数组织。支持带发布组标签的子目录。", "Note")),
                Ui.Text("调整会重建服务器的分类索引，部分封面、识别信息或播放记录可能需要重新获取。完成后请刷新客户端媒体库。视频和现有弹幕文件保持原样。", "Note")) });
            Body.Children.Add(dock); controller.Changed += Render;
            Loaded += async (s,e) => await Load();
            Closed += (s,e) => { closed = true; controller.Changed -= Render; };
            Render();
        }
        async Task Load()
        {
            if (closed || loading) return;
            loading = true; Render();
            try
            {
                while (!closed && (controller.Busy || controller.Loading || controller.BatchRunning)) await Task.Delay(100);
                if (closed) return;
                var manager = new LibraryTypeManager(controller.Services.Api); await manager.RecoverPending();
                var choices = (await manager.Libraries()).Where(x => MediaAuto.Valid(Json.Text(x,"CollectionType") == "" ? "mixed" : Json.Text(x,"CollectionType")))
                    .Select(x => new Choice { Data=x, Label=Json.Text(x,"Name")+" · "+MediaAuto.Label(Json.Text(x,"CollectionType")) }).ToArray();
                if (closed) return;
                string previous = libraries.SelectedItem is Choice ? Json.Text(((Choice)libraries.SelectedItem).Data,"Name") : controller.Settings.LibraryName;
                libraries.ItemsSource=choices; libraries.SelectedItem=choices.FirstOrDefault(x=>Json.Text(x.Data,"Name")==previous) ?? choices.FirstOrDefault();
                status.Text=choices.Length==0 ? "尚未添加视频媒体库，请先到首次设置添加媒体目录。" : "请选择媒体库及识别方式。";
            }
            catch(Exception error) { if (!closed) status.Text=error.Message; }
            finally { loading=false; Render(false); }
        }
        void SelectionChanged()
        {
            var choice=libraries.SelectedItem as Choice;
            if(choice==null) { detail.Text="未选择媒体库。"; return; }
            string type=Json.Text(choice.Data,"CollectionType"); mode.SelectedIndex=Array.IndexOf(MediaAuto.Modes,type==""?"mixed":type);
            detail.Text="当前方式："+MediaAuto.Label(type)+"\n"+String.Join("\n",Json.Array(choice.Data,"Locations").Select(Convert.ToString));
        }
        void Render() { Render(true); }
        void Render(bool updateStatus)
        {
            if(closed) return;
            bool idle=!loading && !controller.Busy && !controller.Loading && !controller.BatchRunning;
            apply.IsEnabled=idle && libraries.SelectedItem!=null; refresh.IsEnabled=idle; libraries.IsEnabled=mode.IsEnabled=idle;
            var current=controller.LibraryProgress;
            progress.Visibility=loading || controller.Busy || current!=null ? Visibility.Visible : Visibility.Collapsed;
            progress.IsIndeterminate=progress.Visibility==Visibility.Visible && (current==null || !current.Percent.HasValue);
            progress.Value=current!=null && current.Percent.HasValue ? current.Percent.Value : 0;
            if(updateStatus && !loading) status.Text=controller.Status;
        }
    }
}
