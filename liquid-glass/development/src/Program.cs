using System;
using System.Linq;
namespace DanmuCinema {
    static class Program {
        public static string StableId(string value) {
            using(var sha=System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value.ToUpperInvariant()))).Replace("-", "").Substring(0,20);
        }
        [STAThread] static int Main(string[] args) {
            if(args.Length>0 && args[0].StartsWith("--installer-",StringComparison.Ordinal)) {
                Paths.Root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..",".."));
                Paths.RuntimeOverride=System.IO.Path.Combine(Paths.Root,"resources","engine","runtime","jellyfin");
                AutoStart.LauncherOverride=System.IO.Path.Combine(Paths.Root,"DanmuCinema.exe");
                return InstallBootstrap.Run(args);
            }
            if(!args.Contains("--electron-bridge")) return 2;
            string root=Environment.GetEnvironmentVariable("DANMU_DATA_ROOT");
            if(String.IsNullOrWhiteSpace(root)||!System.IO.Path.IsPathRooted(root))return 2;
            bool owner;
            using(var singleton=new System.Threading.Mutex(true,"Local\\DanmuCinema-"+StableId(System.IO.Path.GetFullPath(root)),out owner)) {
                if(!owner)return 10;
                AutoStart.LauncherOverride=Environment.GetEnvironmentVariable("DANMU_LAUNCHER");
                return Desktop.ElectronBridge.Run();
            }
        }
        internal static void PrepareInstalledResources() {
            string plugins=System.IO.Path.Combine(Paths.Root,"resources","engine","plugins");
            foreach(string source in System.IO.Directory.GetDirectories(plugins))CopyTree(source,System.IO.Path.Combine(Paths.ServerData,"plugins",System.IO.Path.GetFileName(source)));
            string scripts=System.IO.Path.Combine(Paths.Root,"scripts");System.IO.Directory.CreateDirectory(scripts);
            System.IO.File.Copy(System.IO.Path.Combine(Paths.Root,"resources","engine","scripts","configure-firewall.ps1"),System.IO.Path.Combine(scripts,"configure-firewall.ps1"),true);
        }
        static void CopyTree(string source,string target) {
            System.IO.Directory.CreateDirectory(target);
            foreach(string file in System.IO.Directory.GetFiles(source))System.IO.File.Copy(file,System.IO.Path.Combine(target,System.IO.Path.GetFileName(file)),true);
            foreach(string child in System.IO.Directory.GetDirectories(source))CopyTree(child,System.IO.Path.Combine(target,System.IO.Path.GetFileName(child)));
        }
    }
}
