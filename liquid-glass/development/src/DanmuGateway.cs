using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    // Small GET-only gateway. Large media files never pass through this process.
    public sealed class DanmuGateway : IDisposable
    {
        readonly AppSettings settings;
        readonly SemaphoreSlim slots = new SemaphoreSlim(8, 8);
        readonly HttpClient upstream = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
        readonly JellyfinApi catalogApi;
        public readonly DanmuCatalog Catalog;
        public Func<string, string, CancellationToken, Task<bool>> PreparePlayback;
        string bridgeKey;
        TcpListener listener;
        CancellationTokenSource stopping;
        Task accepting;
        public bool Running { get { return listener != null; } }
        public string Key { get { return SettingsStore.Unprotect(settings.EncryptedDanmuKey); } }
        public DanmuGateway(AppSettings settings, DanmuCatalog catalog = null)
        {
            this.settings = settings; upstream.Timeout = TimeSpan.FromSeconds(30);
            catalogApi = new JellyfinApi(settings); Catalog = catalog ?? new DanmuCatalog(settings, catalogApi);
        }
        public void Start()
        {
            if (Running) return;
            if (String.IsNullOrEmpty(Key))
            {
                settings.EncryptedDanmuKey = SettingsStore.Protect(Guid.NewGuid().ToString("N"));
                SettingsStore.Save(settings);
            }
            var candidate = new TcpListener(IPAddress.Any, settings.DanmuPort);
            candidate.Start(32);
            bridgeKey = Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(Paths.Data);
                for (int attempt = 0; ; attempt++)
                    try { SettingsStore.AtomicWrite(Path.Combine(Paths.Data, "playback-bridge.json"), Json.Write(new { Port = settings.DanmuPort, Key = bridgeKey }), false); break; }
                    catch (IOException) { if (attempt >= 4) throw; Thread.Sleep(50); }
            }
            catch { candidate.Stop(); throw; }
            listener = candidate;
            stopping = new CancellationTokenSource();
            accepting = AcceptLoop(candidate, stopping.Token);
            Log.Write("弹幕接口已启动，端口 " + settings.DanmuPort + "。");
        }
        async Task AcceptLoop(TcpListener active, CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                TcpClient connection;
                try { connection = await active.AcceptTcpClientAsync(); }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { if (cancellation.IsCancellationRequested) break; continue; }
                if (!slots.Wait(0)) { connection.Close(); continue; }
                HandleConnection(connection, cancellation); // bounded; each handler catches all failures
            }
        }
        async void HandleConnection(TcpClient connection, CancellationToken cancellation)
        {
            try
            {
                using (connection)
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(40));
                    using (timeout.Token.Register(() => connection.Close()))
                    {
                    var stream = connection.GetStream();
                    string header = await ReadHeader(stream, timeout.Token);
                    string[] first = header.Split(new[] { "\r\n" }, StringSplitOptions.None)[0].Split(' ');
                    if (first.Length != 3 || (first[2] != "HTTP/1.1" && first[2] != "HTTP/1.0")) { await Reply(stream, 400, "application/json", Encoding.UTF8.GetBytes("{}"), timeout.Token); return; }
                    if (first[0] != "GET") { await Reply(stream, 405, "application/json", Encoding.UTF8.GetBytes("{}"), timeout.Token); return; }
                    var endpoint = (IPEndPoint)connection.Client.RemoteEndPoint;
                    if (!AllowedAddress(endpoint.Address)) { await Reply(stream, 403, "application/json", Encoding.UTF8.GetBytes("{}"), timeout.Token); return; }
                    if (first[1] == "/health")
                    {
                        await Reply(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes("{\"service\":\"DanmuCinema\",\"status\":\"running\"}"), timeout.Token);
                        return;
                    }
                    if (first[1].StartsWith("/playback/", StringComparison.Ordinal))
                    {
                        var target = new Uri("http://localhost" + first[1]); var query = System.Web.HttpUtility.ParseQueryString(target.Query);
                        string id = target.AbsolutePath.Substring(10);
                        if (!IPAddress.IsLoopback(endpoint.Address) || query["key"] != bridgeKey) { await Reply(stream, 403, "application/json", Encoding.UTF8.GetBytes("{}"), timeout.Token); return; }
                        if (!Regex.IsMatch(id, "^[a-zA-Z0-9]{1,64}$")) { await Reply(stream, 400, "application/json", Encoding.UTF8.GetBytes("{}"), timeout.Token); return; }
                        // Preparation itself has a two-minute limit. The transport
                        // must not cut off its third attempt at the old 40s deadline.
                        timeout.CancelAfter(TimeSpan.FromSeconds(130));
                        bool completed = PreparePlayback != null && await PreparePlayback(id, query["source"], timeout.Token);
                        await Reply(stream, 200, "application/json", Encoding.UTF8.GetBytes(Json.Write(new { completed = completed })), timeout.Token); return;
                    }
                    string route;
                    int validation = ValidateRoute(first[1], Key, out route);
                    if (validation != 200) { await Reply(stream, validation, "application/json", Encoding.UTF8.GetBytes("{}"), timeout.Token); return; }
                    string token = SettingsStore.Unprotect(settings.EncryptedToken);
                    if (String.IsNullOrEmpty(token)) { await Reply(stream, 503, "application/json", Encoding.UTF8.GetBytes("{\"errorCode\":503,\"errorMessage\":\"Initialize server first\"}"), timeout.Token); return; }
                    CatalogReply merged = null;
                    try
                    {
                        merged = await Catalog.Route(route);
                    }
                    catch (ArgumentException)
                    {
                        merged = new CatalogReply { Status = 400, Content = "{\"success\":false,\"errorCode\":400,\"errorMessage\":\"Invalid search or source configuration\"}" };
                    }
                    catch (Exception)
                    {
                        merged = new CatalogReply { Status = 502, Content = "{\"success\":false,\"errorCode\":502,\"errorMessage\":\"Source unavailable; search again or select another source\"}" };
                    }
                    if (merged != null) { await Reply(stream, merged.Status, merged.ContentType, Encoding.UTF8.GetBytes(merged.Content), timeout.Token); return; }
                    using (var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + settings.Port + route))
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", "MediaBrowser Token=\"" + token + "\"");
                        using (var response = await upstream.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                        {
                            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) { await Reply(stream, 502, "application/json", Encoding.UTF8.GetBytes("{}"), timeout.Token); return; }
                            using (var input = await response.Content.ReadAsStreamAsync())
                            using (var buffer = new MemoryStream())
                            {
                                var chunk = new byte[32768];
                                int count;
                                while ((count = await input.ReadAsync(chunk, 0, chunk.Length, timeout.Token)) > 0)
                                {
                                    if (buffer.Length + count > 32 * 1024 * 1024) throw new InvalidDataException("弹幕响应超过限制。");
                                    buffer.Write(chunk, 0, count);
                                }
                                string contentType = response.Content.Headers.ContentType == null ? "application/json; charset=utf-8" : response.Content.Headers.ContentType.ToString();
                                await Reply(stream, (int)response.StatusCode, contentType, buffer.ToArray(), timeout.Token);
                            }
                        }
                    }
                    }
                }
            }
            catch (Exception) { /* Closing a timed-out connection does not stop the server. */ }
            finally { slots.Release(); }
        }
        public static bool AllowedAddress(IPAddress address)
        {
            if (IPAddress.IsLoopback(address)) return true;
            byte[] b = address.GetAddressBytes();
            return b.Length == 4 && (b[0] == 10 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31));
        }
        public static int ValidateRoute(string target, string key, out string route)
        {
            route = null;
            if (target.Length > 4096 || !target.StartsWith("/", StringComparison.Ordinal) || target.IndexOf('\\') >= 0 || target.IndexOf('#') >= 0) return 400;
            int separator = target.IndexOf('/', 1);
            if (separator < 0 || !String.Equals(target.Substring(1, separator - 1), key, StringComparison.Ordinal)) return 401;
            string candidate = target.Substring(separator);
            string path = candidate.Split('?')[0];
            if (!Regex.IsMatch(path, @"^/api/v2/(search/(anime|episodes)|bangumi/[A-Za-z0-9_-]+|comment/[A-Za-z0-9_-]+)$")) return 404;
            // Reject encoded paths and path traversal; only query values are URL-encoded.
            if (path.Contains("%") || candidate.Contains("\r") || candidate.Contains("\n")) return 400;
            route = candidate;
            return 200;
        }
        static async Task<string> ReadHeader(NetworkStream stream, CancellationToken cancellation)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var buffer = new byte[8192];
                int total = 0;
                while (total < buffer.Length)
                {
                    int read = await stream.ReadAsync(buffer, total, buffer.Length - total, timeout.Token);
                    if (read == 0) throw new EndOfStreamException();
                    total += read;
                    string text = Encoding.ASCII.GetString(buffer, 0, total);
                    if (text.Contains("\r\n\r\n")) return text;
                }
                throw new InvalidDataException("请求头过长。");
            }
        }
        static async Task Reply(NetworkStream stream, int status, string type, byte[] body, CancellationToken cancellation)
        {
            if (type.Contains("\r") || type.Contains("\n")) type = "application/json";
            string statusText = status == 200 ? "OK" : "Error";
            byte[] headers = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + statusText + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, 0, headers.Length, cancellation);
            await stream.WriteAsync(body, 0, body.Length, cancellation);
        }
        public async Task Stop()
        {
            if (listener == null) return;
            stopping.Cancel();
            listener.Stop();
            listener = null;
            if (accepting != null) await accepting;
            // Drain active handlers before disposing shared resources or restarting.
            for (int i = 0; i < 8; i++) await slots.WaitAsync();
            slots.Release(8);
            stopping.Dispose();
            stopping = null;
            Log.Write("弹幕接口已停止。");
        }
        public void Dispose() { Catalog.Dispose(); catalogApi.Dispose(); upstream.Dispose(); slots.Dispose(); }
    }
}
