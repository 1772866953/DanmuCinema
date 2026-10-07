using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DanmuCinema.Desktop
{
    // Only the new workspace/task/presentation behavior. All API traffic is mocked.
    public static class DesignTests
    {
        sealed class Fixture : HttpMessageHandler
        {
            public int Requests, Comments;
            public bool Offline, FailComment;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); Requests++;
                if (Offline) throw new Exception("Fixture must remain offline");
                string path = request.RequestUri.AbsolutePath, json;
                if (path.Contains("/comment/")) { Comments++; if (FailComment) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); json = "{\"comments\":[{\"cid\":1,\"p\":\"1,1,16777215,0\",\"m\":\"离线测试弹幕\"}]}"; }
                else if (path.Contains("/bangumi/")) json = Json.Write(new { success = true, bangumi = new { episodes = Enumerable.Range(1,12).Select(i => new { episodeId = 100+i, episodeNumber=i, episodeTitle="第 "+i+" 集" }).ToArray() } });
                else if (path.Contains("/match")) json = "{\"success\":true,\"isMatched\":true,\"matches\":[{\"animeId\":10,\"episodeId\":101,\"animeTitle\":\"测试番剧\",\"episodeTitle\":\"第1话\",\"typeDescription\":\"动漫\"}]}";
                else json = "{\"success\":true,\"animes\":[{\"animeId\":10,\"animeTitle\":\"测试番剧\",\"typeDescription\":\"动漫\",\"episodeCount\":12}]}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
            }
        }
        static readonly List<string> report = new List<string>();
        public static int Run()
        {
            string original = Paths.Root, output = Path.Combine(original,"tests","output","design-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(output); Paths.Root=output;
            var app = new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown }; DesktopController controller=null; JellyfinApi api=null; DanmuCatalog catalog=null;
            try
            {
                System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall=false;
                Ui.InstallTheme(app); SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                var settings = new AppSettings { EnableDandan=true, EnableAnimeko=false, EnableBahamut=false, EnableExistingDanmu=false, DownloadIntervalSeconds=1, MediaFolder=Path.Combine(output,"videos") };
                Directory.CreateDirectory(Path.GetDirectoryName(DandanConfig.FilePath)); SettingsStore.AtomicWrite(DandanConfig.FilePath,Json.Write(new DandanConfig { AppId="design-fixture", EncryptedAppSecret=SettingsStore.Protect("fixture-only") }),false);
                var fixture=new Fixture(); api=new JellyfinApi(settings); catalog=new DanmuCatalog(settings,api,fixture);
                var folder=Path.Combine(settings.MediaFolder,"测试番剧"); Directory.CreateDirectory(folder);
                var files=Enumerable.Range(1,12).Select(i => { string path=Path.Combine(folder,"[Group] Long release name S01E"+i.ToString("D2")+" 1080p HEVC.mkv"); File.WriteAllText(path,"synthetic video "+i); return new Dictionary<string,object>{{"Id","fixture"+i},{"Path",path},{"Name","长文件名称"+i},{"SeriesName","测试番剧"},{"Type","Episode"},{"ParentIndexNumber",1},{"IndexNumber",i}}; }).ToArray();
                files[0]["SeriesId"]="fixture-series"; files[0]["SeriesPrimaryImageTag"]="fixture-image";
                string posterKey=DandanApiCache.Key("poster:"+settings.ServerId+":fixture-series:fixture-image");
                var poster=new RenderTargetBitmap(80,120,96,96,PixelFormats.Pbgra32); var art=new DrawingVisual();using(var drawing=art.RenderOpen()){drawing.DrawRectangle(Ui.Brush("#536E97"),null,new Rect(0,0,80,120));drawing.DrawEllipse(Ui.Brush("#68D391"),null,new Point(40,50),24,24);}poster.Render(art);var imageEncoder=new PngBitmapEncoder();imageEncoder.Frames.Add(BitmapFrame.Create(poster));string encoded;using(var stream=new MemoryStream()){imageEncoder.Save(stream);encoded=Convert.ToBase64String(stream.ToArray());}catalog.Cache.Write(posterKey,"poster","测试番剧",encoded,"测试番剧");
                controller=new DesktopController(app,settings,false,catalog); controller.Library.Replace(files); controller.ShowWindow(); controller.Window.Navigate("library"); Pause(300);
                var main=controller.Window.View; main.Width=1200; main.Height=820; main.UpdateLayout();
                var grid=Children<DataGrid>(main).Single(); var root=(LibraryRow)grid.Items[0];
                Check(root.IsFolder && root.Filename.Contains("12") && root.DisplayName=="测试番剧","文件夹首页保留层级并显示影片/弹幕数量");
                Check(root.Cover!=null && root.Cover.IsFrozen && fixture.Requests==0,"文件夹封面离线读取缓存，使用小尺寸冻结位图，不请求弹幕接口");
                root.Selected=true; Check(controller.Library.SelectedItems.Length==12,"文件夹复选仍可选择整季");
                Check(Children<Button>(main).Any(x => (x.Content as string)=="匹配已选影片" && x.IsVisible),"选中后显示批量操作栏");
                Check(grid.Columns[2].Visibility==Visibility.Collapsed && grid.Columns[5].Visibility==Visibility.Collapsed,"普通窗口自动折叠低频列，名称与弹幕列保持可见");
                Capture(main,Path.Combine(output,"library-folders.png"));
                var search=Children<HistoryInput>(main).Single(); search.Editor.Text="does-not-exist"; Pause(80); Check(grid.Items.Count==0 && controller.Library.SelectedItems.Length==12,"搜索仍只筛选列表，保留已有选择"); search.Editor.Clear(); Pause(80);
                var result=Wait(catalog.Search("测试番剧",true,true,1,"dandan")); var source=(Dictionary<string,object>)result.Items.Single(); var episodes=Wait(catalog.Episodes(source));
                controller.Session.Match=new MatchState { Item=files[0],Selected=files,Scope=DanmuMatchScope.Selection,Keyword="测试番剧",ServiceId="dandan",Sources=result.Items,Episodes=episodes,SourceIndex=0,EpisodeIndex=0,Open=true };
                controller.ReleaseWindow(); controller.ShowWindow(); Pause(300); main=controller.Window.View;
                Check(controller.Window.WorkspaceVisible && !app.Windows.OfType<MatchWindow>().Any(),"恢复匹配状态直接显示主窗口内工作区，不新增弹窗");
                var match=Children<MatchView>(main).Single(); var tabs=Children<TabControl>(match).Single();
                Check((DanmuMatchScope)((TabItem)tabs.SelectedItem).Tag==DanmuMatchScope.Selection,"整季/已选范围默认正确");
                Check(Children<ListBox>(match).Sum(x=>x.Items.Count)==13,"作品和完整12集同时展示");
                SearchHistory.Add("danmu","测试番剧"); fixture.Offline=true; int historyRequests=fixture.Requests;
                var history=Children<HistoryInput>(match).Single();history.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Pause(30);
                var popup=(Popup)typeof(HistoryInput).GetField("popup",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(history);
                Children<Button>(popup.Child).Single(x=>x.Content is TextBlock && ((TextBlock)x.Content).Text=="测试番剧").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Settle(controller);
                Check(controller.Session.Match.Sources.Length==1 && fixture.Requests==historyRequests,"新工作区中历史选择实际触发搜索，离线复用搜索缓存");
                Children<ListBox>(match).First(x=>x.Items.Count==1).SelectedIndex=0;Settle(controller);
                Check(controller.Session.Match.Episodes.Length==12 && fixture.Requests==historyRequests,"选择历史搜索作品后完整集数继续读取缓存，不增加请求");fixture.Offline=false;
                Capture(main,Path.Combine(output,"workspace-match.png"));
                Click(match,"预览已选影片并下载"); Settle(controller); Pause(100);
                var preview=Children<BatchView>(main).Single(); var previewGrid=Children<DataGrid>(preview).Single();
                Check(controller.BatchPlan.Count==12 && controller.BatchPlan.All(x=>x.Selected) && !app.Windows.OfType<BatchWindow>().Any(),"整季预览在同一工作区显示，12集默认勾选，无子弹窗");
                ((BatchRow)previewGrid.Items[0]).Selected=false; Check(!controller.BatchPlan[0].Selected,"预览复选框可操作"); ((BatchRow)previewGrid.Items[0]).Selected=true;
                Click(main,"来源与集数"); Check(Object.ReferenceEquals(match,Children<MatchView>(main).Single()) && controller.Session.Match.EpisodeIndex==0,"返回来源保留结果与集数选择");
                Click(main,"下载预览"); Capture(main,Path.Combine(output,"workspace-preview.png"));
                var back=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.XButton1){RoutedEvent=UIElement.PreviewMouseUpEvent};main.RaiseEvent(back);
                Check(back.Handled && match.Visibility==Visibility.Visible && preview.Visibility==Visibility.Collapsed,"鼠标侧键后退切回来源，保持选择");
                var forward=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.XButton2){RoutedEvent=UIElement.PreviewMouseUpEvent};main.RaiseEvent(forward);
                Check(forward.Handled && preview.Visibility==Visibility.Visible,"鼠标侧键前进切回下载预览");
                Check(controller.Window.TryNavigateHistory(System.Windows.Input.Key.Left,System.Windows.Input.ModifierKeys.Alt) && match.Visibility==Visibility.Visible && controller.Window.TryNavigateHistory(System.Windows.Input.Key.Right,System.Windows.Input.ModifierKeys.Alt) && preview.Visibility==Visibility.Visible,"Alt方向键与鼠标侧键共用工作区导航");
                main.Width=1020;main.Height=700;Pause(100);Capture(main,Path.Combine(output,"workspace-minimum.png"));
                Check(previewGrid.ActualHeight>150 && Children<Button>(preview).Single(x=>(x.Content as string)=="开始全部下载").IsVisible,"最小窗口下载预览保持列表与主要操作可见");main.Width=1200;main.Height=820;Pause(50);
                Click(main,"收起"); Pause(80); Check(!controller.Window.WorkspaceVisible && controller.Session.Match.Sources.Length==1,"收起工作区保留匹配数据");
                controller.Window.Match(files[0],DanmuMatchScope.Selection,files); Pause(150);
                Check(controller.Window.WorkspaceVisible && controller.Session.Match.Sources.Length==1,"同一选择重新打开沿用匹配结果"); Click(main,"收起");
                fixture.Offline=true; int before=fixture.Requests;
                var saved=Wait(catalog.Search("测试番剧",true,true,1,"dandan")); Check(saved.Items.Length==1 && fixture.Requests==before,"工作区搜索继续复用本地缓存，离线不增加请求"); fixture.Offline=false;
                int downloads=0; Wait(controller.RunBatch((episode,token)=>{downloads++; if(SmartMatching.RemoteNumber(episode)==2) throw new Exception("simulated failure"); return Task.FromResult("<i><d p='1,1,25,16777215,0,0,0,0'>fixture</d></i>");}));
                Check(controller.DownloadTasks.Count==12 && controller.DownloadTasks.Any(x=>x.Status.Contains("失败")),"批量任务进度与失败记录集中进入任务页");
                Check(controller.DownloadTasks.All(x=>x.Remote!=null),"批量任务记录保留原来源，单项重试不会另选接口");
                Check(File.Exists(Path.ChangeExtension(Json.Text(files[0],"Path"),".xml")) && !File.Exists(Path.ChangeExtension(Json.Text(files[0],"Path"),".xml")+".bak"),"批量下载仍保存同名XML并直接覆盖，不产生备份");
                int comments=fixture.Comments; controller.RetryBatch(true); Settle(controller);
                Check(fixture.Comments==comments+1 && controller.BatchPlan.Count(x=>x.Selected)==1 && controller.BatchPlan.All(x=>x.Status.StartsWith("已保存")),"仅重试失败项不会重复下载已完成集数");
                string originalXml=File.ReadAllText(Path.ChangeExtension(Json.Text(files[2],"Path"),".xml")); fixture.FailComment=true;
                Wait(controller.Execute(()=>controller.DownloadSingle(files[2],(Dictionary<string,object>)episodes[2]),false));
                Check(controller.FindTask(files[2]).Status=="下载失败" && File.ReadAllText(Path.ChangeExtension(Json.Text(files[2],"Path"),".xml"))==originalXml && !app.Windows.OfType<AlertWindow>().Any(),"单集失败进入任务页并保留原弹幕，工作区错误不叠加弹窗"); fixture.FailComment=false;
                controller.RetryTask(controller.FindTask(files[2])); Settle(controller);
                Check(controller.FindTask(files[2]).Status.StartsWith("已保存") && controller.FindTask(files[2]).Origin=="单集下载","单集失败可从统一任务记录重试");
                var prepFolder=Path.Combine(settings.MediaFolder,"提前准备测试"); Directory.CreateDirectory(prepFolder);
                var prep=Enumerable.Range(1,3).Select(i=>{ string path=Path.Combine(prepFolder,"S01E"+i.ToString("D2")+".mkv");File.WriteAllText(path,"fixture");return new Dictionary<string,object>{{"Id","prep"+i},{"Path",path},{"Name","准备影片"+i},{"SeriesName","准备测试"},{"Type","Episode"},{"IndexNumber",i}}; }).ToArray();
                var prepared=new List<string>(); var first=new TaskCompletionSource<bool>();
                Func<Dictionary<string,object>,CancellationToken,Task<bool>> prepare=async (item,token)=>{prepared.Add(Json.Text(item,"Id"));File.WriteAllText(Path.ChangeExtension(Json.Text(item,"Path"),".xml"),"fixture XML");first.TrySetResult(true);await Task.Delay(40,token);return true;};
                var job=controller.PrepareVideos(prep,false,prepare); Wait(first.Task); controller.PausePreparation(); Wait(job);
                Check(controller.PreparationPaused && prepared.Count==1 && File.Exists(Path.ChangeExtension(Json.Text(prep[0],"Path"),".xml")),"提前准备支持暂停且保留已保存弹幕");
                Wait(controller.PrepareVideos(prep,true,(item,token)=>{ if(!DesktopController.HasSidecar(item)) { prepared.Add(Json.Text(item,"Id"));File.WriteAllText(Path.ChangeExtension(Json.Text(item,"Path"),".xml"),"fixture XML"); } return Task.FromResult(true); }));
                Check(!controller.PreparationPaused && prepared.Distinct().Count()==3 && prepared.Count==3,"继续任务不重复识别或覆盖已经完成的文件");
                int progressRequests=fixture.Requests;Wait(controller.Automatic.PrepareQueuedItem(prep[0],CancellationToken.None));Pause(40);
                Check(controller.FindTask(prep[0]).Origin=="提前准备" && controller.FindTask(prep[0]).Status=="已就绪","提前准备按任务来源记录，不依赖前台队列状态");
                Wait(controller.Automatic.PrepareItem(prep[1],CancellationToken.None));Pause(40);
                Check(controller.FindTask(prep[1]).Origin=="播放准备" && fixture.Requests==progressRequests,"播放准备单独记录来源，已有XML无需接口请求");
                controller.Library.Replace(files.Concat(prep)); controller.Window.Navigate("tasks"); Pause(120);
                Check(Children<TasksPage>(main).Count()==1 && Children<DataGrid>(main).Single().Items.Count==15,"下载任务页面统一显示批量及提前准备记录");
                Capture(main,Path.Combine(output,"tasks.png"));
                controller.Window.Navigate("library"); Pause(120); var fileRow=new LibraryRow(controller.Library.Entries.First(),controller.Library.Selection,controller);
                Check(fileRow.DisplayName=="测试番剧 · 第 01 集" && fileRow.Filename.Contains("S01E01") && fileRow.State=="已就绪","影片显示简洁标题、原始文件名与就绪状态，未更改文件身份");
                Check(Ui.StatusBrush("下载失败")==Ui.Resource("StatusError") && Ui.StatusBrush("已就绪")==Ui.Resource("ServiceRunning"),"状态色统一区分就绪、失败和等待");
                string deletedXml=Path.ChangeExtension(Json.Text(files[11],"Path"),".xml"),savedXml=File.ReadAllText(deletedXml);File.Delete(deletedXml);controller.Library.Replace(files.Concat(prep));
                Check(new LibraryRow(controller.Library.Entries.Single(x=>Json.Text(x.Item,"Id")=="fixture12"),controller.Library.Selection,controller).State=="待匹配","删除弹幕后旧任务的成功记录不误报就绪");File.WriteAllText(deletedXml,savedXml);controller.Library.Replace(files.Concat(prep));
                main.Width=1020;main.Height=700;Pause(100);Capture(main,Path.Combine(output,"library-minimum.png"));
                var minimumGrid=Children<DataGrid>(main).Single();Check(minimumGrid.Columns.Where(x=>x.Visibility==Visibility.Visible).Sum(x=>x.ActualWidth)<=minimumGrid.ActualWidth+1,"最小窗口媒体库可见列不超出列表宽度");
                main.WindowState=WindowState.Maximized;Pause(150);Capture(main,Path.Combine(output,"library-maximized.png"));Check(minimumGrid.ActualHeight>200,"最大化时媒体库使用可用空间");main.WindowState=WindowState.Normal;main.Width=1200;main.Height=820;Pause(80);
                var posterEntry=catalog.Cache.Entries().Single(x=>x.Key==posterKey); Check(posterEntry.TypeLabel=="封面" && posterEntry.Anime=="测试番剧","封面纳入现有按番剧分组的缓存管理");
                var posterFile=Directory.GetFiles(catalog.Cache.DirectoryPath,posterKey+".json",SearchOption.AllDirectories).Single();var stale=Json.Read<ApiCacheEntry>(File.ReadAllText(posterFile));stale.CreatedUtc=DateTime.UtcNow.AddMonths(-4);File.WriteAllText(posterFile,Json.Write(stale));
                Check(catalog.Cache.Read(posterKey)==null,"统一三个月有效期也作用于封面缓存");catalog.Cache.SetRetention(0);Check(catalog.Cache.Read(posterKey)==encoded,"统一长期有效期可继续复用已存封面");catalog.Cache.SetRetention(3);
                Check(Directory.GetFiles(folder,"*.mkv").Length==12 && File.ReadAllText(Json.Text(files[0],"Path"))=="synthetic video 1","展示与匹配流程不重命名或修改视频");
                AutomationPeer[] retainedPeers; var weak=ReleaseWorkspace(controller,out retainedPeers); Settle(controller); for(int i=0;i<3;i++){ GC.Collect();GC.WaitForPendingFinalizers();Pause(100); }
                Check(!weak.IsAlive && controller.Window==null,"辅助功能接口保留控件时，托盘仍能释放工作区视觉树和回调，后台模型保留"); GC.KeepAlive(retainedPeers);
                report.Add("PASS: "+report.Count(x=>x.StartsWith("PASS "))+" redesign checks; real APIs never contacted."); return 0;
            }
            catch(Exception error) {report.Add("FAIL "+error);return 1;}
            finally {if(controller!=null){controller.ReleaseWindow();controller.Dispose();}if(catalog!=null)catalog.Dispose();if(api!=null)api.Dispose();app.Shutdown();Paths.Root=original;File.WriteAllLines(Path.Combine(output,"report.txt"),report);File.WriteAllText(Path.Combine(original,"tests","output","design-latest.txt"),output);}
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static WeakReference ReleaseWorkspace(DesktopController controller,out AutomationPeer[] retainedPeers)
        { controller.Window.Match(controller.Library.Entries[0].Item,DanmuMatchScope.Single,new[]{controller.Library.Entries[0].Item}); Pause(100);Settle(controller);var view=Children<MatchView>(controller.Window.View).Single(); retainedPeers=Children<FrameworkElement>(view).Where(x=>x is Button||x is TabControl||x is ListBox||x is TextBox||x is ComboBox||x is CheckBox).Select(FrameworkElementAutomationPeer.CreatePeerForElement).Where(x=>x!=null).ToArray();var reference=new WeakReference(view);controller.ReleaseWindow();return reference; }
        static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
        { if(root==null)yield break;if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Children<T>(VisualTreeHelper.GetChild(root,i)))yield return child; }
        static void Click(DependencyObject root,string label) {Children<Button>(root).Single(x=>(x.Content as string)==label).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));}
        static void Check(bool value,string name){if(!value)throw new Exception(name);report.Add("PASS "+name);}
        static void Settle(DesktopController controller){int count=0;while((controller.Busy||controller.Loading||controller.BatchRunning)&&count++<600)Pause(50);if(controller.Busy||controller.Loading||controller.BatchRunning)throw new TimeoutException();}
        static T Wait<T>(Task<T> task){while(!task.IsCompleted)Pause(20);return task.GetAwaiter().GetResult();}
        static void Wait(Task task){while(!task.IsCompleted)Pause(20);task.GetAwaiter().GetResult();}
        static void Pause(int ms){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Capture(FrameworkElement view,string path){view.UpdateLayout();var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}
    }
}
