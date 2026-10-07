using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
namespace DanmuCinema.Playback
{
    public sealed class Plugin : BasePlugin<BasePluginConfiguration>
    {
        public Plugin(IApplicationPaths paths, IXmlSerializer xml) : base(paths, xml) { }
        public override string Name => "DanmuCinema Playback";
        public override Guid Id => new Guid("0389c672-ad21-421d-a386-84e3771f892f");
        public override string Description => "Prepare sidecar danmu before authorized playback metadata is returned.";
    }
    public sealed class Registration : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection services, IServerApplicationHost host)
        {
            services.AddSingleton<PreparationFilter>();
            services.Configure<MvcOptions>(options => options.Filters.AddService<PreparationFilter>());
        }
    }
    // Action filters run after Jellyfin's authorization filters. Video streams never
    // pass through this bridge; failure only delays metadata for at most 35 seconds.
    public sealed class PreparationFilter : IAsyncActionFilter
    {
        static readonly HttpClient client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(35) };
        readonly IApplicationPaths paths;
        public PreparationFilter(IApplicationPaths paths) { this.paths = paths; }
        public static string ItemId(string path, string method)
        {
            if (method != "GET" && method != "POST") return null;
            var match = Regex.Match(path ?? "", @"^(?:/Items/([a-zA-Z0-9-]{1,64})/PlaybackInfo|/api/danmu/([a-zA-Z0-9-]{1,64})(?:/raw)?)$", RegexOptions.IgnoreCase);
            if (!match.Success || (method == "POST" && !match.Groups[1].Success)) return null;
            string id = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            return Guid.TryParse(id, out var guid) ? guid.ToString("N") : id;
        }
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var request = context.HttpContext.Request;
            string id = ItemId(request.Path.Value, request.Method);
            if (id != null)
            {
                try
                {
                    // Support Jellyfin versions exposing either the data root or its
                    // "data" child, while never discovering a bridge outside this tree.
                    var serverRoot = new DirectoryInfo(paths.DataPath);
                    if (serverRoot.Name.Equals("data", StringComparison.OrdinalIgnoreCase)) serverRoot = serverRoot.Parent;
                    string bridge = Path.Combine(serverRoot.Parent.FullName, "playback-bridge.json");
                    if (new FileInfo(bridge).Length > 4096) throw new InvalidDataException();
                    using var file = new FileStream(bridge, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
                    using var document = await JsonDocument.ParseAsync(file, cancellationToken: context.HttpContext.RequestAborted);
                    int port = document.RootElement.GetProperty("Port").GetInt32();
                    string key = document.RootElement.GetProperty("Key").GetString();
                    if (port < 1024 || port > 65535 || !Regex.IsMatch(key ?? "", "^[a-f0-9]{32}$")) throw new InvalidDataException();
                    string source = request.Query["MediaSourceId"].ToString();
                    if (String.IsNullOrEmpty(source))
                        foreach (var argument in context.ActionArguments.Values)
                        {
                            var property = argument?.GetType().GetProperty("MediaSourceId");
                            if (property != null) { source = property.GetValue(argument)?.ToString() ?? ""; if (source.Length > 0) break; }
                        }
                    using var response = await client.GetAsync("http://127.0.0.1:" + port + "/playback/" + id + "?key=" + key + "&source=" + Uri.EscapeDataString(source), context.HttpContext.RequestAborted);
                }
                catch (OperationCanceledException) { if (context.HttpContext.RequestAborted.IsCancellationRequested) throw; }
                catch { /* Missing bridge, source failure, or timeout must not prevent playback. */ }
            }
            await next();
        }
    }
}
