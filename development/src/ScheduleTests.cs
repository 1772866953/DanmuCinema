using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DanmuCinema
{
    internal static class ScheduleTests
    {
        sealed class FakeClock : IClock
        {
            public DateTimeOffset Now { get; set; }
            public long Milliseconds { get; set; }
            public FakeClock() { Now = new DateTimeOffset(2026, 10, 6, 23, 59, 50, TimeSpan.FromHours(8)); }
            public void Advance(int seconds) { Now += TimeSpan.FromSeconds(seconds); Milliseconds += seconds * 1000L; }
        }
        public static void Run(List<string> report)
        {
            foreach (PowerAction action in Enum.GetValues(typeof(PowerAction)))
            {
                var clock = new FakeClock(); var scheduler = new Scheduler(clock);
                scheduler.StartDelay(TimeSpan.FromSeconds(30), action);
                bool started = scheduler.Active && scheduler.Action == action;
                scheduler.Cancel(); clock.Advance(100);
                SelfTests.Assert(started && !scheduler.Poll() && !scheduler.Cancel() && scheduler.State == ScheduleState.Cancelled, "定时" + PowerActions.Name(action) + "取消后不再执行", report);
            }
            var time = new FakeClock(); var task = new Scheduler(time);
            task.StartDelay(TimeSpan.FromSeconds(30), PowerAction.StopServices);
            for (int i = 0; i < 15; i++) { time.Advance(1); if (task.Poll()) throw new Exception("定时提前执行"); }
            SelfTests.Assert(task.State == ScheduleState.Warning && task.Remaining == TimeSpan.FromSeconds(15), "定时操作执行前 15 秒进入可取消提醒", report);
            for (int i = 0; i < 14; i++) { time.Advance(1); task.Poll(); }
            task.Cancel(); time.Advance(1);
            SelfTests.Assert(!task.Poll(), "到期前最后一秒取消有效", report);
            task.StartDelay(TimeSpan.FromSeconds(2), PowerAction.Lock);
            bool shortWarning = task.State == ScheduleState.Warning;
            time.Advance(1); bool early = task.Poll(); time.Advance(1); bool due = task.Poll();
            SelfTests.Assert(shortWarning && !early && due && !task.Poll() && !task.Cancel(), "短任务立即提醒、按时执行且只提交一次", report);
            task.Finish(true);
            task.StartDelay(TimeSpan.FromSeconds(1), PowerAction.Sleep); time.Advance(1); task.Poll(); task.Finish(false);
            SelfTests.Assert(task.State == ScheduleState.Failed, "完成后可创建新任务并报告失败状态", report);
            task.StartDelay(TimeSpan.FromHours(1), PowerAction.StopServices); time.Now = time.Now.AddHours(5);
            SelfTests.Assert(task.Remaining == TimeSpan.FromHours(1) && task.Target == time.Now.AddHours(1), "倒计时不受手动调整系统时间影响", report);
            bool rejected = false; try { task.StartDelay(TimeSpan.FromSeconds(1), PowerAction.Shutdown); } catch (InvalidOperationException) { rejected = true; }
            SelfTests.Assert(rejected, "活动定时不能被另一任务覆盖", report); task.Cancel();
            foreach (TimeSpan delay in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), TimeSpan.FromDays(31) })
            {
                rejected = false; try { task.StartDelay(delay, PowerAction.Shutdown); } catch (ArgumentException) { rejected = true; }
                SelfTests.Assert(rejected && !task.Active, "拒绝无效倒计时 " + delay, report);
            }
            rejected = false; try { task.StartAt(time.Now, PowerAction.Shutdown); } catch (ArgumentException) { rejected = true; }
            SelfTests.Assert(rejected, "拒绝已过去的指定时间", report);
            time = new FakeClock(); task = new Scheduler(time); task.StartAt(time.Now.AddSeconds(20), PowerAction.StopServices);
            SelfTests.Assert(task.Target.Day == 7, "指定时间支持跨天", report);
            time.Now = time.Now.AddHours(2); task.Poll();
            SelfTests.Assert(task.State == ScheduleState.Warning && task.Remaining == TimeSpan.FromSeconds(15), "系统时钟跳变越过截止时间时保留取消机会", report); task.Cancel();
            task.StartDelay(TimeSpan.FromSeconds(60), PowerAction.Hibernate); time.Advance(120); task.OnResume();
            bool grace = task.Remaining == TimeSpan.FromSeconds(15) && !task.Poll();
            for (int i = 0; i < 14; i++) { time.Advance(1); if (task.Poll()) throw new Exception("恢复后的取消时间不足"); }
            time.Advance(1);
            SelfTests.Assert(grace && task.Poll(), "电脑唤醒后给予完整 15 秒取消时间", report); task.Finish(true);
            task.StartDelay(TimeSpan.FromSeconds(10), PowerAction.Restart); time.Advance(100);
            SelfTests.Assert(!task.Poll() && task.Remaining == TimeSpan.FromSeconds(15) && task.Cancel(), "界面阻塞或等待下载后不立即执行过期任务", report);
            SelfTests.Assert(Scheduler.FormatRemaining(TimeSpan.FromMilliseconds(1)) == "00:00:01" && Scheduler.FormatRemaining(TimeSpan.FromHours(720)) == "720:00:00", "剩余时间向上取整并支持 30 天", report);
            SelfTests.Assert(PowerActions.ShutdownArguments(PowerAction.Shutdown) == "/s /t 0" && PowerActions.ShutdownArguments(PowerAction.Restart) == "/r /f /t 0" && PowerActions.ShutdownArguments(PowerAction.Logoff) == "/l", "电源命令与参考工具一致，仅重启使用强制关闭", report);
            SelfTests.Assert(Marshal.SizeOf(typeof(PowerCapabilities)) == 76 && Marshal.OffsetOf(typeof(PowerCapabilities), "AoAc").ToInt32() == 20 && new PowerCapabilities { AoAc = 1 }.SleepAvailable, "电源能力结构兼容现代待机", report);
            SelfTests.Assert(new PowerCapabilities { SystemS4 = 1, HiberFilePresent = 1, HiberFileType = 2 }.HibernateAvailable && !new PowerCapabilities { SystemS4 = 1, HiberFilePresent = 1, HiberFileType = 1 }.HibernateAvailable, "休眠检测区分完整和精简休眠文件", report);
        }
    }
}
