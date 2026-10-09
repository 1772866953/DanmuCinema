using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    public static class AutomaticResilienceTests
    {
        sealed class SourceFixture : HttpMessageHandler
        {
            public int Hashes, Names, Comments, Searches, Details, FailHashes, FailComments;
            public bool Offline, Duplicate, NameHit;
            public int Anime = 10;
            public HttpStatusCode Failure = HttpStatusCode.ServiceUnavailable;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested(); string path = request.RequestUri.AbsolutePath;
                if (path == "/api/v2/match")
                {
                    var body = Json.Object(await request.Content.ReadAsStringAsync());
                    if (Json.Text(body, "MatchMode") == "hashOnly")
                    {
                        Hashes++; if (Offline || FailHashes-- > 0) return new HttpResponseMessage(Failure);
                        // Keep concurrent playback callers in flight long enough to merge.
                        await Task.Delay(25, cancellation);
                        if (Json.Text(body, "FileName").Contains("[01]")) return Reply(Json.Write(new { success = true, isMatched = true, matches = new[] { new { animeId = Anime, episodeId = Anime * 100 + 1, animeTitle = "你遭难了吗", episodeTitle = "第1话", typeDescription = "动漫", shift = 2 } } }));
                    }
                    else
                    {
                        Names++; if (Offline) return new HttpResponseMessage(Failure);
                        if (NameHit) return Reply(Json.Write(new { success = true, isMatched = false, matches = new[] { new { animeId = Anime, episodeId = Anime * 100 + 9, animeTitle = "你遭难了吗", episodeTitle = "第9话", typeDescription = "动漫" } } }));
                    }
                    return Reply("{\"success\":true,\"isMatched\":false,\"matches\":[]}");
                }
                if (path.StartsWith("/api/v2/comment/"))
                { Comments++; if (Offline || FailComments-- > 0) return new HttpResponseMessage(Failure); return Reply("{\"comments\":[{\"cid\":1,\"p\":\"1,1,16777215,0\",\"m\":\"fixture\"}]}"); }
                if (path.StartsWith("/api/v2/bangumi/"))
                {
                    Details++; if (Offline) return new HttpResponseMessage(Failure);
                    var rows = Enumerable.Range(1, 12).Select(n => (object)new { episodeId = Anime * 100 + n, episodeNumber = n, episodeTitle = "第" + n + "话" }).ToList();
                    if (Duplicate) rows.Add(new { episodeId = 9999, episodeNumber = 9, episodeTitle = "第9话副本" });
                    return Reply(Json.Write(new { success = true, bangumi = new { animeTitle = "你遭难了吗", episodes = rows.ToArray() } }));
                }
                Searches++; if (Offline) return new HttpResponseMessage(Failure); return Reply("{\"success\":true,\"animes\":[]}");
            }
        }
        sealed class ServerFixture : HttpMessageHandler
        {
            public Dictionary<string, object> Item;
            public int Items, Searches;
            public int FailedSearches;
            public bool Playing;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested(); string path = request.RequestUri.AbsolutePath;
                if (path == "/Sessions") return Reply(Json.Write(Playing ? new object[] { new { NowPlayingItem = Item } } : new object[0]));
                if (path == "/api/danmu/search") { Searches++; if (FailedSearches-- > 0) throw new TaskCanceledException(); return Reply("[]"); }
                Items++; await Task.Delay(25, cancellation); return Reply(Json.Write(Item));
            }
        }
        static HttpResponseMessage Reply(string json) { return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }; }
        public static int Run()
        {
            string original = Paths.Root, output = Path.Combine(Paths.TestOutputFor(original), "automatic-resilience"); Directory.CreateDirectory(output);
            Paths.Root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Paths.Data); var report = new List<string>();
            try { RunCore(report).GetAwaiter().GetResult(); Desktop.CornerTintTests.Run(report, output); report.Add("PASS: " + report.Count(x => x.StartsWith("PASS")) + " focused checks. Mock HTTP only; no real API/service/power operations."); return 0; }
            catch (Exception error) { report.Add("FAIL " + error); return 1; }
            finally { Paths.Root = original; File.WriteAllLines(Path.Combine(output, "report.txt"), report); }
        }
        static void Check(bool condition, string name, List<string> report) { if (!condition) throw new Exception(name); report.Add("PASS " + name); }
        static Dictionary<string, object> Video(string folder, int number)
        {
            Directory.CreateDirectory(folder); string path = Path.Combine(folder, "[DMG&VCB-Studio] Sounan Desuka [" + number.ToString("D2") + "][Ma10p_1080p][x265_flac].mkv"); File.WriteAllText(path, new string('a', number * 17));
            return new Dictionary<string, object> { { "Id", "video" + number }, { "Path", path }, { "Name", Path.GetFileNameWithoutExtension(path) }, { "Type", "Episode" }, { "IndexNumber", 777 }, { "ParentIndexNumber", 1 } };
        }
        static async Task RunCore(List<string> report)
        {
            int requests = 0; var tokens = new List<CancellationToken>();
            bool timedOut = false;
            try { await SourceRequests.Run(async token => { requests++; tokens.Add(token); await Task.Delay(1000, token); return 1; }, "fixture", CancellationToken.None, TimeSpan.FromMilliseconds(15)); } catch (OperationCanceledException) { timedOut = true; }
            Check(timedOut && requests == 3 && tokens.All(t => t.IsCancellationRequested), "每次超时取消旧请求，恰好尝试三次且不留并行请求", report);
            requests = 0;
            using (var stop = new CancellationTokenSource(10))
            { try { await SourceRequests.Run(async token => { requests++; await Task.Delay(1000, token); return 1; }, "fixture", stop.Token); } catch (OperationCanceledException) { } }
            Check(requests == 1, "调用方主动取消不触发自动重试", report);
            var settings = new AppSettings { EnableDandan = true, EnableExistingDanmu = false, EnableAnimeko = false, EnableBahamut = false, UserId = "user", EncryptedToken = SettingsStore.Protect("fixture") };
            Directory.CreateDirectory(Path.GetDirectoryName(DandanConfig.FilePath)); File.WriteAllText(DandanConfig.FilePath, Json.Write(new DandanConfig { AppId = "fixture-app", EncryptedAppSecret = SettingsStore.Protect("fixture-secret") }));
            var one = Video(Path.Combine(Paths.Root, "season"), 1); var nine = Video(Path.Combine(Paths.Root, "season"), 9);
            Check(SmartMatching.Episode(nine) == 9, "识别蓝光文件[09]集号，优先于错误刮削元数据", report);
            var source = new SourceFixture { FailHashes = 2 }; var server = new ServerFixture { Item = one };
            using (var api = new JellyfinApi(settings, server)) using (var catalog = new DanmuCatalog(settings, api, source)) using (var automatic = new AutomaticDanmu(settings, api, catalog, () => false))
            {
                var first = await catalog.IdentifyFile(one, CancellationToken.None);
                Check(first.HashMatched && first.Recommended != null && source.Hashes == 3 && source.Names == 0, "官方hash连续失败两次后第三次成功，保持最高优先级", report);
                int calls = source.Hashes; await catalog.IdentifyFile(one, CancellationToken.None);
                Check(source.Hashes == calls, "成功识别直接读取缓存，无额外官方请求", report);
                source.FailComments = 2;
                Check(await automatic.PrepareItem(one, CancellationToken.None) && source.Comments == 3, "弹幕下载临时失败也重试三次并保存XML", report);
                var result = await catalog.AutomaticEpisode(nine, CancellationToken.None);
                Check(result != null && Json.Text(result, "MatchMethod") == "season" && Json.Text(result, "CommentId") == "1009" && Json.Text(result, "Shift") == "", "第9集hash及文件名无命中时，复用同目录已确认季度并准确映射第9集，不继承其他集偏移", report);
                int details = source.Details; await catalog.AutomaticEpisode(nine, CancellationToken.None);
                Check(source.Details == details, "同季度详情优先读取缓存，不重复请求整季列表", report);
                var oldRecord = DanmuAssociations.Read().Values.Single(); oldRecord.AnimeId = null; oldRecord.LocalTitle = null; oldRecord.Season = 0; DanmuAssociations.Save(Json.Text(one, "Path"), oldRecord);
                using (var restarted = new DanmuCatalog(settings, api, new SourceFixture()))
                { result = await restarted.AutomaticEpisode(nine, CancellationToken.None); Check(result != null && Json.Text(result, "CommentId") == "1009", "重启后兼容已有来源记录，可恢复同季度关联", report); }
                var different = Video(Path.Combine(Paths.Root, "different"), 9);
                Check(await catalog.AutomaticEpisode(different, CancellationToken.None) == null, "不同目录不会套用已确认作品", report);
                var unrelated = new Dictionary<string, object>(nine); unrelated["Path"] = Path.Combine(Path.GetDirectoryName(Json.Text(nine, "Path")), "Other Anime [09].mkv"); unrelated["Name"] = "Other Anime [09]"; File.WriteAllText(Json.Text(unrelated, "Path"), "unrelated");
                Check(await catalog.AutomaticEpisode(unrelated, CancellationToken.None) == null, "同一目录混有其他动漫时按名称隔离，不套用错误作品", report);
                var special = new Dictionary<string, object>(nine); special["Path"] = Path.Combine(Path.GetDirectoryName(Json.Text(nine, "Path")), "Sounan Desuka [SP].mkv"); File.WriteAllText(Json.Text(special, "Path"), "special");
                Check(await catalog.AutomaticEpisode(special, CancellationToken.None) == null, "特别篇不会套用正片集号", report);
                nine["ParentIndexNumber"] = 2;
                Check(await catalog.AutomaticEpisode(nine, CancellationToken.None) == null, "季度不同不会套用上一季集号", report); nine["ParentIndexNumber"] = 1;
                catalog.Cache.Clear(false); source.Duplicate = true;
                Check(await catalog.AutomaticEpisode(nine, CancellationToken.None) == null, "在线第9集有多个编号时拒绝自动猜测", report); source.Duplicate = false;
                catalog.Cache.Clear(false);
                var feature = await catalog.Feature(Json.Text(one, "Path"), CancellationToken.None);
                string key = DandanApiCache.Key("fixture-app|/api/v2/match|" + Json.Write(new { fileHash = feature.Hash, fileSize = feature.Length.ToString(CultureInfo.InvariantCulture), matchMode = "hashOnly" }));
                catalog.Cache.Write(key, "match", "legacy negative", "{\"success\":true,\"isMatched\":false,\"matches\":[]}"); calls = source.Hashes;
                Check((await catalog.IdentifyFile(one, CancellationToken.None)).HashMatched && source.Hashes == calls + 1, "旧空匹配缓存不会长期阻止重新识别", report);
                catalog.Cache.Write(key, "match", "broken payload", "broken"); calls = source.Hashes;
                Check((await catalog.IdentifyFile(one, CancellationToken.None)).HashMatched && source.Hashes == calls + 1, "损坏的识别缓存自动重取，不阻断匹配", report);
                await catalog.IdentifyFile(nine, CancellationToken.None); calls = source.Hashes; await catalog.IdentifyFile(nine, CancellationToken.None);
                Check(source.Hashes == calls + 1, "空匹配结果不持久缓存，下次播放仍可重新核对", report);
                source.NameHit = true; nine["SeriesName"] = "你遭难了吗"; nine["Type"] = "Movie";
                var named = await catalog.IdentifyFile(nine, CancellationToken.None);
                Check(!named.HashMatched && named.Recommended != null && Json.Text(named.Recommended, "CommentId") == "1009", "hash未命中后正确按名称与[09]集号匹配，兼容被归入电影库的动漫", report);
                source.NameHit = false; nine.Remove("SeriesName"); nine["Type"] = "Episode";
                File.Delete(Path.ChangeExtension(Json.Text(one, "Path"), ".xml")); catalog.Cache.Clear(false); source.Offline = true;
                string currentStatus = ""; automatic.Progress += (item, status, detail, origin) => currentStatus = status; automatic.Start();
                Check(!await automatic.PrepareForPlayback("video1", "", CancellationToken.None), "播放准备失败如实返回失败，不伪报已完成", report);
                Check(currentStatus == "准备失败", "网络故障与真实无匹配区分，不误报候选不足", report);
                source.Offline = false; calls = server.Items;
                var replay = await Task.WhenAll(Enumerable.Range(0, 4).Select(n => automatic.PrepareForPlayback("video1", "", CancellationToken.None)));
                Check(replay.All(x => x) && server.Items == calls + 1 && File.Exists(Path.ChangeExtension(Json.Text(one, "Path"), ".xml")), "失败后立即重新播放可重新匹配，并发播放请求仅执行一次准备", report);
                await automatic.Stop();
                catalog.Cache.Clear(false); source.Offline = true; source.Failure = HttpStatusCode.Unauthorized; calls = source.Hashes;
                try { await catalog.IdentifyFile(one, CancellationToken.None); } catch { }
                Check(source.Hashes == calls + 1, "鉴权错误不重复消耗接口请求", report);
                source.Failure = (HttpStatusCode)429; calls = source.Hashes;
                try { await catalog.IdentifyFile(one, CancellationToken.None); } catch { }
                Check(source.Hashes == calls + 1, "接口限流不连续重试，避免加重访问额度限制", report);
                source.Offline = false; settings.EnableDandan = false; settings.EnableExistingDanmu = true; server.FailedSearches = 2;
                var search = await catalog.Search("作品", true, false);
                Check(server.Searches == 3 && Json.Text((Dictionary<string, object>)search.Sources[0], "Error") == "", "现有平台超时两次后第三次重试生效", report);
            }
            // Polling is per active playback, rather than a ten-minute failure cooldown.
            settings.EnableDandan = true; settings.EnableExistingDanmu = false;
            var pollServer = new ServerFixture { Item = nine, Playing = true }; bool ready = false;
            using (var api = new JellyfinApi(settings, pollServer)) using (var catalog = new DanmuCatalog(settings, api, new SourceFixture())) using (var auto = new AutomaticDanmu(settings, api, catalog, () => ready))
            {
                catalog.Cache.Clear(false); auto.Start(); ((Timer)typeof(AutomaticDanmu).GetField("timer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(auto)).Change(Timeout.Infinite, Timeout.Infinite); ready = true;
                var poll = typeof(AutomaticDanmu).GetMethod("Poll", BindingFlags.NonPublic | BindingFlags.Instance);
                await (Task)poll.Invoke(auto, new object[] { CancellationToken.None }); await Jobs(auto); int count = pollServer.Items;
                await (Task)poll.Invoke(auto, new object[] { CancellationToken.None }); await Jobs(auto);
                Check(pollServer.Items == count, "同一播放会话中不会每次轮询重复请求", report);
                pollServer.Playing = false; await (Task)poll.Invoke(auto, new object[] { CancellationToken.None }); pollServer.Playing = true;
                await (Task)poll.Invoke(auto, new object[] { CancellationToken.None }); await Jobs(auto);
                Check(pollServer.Items == count + 1, "退出播放后再次播放同一影片，轮询也能立即重新触发", report); await auto.Stop();
            }
        }
        static Task Jobs(AutomaticDanmu auto) { var jobs = (Dictionary<string, Task<bool>>)typeof(AutomaticDanmu).GetField("jobs", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(auto); return Task.WhenAll(jobs.Values.ToArray()); }
    }
}
