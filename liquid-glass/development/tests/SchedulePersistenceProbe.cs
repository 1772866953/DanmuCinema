using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DanmuCinema;

sealed class SchedulePersistenceProbe
{
    sealed class Backend : IScheduleBackend
    {
        public bool Present, Fail; public int Deleted;
        public void Register(string name, SavedSchedule plan, string executable, string root) { if(Fail)throw new IOException("test registration failure");Present=true; }
        public void Delete(string name) { Present=false;Deleted++; }
        public bool Exists(string name) {return Present;}
    }
    sealed class Clock : IClock
    {
        public DateTimeOffset Value;public DateTimeOffset Now {get{return Value;}}public long Milliseconds {get{return Value.UtcTicks/TimeSpan.TicksPerMillisecond;}}
    }
    static readonly List<string> checks=new List<string>();
    static void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
    static void Unit(string root)
    {
        var clock=new Clock {Value=DateTimeOffset.UtcNow};var backend=new Backend();string directory=Path.Combine(root,"unit");
        var store=new PersistentSchedule(directory,backend,null,()=>clock.Now);var due=clock.Now.AddMinutes(30);
        var first=store.Start(due,PowerAction.StopServices,"delay",0,TimeSpan.Zero,false,1800);
        clock.Value=clock.Now.AddMinutes(7);var restored=new PersistentSchedule(directory,backend,null,()=>clock.Now).Read();var display=new Scheduler(clock);display.ObserveExternal(restored);
        Check(restored.Id==first.Id&&restored.Target==due&&Math.Abs(display.Remaining.TotalMinutes-23)<.01,"restart restores the original deadline and elapsed countdown");
        store.Cancel();Check(!backend.Present&&!store.Read().Active,"explicit cancel removes the Windows job and persists cancellation");
        int fired=0;Check(!store.RunDue(first.Id,a=>fired++),"a cancelled queued helper cannot execute");
        backend.Fail=true;try{store.Start(clock.Now.AddMinutes(1),PowerAction.Lock,"delay",0,TimeSpan.Zero,false,60);throw new Exception("registration must fail");}catch(InvalidOperationException){}
        Check(!store.Read().Active&&!backend.Present,"registration failure restores the previous inactive record");backend.Fail=false;
        var single=store.Start(clock.Now.AddSeconds(30),PowerAction.Lock,"delay",0,TimeSpan.Zero,false,30);clock.Value=single.Target.AddSeconds(1);
        Parallel.Invoke(()=>store.RunDue(single.Id,a=>Interlocked.Increment(ref fired)),()=>new PersistentSchedule(directory,backend,null,()=>clock.Now).RunDue(single.Id,a=>Interlocked.Increment(ref fired)));
        Check(fired==1&&!store.Read().Active,"concurrent due helpers claim a one-shot plan exactly once");
        var stale=single.Id;var replacement=store.Start(clock.Now.AddSeconds(30),PowerAction.Lock,"delay",0,TimeSpan.Zero,false,30);clock.Value=replacement.Target.AddSeconds(1);
        Check(!store.RunDue(stale,a=>fired++),"stale helper IDs cannot execute a replacement plan");store.Cancel();
        var weekly=new WeeklyPlan(127,TimeSpan.FromHours(21),null);var week=store.Start(weekly.Next(clock.Now),PowerAction.StopServices,"weekly",127,TimeSpan.FromHours(21),false,0);
        clock.Value=week.Target.AddSeconds(1);Check(store.RunDue(week.Id,a=>fired++),"weekly occurrence executes");var next=store.Read();
        Check(next.Active&&next.State=="Waiting"&&next.Target>clock.Now&&backend.Present,"weekly plan and Windows registration survive the occurrence");
        Check(!store.RunDue(week.Id,a=>fired++),"a weekly occurrence cannot execute twice");
        clock.Value=next.Target.AddDays(14).AddSeconds(1);Check(store.RunDue(week.Id,a=>fired++),"weekly registration recovers a valid occurrence after missed weeks");store.Cancel();
        var expired=store.Start(clock.Now.AddMinutes(1),PowerAction.Lock,"at",0,clock.Now.TimeOfDay,false,0);clock.Value=expired.Target.AddMinutes(6);
        Check(store.Reconcile(true).State=="Missed"&&!backend.Present,"missed one-shot power actions never run unexpectedly on restart");
        var cancelled=store.Start(clock.Now.AddSeconds(10),PowerAction.Lock,"delay",0,TimeSpan.Zero,false,10);clock.Value=cancelled.Target.AddSeconds(1);
        var started=new ManualResetEventSlim();var finish=new ManualResetEventSlim();var execution=Task.Run(()=>store.RunDue(cancelled.Id,a=>{started.Set();finish.Wait();}));started.Wait();
        bool denied=false;try{store.Cancel();}catch(InvalidOperationException){denied=true;}Check(denied,"execution cannot be falsely reported as cancelled");store.Cancel(true);finish.Set();execution.Wait();
        Check(!store.Read().Active&&store.Read().State=="Cancelled","uninstall cancellation cannot be overwritten by a finishing helper");
        var old=store.Start(clock.Now.AddSeconds(10),PowerAction.StopServices,"delay",0,TimeSpan.Zero,false,10);clock.Value=old.Target.AddSeconds(1);started.Reset();finish.Reset();
        execution=Task.Run(()=>store.RunDue(old.Id,a=>{started.Set();finish.Wait();}));started.Wait();store.Cancel(true);
        var fresh=store.Start(clock.Now.AddHours(1),PowerAction.StopServices,"delay",0,TimeSpan.Zero,false,3600);finish.Set();execution.Wait();
        Check(backend.Present&&store.Read().Id==fresh.Id&&store.Read().Active,"a finishing old helper cannot delete the replacement Windows task");store.Cancel();
        File.WriteAllText(Path.Combine(root,"unit-result.json"),Json.Write(new{passed=true,checks=checks}));
    }
    [STAThread]static int Main(string[] args)
    {
        string root=args.Length>1?Path.GetFullPath(args[1]):Environment.CurrentDirectory;
        try {
            Directory.CreateDirectory(root);
            if(args[0]=="--unit"){Unit(root);return 0;}
            var store=new PersistentSchedule(root);
            if(args[0]=="--create"){
                var plan=store.Start(DateTimeOffset.UtcNow.AddSeconds(8),PowerAction.StopServices,"delay",0,TimeSpan.Zero,false,8);
                File.WriteAllText(Path.Combine(root,"created.json"),Json.Write(new{id=plan.Id,task=store.TaskName,file=store.FileName,creator=Process.GetCurrentProcess().Id,target=plan.TargetUtc}));return 0;
            }
            if(args[0]=="--weekly"){
                var next=new WeeklyPlan(5,TimeSpan.FromHours(21),null).Next(DateTimeOffset.UtcNow);
                var plan=store.Start(next,PowerAction.StopServices,"weekly",5,TimeSpan.FromHours(21),false,0);
                dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service",true));service.Connect();
                File.WriteAllText(Path.Combine(root,"task.xml"),(string)service.GetFolder("\\").GetTask(store.TaskName).Xml);return 0;
            }
            if(args[0]=="--execute-schedule"){
                store.RunDue(args[2],a=>File.WriteAllText(Path.Combine(root,"fired.json"),Json.Write(new{process=Process.GetCurrentProcess().Id,at=DateTimeOffset.UtcNow.ToString("o"),action=(int)a})));return 0;
            }
            if(args[0]=="--status"){File.WriteAllText(Path.Combine(root,"status.json"),Json.Write(new{exists=new WindowsScheduleBackend().Exists(store.TaskName),plan=store.Read()}));return 0;}
            if(args[0]=="--cancel"){store.Cancel(true);return 0;}
            return 2;
        }catch(Exception e){File.WriteAllText(Path.Combine(root,"error.txt"),e.ToString());return 1;}
    }
}
