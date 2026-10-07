using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    // Background service has no Window/control references and survives tray release.
    public sealed class AutomaticDanmu : IDisposable
    {
        readonly AppSettings settings;
        readonly JellyfinApi api;
        readonly DanmuCatalog catalog;
        readonly Func<bool> ready;
        readonly SemaphoreSlim serial = new SemaphoreSlim(1, 1);
        readonly object sync = new object();
        readonly Dictionary<string, DateTime> attempts = new Dictionary<string, DateTime>();
        readonly Dictionary<string, Task> jobs = new Dictionary<string, Task>();
        readonly HashSet<string> suppressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CancellationTokenSource stopping;
        Timer timer;
        Task polling = Task.FromResult(0);
        public event Action<string> Saved;
        public event Action<Dictionary<string, object>, string, string, string> Progress;
        void Report(Dictionary<string, object> item, string status, string detail, string origin) { var handler = Progress; if (handler != null) handler(item, status, detail, origin); }
        public async Task<bool> PrepareQueuedItem(Dictionary<string, object> item, CancellationToken cancellation)
        { await serial.WaitAsync(cancellation).ConfigureAwait(false); try { return await PrepareItem(item, cancellation, "提前准备").ConfigureAwait(false); } finally { serial.Release(); } }
        public bool Running { get { lock (sync) return timer != null; } }
        public void SuppressUntilPlaybackEnds(IEnumerable<string> ids)
        { lock (sync) foreach (string id in ids.Where(x => !String.IsNullOrEmpty(x))) suppressed.Add(id); }
        public AutomaticDanmu(AppSettings settings, JellyfinApi api, DanmuCatalog catalog, Func<bool> ready)
        { this.settings = settings; this.api = api; this.catalog = catalog; this.ready = ready; }
        public void Start()
        { lock (sync) { if (timer != null) return; stopping = new CancellationTokenSource(); timer = new Timer(Tick, null, 0, 3000); } }
        void Tick(object state)
        { lock (sync) { if (timer == null || !polling.IsCompleted) return; polling = Poll(stopping.Token); } }
        async Task Poll(CancellationToken cancellation)
        {
            try
            {
                if (!ready() || String.IsNullOrEmpty(api.Token) || String.IsNullOrEmpty(settings.UserId)) return;
                var sessions = Json.Read<object[]>(await api.Request("GET", "Sessions", null, true, cancellation).ConfigureAwait(false));
                var activeIds = new HashSet<string>(sessions.OfType<Dictionary<string, object>>().Select(x => Json.Text(Json.Child(x, "NowPlayingItem"), "Id")), StringComparer.OrdinalIgnoreCase);
                lock (sync) suppressed.RemoveWhere(id => !activeIds.Contains(id));
                foreach (var session in sessions.OfType<Dictionary<string, object>>())
                {
                    var item = Json.Child(session, "NowPlayingItem"); string id = Json.Text(item, "Id");
                    if (item == null || !Regex.IsMatch(id, "^[a-zA-Z0-9]{1,64}$") || (Json.Text(item, "Type") != "Movie" && Json.Text(item, "Type") != "Episode" && Json.Text(item, "Type") != "Video")) continue;
                    lock (sync)
                    {
                        DateTime last;
                        if (suppressed.Contains(id) || jobs.ContainsKey(id) || attempts.TryGetValue(id, out last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(10)) continue;
                        if (attempts.Count > 2000) attempts.Clear(); attempts[id] = DateTime.UtcNow;
                        string sourceId = Json.Text(session, "MediaSourceId");
                        jobs[id] = Task.Run(async () => { try { await serial.WaitAsync(cancellation).ConfigureAwait(false); try { await Prepare(id, sourceId, cancellation).ConfigureAwait(false); } finally { serial.Release(); } } catch (OperationCanceledException) { } catch { Log.Write("自动弹幕处理暂未完成，可在影片页面手动匹配。"); } });
                    }
                }
                lock (sync) foreach (var key in jobs.Where(x => x.Value.IsCompleted).Select(x => x.Key).ToArray()) jobs.Remove(key);
            }
            catch (OperationCanceledException) { }
            catch { /* Local server startup/authentication failures never interrupt playback. */ }
        }
        async Task Prepare(string id, string sourceId, CancellationToken cancellation)
        {
            var item = Json.Object(await api.Request("GET", "Users/" + Uri.EscapeDataString(settings.UserId) + "/Items/" + id + "?Fields=Path,MediaSources", null, true, cancellation).ConfigureAwait(false));
            var source = Json.Array(item, "MediaSources").OfType<Dictionary<string, object>>().FirstOrDefault(x => Json.Text(x, "Id") == sourceId && !String.IsNullOrEmpty(sourceId));
            if (source != null && !String.IsNullOrEmpty(Json.Text(source, "Path"))) item["Path"] = Json.Text(source, "Path");
            await PrepareItem(item, cancellation).ConfigureAwait(false);
        }
        public async Task<bool> PrepareForPlayback(string id, string sourceId, CancellationToken cancellation)
        {
            if (!Regex.IsMatch(id ?? "", "^[a-zA-Z0-9]{1,64}$")) return false;
            Task job;
            lock (sync)
            {
                if (timer == null || suppressed.Contains(id) || String.IsNullOrEmpty(api.Token) || String.IsNullOrEmpty(settings.UserId)) return false;
                if (!jobs.TryGetValue(id, out job))
                {
                    DateTime last; if (attempts.TryGetValue(id, out last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(10)) return false;
                    attempts[id] = DateTime.UtcNow;
                    var lifetime = stopping.Token;
                    job = Task.Run(async () => { try { await serial.WaitAsync(lifetime).ConfigureAwait(false); try { await Prepare(id, sourceId, lifetime).ConfigureAwait(false); } finally { serial.Release(); } } catch (OperationCanceledException) { } catch { Log.Write("播放前弹幕准备未完成，继续正常播放。"); } }); jobs[id] = job;
                }
            }
            var cancelled = Task.Delay(Timeout.Infinite, cancellation);
            if (await Task.WhenAny(job, cancelled).ConfigureAwait(false) != job) cancellation.ThrowIfCancellationRequested();
            await job.ConfigureAwait(false); return true;
        }
        public async Task<bool> PrepareItem(Dictionary<string, object> item, CancellationToken cancellation, string origin = "播放准备")
        {
            try { return await PrepareItemCore(item, cancellation, origin).ConfigureAwait(false); }
            catch (OperationCanceledException) { Report(item, cancellation.IsCancellationRequested ? "已停止" : "准备超时", "可继续准备或手动重试", origin); throw; }
            catch { Report(item, "准备失败", "接口或文件处理失败，可手动重试", origin); throw; }
        }
        async Task<bool> PrepareItemCore(Dictionary<string, object> item, CancellationToken cancellation, string origin)
        {
            string video = Json.Text(item, "Path"); if (!File.Exists(video)) { Report(item, "文件已移除", "本地视频不存在，跳过此任务", origin); return false; }
            string xml = Path.ChangeExtension(video, ".xml");
            // Preserve explicit selections and existing sidecar files. Manual replacement remains available.
            if (File.Exists(xml) && new FileInfo(xml).Length > 0) { Report(item, "已就绪", "保留已有 XML", origin); return false; }
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                Report(item, "匹配中", "缓存优先 · 文件指纹 → 文件名", origin);
                var episode = await catalog.AutomaticEpisode(item, timeout.Token).ConfigureAwait(false);
                if (episode == null) { Report(item, "待确认", "没有唯一可靠候选，请手动选择来源", origin); Log.Write("自动弹幕：未找到唯一可靠候选，请手动匹配「" + Path.GetFileName(video) + "」。"); return false; }
                Report(item, "下载中", Json.Text(episode, "MatchMethod") == "hash" ? "文件指纹一致" : "文件名与集数匹配", origin);
                string content = await catalog.Download(episode, timeout.Token).ConfigureAwait(false);
                if (DanmuCatalog.ParseXml(content).GetElementsByTagName("d").Count == 0) { Report(item, "暂无弹幕", "此集暂无可用弹幕", origin); return false; }
                timeout.Token.ThrowIfCancellationRequested();
                if (!SettingsStore.WriteMissingSidecar(xml, content)) return false;
                catalog.RecordAssociation(item, episode);
                Report(item, "已就绪", catalog.EpisodeSource(episode), origin);
                Log.Write("自动弹幕已保存：「" + Path.GetFileName(video) + "」 · " + (Json.Text(episode, "MatchMethod") == "hash" ? "hash 精确识别" : "文件名 / 剧集匹配"));
                var saved = Saved; if (saved != null) saved(video); return true;
            }
        }
        public async Task Stop()
        {
            Task pending; CancellationTokenSource cancellation;
            lock (sync) { if (timer == null) return; timer.Dispose(); timer = null; cancellation = stopping; stopping = null; cancellation.Cancel(); pending = polling; }
            await pending.ConfigureAwait(false);
            Task[] active; lock (sync) active = jobs.Values.ToArray(); await Task.WhenAll(active).ConfigureAwait(false);
            lock (sync) { jobs.Clear(); attempts.Clear(); } cancellation.Dispose();
        }
        public void Dispose() { lock (sync) { if (timer != null) { timer.Dispose(); timer = null; } if (stopping != null) stopping.Cancel(); Saved = null; Progress = null; } }
    }
}
