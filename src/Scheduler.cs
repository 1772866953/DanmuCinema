using System;
using System.Runtime.InteropServices;

namespace DanmuCinema
{
    public enum PowerAction { StopServices, Shutdown, Restart, Sleep, Hibernate, Lock, Logoff }
    public enum ScheduleState { Idle, Waiting, Warning, Executing, Completed, Cancelled, Failed }

    public interface IClock
    {
        DateTimeOffset Now { get; }
        long Milliseconds { get; }
    }

    public sealed class SystemClock : IClock
    {
        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();
        public DateTimeOffset Now { get { return DateTimeOffset.UtcNow; } }
        public long Milliseconds { get { return (long)GetTickCount64(); } }
    }

    // This state machine never invokes an OS action. Only Poll's one-time true result permits dispatch.
    public sealed class Scheduler
    {
        private readonly IClock clock;
        private bool relative;
        private long dueMilliseconds;
        private DateTimeOffset target;
        private long? graceDeadline;
        private long lastPoll;
        private WeeklyPlan weekly;
        public bool Weekly { get { return weekly != null; } }
        public string WeeklyLabel { get { return weekly == null ? "" : weekly.Label; } }
        public ScheduleState State { get; private set; }
        public PowerAction Action { get; private set; }
        public bool Active { get { return State == ScheduleState.Waiting || State == ScheduleState.Warning; } }
        public Scheduler(IClock clock) { this.clock = clock; State = ScheduleState.Idle; }

        public TimeSpan Remaining
        {
            get
            {
                if (!Active) return TimeSpan.Zero;
                if (graceDeadline.HasValue) return TimeSpan.FromMilliseconds(Math.Max(0, graceDeadline.Value - clock.Milliseconds));
                return RawRemaining();
            }
        }
        public DateTimeOffset Target
        {
            get { return relative || graceDeadline.HasValue ? clock.Now + Remaining : target; }
        }
        private TimeSpan RawRemaining()
        {
            return relative ? TimeSpan.FromMilliseconds(dueMilliseconds - clock.Milliseconds) : target - clock.Now;
        }
        public void StartDelay(TimeSpan delay, PowerAction action)
        {
            if (delay <= TimeSpan.Zero || delay > TimeSpan.FromDays(30))
                throw new ArgumentException("请输入大于 0 且不超过 30 天的倒计时。");
            EnsureCanStart();
            weekly = null;
            relative = true;
            dueMilliseconds = clock.Milliseconds + (long)delay.TotalMilliseconds;
            target = clock.Now + delay;
            Begin(action);
        }
        public void StartAt(DateTimeOffset time, PowerAction action)
        {
            if (time <= clock.Now) throw new ArgumentException("执行时间必须晚于当前时间。");
            EnsureCanStart();
            weekly = null;
            relative = false;
            target = time;
            Begin(action);
        }
        public void StartWeekly(int days, TimeSpan time, PowerAction action, TimeZoneInfo zone = null)
        {
            EnsureCanStart(); var plan = new WeeklyPlan(days, time, zone);
            var next = plan.Next(clock.Now); weekly = plan; relative = false; target = next; Begin(action);
        }
        private void EnsureCanStart()
        {
            if (Active || State == ScheduleState.Executing) throw new InvalidOperationException("请先取消当前定时任务。");
        }
        private void Begin(PowerAction action)
        {
            Action = action;
            graceDeadline = null;
            lastPoll = clock.Milliseconds;
            State = RawRemaining() <= TimeSpan.FromSeconds(15) ? ScheduleState.Warning : ScheduleState.Waiting;
        }
        public bool Cancel()
        {
            if (!Active) return false;
            graceDeadline = null;
            weekly = null;
            State = ScheduleState.Cancelled;
            return true;
        }
        public void OnResume()
        {
            // Give a fresh opportunity to cancel after waking from sleep or a long UI stall.
            if (Active && Remaining < TimeSpan.FromSeconds(15))
            {
                graceDeadline = clock.Milliseconds + 15000;
                State = ScheduleState.Warning;
            }
        }
        public bool Poll()
        {
            if (!Active) return false;
            if (clock.Milliseconds - lastPoll > 5000) OnResume();
            lastPoll = clock.Milliseconds;
            TimeSpan remaining = Remaining;
            if (State == ScheduleState.Waiting && remaining <= TimeSpan.FromSeconds(15))
            {
                State = ScheduleState.Warning;
                if (remaining <= TimeSpan.Zero) graceDeadline = clock.Milliseconds + 15000;
                return false;
            }
            if (State == ScheduleState.Warning && remaining <= TimeSpan.Zero)
            {
                State = ScheduleState.Executing;
                return true;
            }
            return false;
        }
        public void Finish(bool success)
        {
            if (State != ScheduleState.Executing) throw new InvalidOperationException("任务尚未开始执行。");
            if (success && weekly != null)
            {
                target = weekly.Next(clock.Now > target ? clock.Now : target); relative = false; Begin(Action);
            }
            else { weekly = null; State = success ? ScheduleState.Completed : ScheduleState.Failed; }
        }
        public static string FormatRemaining(TimeSpan remaining)
        {
            long seconds = Math.Max(0, (long)Math.Ceiling(remaining.TotalSeconds));
            return string.Format("{0:00}:{1:00}:{2:00}", seconds / 3600, seconds / 60 % 60, seconds % 60);
        }
    }
}
