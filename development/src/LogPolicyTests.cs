using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DanmuCinema
{
    public static class LogPolicyTests
    {
        [STAThread]
        public static int Run()
        {
            string original = Paths.Root, output = Path.Combine(Paths.TestOutputFor(original), "log-policy");
            Directory.CreateDirectory(output); var report = new List<string>();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Desktop.DesktopController controller = null; Desktop.ShellWindow window = null;
            try
            {
                Paths.Root = Path.Combine(output, "fixture"); Directory.CreateDirectory(Paths.Data);
                string server = Path.Combine(Paths.Data, "server-logs"); Directory.CreateDirectory(server);
                var legacy = Json.Object(Json.Write(new AppSettings())); legacy.Remove("LogRetentionDays");
                Check(Json.Read<AppSettings>(Json.Write(legacy)).LogRetentionDays == 30, "旧配置缺少日志字段时默认保留30天", report);
                foreach (int days in new[] { 0, 3651 }) { bool rejected = false; try { new AppSettings { LogRetentionDays = days }.Validate(); } catch (ArgumentException) { rejected = true; } Check(rejected, "拒绝无效保留天数 " + days, report); }
                var settings = new AppSettings { LogRetentionDays = 7 }; SettingsStore.Save(settings); Log.Configure(7);
                Check(SettingsStore.Load().LogRetentionDays == 7, "保留天数可持久化", report);
                DateTime sample = DateTime.Now; DateTime now = new DateTime(sample.Ticks - sample.Ticks % TimeSpan.TicksPerSecond), cutoff = now.AddDays(-7);
                string old = cutoff.AddSeconds(-1).ToString("yyyy-MM-dd HH:mm:ss") + "  expired";
                string current = cutoff.ToString("yyyy-MM-dd HH:mm:ss") + "  boundary";
                File.WriteAllLines(Paths.LogPath, new[] { old, "old exception stack", current, "current exception stack" });
                File.WriteAllText(Paths.LogPath + ".1", old);
                string expiredServer = Path.Combine(server, "log_old.log"), recentServer = Path.Combine(server, "ffmpeg_recent.txt");
                File.WriteAllText(expiredServer, "server expired"); File.SetLastWriteTime(expiredServer, now.AddDays(-8)); File.WriteAllText(recentServer, "server recent");
                string nested = Path.Combine(server, "nested", "worker.log"); Directory.CreateDirectory(Path.GetDirectoryName(nested)); File.WriteAllText(nested, "nested recent log");
                Log.Prune(now);
                Check(File.ReadAllText(Paths.LogPath).Contains("boundary") && File.ReadAllText(Paths.LogPath).Contains("current exception stack") && !File.ReadAllText(Paths.LogPath).Contains("expired") && !File.ReadAllText(Paths.LogPath).Contains("old exception stack"), "按日志记录日期清理，包含边界与多行异常", report);
                Check(!File.Exists(Paths.LogPath + ".1") && !File.Exists(expiredServer) && File.Exists(recentServer), "清理过期归档与服务器日志，保留近期日志", report);
                string config = Path.Combine(Paths.ServerData, "config"); Directory.CreateDirectory(config);
                File.WriteAllText(Path.Combine(config, "system.xml"), "<ServerConfiguration><LogFileRetentionDays>3</LogFileRetentionDays><ActivityLogRetentionDays>30</ActivityLogRetentionDays></ServerConfiguration>");
                File.WriteAllText(Path.Combine(config, "logging.default.json"), "{\"Serilog\":{\"WriteTo\":[{\"Name\":\"File\",\"Args\":{\"path\":\"%JELLYFIN_LOG_DIR%//log_.log\",\"retainedFileCountLimit\":3}},{\"Name\":\"File\",\"Args\":{\"path\":\"D:/external.log\",\"retainedFileCountLimit\":9}}]}}");
                Log.ConfigureServerRetention(7);
                string serverConfig = File.ReadAllText(Path.Combine(config, "system.xml")), loggerConfig = File.ReadAllText(Path.Combine(config, "logging.default.json"));
                Check(serverConfig.Contains("<LogFileRetentionDays>7</LogFileRetentionDays>") && serverConfig.Contains("<ActivityLogRetentionDays>30</ActivityLogRetentionDays>"), "服务器文件日志保留期同步且不修改活动记录", report);
                Check(loggerConfig.Contains("\"retainedFileCountLimit\":null") && loggerConfig.Contains("\"retainedFileCountLimit\":9"), "解除旧的3文件上限，不修改自定义外部日志", report);
                long generation = Log.Generation;
                var cleared = Log.ClearAll(); Check(cleared.Pending == 0 && !File.Exists(Paths.LogPath) && !File.Exists(recentServer) && !File.Exists(nested) && File.Exists(Paths.SettingsFile) && Directory.Exists(config), "全部清理只删除日志，保留配置", report);
                Check(Log.Generation > generation, "清理后刷新日志视图版本", report);
                Log.Write("new after clear"); Check(Log.RecentText().Contains("new after clear"), "清理后新日志正常记录", report);
                string locked = Path.Combine(server, "log_active.log"); File.WriteAllText(locked, "in use");
                using (var handle = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                { cleared = Log.ClearAll(); Check(cleared.Pending == 1 && File.Exists(locked) && File.Exists(Path.Combine(Paths.Data, "log-cleanup-pending.json")), "运行中被占用的日志登记延迟删除", report); Log.CleanupPending(); Check(File.Exists(locked), "日志仍占用时保留文件且不报错", report); }
                Log.CleanupPending(); Check(!File.Exists(locked) && !File.Exists(Path.Combine(Paths.Data, "log-cleanup-pending.json")), "释放占用后完成延迟删除并移除队列", report);
                File.WriteAllText(Path.Combine(Paths.Data, "log-cleanup-pending.json"), Json.Write(new[] { "settings.json", "../outside.txt" })); Log.CleanupPending();
                Check(File.Exists(Paths.SettingsFile), "延迟删除拒绝非日志文件与越界路径", report);
                Desktop.Ui.InstallTheme(app); controller = new Desktop.DesktopController(app, settings, false); window = new Desktop.ShellWindow(controller); window.Navigate("logs");
                window.View.Measure(new Size(1200, 820)); window.View.Arrange(new Rect(0, 0, 1200, 820)); window.View.UpdateLayout();
                var buttons = Children<Button>(window.View).ToArray();
                Check(buttons.Any(x => Convert.ToString(x.Content) == "保存保留期") && buttons.Any(x => Convert.ToString(x.Content) == "删除全部日志"), "日志页显示保留期设置和全部删除按钮", report);
                Check(buttons.Single(x => Convert.ToString(x.Content) == "删除全部日志").ToolTip != null, "删除按钮说明运行中日志的处理方式", report);
                controller.SaveLogRetention(14).GetAwaiter().GetResult(); Check(SettingsStore.Load().LogRetentionDays == 14, "日志页保存操作更新配置", report);
                bool invalid = false; try { controller.SaveLogRetention(-1); } catch (ArgumentException) { invalid = true; }
                Check(invalid && SettingsStore.Load().LogRetentionDays == 14, "无效保存不会破坏已有日志设置", report);
                return 0;
            }
            catch (Exception error) { report.Add("FAIL: " + error); return 1; }
            finally
            {
                if (window != null) window.Release(); if (controller != null) controller.Dispose(); app.Shutdown(); Paths.Root = original;
                File.WriteAllLines(Path.Combine(output, "report.txt"), report);
            }
        }
        static void Check(bool condition, string text, List<string> report) { if (!condition) throw new Exception(text); report.Add("PASS: " + text); }
        static IEnumerable<T> Children<T>(DependencyObject value) where T : DependencyObject
        { if (value == null) yield break; if (value is T) yield return (T)value; foreach (var node in LogicalTreeHelper.GetChildren(value).OfType<DependencyObject>()) foreach (var child in Children<T>(node)) yield return child; }
    }
}
