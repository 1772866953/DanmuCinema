using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    public static class MatchingTests
    {
        static Dictionary<string, object> Video(string path, string name = "") { return new Dictionary<string, object> { { "Path", path }, { "Name", name }, { "Type", "Episode" } }; }
        static Dictionary<string, object> Remote(string number) { return new Dictionary<string, object> { { "Number", number }, { "CommentId", number }, { "Title", "" } }; }
        sealed class FallbackHandler : HttpMessageHandler
        {
            public int Searches;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                Searches++;
                string keyword = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["keyword"];
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(keyword == "骸骨骑士" ? "{\"success\":true,\"animes\":[{\"animeId\":100,\"animeTitle\":\"骸骨骑士大人异世界冒险中 第二季\",\"typeDescription\":\"动漫\"}]}" : "{\"success\":true,\"animes\":[]}") });
            }
        }
        public static async Task Run(List<string> report)
        {
            string root = Paths.Root;
            string allowedTestRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "tests", "output")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(DandanConfig.FilePath).StartsWith(allowedTestRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Credential tests require an isolated tests/output fixture; real project configuration is never overwritten.");
            var s2 = Video(Path.Combine(root, "ShowS2", "Show (3).mkv")); s2["SeriesName"] = "骸骨骑士大人冒险中S2";
            SelfTests.Assert(SmartMatching.Season(s2) == 2 && SmartMatching.Episode(s2) == 3 && SmartMatching.Episode(Video("C:\\video\\Show S02E10.mkv")) == 10, "缺失元数据时识别季号及括号、SxxExx 集号", report);
            SelfTests.Assert(SmartMatching.Episode(Video("C:\\video\\Show 2026.mkv")) == 0 && SmartMatching.Episode(Video("C:\\video\\Show 01-02.mkv")) == 0 && SmartMatching.Episode(Video("C:\\video\\Show SP01.mkv")) == 0, "年份、合并集与特别篇不误识别为普通集数", report);
            SelfTests.Assert(SmartMatching.CleanTitle("[字幕组] 测试番剧 S02E03 1080p HEVC") == "测试番剧" && SmartMatching.Queries("骸骨骑士大人冒险中S2").Contains("骸骨骑士"), "智能搜索清理发布标签并生成短标题回退", report);
            var a = new Dictionary<string, object> { { "Name", "骸骨骑士大人异世界冒险中 第二季" } };
            var b = new Dictionary<string, object> { { "Name", "骸骨骑士大人异世界冒险中 第一季" } };
            SelfTests.Assert(SmartMatching.Score("骸骨骑士大人冒险中", a, 2) >= 75 && SmartMatching.Score("骸骨骑士大人冒险中", a, 2) > SmartMatching.Score("骸骨骑士大人冒险中", b, 2), "近似名称匹配优先正确季度", report);
            var otherSeason = Video(Path.Combine(root, "ShowS1", "Show (3).mkv")); otherSeason["SeriesId"] = "same"; s2["SeriesId"] = "same";
            SelfTests.Assert(!SmartMatching.SameSeason(s2, otherSeason), "整季下载不混入同番剧其他季度", report);
            string media = Path.Combine(root, "batch-fixture"); Directory.CreateDirectory(media);
            var videos = new[] { 10, 2, 1 }.Select(n => Video(Path.Combine(media, "Show (" + n + ").mkv"))).ToArray();
            foreach (var video in videos) File.WriteAllText(Json.Text(video, "Path"), "synthetic video");
            var plan = BatchMatching.Plan(videos, new[] { Remote("01"), Remote("2.0"), Remote("10") });
            SelfTests.Assert(plan.Select(x => x.Number).SequenceEqual(new[] { 1, 2, 10 }) && plan.All(x => x.Selected), "批量下载按数值集号对应，不按文件列表顺序", report);
            var duplicate = BatchMatching.Plan(videos.Concat(new[] { Video(Path.Combine(media, "Alternative (1).mkv")) }), new[] { Remote("1"), Remote("2"), Remote("10") });
            SelfTests.Assert(duplicate.Where(x => x.Number == 1).All(x => !x.Selected), "本地重复版本不自动选择覆盖", report);
            string firstXml = Path.ChangeExtension(Json.Text(videos[2], "Path"), ".xml"); File.WriteAllText(firstXml, "preserve-existing");
            string tenthXml = Path.ChangeExtension(Json.Text(videos[0], "Path"), ".xml"); File.WriteAllText(tenthXml, "old-comments");
            var result = await BatchDownloads.Run(plan, e => { if (Json.Text(e, "Number") == "2.0") throw new IOException("test failure"); return Task.FromResult("<i><d p=\"1,1,25,16777215\">test</d></i>"); }, false, CancellationToken.None, null, 0);
            SelfTests.Assert(result.Saved == 2 && result.Failed == 1 && File.ReadAllText(tenthXml + ".bak") == "old-comments" && File.ReadAllText(Json.Text(videos[0], "Path")) == "synthetic video", "批量下载单集失败不中断，替换备份且视频不改写", report);
            result = await BatchDownloads.Run(plan, e => { if (Json.Text(e, "Number") == "2.0") { var failed = new TaskCompletionSource<string>(); failed.SetException(new OperationCanceledException("source timeout")); return failed.Task; } return Task.FromResult("<i><d p=\"1,1,25,16777215\">test</d></i>"); }, false, CancellationToken.None, null, 0);
            SelfTests.Assert(result.Failed == 1 && result.Saved == 2 && !result.Cancelled, "上游单集超时不会被误当作用户取消整个批次", report);
            int calls = 0;
            result = await BatchDownloads.Run(plan, e => { calls++; return Task.FromResult("<i/>"); }, true, CancellationToken.None, null, 0);
            SelfTests.Assert(calls == 1 && result.Skipped == 3, "批量下载保留已有文件，空弹幕不写入", report);
            using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); result = await BatchDownloads.Run(plan, e => Task.FromResult("<i/>"), false, cancel.Token, null, 0); }
            SelfTests.Assert(result.Cancelled && result.Saved == 0, "取消批量下载停止后续文件", report);
            using (var cancel = new CancellationTokenSource())
            {
                cancel.CancelAfter(20);
                result = await BatchDownloads.Run(plan, async e => { await Task.Delay(1000, cancel.Token); return "<i/>"; }, false, cancel.Token, null, 0);
                SelfTests.Assert(result.Cancelled && result.Saved == 0, "下载请求执行中可取消且不写入半成品", report);
            }
            var settings = new AppSettings { EnableAnimeko = false, EnableBahamut = false, EnableDandan = false, EnableExistingDanmu = false, EncryptedAdditionalApis = SettingsStore.Protect("智能源|https://smart.test") };
            var handler = new FallbackHandler();
            using (var api = new JellyfinApi(settings)) using (var catalog = new DanmuCatalog(settings, api, handler))
            {
                var search = await catalog.Search("骸骨骑士大人冒险中S2", true, true, 2);
                SelfTests.Assert(search.Items.Length == 1 && handler.Searches == 3, "所有来源支持智能多词回退查询", report);
            }
            settings.DandanAppId = "synthetic-app"; settings.EncryptedDandanSecret = SettingsStore.Protect("synthetic-secret"); DandanConfig.Ensure(settings);
            string credentials = File.ReadAllText(DandanConfig.FilePath);
            SelfTests.Assert(DandanConfig.Load().Ready && DandanConfig.Load().Secret == "synthetic-secret" && !credentials.Contains("synthetic-secret") && settings.EncryptedDandanSecret == "", "官方凭证从旧设置迁移到独立配置，迁移保留加密", report);
            File.WriteAllText(DandanConfig.FilePath, "{\"AppId\":\"manual-app\",\"AppSecret\":\"manual-secret\"}");
            SelfTests.Assert(DandanConfig.Load().Ready && DandanConfig.Load().Secret == "manual-secret", "独立配置文件修改在下次请求读取", report);
        }
    }
}
