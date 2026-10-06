using System;
using System.Linq;
using System.Windows;

namespace DanmuCinema.Desktop
{
    public static class DesktopBootstrap
    {
        public static int Run(string[] args)
        {
            System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false;
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            DesktopController controller = null;
            try
            {
                Ui.InstallTheme(application);
                var settings = SettingsStore.Load(); AutoStart.Migrate();
                try { DandanConfig.Ensure(settings); } catch { Log.Write("官方源配置无效，其他来源可继续使用。"); }
                controller = new DesktopController(application, settings);
                application.DispatcherUnhandledException += (s, e) => { Log.Write("界面错误：" + e.Exception); AlertWindow.Show(controller.Window == null ? null : controller.Window.View, "界面操作未完成", e.Exception.Message, false); e.Handled = true; };
                application.Startup += async (s, e) => { if (!args.Contains("--tray")) controller.ShowWindow(); await controller.Initialize(args.Contains("--start")); };
                application.SessionEnding += (s, e) => { controller.CancelSchedule(); controller.Dispose(); };
                return application.Run();
            }
            catch (Exception error) { Log.Write("启动错误：" + error); MessageBox.Show(error.Message, "弹幕影院", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
            finally { if (controller != null) { controller.ReleaseWindow(); controller.Dispose(); } }
        }
    }
}
