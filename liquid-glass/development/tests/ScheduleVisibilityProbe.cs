using System;
using System.IO;
using System.Reflection;
using System.Windows;
using DanmuCinema;
using DanmuCinema.Desktop;

sealed class ScheduleVisibilityProbe {
 sealed class Backend : IScheduleBackend {
  public void Register(string name, SavedSchedule plan, string executable, string root) { }
  public void Delete(string name) { }
  public bool Exists(string name) { return true; }
 }
 [STAThread] static int Main(string[] args) {
  try {
   var app=new Application();int checkedCases=0;
   foreach(string mode in new[]{"visible","minimized","tray"}) {
    Paths.Root=Path.Combine(Path.GetFullPath(args[0]),mode);Directory.CreateDirectory(Paths.Root);
    var now=DateTimeOffset.UtcNow;
    var store=new PersistentSchedule(Paths.Root,new Backend(),null,()=>now);
    var plan=store.Start(now.AddSeconds(8),PowerAction.StopServices,"delay",0,TimeSpan.Zero,false,8);
    using(var controller=new DesktopController(app,new AppSettings(),false)) {
     typeof(DesktopController).GetField("persistentSchedule",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(controller,store);
     int foreground=0,notices=0;controller.RequestForeground+=()=>foreground++;controller.Notice+=(a,b)=>notices++;
     controller.Session.Page="library";controller.FrontendVisible=mode=="visible";
     var tick=typeof(DesktopController).GetMethod("ScheduleTick",BindingFlags.Instance|BindingFlags.NonPublic);
     tick.Invoke(controller,new object[]{null,EventArgs.Empty});
     if(foreground!=0||notices!=1||controller.Session.Page!="library"||controller.FrontendVisible!=(mode=="visible"))throw new Exception(mode+": warning changed presentation");
     now=now.AddSeconds(9);store.RunDue(plan.Id,a=>{});
     tick.Invoke(controller,new object[]{null,EventArgs.Empty});
     if(foreground!=0||controller.CurrentSchedule.State!="Completed"||controller.Session.Page!="library")throw new Exception(mode+": completion changed presentation");
     checkedCases+=2;
    }
   }
   File.WriteAllText(Path.Combine(args[0],"result.json"),"{\"passed\":true,\"cases\":"+checkedCases+"}");return 0;
  }catch(Exception e){File.WriteAllText(Path.Combine(args[0],"error.txt"),e.ToString());return 1;}
 }
}
