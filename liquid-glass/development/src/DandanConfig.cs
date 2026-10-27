using System;
using System.IO;

namespace DanmuCinema
{
    public sealed class DandanConfig
    {
        public string AppId { get; set; }
        public string AppSecret { get; set; }
        public string EncryptedAppSecret { get; set; }
        public string CallbackUrl { get; set; }
        public static string FilePath { get { return Path.Combine(Paths.Root, "config", "dandanplay.json"); } }
        public static void Ensure(AppSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            if (!File.Exists(FilePath))
            {
                // Fresh installations have no credentials. Keep the editable file free of ciphertext fields.
                if (String.IsNullOrEmpty(settings.EncryptedDandanSecret))
                    SettingsStore.AtomicWrite(FilePath, Json.Write(new { AppId = settings.DandanAppId ?? "", AppSecret = "" }));
                else
                    SettingsStore.AtomicWrite(FilePath, Json.Write(new { AppId = settings.DandanAppId ?? "", AppSecret = "", EncryptedAppSecret = settings.EncryptedDandanSecret }));
            }
            var config = Load();
            if (!String.IsNullOrEmpty(settings.DandanAppId) && config.AppId == settings.DandanAppId && config.Secret == SettingsStore.Unprotect(settings.EncryptedDandanSecret))
            { settings.DandanAppId = ""; settings.EncryptedDandanSecret = ""; SettingsStore.Save(settings); }
        }
        public string Secret { get { return String.IsNullOrEmpty(AppSecret) ? SettingsStore.Unprotect(EncryptedAppSecret) : AppSecret.Trim(); } }
        public bool Ready { get { return !String.IsNullOrWhiteSpace(AppId) && !String.IsNullOrWhiteSpace(Secret) && AppId.Length <= 100 && !AppId.Contains("\r") && !AppId.Contains("\n"); } }
        public static DandanConfig Load()
        {
            try { return File.Exists(FilePath) ? Json.Read<DandanConfig>(File.ReadAllText(FilePath)) ?? new DandanConfig() : new DandanConfig(); }
            catch { throw new InvalidOperationException("官方源配置无效。"); }
        }
    }
}
