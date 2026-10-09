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
using System.Windows.Threading;
using DanmuCinema;
using DanmuCinema.Desktop;

public static class LibraryScanTests
{
    static readonly List<string> report = new List<string>();
    static void Check(bool value, string text) { if (!value) throw new Exception(text); report.Add("PASS: " + text); }
    static string Snapshot(string state, string execution = "old", string result = "Completed", object percent = null)
    { return Json.Write(new { Id="scan", Key="RefreshLibrary", State=state, CurrentProgressPercentage=percent, LastExecutionResult=new { StartTimeUtc=execution, EndTimeUtc=execution, Status=result } }); }
    sealed class Fixture : HttpMessageHandler
    {
        public string Initial = Snapshot("Idle");
        public readonly Queue<string> States = new Queue<string>();
        public int Posts, Reads, Polls, Items;
        public bool Finished, Paginated, Gate;
        public TaskCompletionSource<bool> Release = new TaskCompletionSource<bool>();
        public string Folder;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path=request.RequestUri.AbsolutePath; string content;
            if(path=="/ScheduledTasks") { Reads++; content="["+Initial+"]"; }
            else if(request.Method==HttpMethod.Post && path=="/ScheduledTasks/Running/scan") { Posts++; content=""; }
            else if(path=="/ScheduledTasks/scan")
            {
                Polls++;
                if(Gate && Polls==2)
                {
                    using(token.Register(()=>Release.TrySetCanceled())) await Release.Task;
                }
                content=States.Count>0 ? States.Dequeue() : Snapshot("Idle");
                if(content=="ERROR") return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                Finished=content.Contains("new") && content.Contains("Completed") && content.Contains("Idle");
            }
            else if(path=="/Items")
            {
                Items++; if(!Finished) throw new Exception("Items read before scan completion");
                bool last=request.RequestUri.Query.Contains("StartIndex=250");
                int count=Paginated && !last ? 250 : 1;
                content=Json.Write(new { TotalRecordCount=Paginated ? 251 : 1, Items=Enumerable.Range(last?251:1,count).Select(n=>new { Id=n.ToString(),Type="Movie",Name="Show ["+n.ToString("D2")+"]", Path=Path.Combine(Folder,"Show ["+n.ToString("D2")+"].mkv") }).ToArray() });
            }
            else throw new Exception("Unexpected route: "+path);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(content) };
        }
    }
    static AppSettings Settings() { return new AppSettings { EncryptedToken=SettingsStore.Protect("fixture"),EnableDandan=false,EnableExistingDanmu=false,EnableAnimeko=false,EnableBahamut=false }; }
    static Task Instant(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(0); }
    static bool Reject(Fixture fixture, string folder)
    {
        fixture.Folder=folder;
        using(var api=new JellyfinApi(Settings(),fixture))
        { try { new LibraryScanner(api,Instant).Load(true,null,CancellationToken.None).GetAwaiter().GetResult(); return false; } catch(InvalidOperationException) { return true; } catch(TimeoutException) { return true; } }
    }
    static void PumpUntil(Func<bool> done, string phase = "completion")
    {
        var limit=DateTime.UtcNow.AddSeconds(8);
        while(!done()) { if(DateTime.UtcNow>limit) throw new TimeoutException("UI test timed out: " + phase); var frame=new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false)); Dispatcher.PushFrame(frame); Thread.Sleep(10); }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Paths.Root=Path.GetFullPath(args[0]); Directory.CreateDirectory(Paths.Root);
            var fixture=new Fixture { Folder=Paths.Root,Paginated=true };
            foreach(string s in new[]{Snapshot("Idle"),Snapshot("Running",percent:35.0),Snapshot("Running",percent:100.0),Snapshot("Idle","new")}) fixture.States.Enqueue(s);
            var progress=new List<LibraryLoadProgress>();
            using(var api=new JellyfinApi(Settings(),fixture))
            {
                var items=new LibraryScanner(api,Instant).Load(true,progress.Add,CancellationToken.None).GetAwaiter().GetResult();
                Check(items.Length==251 && fixture.Posts==1 && fixture.Items==2,"Queued scan completes before paginated list is read");
                Check(progress.Any(p=>p.Percent==35) && progress.Any(p=>p.Message.Contains("等待开始")),"Waiting and real scan percentage are reported");
                Check(progress.Any(p=>p.Message.Contains("250 / 251")) && progress.Any(p=>p.Message.Contains("251 / 251")),"Paged list reports read counts through completion");
            }
            fixture=new Fixture { Folder=Paths.Root }; fixture.States.Enqueue(Snapshot("Idle","new"));
            using(var api=new JellyfinApi(Settings(),fixture)) { var items=new LibraryScanner(api,Instant).Load(true,null,CancellationToken.None).GetAwaiter().GetResult(); Check(items.Length==1 && fixture.Polls==1,"Fast completion detected without ever observing Running"); }
            fixture=new Fixture { Folder=Paths.Root,Initial=Snapshot("Running") }; fixture.States.Enqueue(Snapshot("Running",percent:60)); fixture.States.Enqueue(Snapshot("Idle","new"));
            using(var api=new JellyfinApi(Settings(),fixture)) { new LibraryScanner(api,Instant).Load(false,null,CancellationToken.None).GetAwaiter().GetResult(); Check(fixture.Posts==0 && fixture.Polls==2,"Refresh joins an existing scan instead of publishing an old list"); }
            fixture=new Fixture { Folder=Paths.Root }; fixture.States.Enqueue("ERROR"); fixture.States.Enqueue("ERROR"); fixture.States.Enqueue(Snapshot("Idle","new"));
            using(var api=new JellyfinApi(Settings(),fixture)) { new LibraryScanner(api,Instant).Load(true,null,CancellationToken.None).GetAwaiter().GetResult(); Check(fixture.Items==1 && fixture.Polls==3,"Transient progress errors reconnect and finish"); }
            fixture=new Fixture(); fixture.States.Enqueue(Snapshot("Idle","new","Failed")); Check(Reject(fixture,Paths.Root) && fixture.Items==0,"Failed scan does not publish a completed list");
            fixture=new Fixture(); fixture.States.Enqueue(Snapshot("Idle","new","Cancelled")); Check(Reject(fixture,Paths.Root) && fixture.Items==0,"Cancelled scan does not publish success");
            fixture=new Fixture(); fixture.States.Enqueue("ERROR");fixture.States.Enqueue("ERROR");fixture.States.Enqueue("ERROR"); Check(Reject(fixture,Paths.Root) && fixture.Polls==3,"Repeated connection failures terminate progress monitoring");
            fixture=new Fixture(); Check(Reject(fixture,Paths.Root) && fixture.Polls==30 && fixture.Items==0,"Old successful execution cannot be mistaken for a new completed scan");
            using(var api=new JellyfinApi(Settings(),new Fixture { Folder=Paths.Root })) using(var cancellation=new CancellationTokenSource())
            { cancellation.Cancel(); bool cancelled=false; try { new LibraryScanner(api,Instant).Load(true,null,cancellation.Token).GetAwaiter().GetResult(); } catch(OperationCanceledException) { cancelled=true; } Check(cancelled,"Cancellation stops monitoring without reading items"); }

            var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown }; Ui.InstallTheme(app);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            fixture=new Fixture { Folder=Paths.Root,Gate=true }; fixture.States.Enqueue(Snapshot("Running",percent:42));fixture.States.Enqueue(Snapshot("Idle","new"));
            using(var controller=new DesktopController(app,Settings(),false,null,fixture))
            {
                controller.Library.Replace(new[]{new Dictionary<string,object>{{"Id","old"},{"Name","Old list"},{"Path",Path.Combine(Paths.Root,"Old.mkv")}}});
                var shell=new ShellWindow(controller); var progressBar=(ProgressBar)shell.View.FindName("LibraryScanProgress"); progressBar.ApplyTemplate();
                var operation=controller.ScanLibrary(); PumpUntil(()=>fixture.Polls>=2 || operation.IsCompleted,"running"); if(operation.IsCompleted) operation.GetAwaiter().GetResult();
                Check(controller.Loading && controller.LibraryProgress.Percent==42 && controller.Status.Contains("扫描") && fixture.Items==0,"Controller remains loading while server scans and preserves old entries");
                Check(progressBar.Visibility==Visibility.Visible && !progressBar.IsIndeterminate && progressBar.Value==42,"WPF shows the real percentage while scanning");
                shell.Release(); shell=new ShellWindow(controller); progressBar=(ProgressBar)shell.View.FindName("LibraryScanProgress"); progressBar.ApplyTemplate();
                Check(progressBar.Visibility==Visibility.Visible && progressBar.Value==42,"Recreated foreground restores the in-process scan progress");
                progressBar.IsIndeterminate=true; progressBar.ApplyTemplate();
                Check(((System.Windows.Media.GradientStop)progressBar.Template.FindName("middle",progressBar)).HasAnimatedProperties,"Unknown-progress animation starts in the themed progress bar");
                progressBar.IsIndeterminate=false;
                controller.ScanLibrary().GetAwaiter().GetResult(); Check(fixture.Posts==1,"Repeated scan click does not queue duplicate scans");
                fixture.Release.SetResult(true); PumpUntil(()=>operation.IsCompleted); operation.GetAwaiter().GetResult();
                Check(!controller.Loading && controller.LibraryProgress==null && controller.Library.Entries.Length==1 && controller.Library.Entries[0].Key=="id:1" && controller.Status.Contains("扫描完成"),"Controller automatically replaces items after completion without a second refresh");
                Check(progressBar.Visibility==Visibility.Collapsed && !progressBar.IsIndeterminate,"WPF stops progress animation after completion");
                shell.Release();
            }
            fixture=new Fixture(); fixture.States.Enqueue(Snapshot("Idle","new","Failed"));
            using(var controller=new DesktopController(app,Settings(),false,null,fixture))
            { var operation=controller.ScanLibrary(); PumpUntil(()=>operation.IsCompleted); try { operation.GetAwaiter().GetResult(); } catch(InvalidOperationException) { } Check(!controller.Loading && controller.LibraryProgress==null,"Controller clears loading state on failure"); }
            app.Shutdown(); File.WriteAllLines(args[1],report); foreach(var line in report) Console.WriteLine(line); return 0;
        }
        catch(Exception error) { foreach(var line in report) Console.WriteLine(line); Console.Error.WriteLine(error.ToString()); return 1; }
    }
}
