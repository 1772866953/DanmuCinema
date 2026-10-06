using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    public static class SelfTests
    {
        public static int Run()
        {
            string originalRoot = Paths.Root;
            string output = Path.Combine(originalRoot, "tests", "output");
            Directory.CreateDirectory(output);
            var report = new List<string>();
            try
            {
                Paths.Root = Path.Combine(output, "selftest-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Paths.Data);
                var settings = new AppSettings { MediaFolder = Path.Combine(Paths.Root, "视频目录"), Port = 18096, DanmuPort = 19321, CloseToTray = false, StartServicesOnLaunch = true };
                SettingsStore.Save(settings);
                var loaded = SettingsStore.Load();
                Assert(!loaded.CloseToTray && loaded.StartServicesOnLaunch && loaded.MediaFolder == settings.MediaFolder, "关闭行为、启动选项、中文路径持久化", report);
                settings.CloseToTray = true; SettingsStore.Save(settings);
                Assert(SettingsStore.Load().CloseToTray && File.Exists(Paths.SettingsFile + ".bak"), "配置更新与原子替换备份", report);
                Assert(SettingsStore.Unprotect(SettingsStore.Protect("synthetic-test-token")) == "synthetic-test-token", "Windows 当前用户加密凭证往返", report);
                Assert(SettingsStore.Unprotect("invalid") == "", "损坏凭证不会被当作有效登录", report);
                string route;
                Assert(DanmuGateway.ValidateRoute("/secret/api/v2/search/anime?keyword=%E6%B5%8B%E8%AF%95", "secret", out route) == 200 && route.StartsWith("/api/v2/"), "弹幕搜索与中文查询路由", report);
                Assert(DanmuGateway.ValidateRoute("/wrong/api/v2/search/anime", "secret", out route) == 401, "错误弹幕访问密钥被拒绝", report);
                Assert(DanmuGateway.ValidateRoute("/secret/System/Shutdown", "secret", out route) == 404, "弹幕网关无法调用管理接口", report);
                Assert(DanmuGateway.ValidateRoute("/secret/api/v2/comment/../System", "secret", out route) == 404, "路径穿越被拒绝", report);
                Assert(DanmuGateway.ValidateRoute("/secret/api/v2/comment/%2e%2e", "secret", out route) != 200, "编码路径穿越被拒绝", report);
                Assert(!DanmuGateway.AllowedAddress(IPAddress.Parse("8.8.8.8")) && DanmuGateway.AllowedAddress(IPAddress.Parse("192.168.1.9")), "网关仅接受本机与私有网络", report);
                Assert(AutoStart.Quote(@"C:\路径 with space\DanmuCinema.exe") == "\"C:\\路径 with space\\DanmuCinema.exe\"" && AutoStart.Quote("C:\\folder\\") == "\"C:\\folder\\\\\"", "开机启动命令正确处理空格和尾部反斜杠", report);
                Assert(Json.Text(Json.Object("{\"version\":\"12.1\",\"items\":[1]}"), "Version") == "12.1", "插件与服务器 JSON 字段大小写兼容", report);
                var source = Json.Object("{\"site_id\":\"youku\",\"episode_size\":12}");
                Assert(Json.Text(source, "SiteId") == "youku" && Json.Text(source, "EpisodeSize") == "12", "真实插件 snake_case 来源字段兼容", report);
                Assert(MediaNames.CommentId(Json.Object("{\"cid\":\"XNjIzNzA4NjU2OA==\"}")) == "XNjIzNzA4NjU2OA==", "真实插件 cid 弹幕下载字段兼容", report);
                var romanized = new Dictionary<string, object> { { "Name", "Gaikotsu Kishi-sama II (3)" }, { "SeriesName", "骸骨骑士大人冒险中S2" }, { "Path", @"C:\Videos\骸骨骑士大人冒险中S2\Gaikotsu (3).mkv" }, { "Type", "Episode" } };
                Assert(MediaNames.Matches(romanized, "骸骨骑士") && MediaNames.Matches(romanized, "骸骨骑士 (3)") && !MediaNames.Matches(romanized, "无关影片"), "罗马字集名按中文剧集名搜索与多词过滤", report);
                var generic = new Dictionary<string, object> { { "Name", "Romanized 03" }, { "Path", @"C:\Videos\中文剧集S2\Romanized 03.mkv" }, { "Type", "Video" } };
                Assert(MediaNames.Matches(generic, "中文剧集") && MediaNames.SearchTitle(generic) == "中文剧集", "普通视频可按中文父目录搜索和发现弹幕", report);
                Assert(MediaNames.SearchTitle(romanized) == "骸骨骑士大人冒险中" && MediaNames.EpisodeLabel(romanized).Contains("集号未识别"), "候选搜索清理 S 季号且未识别集数明确提示", report);
                CatalogTests.Run(report).GetAwaiter().GetResult();
                MatchingTests.Run(report).GetAwaiter().GetResult();
                ScheduleTests.Run(report);
                LibraryTests.Run(report).GetAwaiter().GetResult();
                BrowsingTests.Run(report);
                WindowPlacementTests.Run(report);
                var invalid = new AppSettings { Port = 80 };
                bool rejected = false; try { invalid.Validate(); } catch (ArgumentException) { rejected = true; }
                Assert(rejected, "无效端口配置被拒绝", report);
                File.WriteAllText(Paths.SettingsFile, "broken");
                rejected = false; try { SettingsStore.Load(); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected && File.ReadAllText(Paths.SettingsFile) == "broken", "损坏配置明确报错并保留原文件", report);
                report.Add("PASS: " + report.Count + " checks");
                return 0;
            }
            catch (Exception e) { report.Add("FAIL: " + e); return 1; }
            finally { Paths.Root = originalRoot; File.WriteAllLines(Path.Combine(output, "self-test.txt"), report, new UTF8Encoding(false)); }
        }
        public static void Assert(bool condition, string description, List<string> report)
        {
            if (!condition) throw new Exception(description);
            report.Add("PASS: " + description);
        }
    }

    public static class IntegrationTests
    {
        public static async Task<int> Run()
        {
            string originalRoot = Paths.Root, runtime = Paths.Runtime;
            string output = Path.Combine(originalRoot, "tests", "output"); Directory.CreateDirectory(output);
            var report = new List<string>();
            string fixture = Path.Combine(output, "integration-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(fixture);
            ServiceManager manager = null; DanmuGateway gateway = null;
            int result = 0;
            try
            {
                Paths.Root = fixture; Paths.RuntimeOverride = runtime;
                Directory.CreateDirectory(Paths.Data);
                string media = Path.Combine(fixture, "media"); Directory.CreateDirectory(media);
                string filename = "DanMu Integration Pattern";
                var ffmpeg = new ProcessStartInfo(Path.Combine(runtime, "ffmpeg.exe"), "-hide_banner -loglevel error -f lavfi -i color=c=blue:s=320x180:r=24 -t 2 -c:v libx264 -pix_fmt yuv420p -y " + AutoStart.Quote(Path.Combine(media, filename + ".mp4"))) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                using (var generator = Process.Start(ffmpeg)) { generator.WaitForExit(); if (generator.ExitCode != 0) throw new Exception("无法生成测试视频。"); }
                string sampleXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><i><d p=\"0.5,1,25,16777215,0,0,test,1\">测试弹幕</d></i>";
                File.WriteAllText(Path.Combine(media, filename + ".xml"), sampleXml, new UTF8Encoding(false));
                string plugin = Path.Combine(Paths.ServerData, "plugins", "Danmu_2.8.0.0"); Directory.CreateDirectory(plugin);
                File.Copy(Path.Combine(originalRoot, "data", "jellyfin", "plugins", "Danmu_2.8.0.0", "Jellyfin.Plugin.Danmu.dll"), Path.Combine(plugin, "Jellyfin.Plugin.Danmu.dll"));
                var settings = new AppSettings { Port = FreePort(), DanmuPort = FreePort(), MediaFolder = media };
                while (settings.Port == settings.DanmuPort) settings.DanmuPort = FreePort();
                SettingsStore.Save(settings);
                manager = new ServiceManager(settings);
                await manager.Start();
                SelfTests.Assert(manager.OwnsProcess, "Jellyfin 真实进程启动与就绪检测", report);
                var publicInfo = await manager.Api.PublicInfo();
                report.Add("INFO: Jellyfin " + Json.Text(publicInfo, "Version"));
                await manager.Api.Initialize("integration-admin", "Test-" + Guid.NewGuid().ToString("N"));
                SelfTests.Assert(!String.IsNullOrEmpty(manager.Api.Token), "首次设置与管理员登录接口", report);
                await manager.Api.SetOriginalPolicy(true);
                var user = Json.Object(await manager.Api.Request("GET", "Users/" + settings.UserId, null, true));
                SelfTests.Assert(Json.Text(Json.Child(user, "Policy"), "EnableVideoPlaybackTranscoding") == "False", "原画模式禁止视频转码", report);
                var plugins = await manager.Api.Plugins();
                var installed = plugins.Cast<Dictionary<string, object>>().FirstOrDefault(x => Json.Text(x, "Name") == "Danmu");
                SelfTests.Assert(installed != null && Json.Text(installed, "Status") == "Active", "Danmu 插件已实际加载为 Active", report);
                await manager.Api.AddLibrary(media, "Integration", "movies");
                object[] items = new object[0];
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    items = await manager.Api.Items("DanMu");
                    if (items.Length > 0) break;
                    await Task.Delay(1500);
                }
                SelfTests.Assert(items.Length > 0, "真实测试视频入库并可搜索", report);
                var allItems = await manager.Api.Items("");
                var library = new MediaLibrary(); library.Replace(allItems.Cast<Dictionary<string, object>>());
                SelfTests.Assert(library.View("", LibrarySort.Name, false).Length == allItems.Length && library.Entries.Any(x => x.Name == filename + ".mp4" && x.HasXml && x.ModifiedUtc.HasValue && x.Size > 0), "真实媒体库无需搜索即可列出视频、XML 状态和文件元数据", report);
                SelfTests.Assert(library.View("Integration", LibrarySort.Modified, true).Length == 1 && library.View("不存在的影片", LibrarySort.Size, false).Length == 0, "真实入库视频支持本地筛选和排序", report);
                var folders = library.Browse(null, "", LibrarySort.Name, false);
                SelfTests.Assert(folders.Length == 1 && folders[0].IsFolder && library.Browse(folders[0].FolderPath, "", LibrarySort.Name, false).Any(x => x.HasXml), "真实媒体库默认展示目录，进入文件夹才列出完整影片", report);
                string itemId = Json.Text((Dictionary<string, object>)items[0], "Id");
                string xml = await manager.Api.Request("GET", "api/danmu/" + itemId + "/raw", null, true);
                SelfTests.Assert(xml.Contains("测试弹幕"), "插件 raw 接口读取同名 XML 弹幕", report);
                var duplicateBlocked = false;
                try { await manager.Api.AddLibrary(media, "Another", "movies"); } catch (InvalidOperationException) { duplicateBlocked = true; }
                SelfTests.Assert(duplicateBlocked, "同一媒体路径重复添加被阻止", report);
                using (var client = new HttpClient(new HttpClientHandler { UseProxy = false }))
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "MediaBrowser Token=\"" + manager.Api.Token + "\"");
                    var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + settings.Port + "/Videos/" + itemId + "/stream?Static=true");
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 127);
                    using (var response = await client.SendAsync(request))
                    {
                        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                        SelfTests.Assert(response.StatusCode == HttpStatusCode.PartialContent && bytes.Length == 128, "视频直传 HTTP Range 正确返回 206 与请求的 128 字节", report);
                    }
                }
                gateway = new DanmuGateway(settings); gateway.Start();
                using (var client = new HttpClient(new HttpClientHandler { UseProxy = false }))
                {
                    string baseUrl = "http://127.0.0.1:" + settings.DanmuPort;
                    using (var health = await client.GetAsync(baseUrl + "/health")) SelfTests.Assert(health.IsSuccessStatusCode, "弹幕网关健康检查", report);
                    using (var invalidKey = await client.GetAsync(baseUrl + "/wrong/api/v2/search/anime")) SelfTests.Assert(invalidKey.StatusCode == HttpStatusCode.Unauthorized, "网关拒绝错误访问密钥", report);
                    using (var management = await client.GetAsync(baseUrl + "/" + gateway.Key + "/System/Shutdown")) SelfTests.Assert(management.StatusCode == HttpStatusCode.NotFound, "网关不能代理管理接口", report);
                    string proxied = await client.GetStringAsync(baseUrl + "/" + gateway.Key + "/api/v2/bangumi/0");
                    SelfTests.Assert(proxied.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0, "播放器弹幕 API 请求已透传到真实插件", report);
                }
                await gateway.Stop(); await manager.Stop();
                SelfTests.Assert(NetworkInfo.PortFree(settings.Port) && NetworkInfo.PortFree(settings.DanmuPort), "停止服务后视频和弹幕端口均释放", report);
                await manager.Start(); gateway.Start();
                SelfTests.Assert(manager.OwnsProcess && gateway.Running, "停止后可重新启动服务", report);
                await gateway.Stop(); await manager.Stop();
                report.Add("PASS: Integration completed");
            }
            catch (Exception e) { report.Add("FAIL: " + e); result = 1; }
            {
                try { if (gateway != null) await gateway.Stop(); if (manager != null) await manager.Stop(); }
                catch (Exception e) { report.Add("CLEANUP FAIL: " + e.Message); result = 1; }
                if (gateway != null) gateway.Dispose(); if (manager != null) manager.Dispose();
                Paths.Root = originalRoot; Paths.RuntimeOverride = null;
                File.WriteAllLines(Path.Combine(output, "integration-test.txt"), report, new UTF8Encoding(false));
            }
            return result;
        }
        static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
        }
    }
}
