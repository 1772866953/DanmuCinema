using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace DanmuCinema
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Contains("--self-test")) return SelfTests.Run();
            if (args.Contains("--integration-test")) return IntegrationTests.Run().GetAwaiter().GetResult();
            bool owner;
            using (var singleton = new Mutex(true, "Local\\DanmuCinema-" + StableId(Paths.Root), out owner))
            {
                if (!owner)
                {
                    using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DanmuCinema-Show-" + StableId(Paths.Root))) signal.Set();
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (sender, e) => { Log.Write("界面错误：" + e.Exception.Message); MessageBox.Show(e.Exception.Message, "弹幕影院", MessageBoxButtons.OK, MessageBoxIcon.Error); };
                try
                {
                    var settings = SettingsStore.Load();
                    AutoStart.Migrate();
                    try { DandanConfig.Ensure(settings); } catch { Log.Write("官方源配置无效，其他来源可继续使用。"); }
                    using (var form = new MainForm(settings, args.Contains("--tray"), args.Contains("--start"))) Application.Run(form);
                }
                catch (Exception e) { Log.Write("启动错误：" + e.Message); MessageBox.Show(e.Message, "弹幕影院", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
                return 0;
            }
        }
        public static string StableId(string value)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value.ToUpperInvariant()))).Replace("-", "").Substring(0, 20);
        }
    }
}
