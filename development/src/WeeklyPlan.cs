using System;
using System.Linq;

namespace DanmuCinema
{
    public sealed class WeeklyPlan
    {
        public static readonly string[] DayNames = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        public readonly int Days;
        public readonly TimeSpan Time;
        readonly TimeZoneInfo zone;
        public WeeklyPlan(int days, TimeSpan time, TimeZoneInfo zone)
        {
            if (days <= 0 || days > 127) throw new ArgumentException("请至少选择一个执行日期。");
            if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1)) throw new ArgumentException("执行时间必须在 00:00:00 到 23:59:59 之间。");
            Days = days; Time = time; this.zone = zone ?? TimeZoneInfo.Local;
        }
        public string Label { get { return String.Join("、", DayNames.Where((name, index) => (Days & (1 << index)) != 0)) + " " + Time.ToString(@"hh\:mm\:ss"); } }
        public DateTimeOffset Next(DateTimeOffset after)
        {
            var date = TimeZoneInfo.ConvertTime(after, zone).Date;
            // Skip nonexistent local times rather than shifting the requested clock
            // time; choose the later occurrence when the clock repeats in autumn.
            for (int offset = 0; offset < 15; offset++)
            {
                var local = DateTime.SpecifyKind(date.AddDays(offset) + Time, DateTimeKind.Unspecified);
                int index = ((int)local.DayOfWeek + 6) % 7;
                if ((Days & (1 << index)) == 0 || zone.IsInvalidTime(local)) continue;
                var utcOffset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Min() : zone.GetUtcOffset(local);
                var target = new DateTimeOffset(local, utcOffset);
                if (target > after) return target;
            }
            throw new InvalidOperationException("无法确定下次执行时间，请检查日期和时区。");
        }
    }
}
