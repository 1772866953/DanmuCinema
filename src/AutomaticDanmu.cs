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
        CancellationTokenSource stopping;
        Timer timer;
        Task polling = Task.FromResult(0);
        public event Action<string> Saved;
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
                foreach (var session in sessions.OfType<Dictionary<string, object>>())
                {
                    var item = Json.Child(session, "NowPlayingItem"); string id = Json.Text(item, "Id");
                    if (item == null || !Regex.IsMatch(id, "^[a-zA-Z0-9]{1,64}$") || (Json.Text(item, "Type") != "Movie" && Json.Text(item, "Type") != "Episode" && Json.Text(item, "Type") != "Video")) continue;
                    lock (sync)
                    {
                        DateTime last;
                        if (jobs.ContainsKey(id) || attempts.TryGetValue(id, out last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(10)) continue;
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
        public async Task<bool> PrepareItem(Dictionary<string, object> item, CancellationToken cancellation)
        {
            string video = Json.Text(item, "Path"); if (!File.Exists(video)) return false;
            string xml = Path.ChangeExtension(video, ".xml");
            // Preserve explicit selections and existing sidecar files. Manual replacement remains available.
            if (File.Exists(xml) && new FileInfo(xml).Length > 0) return false;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                var episode = await catalog.AutomaticEpisode(item, timeout.Token).ConfigureAwait(false);
                if (episode == null) { Log.Write("自动弹幕：未找到唯一可靠候选，请手动匹配「" + Path.GetFileName(video) + "」。"); return false; }
                string content = await catalog.Download(episode, timeout.Token).ConfigureAwait(false);
                if (DanmuCatalog.ParseXml(content).GetElementsByTagName("d").Count == 0) return false;
                timeout.Token.ThrowIfCancellationRequested();
                if (!SettingsStore.WriteMissingSidecar(xml, content)) return false;
                catalog.RecordAssociation(item, episode);
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
        public void Dispose() { lock (sync) { if (timer != null) { timer.Dispose(); timer = null; } if (stopping != null) stopping.Cancel(); Saved = null; } }
    }
}
