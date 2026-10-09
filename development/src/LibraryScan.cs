using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    public sealed class LibraryLoadProgress
    {
        public readonly string Message;
        public readonly double? Percent;
        public LibraryLoadProgress(string message, double? percent = null) { Message = message; Percent = percent; }
    }

    // A successful refresh request only queues work. Do not publish the old
    // item list as complete until this execution has actually finished.
    public sealed class LibraryScanner
    {
        readonly JellyfinApi api;
        readonly Func<CancellationToken, Task> pause;
        public LibraryScanner(JellyfinApi api, Func<CancellationToken, Task> pause = null)
        { this.api = api; this.pause = pause ?? (token => Task.Delay(1000, token)); }

        static bool Active(Dictionary<string, object> task)
        { string state = Json.Text(task, "State"); return state == "Running" || state == "Cancelling"; }
        static string Execution(Dictionary<string, object> task)
        { var result = Json.Child(task, "LastExecutionResult"); return Json.Text(result, "StartTimeUtc") + "|" + Json.Text(result, "EndTimeUtc"); }
        static void Report(Action<LibraryLoadProgress> progress, string message, double? percent = null)
        { if (progress != null) progress(new LibraryLoadProgress(message, percent)); }

        public async Task<Dictionary<string, object>[]> Load(bool requestScan, Action<LibraryLoadProgress> progress, CancellationToken cancellation)
        {
            Report(progress, requestScan ? "正在准备扫描媒体库…" : "正在检查媒体库扫描状态…");
            var tasks = Json.Read<object[]>(await api.Request("GET", "ScheduledTasks", null, true, cancellation));
            var scan = tasks.OfType<Dictionary<string, object>>().FirstOrDefault(x => Json.Text(x, "Key") == "RefreshLibrary");
            if (requestScan && scan == null) throw new InvalidOperationException("服务器未提供媒体库扫描任务，请检查服务器日志。");
            if (scan != null && (requestScan || Active(scan)))
            {
                string previous = Execution(scan), id = Uri.EscapeDataString(Json.Text(scan, "Id"));
                if (!Active(scan)) await api.Request("POST", "ScheduledTasks/Running/" + id, null, true, cancellation);
                else Report(progress, "媒体库正在扫描，等待完成后自动更新…");
                bool observedRunning = Active(scan);
                int waiting = 0, failures = 0;
                var elapsed = Stopwatch.StartNew();
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Dictionary<string, object> current = null;
                    try { current = Json.Object(await api.Request("GET", "ScheduledTasks/" + id, null, true, cancellation)); failures = 0; }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception)
                    {
                        if (++failures >= 3) throw new InvalidOperationException("无法获取媒体库扫描进度，请检查服务状态后重试。");
                        Report(progress, "扫描进度连接暂时中断，正在重新连接…");
                    }
                    if (current == null) { await pause(cancellation); continue; }
                    if (!Active(current) && Execution(current) != previous)
                    {
                        var result = Json.Child(current, "LastExecutionResult");
                        string status = Json.Text(result, "Status");
                        if (status != "Completed") throw new InvalidOperationException(status == "Cancelled" || status == "Canceled" ? "媒体库扫描已取消。" : "媒体库扫描失败，请查看服务器日志。");
                        break;
                    }
                    if (Active(current))
                    {
                        observedRunning = true;
                        double percent;
                        double? value = Double.TryParse(Json.Text(current, "CurrentProgressPercentage"), NumberStyles.Float, CultureInfo.InvariantCulture, out percent) && !Double.IsNaN(percent) && !Double.IsInfinity(percent) ? (double?)Math.Max(0, Math.Min(100, percent)) : null;
                        Report(progress, Json.Text(current, "State") == "Cancelling" ? "正在等待扫描取消…" : "正在扫描媒体库" + (value.HasValue ? " · " + value.Value.ToString("0.0") + "%" : "…"), value);
                    }
                    else
                    {
                        Report(progress, observedRunning ? "正在等待服务器保存扫描结果…" : "扫描任务已提交，正在等待开始…");
                        if (++waiting >= 30) throw new TimeoutException("服务器未确认扫描完成，请查看日志后重试。");
                    }
                    if (elapsed.Elapsed.TotalHours >= 6) throw new TimeoutException("媒体库扫描长时间未完成，请检查服务器日志。");
                    await pause(cancellation);
                }
            }
            Report(progress, "扫描已完成，正在读取影片列表…");
            var items = await api.Items("", (read, total) => Report(progress, "正在读取影片列表 · " + read + (total.HasValue ? " / " + total.Value : "") + " 个影片", total.HasValue && total.Value > 0 ? (double?)(100.0 * read / total.Value) : null), cancellation);
            cancellation.ThrowIfCancellationRequested();
            return items.OfType<Dictionary<string, object>>().ToArray();
        }
    }
}
