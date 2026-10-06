using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace DanmuCinema
{
    public sealed class JellyfinApi : IDisposable
    {
        readonly HttpClient client;
        readonly AppSettings settings;
        public string Token { get { return SettingsStore.Unprotect(settings.EncryptedToken); } }
        public JellyfinApi(AppSettings settings, HttpMessageHandler handler = null) { this.settings = settings; client = new HttpClient(handler ?? new HttpClientHandler { UseProxy = false }); client.Timeout = TimeSpan.FromSeconds(45); }
        public async Task<string> Request(string method, string route, object body, bool auth, CancellationToken cancellation = default(CancellationToken))
        {
            using (var request = new HttpRequestMessage(new HttpMethod(method), "http://127.0.0.1:" + settings.Port + "/" + route.TrimStart('/')))
            {
                string authorization = "MediaBrowser Client=\"DanmuCinema\", Device=\"Windows\", DeviceId=\"danmu-lan-controller\", Version=\"1.0.0\"";
                if (auth)
                {
                    if (String.IsNullOrEmpty(Token)) throw new InvalidOperationException("请先在「首次设置」中初始化服务器或登录管理员账号。");
                    authorization += ", Token=\"" + Token + "\"";
                }
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
                if (body != null) request.Content = new StringContent(Json.Write(body), Encoding.UTF8, "application/json");
                using (var response = await client.SendAsync(request, cancellation))
                {
                    string content = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("Jellyfin 接口 " + route.Split('?')[0] + " 返回 " + (int)response.StatusCode + "。请查看服务器日志，或重新登录。");
                    return content;
                }
            }
        }
        public async Task<Dictionary<string, object>> PublicInfo()
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            using (var response = await client.GetAsync("http://127.0.0.1:" + settings.Port + "/System/Info/Public", timeout.Token))
            {
                response.EnsureSuccessStatusCode();
                return Json.Object(await response.Content.ReadAsStringAsync());
            }
        }
        public async Task WaitForApplicationReady()
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            using (var response = await client.GetAsync("http://127.0.0.1:" + settings.Port + "/health", timeout.Token))
            {
                response.EnsureSuccessStatusCode();
                string health = await response.Content.ReadAsStringAsync();
                if (!String.Equals(health.Trim(), "Healthy", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Jellyfin 尚未就绪。");
            }
        }
        public async Task Login(string name, string password)
        {
            var response = Json.Object(await Request("POST", "Users/AuthenticateByName", new { Username = name, Pw = password }, false));
            string token = Json.Text(response, "AccessToken");
            if (String.IsNullOrEmpty(token)) throw new InvalidOperationException("服务器未返回登录凭证。");
            var user = Json.Child(response, "User");
            var policy = Json.Child(user, "Policy");
            if (policy == null || Json.Text(policy, "IsAdministrator") != "True") throw new InvalidOperationException("请使用 Jellyfin 管理员账号登录控制台。");
            var info = await PublicInfo();
            settings.AdminName = name;
            settings.UserId = Json.Text(user, "Id");
            settings.ServerId = Json.Text(info, "Id");
            settings.EncryptedToken = SettingsStore.Protect(token);
            SettingsStore.Save(settings);
        }
        public async Task Initialize(string name, string password)
        {
            if (String.IsNullOrWhiteSpace(name) || password.Length < 8) throw new ArgumentException("管理员名称不能为空，密码至少 8 位。");
            var info = await PublicInfo();
            if (Json.Text(info, "StartupWizardCompleted") == "True")
            {
                await Login(name, password);
                return;
            }
            await Request("POST", "Startup/Configuration", new { ServerName = "弹幕影院", UICulture = "zh-CN", MetadataCountryCode = "CN", PreferredMetadataLanguage = "zh" }, false);
            // Jellyfin 12 creates the initial user lazily through this GET endpoint.
            await Request("GET", "Startup/User", null, false);
            await Request("POST", "Startup/User", new { Name = name, Password = password }, false);
            await Request("POST", "Startup/RemoteAccess", new { EnableRemoteAccess = false, EnableAutomaticPortMapping = false }, false);
            await Request("POST", "Startup/Complete", new { }, false);
            await Login(name, password);
        }
        public async Task AddLibrary(string folder, string name, string type)
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("视频目录不存在：" + folder);
            var libraries = Json.Read<object[]>(await Request("GET", "Library/VirtualFolders", null, true));
            foreach (Dictionary<string, object> library in libraries)
            {
                foreach (var location in Json.Array(library, "Locations"))
                    if (String.Equals(Path.GetFullPath(Convert.ToString(location)).TrimEnd('\\'), Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("这个目录已经在媒体库中。请使用「扫描媒体库」。");
                if (String.Equals(Json.Text(library, "Name"), name, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("媒体库名称已存在，请更换名称。");
            }
            var options = new
            {
                PathInfos = new[] { new { Path = folder } },
                EnableRealtimeMonitor = true,
                SaveLocalMetadata = false,
                EnableChapterImageExtraction = false,
                ExtractChapterImagesDuringLibraryScan = false,
                EnableTrickplayImageExtraction = false,
                ExtractTrickplayImagesDuringLibraryScan = false,
                EnableAutomaticSeriesGrouping = true,
                PreferredMetadataLanguage = "zh",
                MetadataCountryCode = "CN",
                SubtitleDownloadLanguages = new[] { "chi" },
                SubtitleFetcherOrder = new[] { "Danmu" },
                DisabledSubtitleFetchers = new string[0],
                SkipSubtitlesIfEmbeddedSubtitlesPresent = false,
                SkipSubtitlesIfAudioTrackMatches = false,
                RequirePerfectSubtitleMatch = false
            };
            await Request("POST", "Library/VirtualFolders?name=" + Uri.EscapeDataString(name) + "&collectionType=" + type + "&refreshLibrary=true", new { LibraryOptions = options }, true);
            Log.Write("已添加媒体库：" + name);
        }
        public async Task SetOriginalPolicy(bool preferOriginal)
        {
            var user = Json.Object(await Request("GET", "Users/" + settings.UserId, null, true));
            var policy = Json.Child(user, "Policy");
            if (policy == null) throw new InvalidOperationException("无法读取用户播放设置。");
            policy["EnableVideoPlaybackTranscoding"] = !preferOriginal;
            policy["EnableAudioPlaybackTranscoding"] = true;
            policy["EnablePlaybackRemuxing"] = true;
            await Request("POST", "Users/" + settings.UserId + "/Policy", policy, true);
            Log.Write(preferOriginal ? "当前账号已禁止视频转码，允许无损重新封装和音频转换。" : "当前账号允许视频转码。");
        }
        public async Task<object[]> Items(string search)
        {
            // Jellyfin SearchTerm does not match SeriesName or the Chinese parent folder
            // of an episode whose own name is romanized. Filter the complete paged list.
            var matches = new List<object>();
            int start = 0;
            while (true)
            {
                string route = "Items?Recursive=true&IncludeItemTypes=Movie,Episode,Video&Fields=Path,MediaSources,ProviderIds&Limit=250&SortBy=SortName&SortOrder=Ascending&StartIndex=" + start;
                var page = Json.Object(await Request("GET", route, null, true));
                var items = Json.Array(page, "Items");
                foreach (Dictionary<string, object> item in items)
                    if (MediaNames.Matches(item, search)) matches.Add(item);
                start += items.Length;
                int total;
                if (items.Length == 0 || (Int32.TryParse(Json.Text(page, "TotalRecordCount"), out total) && start >= total) || items.Length < 250) break;
            }
            return matches.ToArray();
        }
        public async Task<object[]> Plugins() { return Json.Read<object[]>(await Request("GET", "Plugins", null, true)); }
        public async Task<object[]> Sessions() { return Json.Read<object[]>(await Request("GET", "Sessions", null, true)); }
        public void Dispose() { client.Dispose(); }
    }

    public class ProcessRecord
    {
        public int Id { get; set; }
        public long StartTicks { get; set; }
        public string Executable { get; set; }
    }

    public sealed class ServiceManager : IDisposable
    {
        readonly AppSettings settings;
        readonly SemaphoreSlim lifecycle = new SemaphoreSlim(1, 1);
        Process ownedProcess;
        public readonly JellyfinApi Api;
        public bool DesiredRunning { get; private set; }
        public bool Transitioning { get; private set; }
        public bool OwnsProcess { get { return ownedProcess != null && !ownedProcess.HasExited; } }
        public int ProcessId { get { return OwnsProcess ? ownedProcess.Id : 0; } }
        string RecordPath { get { return Path.Combine(Paths.Data, "server-process.json"); } }
        public ServiceManager(AppSettings settings)
        {
            this.settings = settings;
            Api = new JellyfinApi(settings);
            RecoverOwnership();
        }
        void RecoverOwnership()
        {
            try
            {
                if (!File.Exists(RecordPath)) return;
                var record = Json.Read<ProcessRecord>(File.ReadAllText(RecordPath));
                var candidate = Process.GetProcessById(record.Id);
                if (!candidate.HasExited && candidate.StartTime.ToUniversalTime().Ticks == record.StartTicks &&
                    String.Equals(candidate.MainModule.FileName, record.Executable, StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(record.Executable, Paths.FindServer(), StringComparison.OrdinalIgnoreCase))
                {
                    ownedProcess = candidate;
                    DesiredRunning = true;
                    Log.Write("已接管本项目仍在运行的 Jellyfin 进程。");
                }
                else candidate.Dispose();
            }
            catch { }
        }
        public async Task Start()
        {
            await lifecycle.WaitAsync();
            Transitioning = true;
            try
            {
                Exception failure = null;
                try
                {
                DesiredRunning = true;
                if (OwnsProcess)
                {
                    await WaitReady();
                    return;
                }
                if (!NetworkInfo.PortFree(settings.Port)) throw new InvalidOperationException("端口 " + settings.Port + " 已被占用。请更换端口；本程序不会关闭其他服务。");
                string server = Paths.FindServer();
                if (server == null) throw new FileNotFoundException("尚未安装 Jellyfin。请到「首次设置」下载运行组件。");
                WriteNetworkConfiguration();
                string parent = Path.GetDirectoryName(server);
                var process = new Process();
                process.StartInfo = new ProcessStartInfo(server,
                    "--datadir " + AutoStart.Quote(Paths.ServerData) +
                    " --cachedir " + AutoStart.Quote(Path.Combine(Paths.Data, "cache")) +
                    " --logdir " + AutoStart.Quote(Path.Combine(Paths.Data, "server-logs")) +
                    " --webdir " + AutoStart.Quote(Path.Combine(parent, "jellyfin-web")))
                {
                    WorkingDirectory = parent, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                };
                process.Start();
                ownedProcess = process;
                SettingsStore.AtomicWrite(RecordPath, Json.Write(new ProcessRecord { Id = process.Id, StartTicks = process.StartTime.ToUniversalTime().Ticks, Executable = server }));
                Log.Write("正在启动 Jellyfin，端口 " + settings.Port + "。");
                await WaitReady();
                var info = await Api.PublicInfo();
                if (!String.IsNullOrEmpty(settings.ServerId) && settings.ServerId != Json.Text(info, "Id"))
                {
                    settings.EncryptedToken = "";
                    settings.UserId = "";
                    settings.ServerId = "";
                    SettingsStore.Save(settings);
                }
                Log.Write("Jellyfin " + Json.Text(info, "Version") + " 已就绪。");
                }
                catch (Exception error) { DesiredRunning = false; failure = error; }
                if (failure != null)
                {
                    await StopOwned();
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                }
            }
            finally { Transitioning = false; lifecycle.Release(); }
        }
        async Task WaitReady()
        {
            for (int attempt = 0; attempt < 90; attempt++)
            {
                if (ownedProcess == null || ownedProcess.HasExited) throw new InvalidOperationException("Jellyfin 启动失败，请查看 data/server-logs 中的日志。");
                try { await Api.WaitForApplicationReady(); await Api.PublicInfo(); return; }
                catch { }
                await Task.Delay(1000);
            }
            throw new TimeoutException("Jellyfin 启动超时，请检查服务器日志。");
        }
        public async Task Stop()
        {
            DesiredRunning = false;
            await lifecycle.WaitAsync();
            Transitioning = true;
            try { await StopOwned(); }
            finally { Transitioning = false; lifecycle.Release(); }
        }
        async Task StopOwned()
        {
            if (ownedProcess == null) return;
            var process = ownedProcess;
            if (!process.HasExited)
            {
                try { if (!String.IsNullOrEmpty(Api.Token)) await Api.Request("POST", "System/Shutdown", new { }, true); }
                catch { }
                if (!await Task.Run(() => process.WaitForExit(10000)))
                {
                    // The handle belongs to the exact process created/recovered by this project.
                    using (var killer = Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + process.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
                        await Task.Run(() => killer.WaitForExit(10000));
                    if (!await Task.Run(() => process.WaitForExit(5000))) throw new InvalidOperationException("无法停止 Jellyfin，请检查进程权限。");
                }
            }
            process.Dispose();
            ownedProcess = null;
            if (File.Exists(RecordPath)) File.Delete(RecordPath);
            Log.Write("本项目的 Jellyfin 服务已停止。");
        }
        void WriteNetworkConfiguration()
        {
            string directory = Path.Combine(Paths.ServerData, "config");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "network.xml");
            var xml = new XmlDocument();
            xml.XmlResolver = null;
            if (File.Exists(file)) xml.Load(file);
            else xml.LoadXml("<?xml version=\"1.0\" encoding=\"utf-8\"?><NetworkConfiguration />");
            SetXml(xml, "InternalHttpPort", settings.Port.ToString());
            SetXml(xml, "PublicHttpPort", settings.Port.ToString());
            SetXml(xml, "EnableUPnP", "false");
            SetXml(xml, "EnableRemoteAccess", "false");
            SetXml(xml, "EnableIPv4", "true");
            SetXml(xml, "EnableIPv6", "false");
            SetXml(xml, "EnableHttps", "false");
            SetXml(xml, "RequireHttps", "false");
            xml.Save(file);
        }
        static void SetXml(XmlDocument xml, string name, string value)
        {
            var node = xml.DocumentElement.SelectSingleNode(name);
            if (node == null) { node = xml.CreateElement(name); xml.DocumentElement.AppendChild(node); }
            node.InnerText = value;
        }
        public void Dispose() { Api.Dispose(); lifecycle.Dispose(); if (ownedProcess != null) ownedProcess.Dispose(); }
    }
}
