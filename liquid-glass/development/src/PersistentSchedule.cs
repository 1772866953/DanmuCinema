using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace DanmuCinema
{
    public sealed class SavedSchedule
    {
        public int Version = 1, Days, Action, Seconds, ProcessId;
        public string Id, Owner, Mode, Time, TargetUtc, CreatedUtc, State, Message, LastOccurrenceUtc, ProcessStartedUtc;
        public bool Active, Awake;
        public DateTimeOffset Target { get { return DateTimeOffset.Parse(TargetUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); } }
        public bool Weekly { get { return Mode == "weekly"; } }
    }
    public interface IScheduleBackend
    {
        void Register(string name, SavedSchedule plan, string executable, string root);
        void Delete(string name);
        bool Exists(string name);
    }
    // Only the interactive Windows user is used. No credentials or administrator
    // account is stored, and the application does not change any power policy.
    public sealed class WindowsScheduleBackend : IScheduleBackend
    {
        static dynamic Connect()
        {
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
            service.Connect(); return service;
        }
        internal static string Quote(string value)
        { return "\"" + System.Text.RegularExpressions.Regex.Replace(value, @"(\\+)$", "$1$1") + "\""; }
        public void Register(string name, SavedSchedule plan, string executable, string root)
        {
            dynamic service = Connect(), folder = null, definition = null;
            try
            {
                folder = service.GetFolder("\\"); definition = service.NewTask(0);
                definition.RegistrationInfo.Author = "DanmuCinema";
                definition.RegistrationInfo.Description = "弹幕影院定时计划 · " + root;
                definition.Principal.UserId = plan.Owner; definition.Principal.LogonType = 3; definition.Principal.RunLevel = 0;
                definition.Settings.Enabled = true; definition.Settings.Hidden = true;
                definition.Settings.StartWhenAvailable = false; definition.Settings.WakeToRun = plan.Awake;
                definition.Settings.DisallowStartIfOnBatteries = false; definition.Settings.StopIfGoingOnBatteries = false;
                definition.Settings.MultipleInstances = 2; definition.Settings.ExecutionTimeLimit = "PT0S";
                dynamic trigger = definition.Triggers.Create(plan.Weekly ? 3 : 1);
                if (plan.Weekly)
                {
                    trigger.DaysOfWeek = ((plan.Days & 63) << 1) | ((plan.Days >> 6) & 1);
                    trigger.WeeksInterval = 1;
                    trigger.StartBoundary = plan.Target.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
                }
                else trigger.StartBoundary = plan.Target.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                trigger.Enabled = true;
                dynamic action = definition.Actions.Create(0);
                action.Path = executable; action.WorkingDirectory = root;
                action.Arguments = "--execute-schedule " + Quote(root) + " " + plan.Id;
                folder.RegisterTaskDefinition(name, definition, 6, plan.Owner, null, 3, null);
            }
            finally { Release(definition); Release(folder); Release(service); }
        }
        public void Delete(string name)
        {
            dynamic service = Connect(), folder = null;
            try { folder = service.GetFolder("\\"); try { folder.DeleteTask(name, 0); } catch (FileNotFoundException) { } catch (COMException e) { if ((uint)e.ErrorCode != 0x80070002) throw; } }
            finally { Release(folder); Release(service); }
        }
        public bool Exists(string name)
        {
            dynamic service = Connect(), folder = null, task = null;
            try { folder = service.GetFolder("\\"); try { task = folder.GetTask(name); return (bool)task.Enabled; } catch (FileNotFoundException) { return false; } catch (COMException e) { if ((uint)e.ErrorCode == 0x80070002) return false; throw; } }
            finally { Release(task); Release(folder); Release(service); }
        }
        static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    }
    public sealed class PersistentSchedule
    {
        readonly string root, owner, executable, mutexName;
        readonly IScheduleBackend backend;
        readonly Func<DateTimeOffset> now;
        public readonly string FileName, TaskName;
        public PersistentSchedule(string root, IScheduleBackend backend = null, string executable = null, Func<DateTimeOffset> now = null)
        {
            this.root = Path.GetFullPath(root); owner = WindowsIdentity.GetCurrent().User.Value;
            this.backend = backend ?? new WindowsScheduleBackend(); this.executable = executable ?? Process.GetCurrentProcess().MainModule.FileName;
            this.now = now ?? (() => DateTimeOffset.UtcNow);
            string key = Program.StableId(this.root + "|" + owner);
            FileName = Path.Combine(this.root, "data", "scheduler", "plan-" + Program.StableId(owner) + ".json");
            TaskName = "DanmuCinema-" + key; mutexName = "Local\\DanmuCinema.Schedule-" + key;
        }
        T Locked<T>(Func<T> operation)
        {
            using (var mutex = new Mutex(false, mutexName))
            {
                bool held = false;
                try { try { held = mutex.WaitOne(10000); } catch (AbandonedMutexException) { held = true; } if (!held) throw new TimeoutException("定时计划正在更新，请稍后重试。"); return operation(); }
                finally { if (held) mutex.ReleaseMutex(); }
            }
        }
        SavedSchedule ReadCore()
        {
            if (!File.Exists(FileName)) return null;
            var plan = Json.Read<SavedSchedule>(File.ReadAllText(FileName, Encoding.UTF8)); Guid id; DateTimeOffset target, created;
            if (plan == null || plan.Version != 1 || plan.Owner != owner || !Guid.TryParseExact(plan.Id, "N", out id) ||
                !Enum.IsDefined(typeof(PowerAction), plan.Action) || !DateTimeOffset.TryParse(plan.TargetUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out target) ||
                !DateTimeOffset.TryParse(plan.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out created) ||
                (plan.Mode != "delay" && plan.Mode != "at" && plan.Mode != "weekly")) throw new InvalidDataException("定时计划文件无效，原文件已保留。");
            if (plan.Weekly) new WeeklyPlan(plan.Days, TimeSpan.Parse(plan.Time, CultureInfo.InvariantCulture), null);
            return plan;
        }
        void Save(SavedSchedule plan)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FileName)); string temporary = FileName + ".tmp";
            File.WriteAllText(temporary, Json.Write(plan), new UTF8Encoding(false));
            if (File.Exists(FileName)) File.Replace(temporary, FileName, null); else File.Move(temporary, FileName);
        }
        public SavedSchedule Read() { return Locked(ReadCore); }
        public SavedSchedule Start(DateTimeOffset target, PowerAction action, string mode, int days, TimeSpan time, bool awake, int seconds)
        {
            if (target <= now()) throw new ArgumentException("执行时间必须晚于当前时间。");
            if (!Enum.IsDefined(typeof(PowerAction), action)) throw new ArgumentException("到时操作无效。");
            if (mode == "weekly") new WeeklyPlan(days, time, null);
            if (mode != "weekly" && mode != "at" && mode != "delay") throw new ArgumentException("计时方式无效。");
            return Locked(() => {
                var previous = ReadCore(); if (previous != null && previous.Active) throw new InvalidOperationException("请先取消当前定时计划。");
                var plan = new SavedSchedule { Id = Guid.NewGuid().ToString("N"), Owner = owner, Mode = mode, Days = days,
                    Time = time.ToString(@"hh\:mm\:ss"), TargetUtc = target.ToUniversalTime().ToString("o"), CreatedUtc = now().ToString("o"),
                    Action = (int)action, Active = true, Awake = awake, Seconds = seconds, State = "Waiting", Message = "系统定时计划已保存，退出程序后继续有效。" };
                Save(plan);
                try { backend.Register(TaskName, plan, executable, root); }
                catch (Exception e) { if (previous != null) Save(previous); else File.Delete(FileName); try { backend.Delete(TaskName); } catch { } throw new InvalidOperationException("系统定时计划创建失败，未启用定时：" + e.Message, e); }
                return plan;
            });
        }
        public void Cancel(bool force = false)
        {
            Locked(() => { SavedSchedule plan; try { plan = ReadCore(); } catch { if (!force) throw; plan = null; } if (!force && plan != null && plan.State == "Executing") throw new InvalidOperationException("操作已开始执行，无法撤回。");
                backend.Delete(TaskName); if (plan != null) { plan.Active = false; plan.State = "Cancelled"; plan.Message = "已取消定时，原定操作不会执行。"; Save(plan); } return true; });
        }
        static bool ProcessAlive(SavedSchedule plan)
        {
            try { using (var process = Process.GetProcessById(plan.ProcessId)) return !process.HasExited && Math.Abs((process.StartTime.ToUniversalTime() - DateTime.Parse(plan.ProcessStartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime()).TotalSeconds) < 1; }
            catch { return false; }
        }
        public SavedSchedule Reconcile(bool verifyRegistration)
        {
            return Locked(() => { var plan = ReadCore(); if (plan == null || !plan.Active) return plan; bool changed = false;
                if (plan.State == "Executing" && !ProcessAlive(plan)) { plan.State = plan.Weekly ? "Waiting" : "Completed"; plan.Active = plan.Weekly; plan.Message = "计划已触发，执行进程已结束。"; changed = true; }
                if (plan.State == "Waiting" && plan.Target < now() - TimeSpan.FromMinutes(5))
                {
                    if (plan.Weekly) { plan.TargetUtc = new WeeklyPlan(plan.Days, TimeSpan.Parse(plan.Time), null).Next(now()).ToString("o"); plan.Message = "上次执行时间已错过，已保留下次每周计划。"; }
                    else { backend.Delete(TaskName); plan.Active = false; plan.State = "Missed"; plan.Message = "已错过执行时间，请重新设置。"; }
                    changed = true;
                }
                if (verifyRegistration && plan.Active && plan.State != "Executing" && !backend.Exists(TaskName)) { plan.Active = false; plan.State = "Failed"; plan.Message = "Windows 中的定时计划已被删除或停用，请重新设置。"; changed = true; }
                if (changed) Save(plan); return plan;
            });
        }
        DateTimeOffset Occurrence(SavedSchedule plan, DateTimeOffset at)
        {
            if (!plan.Weekly || plan.Target > at && plan.Target - at < TimeSpan.FromSeconds(3)) return plan.Target;
            var weekly = new WeeklyPlan(plan.Days, TimeSpan.Parse(plan.Time), null);
            var candidate = weekly.Next(at - TimeSpan.FromDays(8)); var last = candidate;
            while (candidate <= at) { last = candidate; candidate = weekly.Next(candidate); }
            return last;
        }
        public bool RunDue(string id, Action<PowerAction> execute)
        {
            var first = Read(); if (first == null || first.Id != id || !first.Active) return false;
            var due = Occurrence(first, now()); var delay = due - now();
            // Task Scheduler may start within the same second as the boundary.
            // Wait outside the mutex so a last-moment explicit cancel can win.
            if (delay > TimeSpan.Zero && delay < TimeSpan.FromSeconds(3)) Thread.Sleep((int)Math.Ceiling(delay.TotalMilliseconds));
            SavedSchedule claimed = Locked(() => { var plan = ReadCore(); if (plan == null || plan.Id != id || !plan.Active || plan.State != "Waiting") return null;
                var occurrence = Occurrence(plan, now()); if (occurrence > now() || occurrence < DateTimeOffset.Parse(plan.CreatedUtc, CultureInfo.InvariantCulture) || now() - occurrence > TimeSpan.FromMinutes(5) || plan.LastOccurrenceUtc == occurrence.ToString("o")) return null;
                plan.State = "Executing"; plan.LastOccurrenceUtc = occurrence.ToString("o"); plan.ProcessId = Process.GetCurrentProcess().Id;
                plan.ProcessStartedUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("o"); plan.Message = "正在执行" + PowerActions.Name((PowerAction)plan.Action) + "。";
                if (plan.Weekly) plan.TargetUtc = new WeeklyPlan(plan.Days, TimeSpan.Parse(plan.Time), null).Next(now()).ToString("o");
                Save(plan); return plan;
            });
            if (claimed == null) return false;
            try { execute((PowerAction)claimed.Action); Finish(claimed, true, "定时操作已完成。"); }
            catch (Exception e) { Finish(claimed, false, "定时操作失败：" + e.Message); throw; }
            return true;
        }
        void Finish(SavedSchedule claimed, bool success, string message)
        {
            Locked(() => { var current = ReadCore(); if (current == null || current.Id != claimed.Id || current.State != "Executing" || current.LastOccurrenceUtc != claimed.LastOccurrenceUtc) return false;
                // Delete while the same plan still owns the registration. A new
                // plan may be created immediately after completion is published.
                if (!current.Weekly) try { backend.Delete(TaskName); } catch (Exception e) { message += " 系统任务清理未完成：" + e.Message; }
                current.Active = current.Weekly; current.State = current.Weekly ? "Waiting" : success ? "Completed" : "Failed"; current.Message = message; Save(current); return true; });
        }
        public static string StopEventName(string root) { return "Local\\DanmuCinema.Schedule.Stop-" + Program.StableId(Path.GetFullPath(root) + "|" + WindowsIdentity.GetCurrent().User.Value); }
        public static void StopServices(string root)
        {
            // A fully exited application has already stopped its owned services.
            try { using (var request = EventWaitHandle.OpenExisting(StopEventName(root))) using (var ack = EventWaitHandle.OpenExisting(StopEventName(root) + ".ack")) { ack.Reset(); request.Set(); if (!ack.WaitOne(30000)) throw new TimeoutException("等待视频服务停止超时。"); } }
            catch (WaitHandleCannotBeOpenedException) { }
        }
    }
}
