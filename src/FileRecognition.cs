using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    public sealed class VideoFeature
    {
        public string Hash { get; set; }
        public string FileName { get; set; }
        public long Length { get; set; }
        public long WriteTicks { get; set; }
    }
    public sealed class FileRecognition
    {
        public Dictionary<string, object>[] Animes = new Dictionary<string, object>[0], Episodes = new Dictionary<string, object>[0];
        public Dictionary<string, object> Recommended;
        public bool HashMatched;
        public string Method = "", Status = "没有可靠匹配，请手动选择弹幕来源。";
    }
    public sealed partial class DanmuCatalog
    {
        public async Task<VideoFeature> Feature(string video, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var file = new FileInfo(video); if (!file.Exists) throw new FileNotFoundException("本地视频不存在。");
            long length = file.Length, ticks = file.LastWriteTimeUtc.Ticks;
            string key = DandanApiCache.Key("hash16m|" + file.FullName.ToUpperInvariant() + "|" + length + "|" + ticks);
            string cached = Cache.Read(key);
            if (cached != null) { try { var feature = Json.Read<VideoFeature>(cached); if (feature != null && Regex.IsMatch(feature.Hash ?? "", "^[a-fA-F0-9]{32}$") && feature.Length == length && feature.WriteTicks == ticks) return feature; } catch { } }
            // Only sample the first 16 MiB. Never scan tens of GB for identification.
            var result = await Task.Run(() =>
            {
                using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, FileOptions.SequentialScan))
                using (var md5 = MD5.Create())
                {
                    var buffer = new byte[65536]; long remaining = Math.Min(length, 16L * 1024 * 1024);
                    while (remaining > 0) { cancellation.ThrowIfCancellationRequested(); int count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining)); if (count == 0) throw new IOException("文件在识别时发生变化。"); md5.TransformBlock(buffer, 0, count, null, 0); remaining -= count; }
                    md5.TransformFinalBlock(new byte[0], 0, 0); file.Refresh();
                    if (!file.Exists || file.Length != length || file.LastWriteTimeUtc.Ticks != ticks) throw new IOException("文件在识别时发生变化，请重试。");
                    return new VideoFeature { Hash = BitConverter.ToString(md5.Hash).Replace("-", "").ToLowerInvariant(), Length = length, WriteTicks = ticks, FileName = Path.GetFileNameWithoutExtension(file.Name) };
                }
            }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested(); Cache.Write(key, "hash", Path.GetFileName(video), Json.Write(result), TimeSpan.FromDays(365)); return result;
        }
        public async Task<FileRecognition> IdentifyFile(Dictionary<string, object> item, CancellationToken cancellation)
        {
            var provider = Providers().FirstOrDefault(x => x.Id == "dandan");
            if (provider == null || !DandanConfig.Load().Ready) return new FileRecognition { Status = "官方源未启用或配置未就绪。" };
            var feature = await Feature(Json.Text(item, "Path"), cancellation).ConfigureAwait(false);
            long runtime; Int64.TryParse(Json.Text(item, "RunTimeTicks"), out runtime);
            int duration = (int)Math.Min(Int32.MaxValue, Math.Max(0, runtime / TimeSpan.TicksPerSecond));
            var hashResponse = Json.Object(await Fetch(provider, "/api/v2/match", new { fileName = feature.FileName, fileHash = feature.Hash, fileSize = feature.Length, videoDuration = duration, matchMode = "hashOnly" }, cancellation).ConfigureAwait(false));
            var hashRows = Json.Array(hashResponse, "Matches").OfType<Dictionary<string, object>>().ToArray();
            if (Json.Text(hashResponse, "IsMatched") == "True" && hashRows.Length == 1)
                return Recognized(provider, hashRows, item, true);
            var named = Json.Object(await Fetch(provider, "/api/v2/match", new { fileName = feature.FileName, fileHash = feature.Hash, fileSize = feature.Length, videoDuration = duration, matchMode = "fileNameOnly" }, cancellation).ConfigureAwait(false));
            return Recognized(provider, Json.Array(named, "Matches").OfType<Dictionary<string, object>>().ToArray(), item, false);
        }
        FileRecognition Recognized(Provider provider, Dictionary<string, object>[] rows, Dictionary<string, object> local, bool hashMatched)
        {
            var result = new FileRecognition { HashMatched = hashMatched, Method = hashMatched ? "hash" : "filename" };
            var works = new List<Dictionary<string, object>>(); var eps = new List<Dictionary<string, object>>();
            foreach (var raw in rows)
            {
                long animeId, episodeId;
                if (!Int64.TryParse(Json.Text(raw, "AnimeId"), out animeId) || animeId <= 0 || !Int64.TryParse(Json.Text(raw, "EpisodeId"), out episodeId) || episodeId <= 0 || String.IsNullOrWhiteSpace(Json.Text(raw, "AnimeTitle"))) continue;
                var anime = Anime(provider, animeId.ToString(), Json.Text(raw, "AnimeTitle"), "", "", Json.Text(raw, "TypeDescription"));
                var number = Regex.Match(Json.Text(raw, "EpisodeTitle"), @"^(?:第\s*)?(\d{1,3})(?=\s|话|話|集|$|[.：:])").Groups[1].Value;
                var episode = Episode(anime, episodeId.ToString(), number, Json.Text(raw, "EpisodeTitle")); episode["MatchMethod"] = result.Method;
                double shift; Double.TryParse(Json.Text(raw, "Shift"), NumberStyles.Float, CultureInfo.InvariantCulture, out shift); if (!Double.IsNaN(shift) && !Double.IsInfinity(shift)) episode["Shift"] = shift;
                works.Add(anime); eps.Add(episode);
            }
            result.Animes = works.GroupBy(x => Json.Text(x, "Id")).Select(x => x.First()).ToArray(); result.Episodes = eps.ToArray();
            lock (sync) { foreach (var anime in result.Animes) animes[Json.Text(anime, "Id")] = anime; foreach (var episode in eps) episodes[Json.Text(episode, "Id")] = episode; SaveCache(); }
            if (hashMatched && eps.Count == 1) result.Recommended = eps[0];
            else
            {
                string title = MediaNames.SearchTitle(local); int season = SmartMatching.Season(local), number = SmartMatching.Episode(local);
                var reliable = eps.Where(e =>
                {
                    var anime = works.First(a => Json.Text(a, "Id") == Json.Text(e, "AnimeId")); int remoteSeason = SmartMatching.SeasonTitle(Json.Text(anime, "Name"));
                    return SmartMatching.Score(title, anime, season) >= 90 && (season <= 1 ? remoteSeason <= 1 : remoteSeason == season) &&
                        (Json.Text(local, "Type") == "Movie" ? eps.Count == 1 : number > 0 && SmartMatching.RemoteNumber(e) == number);
                }).ToArray();
                if (reliable.Length == 1) result.Recommended = reliable[0];
            }
            result.Status = hashMatched && result.Recommended != null ? "弹弹play hash 精确识别成功。" : result.Recommended != null ? "hash 未命中，文件名、季度和集号匹配成功。" : eps.Count > 0 ? "找到文件名候选，存在歧义，请手动核对后选择。" : "hash 和文件名未找到匹配，请搜索作品名。";
            return result;
        }
        public async Task<Dictionary<string, object>> AutomaticEpisode(Dictionary<string, object> item, CancellationToken cancellation)
        {
            bool ambiguous = false;
            try { var match = await IdentifyFile(item, cancellation).ConfigureAwait(false); if (match.Recommended != null) return match.Recommended; ambiguous = match.Episodes.Length > 0; }
            catch (OperationCanceledException) { cancellation.ThrowIfCancellationRequested(); }
            catch { Log.Write("官方文件识别暂不可用，将尝试已启用的其他来源。"); }
            cancellation.ThrowIfCancellationRequested(); if (ambiguous) return null;
            string title = MediaNames.SearchTitle(item); if (String.IsNullOrWhiteSpace(title)) return null;
            int season = SmartMatching.Season(item), number = SmartMatching.Episode(item);
            if (Json.Text(item, "Type") != "Movie" && number <= 0) return null;
            foreach (var provider in Providers().Where(x => x.Id != "dandan"))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var search = await Search(title, Json.Text(item, "Type") != "Movie", true, season, provider.Id, cancellation).ConfigureAwait(false);
                    var exact = search.Items.OfType<Dictionary<string, object>>().Where(x => SmartMatching.Score(title, x, season) >= 90 && (season <= 1 ? SmartMatching.SeasonTitle(Json.Text(x, "Name")) <= 1 : SmartMatching.SeasonTitle(Json.Text(x, "Name")) == season)).ToArray();
                    if (exact.Length != 1) continue;
                    var episodes = (await Episodes(exact[0], cancellation).ConfigureAwait(false)).OfType<Dictionary<string, object>>().Where(x => Json.Text(item, "Type") == "Movie" || SmartMatching.RemoteNumber(x) == number).ToArray();
                    if (episodes.Length == 1) return episodes[0];
                }
                catch (OperationCanceledException) { cancellation.ThrowIfCancellationRequested(); }
                catch { }
            }
            return null;
        }
    }
}
