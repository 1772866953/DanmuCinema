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
            if (args.Length > 0 && args[0].StartsWith("--installer-", StringComparison.Ordinal)) return InstallBootstrap.Run(args);
            if (args.Contains("--log-policy-test")) return LogPolicyTests.Run();
            if (args.Contains("--self-test")) return SelfTests.Run();
            if (args.Contains("--integration-test")) return IntegrationTests.Run().GetAwaiter().GetResult();
            if (args.Contains("--wpf-test")) return Desktop.DesktopTests.Run();
            if (args.Contains("--current-fixes-test")) return Desktop.CurrentFixTests.Run();
            if (args.Contains("--dialog-frames-test")) return Desktop.DialogFrameTests.Run();
            if (args.Contains("--dialog-close-test")) return Desktop.DialogFrameTests.RunCloseTransitions();
            if (args.Contains("--design-test")) return Desktop.DesignTests.Run();
            if (args.Contains("--interaction-fixes-test")) return Desktop.InteractionFixTests.Run();
            if (args.Contains("--combo-borders-test")) return Desktop.ComboBorderTests.Run();
            if (args.Contains("--motion-test")) return Desktop.MotionTests.Run();
            if (args.Contains("--branding-weekly-test")) return Desktop.BrandingWeeklyTests.Run();
            if (args.Contains("--ui-polish-test")) return Desktop.UiPolishTests.Run();
            if (args.Contains("--scale-corners-test")) return Desktop.ScaleCornerTests.Run();
            if (args.Contains("--automatic-resilience-test")) return AutomaticResilienceTests.Run();
            if (args.Contains("--dialog-outline-test")) return Desktop.DialogOutlineTests.Run();
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
                if (!args.Contains("--legacy-ui")) return Desktop.DesktopBootstrap.Run(args);
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
