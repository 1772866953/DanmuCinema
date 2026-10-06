using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace DanmuCinema
{
    public class AppSettings
    {
        public int Port { get; set; }
        public string MediaFolder { get; set; }
        public string LibraryName { get; set; }
        public string LibraryType { get; set; }
        public bool CloseToTray { get; set; }
        public bool StartServicesOnLaunch { get; set; }
        public bool PreferOriginal { get; set; }
        public string AdminName { get; set; }
        public string EncryptedToken { get; set; }
        public string UserId { get; set; }
        public string ServerId { get; set; }
        public int DanmuPort { get; set; }
        public string EncryptedDanmuKey { get; set; }
        public bool EnableAnimeko { get; set; }
        public bool EnableBahamut { get; set; }
        public bool EnableExistingDanmu { get; set; }
        public bool EnableDandan { get; set; }
        public bool AnimeOnly { get; set; }
        public string DandanAppId { get; set; }
        public string EncryptedDandanSecret { get; set; }
        public string EncryptedAdditionalApis { get; set; }
        public AppSettings()
        {
            Port = 8096;
            MediaFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            LibraryName = "我的影片";
            LibraryType = "movies";
            CloseToTray = true;
            StartServicesOnLaunch = false;
            PreferOriginal = true;
            AdminName = "admin";
            DanmuPort = 9321;
            EnableAnimeko = EnableBahamut = EnableExistingDanmu = EnableDandan = true;
            AnimeOnly = true;
            EncryptedDanmuKey = SettingsStore.Protect(Guid.NewGuid().ToString("N"));
        }
        public void Validate()
        {
            if (Port < 1024 || Port > 65535) throw new ArgumentException("端口必须在 1024 到 65535 之间。");
            if (DanmuPort < 1024 || DanmuPort > 65535 || DanmuPort == Port) throw new ArgumentException("弹幕端口必须在 1024 到 65535 之间，并与视频端口不同。");
            if (String.IsNullOrWhiteSpace(MediaFolder) || !Path.IsPathRooted(MediaFolder)) throw new ArgumentException("请选择完整的视频目录路径。");
            if (String.IsNullOrWhiteSpace(LibraryName)) throw new ArgumentException("媒体库名称不能为空。");
            if (LibraryType != "movies" && LibraryType != "tvshows") throw new ArgumentException("媒体库类型不正确。");
        }
    }

    public static class Paths
    {
        public static string Root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        public static string Data { get { return Path.Combine(Root, "data"); } }
        public static string ServerData { get { return Path.Combine(Data, "jellyfin"); } }
        public static string LogPath { get { return Path.Combine(Data, "controller.log"); } }
        internal static string RuntimeOverride;
        public static string Runtime { get { return RuntimeOverride ?? Path.Combine(Root, "runtime", "jellyfin"); } }
        public static string SettingsFile { get { return Path.Combine(Data, "settings.json"); } }
        public static string FindServer()
        {
            if (!Directory.Exists(Runtime)) return null;
            return Directory.GetFiles(Runtime, "jellyfin.exe", SearchOption.AllDirectories).FirstOrDefault();
        }
    }

    public static class Json
    {
        public static string Write(object data) { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }.Serialize(data); }
        public static T Read<T>(string data) { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }.Deserialize<T>(data); }
        public static Dictionary<string, object> Object(string data) { return Read<Dictionary<string, object>>(data); }
        public static string Text(IDictionary<string, object> data, string key)
        {
            var value = Value(data, key);
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        static object Value(IDictionary<string, object> data, string key)
        {
            if (data == null) return null;
            object value;
            if (data.TryGetValue(key, out value)) return value;
            foreach (var pair in data) if (String.Equals(pair.Key.Replace("_", ""), key.Replace("_", ""), StringComparison.OrdinalIgnoreCase)) return pair.Value;
            return null;
        }
        public static Dictionary<string, object> Child(IDictionary<string, object> data, string key)
        {
            return Value(data, key) as Dictionary<string, object>;
        }
        public static object[] Array(IDictionary<string, object> data, string key)
        {
            object value = Value(data, key);
            var array = value as object[];
            var list = value as System.Collections.ArrayList;
            return array ?? (list == null ? new object[0] : list.ToArray());
        }
    }

    public static class SettingsStore
    {
        static readonly object WriteSync = new object();
        public static AppSettings Load()
        {
            Directory.CreateDirectory(Paths.Data);
            if (!File.Exists(Paths.SettingsFile)) return new AppSettings();
            try
            {
                var result = Json.Read<AppSettings>(File.ReadAllText(Paths.SettingsFile));
                result.Validate();
                return result;
            }
            catch (Exception e)
            {
                Log.Write("配置读取失败，原文件已保留：" + e.Message);
                throw new InvalidDataException("配置文件损坏。请检查 data/settings.json，或将它改名后重新启动。", e);
            }
        }
        public static void Save(AppSettings settings)
        {
            settings.Validate();
            Directory.CreateDirectory(Paths.Data);
            AtomicWrite(Paths.SettingsFile, Json.Write(settings));
        }
        public static void AtomicWrite(string path, string text, bool backup = true)
        {
            lock (WriteSync)
            {
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, backup ? path + ".bak" : null);
                else File.Move(temporary, path);
            }
        }
        public static bool WriteMissingSidecar(string path, string content)
        {
            lock (WriteSync)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0) return false;
                AtomicWrite(path, content, false); return true;
            }
        }
        public static string Protect(string token)
        {
            if (String.IsNullOrEmpty(token)) return "";
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
        }
        public static string Unprotect(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
    }

    public static class Log
    {
        static readonly object Sync = new object();
        public static event Action<string> Added;
        public static void Write(string message)
        {
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message;
            lock (Sync)
            {
                try
                {
                    Directory.CreateDirectory(Paths.Data);
                    if (File.Exists(Paths.LogPath) && new FileInfo(Paths.LogPath).Length > 3 * 1024 * 1024)
                    {
                        var archive = Paths.LogPath + ".1";
                        if (File.Exists(archive)) File.Delete(archive);
                        File.Move(Paths.LogPath, archive);
                    }
                    File.AppendAllText(Paths.LogPath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
            var handler = Added;
            if (handler != null) handler(line);
        }
    }

    public static class AutoStart
    {
        const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "DanmuCinema";
        const string LegacyValueName = "DanMuLAN";
        public static void Migrate()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(KeyPath, true))
            {
                if (key == null) return;
                string previous = Convert.ToString(key.GetValue(LegacyValueName));
                string legacyCommand = Quote(Path.Combine(Paths.Root, "bin", "DanMuLAN.exe")) + " --tray --start";
                if (previous == legacyCommand || previous == Command)
                { key.SetValue(ValueName, Command, RegistryValueKind.String); key.DeleteValue(LegacyValueName, false); }
            }
        }
        public static bool Enabled
        {
            get { using (var key = Registry.CurrentUser.OpenSubKey(KeyPath)) return key != null && Convert.ToString(key.GetValue(ValueName)) == Command; }
        }
        static string Command { get { return Quote(System.Windows.Forms.Application.ExecutablePath) + " --tray --start"; } }
        public static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
                else key.DeleteValue(ValueName, false);
            }
        }
        public static string Quote(string value)
        {
            // Implements Windows command line quoting, including terminal backslashes.
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                if (ch == '"') result.Append('\\', slashes * 2 + 1);
                else result.Append('\\', slashes);
                result.Append(ch);
                slashes = 0;
            }
            result.Append('\\', slashes * 2).Append('"');
            return result.ToString();
        }
    }

    public static class NetworkInfo
    {
        public static string[] Addresses()
        {
            var addresses = new List<string>();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ip in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var bytes = ip.Address.GetAddressBytes();
                    if (bytes[0] == 10 || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31))
                        addresses.Add(ip.Address.ToString());
                }
            }
            return addresses.Distinct().ToArray();
        }
        public static bool PortFree(int port)
        {
            try { var listener = new TcpListener(IPAddress.Any, port); listener.Start(); listener.Stop(); return true; }
            catch (SocketException) { return false; }
        }
    }
}
