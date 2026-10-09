using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DanmuCinema;

public static class AuthenticationCompatibilityTests
{
    static readonly List<string> report = new List<string>();
    const string User = "auth_fixture";
    const string Password = "Fixture_Auth_Test_6197";
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); report.Add("PASS: " + message); Console.WriteLine("PASS: " + message); }
    static async Task<HttpStatusCode> Login(int port, string header, string scheme, string password)
    {
        using (var client = new HttpClient(new HttpClientHandler { UseProxy = false }))
        using (var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + port + "/Users/AuthenticateByName"))
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            request.Headers.TryAddWithoutValidation(header, scheme + " Client=\"FixtureClient\", Device=\"FixtureWindows\", DeviceId=\"auth-compatibility-fixture\", Version=\"1.0\"");
            request.Content = new StringContent(Json.Write(new { Username = User, Pw = password }), System.Text.Encoding.UTF8, "application/json");
            using (var response = await client.SendAsync(request)) return response.StatusCode;
        }
    }
    static async Task ApplyCurrent(string root)
    {
        var settingsPath = Path.Combine(root, "data", "settings.json");
        byte[] settingsBefore = File.ReadAllBytes(settingsPath);
        string official = Path.Combine(root, "config", "dandanplay.json");
        byte[] officialBefore = File.Exists(official) ? File.ReadAllBytes(official) : null;
        var settings = Json.Read<AppSettings>(System.Text.Encoding.UTF8.GetString(settingsBefore).TrimStart('\ufeff'));
        using (var api = new JellyfinApi(settings))
        {
            var info = await api.PublicInfo();
            Check(Json.Text(info, "Id") == settings.ServerId, "Running server matches the installed application");
            var before = Json.Object(await api.Request("GET", "System/Configuration", null, true));
            await api.EnsureClientCompatibility();
            var after = Json.Object(await api.Request("GET", "System/Configuration", null, true));
            Check(Json.Text(after, "EnableLegacyAuthorization") == "True", "Current running server accepts legacy authentication immediately");
            before.Remove("EnableLegacyAuthorization"); after.Remove("EnableLegacyAuthorization");
            Check(Json.Write(before) == Json.Write(after), "All other server configuration fields are unchanged");
            Check(settingsBefore.SequenceEqual(File.ReadAllBytes(settingsPath)), "Application account configuration is unchanged");
            Check(officialBefore == null ? !File.Exists(official) : officialBefore.SequenceEqual(File.ReadAllBytes(official)), "Official danmu API configuration is unchanged");
        }
    }
    static async Task Integration(string root)
    {
        Paths.Root = root;
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var settings = new AppSettings { Port = port, EnableDandan = false, EnableExistingDanmu = false, EnableAnimeko = false, EnableBahamut = false };
        using (var service = new ServiceManager(settings))
        {
            try
            {
                await service.Start();
                await service.Api.Initialize(User, Password);
                var configuration = Json.Object(await service.Api.Request("GET", "System/Configuration", null, true));
                Check(Json.Text(configuration, "EnableLegacyAuthorization") == "True", "Fresh administrator initialization enables compatibility after server migrations");
                configuration["EnableLegacyAuthorization"] = false;
                await service.Api.Request("POST", "System/Configuration", configuration, true);
                Check(await Login(port, "X-Emby-Authorization", "Emby", Password) == HttpStatusCode.BadRequest, "Disabled compatibility reproduces legacy client login HTTP 400");
                Check(await Login(port, "Authorization", "MediaBrowser", Password) == HttpStatusCode.OK, "Modern login works when legacy authentication is disabled");
                var before = Json.Object(await service.Api.Request("GET", "System/Configuration", null, true));
                Check(await service.Api.EnsureClientCompatibility(), "Compatibility updates the running server without restart");
                var after = Json.Object(await service.Api.Request("GET", "System/Configuration", null, true));
                before.Remove("EnableLegacyAuthorization"); after.Remove("EnableLegacyAuthorization");
                Check(Json.Write(before) == Json.Write(after), "Enabling compatibility preserves all other server settings");
                Check(!await service.Api.EnsureClientCompatibility(), "Already enabled compatibility does not write again");
                Check(await Login(port, "X-Emby-Authorization", "Emby", Password) == HttpStatusCode.OK, "Legacy Emby login succeeds after enabling compatibility");
                Check(await Login(port, "X-Emby-Authorization", "MediaBrowser", Password) == HttpStatusCode.OK, "Legacy MediaBrowser header login succeeds");
                Check(await Login(port, "Authorization", "MediaBrowser", Password) == HttpStatusCode.OK, "Modern authentication continues to work");
                Check(await Login(port, "X-Emby-Authorization", "Emby", "WrongFixturePassword") == HttpStatusCode.Unauthorized, "Wrong password is still rejected with HTTP 401");
                // Simulate an upgrade that has disabled legacy auth on an existing server.
                configuration = Json.Object(await service.Api.Request("GET", "System/Configuration", null, true));
                configuration["EnableLegacyAuthorization"] = false;
                await service.Api.Request("POST", "System/Configuration", configuration, true);
                await service.Start();
                Check(await Login(port, "X-Emby-Authorization", "Emby", Password) == HttpStatusCode.OK, "Starting an already running owned server applies compatibility");
                configuration = Json.Object(await service.Api.Request("GET", "System/Configuration", null, true));
                configuration["EnableLegacyAuthorization"] = false;
                await service.Api.Request("POST", "System/Configuration", configuration, true);
                await service.Stop(); await service.Start();
                Check(await Login(port, "X-Emby-Authorization", "Emby", Password) == HttpStatusCode.OK, "Cold start restores compatibility for existing installations");
                await service.Stop(); await service.Start();
                Check(await Login(port, "X-Emby-Authorization", "Emby", Password) == HttpStatusCode.OK, "Compatibility remains enabled after another restart");
            }
            finally { service.Stop().GetAwaiter().GetResult(); }
        }
    }
    public static int Main(string[] args)
    {
        try
        {
            if (args[0] == "--apply-current") ApplyCurrent(args[1]).GetAwaiter().GetResult();
            else Integration(args[0]).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
        finally { if (args.Length > 2) File.WriteAllLines(args[2], report); }
    }
}
