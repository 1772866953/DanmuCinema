using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    // Runs as the installing Windows user, so DPAPI tokens belong to the same user
    // who later launches the app. Passwords are never command-line arguments.
    public sealed class InstallOptions
    {
        public string AdminName { get; set; }
        public string Password { get; set; }
        public string MediaFolder { get; set; }
        public string LibraryType { get; set; }
        public int Port { get; set; }
        public int DanmuPort { get; set; }
        public bool CloseToTray { get; set; }
        public bool AutoStart { get; set; }
        public string DandanConfigFile { get; set; }
    }
    public static class InstallBootstrap
    {
        public static int Run(string[] args)
        {
            if (args[0] == "--installer-check") return Running() ? 10 : 0;
            if (args[0] == "--installer-firewall") return Firewall(false);
            if (args[0] == "--installer-remove-firewall") return Firewall(true);
            if (args[0] == "--installer-remove-data" || args[0] == "--installer-remove-data-keep-cache-logs")
            {
                if (Running()) return 10;
                try { UninstallData.Remove(args[0] == "--installer-remove-data-keep-cache-logs"); return 0; }
                catch { return 1; }
            }
            if (args[0] == "--installer-cleanup")
            {
                if (Running()) return 10;
                if (AutoStart.Enabled) AutoStart.Set(false);
                return 0;
            }
            if (args[0] != "--installer-configure" || args.Length != 2) return 2;
            string request = Path.GetFullPath(args[1]);
            string result = request + ".result";
            try
            {
                if (Running()) throw new InvalidOperationException("请先从托盘退出此安装目录的弹幕影院，再重新安装。");
                var options = Json.Read<InstallOptions>(File.ReadAllText(request));
                // Consume the private temporary file before starting any child process.
                File.Delete(request);
                Configure(options).GetAwaiter().GetResult();
                File.WriteAllText(result, "OK", System.Text.Encoding.UTF8);
                return 0;
            }
            catch (Exception error)
            {
                // Do not serialize options or HTTP request bodies into diagnostics.
                try { File.WriteAllText(result, "初始化未完成：" + error.Message, System.Text.Encoding.UTF8); } catch { }
                return 1;
            }
            finally { try { if (File.Exists(request)) File.Delete(request); } catch { } }
        }
        static bool Running()
        {
            Mutex mutex;
            if (Mutex.TryOpenExisting("Local\\DanmuCinema-" + Program.StableId(Paths.Root), out mutex))
            {
                using (mutex)
                {
                    try { if (!mutex.WaitOne(0)) return true; }
                    catch (AbandonedMutexException) { }
                    mutex.ReleaseMutex();
                }
            }
            // A recovered server may outlive the UI. Do not overwrite its runtime
            // or configuration; never inspect or stop unrelated Jellyfin instances.
            try
            {
                string recordFile = Path.Combine(Paths.Data, "server-process.json");
                if (File.Exists(recordFile))
                {
                    var record = Json.Read<ProcessRecord>(File.ReadAllText(recordFile));
                    using (var process = Process.GetProcessById(record.Id))
                        if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == record.StartTicks &&
                            String.Equals(process.MainModule.FileName, record.Executable, StringComparison.OrdinalIgnoreCase) &&
                            String.Equals(record.Executable, Paths.FindServer(), StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }
        internal static AppSettings Validate(InstallOptions options)
        {
            if (options == null || String.IsNullOrWhiteSpace(options.AdminName) || options.AdminName.Length > 64)
                throw new ArgumentException("管理员账号必须为 1 至 64 个字符。");
            if (String.IsNullOrEmpty(options.Password) || options.Password.Length < 8)
                throw new ArgumentException("管理员密码至少需要 8 个字符。");
            if (!Directory.Exists(options.MediaFolder)) throw new DirectoryNotFoundException("请选择已存在的视频文件夹。");
            var settings = new AppSettings
            {
                AdminName = options.AdminName.Trim(), MediaFolder = Path.GetFullPath(options.MediaFolder),
                LibraryType = options.LibraryType, Port = options.Port, DanmuPort = options.DanmuPort,
                CloseToTray = options.CloseToTray, StartServicesOnLaunch = true
            };
            settings.Validate();
            return settings;
        }
        static void ProtectDirectory(string path)
        {
            Directory.CreateDirectory(path);
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WindowsIdentity.GetCurrent().User, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
                acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(path, acl);
        }
        static async Task Configure(InstallOptions options)
        {
            AppSettings settings;
            bool upgrade = false;
            if (File.Exists(Paths.SettingsFile))
            {
                settings = SettingsStore.Load();
                upgrade = File.Exists(Path.Combine(Paths.Data, "install-complete.json")) && !String.IsNullOrEmpty(settings.ServerId) && !String.IsNullOrEmpty(SettingsStore.Unprotect(settings.EncryptedToken));
            }
            else settings = null;
            if (upgrade) return; // Preserve all existing accounts, media roots and preferences.
            settings = Validate(options);
            DandanConfig official = null;
            if (!String.IsNullOrWhiteSpace(options.DandanConfigFile))
            {
                official = Json.Read<DandanConfig>(File.ReadAllText(options.DandanConfigFile));
                if (official == null || !official.Ready) throw new ArgumentException("所选官方弹幕配置无效，或其加密配置不属于当前 Windows 用户。");
            }
            if (!NetworkInfo.PortFree(settings.Port) || !NetworkInfo.PortFree(settings.DanmuPort))
                throw new InvalidOperationException("视频或弹幕端口已被占用，请重新运行安装器并选择其他端口。");
            ProtectDirectory(Paths.Data);
            ProtectDirectory(Path.Combine(Paths.Root, "config"));
            SettingsStore.Save(settings);
            if (official != null)
                SettingsStore.AtomicWrite(DandanConfig.FilePath, Json.Write(new { AppId = official.AppId, AppSecret = "", EncryptedAppSecret = SettingsStore.Protect(official.Secret), CallbackUrl = official.CallbackUrl }), false);
            DandanConfig.Ensure(settings);
            using (var services = new ServiceManager(settings))
            {
                try
                {
                    await services.Start();
                    await services.Api.Initialize(settings.AdminName, options.Password);
                    var libraries = Json.Read<object[]>(await services.Api.Request("GET", "Library/VirtualFolders", null, true));
                    if (!libraries.OfType<Dictionary<string, object>>().Any(x => Json.Array(x, "Locations").Any(p => String.Equals(Path.GetFullPath(Convert.ToString(p)).TrimEnd('\\'), settings.MediaFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))))
                        await services.Api.AddLibrary(settings.MediaFolder, settings.LibraryName, settings.LibraryType);
                    await services.Api.SetOriginalPolicy(true);
                    var plugins = (await services.Api.Plugins()).OfType<Dictionary<string, object>>().ToArray();
                    foreach (string name in new[] { "Danmu", "DanmuCinema Playback" })
                        if (!plugins.Any(x => Json.Text(x, "Name") == name && Json.Text(x, "Status") == "Active"))
                            throw new InvalidOperationException("内置插件未正常加载：" + name + "。请查看服务器日志。");
                    // Test the local bridge without contacting any upstream danmu source.
                    using (var gateway = new DanmuGateway(settings)) { gateway.Start(); await gateway.Stop(); }
                    SettingsStore.AtomicWrite(Path.Combine(Paths.Data, "install-complete.json"), Json.Write(new { Version = "1.0.0", CompletedUtc = DateTime.UtcNow }), false);
                    if (options.AutoStart) AutoStart.Set(true);
                }
                finally { services.Stop().GetAwaiter().GetResult(); }
            }
        }
        static int Firewall(bool remove)
        {
            try
            {
                if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return 5;
                var settings = SettingsStore.Load();
                string prefix = "DanmuCinema-" + Program.StableId(Paths.Root);
                foreach (var entry in new[] { new { Name = prefix + "-Media", Port = settings.Port }, new { Name = prefix + "-Danmu", Port = settings.DanmuPort } })
                {
                    RunNetsh("advfirewall firewall delete rule name=" + AutoStart.Quote(entry.Name));
                    if (!remove && RunNetsh("advfirewall firewall add rule name=" + AutoStart.Quote(entry.Name) + " dir=in action=allow protocol=TCP localport=" + entry.Port + " profile=private remoteip=localsubnet") != 0) return 1;
                }
                return 0;
            }
            catch { return 1; }
        }
        static int RunNetsh(string args)
        {
            using (var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), args) { UseShellExecute = false, CreateNoWindow = true }))
            { process.WaitForExit(); return process.ExitCode; }
        }
    }
}
