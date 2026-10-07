using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    public static class RecognitionTests
    {
        sealed class Fixture : HttpMessageHandler
        {
            public int Requests, Hashes, Names, Comments;
            public bool HashHit = true, Ambiguous, Offline, Redirect, Empty;
            public Dictionary<string, object> LastMatch;
            public bool SignatureValid, RedirectHasCredentials;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                Requests++; cancellation.ThrowIfCancellationRequested();
                if (Offline) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                if (request.RequestUri.Host == "comments.dandanplay.net")
                { RedirectHasCredentials = request.Headers.Contains("X-AppId") || request.Headers.Contains("X-Signature") || request.Headers.Contains("Authorization"); return Reply(CommentsJson()); }
                string time = request.Headers.GetValues("X-Timestamp").Single();
                using (var sha = SHA256.Create()) SignatureValid = request.Headers.GetValues("X-Signature").Single() == Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes("fixture-app" + time + request.RequestUri.AbsolutePath + "fixture-secret")));
                if (request.RequestUri.AbsolutePath == "/api/v2/match")
                {
                    LastMatch = Json.Object(await request.Content.ReadAsStringAsync()); string mode = Json.Text(LastMatch, "MatchMode");
                    if (mode == "hashOnly") { Hashes++; return Reply(HashHit ? "{\"success\":true,\"isMatched\":true,\"matches\":[" + Candidate(101, "测试番剧") + "]}" : "{\"success\":true,\"isMatched\":false,\"matches\":[]}"); }
                    Names++; return Reply("{\"success\":true,\"isMatched\":false,\"matches\":[" + Candidate(101, "测试番剧") + (Ambiguous ? "," + Candidate(102, "测试番剧") : "") + "]}");
                }
                if (request.RequestUri.AbsolutePath.StartsWith("/api/v2/comment/")) { Comments++; if (Redirect) { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri("https://comments.dandanplay.net/data"); return response; } return Reply(CommentsJson()); }
                if (request.RequestUri.AbsolutePath.StartsWith("/api/v2/search/")) return Reply("{\"success\":true,\"animes\":[{\"animeId\":10,\"animeTitle\":\"测试番剧\",\"typeDescription\":\"动漫\"}]}");
                return Reply("{\"success\":true,\"bangumi\":{\"episodes\":[{\"episodeId\":101,\"episodeNumber\":3,\"episodeTitle\":\"第3话\"}]}}");
            }
            static string Candidate(int id, string title) { return "{\"animeId\":10,\"episodeId\":" + id + ",\"animeTitle\":\"" + title + "\",\"episodeTitle\":\"第3话 测试标题\",\"typeDescription\":\"动漫\",\"shift\":2}"; }
            string CommentsJson() { return Empty ? "{\"comments\":[]}" : "{\"count\":1,\"comments\":[{\"cid\":1,\"p\":\"1,1,16777215,0\",\"m\":\"测试弹幕\"}]}"; }
            static HttpResponseMessage Reply(string text) { return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) }; }
        }
        sealed class PlayingFixture : HttpMessageHandler
        {
            readonly Dictionary<string, object> item;
            public int Sessions, Items;
            public bool Playing = true;
            public PlayingFixture(Dictionary<string, object> item) { this.item = item; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested(); string content;
                if (request.RequestUri.AbsolutePath == "/Sessions") { Sessions++; content = Json.Write(new[] { new { NowPlayingItem = Playing ? (object)new { Id = "fixturevideo", Type = "Episode" } : null, Client = "SenPlayer" } }); }
                else { Items++; content = Json.Write(item); }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
            }
        }
        public static async Task Run(List<string> report)
        {
            string root = Path.Combine(Paths.Root, "recognition"); Directory.CreateDirectory(root);
            var settings = new AppSettings { EnableExistingDanmu = false, EnableAnimeko = false, EnableBahamut = false, EnableDandan = true, UserId = "fixtureuser", EncryptedToken = SettingsStore.Protect("fixture-token") };
            SettingsStore.AtomicWrite(DandanConfig.FilePath, Json.Write(new DandanConfig { AppId = "fixture-app", EncryptedAppSecret = SettingsStore.Protect("fixture-secret") }), false);
            string video = Path.Combine(root, "测试番剧 S01E03.mkv");
            using (var stream = File.Create(video)) { stream.SetLength(17 * 1024 * 1024); stream.Write(new byte[] { 1, 2, 3 }, 0, 3); }
            var local = new Dictionary<string, object> { { "Id", "fixturevideo" }, { "Path", video }, { "SeriesName", "测试番剧" }, { "Name", "第3集" }, { "Type", "Episode" }, { "ParentIndexNumber", 1 }, { "IndexNumber", 3 } };
            var handler = new Fixture();
            using (var api = new JellyfinApi(settings)) using (var catalog = new DanmuCatalog(settings, api, handler))
            {
                catalog.Cache.Clear(false); var feature = await catalog.Feature(video, CancellationToken.None);
                var sample = new byte[16 * 1024 * 1024]; sample[0] = 1; sample[1] = 2; sample[2] = 3;
                string expected; using (var md5 = MD5.Create()) expected = BitConverter.ToString(md5.ComputeHash(sample)).Replace("-", "").ToLowerInvariant();
                SelfTests.Assert(feature.Hash == expected && feature.Length == 17 * 1024 * 1024 && !feature.FileName.EndsWith(".mkv"), "只使用前 16 MiB 的 MD5，提交真实文件大小和不含扩展名的文件名", report);
                var result = await catalog.IdentifyFile(local, CancellationToken.None);
                SelfTests.Assert(result.HashMatched && result.Recommended != null && handler.Hashes == 1 && handler.Names == 0 && handler.SignatureValid, "官方签名正确且 hash 命中后不调用文件名匹配", report);
                await catalog.IdentifyFile(local, CancellationToken.None); SelfTests.Assert(handler.Requests == 1, "第二次识别完全读取本地缓存，不消耗接口调用", report);
                local["RunTimeTicks"] = 24L * TimeSpan.TicksPerMinute;
                await catalog.IdentifyFile(local, CancellationToken.None);
                SelfTests.Assert(handler.Requests == 1, "播放会话补充时长后仍复用同一 hash 识别缓存", report);
                using (var restarted = new DanmuCatalog(settings, api, new Fixture { Offline = true })) SelfTests.Assert((await restarted.IdentifyFile(local, CancellationToken.None)).HashMatched, "重启后离线可恢复 hash 与节目编号的缓存关联", report);
                handler.Redirect = true;
                string comments = await catalog.Download(result.Recommended);
                SelfTests.Assert(handler.Comments == 1 && !handler.RedirectHasCredentials && comments.Contains("3,1,25,16777215") && comments.Contains("测试弹幕"), "官方弹幕加速跳转不发送凭证，识别偏移正确应用", report);
                int before = handler.Requests; await catalog.Download(result.Recommended);
                SelfTests.Assert(handler.Requests == before, "再次下载从缓存生成 XML，不重复请求官方或加速服务", report);
                SelfTests.Assert(!DanmuCatalog.AllowedCommentRedirect(new Uri("https://dandanplay.net.evil.test/")) && !DanmuCatalog.AllowedCommentRedirect(new Uri("http://127.0.0.1/")), "弹幕跳转限制官方域名与 HTTPS", report);
                var auto = new AutomaticDanmu(settings, api, catalog, () => true);
                string xml = Path.ChangeExtension(video, ".xml");
                SelfTests.Assert(await auto.PrepareItem(local, CancellationToken.None) && File.Exists(xml) && !File.Exists(xml + ".bak") && handler.Requests == before, "自动播放匹配使用缓存保存同名 XML，无 BAK 和额外接口请求", report);
                File.WriteAllText(xml, "manual selection"); await auto.PrepareItem(local, CancellationToken.None);
                SelfTests.Assert(File.ReadAllText(xml) == "manual selection" && handler.Requests == before, "自动查找保留已有 XML 和用户手动来源", report);
                File.Delete(xml);
                var playing = new PlayingFixture(local);
                using (var playApi = new JellyfinApi(settings, playing)) using (var preparation = new AutomaticDanmu(settings, playApi, catalog, () => false))
                {
                    preparation.Start(); int beforeItems = playing.Items;
                    await Task.WhenAll(Enumerable.Range(0, 4).Select(i => preparation.PrepareForPlayback("fixturevideo", "", CancellationToken.None)));
                    SelfTests.Assert(File.Exists(xml) && playing.Items == beforeItems + 1 && handler.Requests == before, "并发首播请求合并准备任务，缓存命中时无需接口请求，UI忙碌也可准备", report);
                    SelfTests.Assert(!await preparation.PrepareForPlayback("../invalid", "", CancellationToken.None), "播放准备拒绝无效影片编号", report);
                    await preparation.Stop(); File.Delete(xml);
                }
                using (var playApi = new JellyfinApi(settings, playing)) using (var monitor = new AutomaticDanmu(settings, playApi, catalog, () => true))
                {
                    monitor.Start(); int attempts = 0; while (!File.Exists(xml) && attempts++ < 100) await Task.Delay(30); await monitor.Stop();
                    SelfTests.Assert(File.Exists(xml) && playing.Sessions > 0 && handler.Requests == before, "模拟 SenPlayer 发起 Jellyfin 播放，后台监测自动识别和缓存下载", report);
                    File.Delete(xml); monitor.SuppressUntilPlaybackEnds(new[] { "fixturevideo" }); int sessionsBefore = playing.Sessions;
                    monitor.Start(); attempts = 0; while (playing.Sessions == sessionsBefore && attempts++ < 100) await Task.Delay(30); await monitor.Stop();
                    SelfTests.Assert(!File.Exists(xml) && handler.Requests == before, "当前播放中删除弹幕后不会立刻自动重新下载", report);
                    playing.Playing = false; sessionsBefore = playing.Sessions; monitor.Start(); attempts = 0;
                    while (playing.Sessions == sessionsBefore && attempts++ < 100) await Task.Delay(30); await monitor.Stop();
                    playing.Playing = true; monitor.Start(); attempts = 0; while (!File.Exists(xml) && attempts++ < 100) await Task.Delay(30); await monitor.Stop();
                    SelfTests.Assert(File.Exists(xml) && handler.Requests == before, "播放结束后解除删除抑制，下次观看仍能从缓存准备弹幕", report);
                }
                catalog.Cache.Clear(false); handler.HashHit = false; handler.Redirect = false;
                result = await catalog.IdentifyFile(local, CancellationToken.None);
                SelfTests.Assert(result.Method == "filename" && result.Recommended != null && handler.Names == 1 && Json.Text(handler.LastMatch, "MatchMode") == "fileNameOnly", "hash 未命中后使用文件名，严格核对标题、季度和集号", report);
                catalog.Cache.Clear(false); handler.Ambiguous = true;
                result = await catalog.IdentifyFile(local, CancellationToken.None);
                SelfTests.Assert(result.Episodes.Length == 2 && result.Recommended == null && await catalog.AutomaticEpisode(local, CancellationToken.None) == null, "文件名存在多个候选时不会自动选择或下载错误集数", report);
                var cache = catalog.Cache; cache.Clear(false); int calls = 0; string key = DandanApiCache.Key("concurrent");
                await Task.WhenAll(Enumerable.Range(0, 5).Select(i => cache.Get(key, "match", "fixture", async () => { Interlocked.Increment(ref calls); await Task.Delay(25); return "cached"; }, CancellationToken.None)));
                SelfTests.Assert(calls == 1 && cache.Read(key) == "cached", "并发请求合并为一次官方调用", report);
                string expired = DandanApiCache.Key("expired"); WriteAgedCache(cache, expired, "comment", "fixture", "expired");
                SelfTests.Assert(cache.Read(expired) == null && cache.Entries().Length == 2, "过期缓存可管理但不会用于播放", report);
                cache.Clear(true); SelfTests.Assert(cache.Read(key) == "cached" && cache.Entries().Length == 1, "清理过期缓存保留有效数据", report);
                cache.Remove(new[] { key }); SelfTests.Assert(cache.Entries().Length == 0 && File.Exists(xml), "逐条删除缓存不删除视频旁的弹幕 XML", report);
                bool rejected = false; try { cache.Remove(new[] { "../settings" }); } catch (ArgumentException) { rejected = true; }
                SelfTests.Assert(rejected, "缓存管理拒绝路径穿越", report);
                handler.Offline = true; int failures = handler.Requests;
                await catalog.Search("测试番剧", true, false); await catalog.Search("测试番剧", true, false);
                SelfTests.Assert(handler.Requests == failures + 2 && cache.Entries().Length == 0, "失败响应不缓存，后续仍可重试", report);
                File.WriteAllText(Path.Combine(cache.DirectoryPath, DandanApiCache.Key("broken") + ".json"), "broken"); cache.Clear(false);
                SelfTests.Assert(Directory.GetFiles(cache.DirectoryPath, "*.json").Length == 0, "清空缓存也清理损坏条目", report);
                auto.Dispose();
            }
        }
        public static void WriteAgedCache(DandanApiCache cache, string key, string kind, string label, string content)
        {
            Directory.CreateDirectory(cache.DirectoryPath);
            File.WriteAllText(Path.Combine(cache.DirectoryPath, key + ".json"), Json.Write(new ApiCacheEntry { Key = key, Kind = kind, Label = label, Content = content, CreatedUtc = DateTime.UtcNow.AddYears(-2), ExpiresUtc = DateTime.UtcNow.AddSeconds(-1) }));
        }
    }
}
