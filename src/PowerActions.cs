using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DanmuCinema
{
    // Native SYSTEM_POWER_CAPABILITIES is 76 bytes in the Windows SDK.
    // BOOLEAN is one byte; AoAc at offset 20 identifies S0 Modern Standby.
    [StructLayout(LayoutKind.Explicit, Size = 76)]
    public struct PowerCapabilities
    {
        [FieldOffset(3)] public byte SystemS1;
        [FieldOffset(4)] public byte SystemS2;
        [FieldOffset(5)] public byte SystemS3;
        [FieldOffset(6)] public byte SystemS4;
        [FieldOffset(8)] public byte HiberFilePresent;
        [FieldOffset(20)] public byte AoAc;
        [FieldOffset(22)] public byte HiberFileType;
        public bool SleepAvailable { get { return SystemS1 != 0 || SystemS2 != 0 || SystemS3 != 0 || AoAc != 0; } }
        public bool HibernateAvailable { get { return SystemS4 != 0 && HiberFilePresent != 0 && HiberFileType != 1; } }
    }

    public static class PowerActions
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Luid { public uint LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential)]
        private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string system, string name, out Luid luid);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disable, ref TokenPrivileges privileges,
            uint length, out TokenPrivileges previous, out uint required);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("powrprof.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,
            [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool disableWake);
        [DllImport("powrprof.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool GetPwrCapabilities(out PowerCapabilities capabilities);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool LockWorkStation();
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint flags);

        public static string Name(PowerAction action)
        {
            switch (action)
            {
                case PowerAction.StopServices: return "停止服务";
                case PowerAction.Restart: return "重启";
                case PowerAction.Sleep: return "睡眠";
                case PowerAction.Hibernate: return "休眠";
                case PowerAction.Lock: return "锁定";
                case PowerAction.Logoff: return "注销";
                default: return "关机";
            }
        }
        public static string Description(PowerAction action)
        {
            switch (action)
            {
                case PowerAction.StopServices: return "停止视频和弹幕服务，iPad 播放将中断，电脑保持运行。";
                case PowerAction.Restart: return "强制关闭应用并重新启动，请提前保存文件。";
                case PowerAction.Sleep: return "进入低功耗睡眠状态，唤醒后继续使用。";
                case PowerAction.Hibernate: return "将当前状态保存到磁盘，再关闭电源。";
                case PowerAction.Lock: return "锁定当前桌面，登录后继续使用。";
                case PowerAction.Logoff: return "退出当前 Windows 账户，请提前保存文件。";
                default: return "关闭 Windows 和电脑电源，请提前保存文件。";
            }
        }
        public static void Validate(PowerAction action)
        {
            if (action != PowerAction.Hibernate && action != PowerAction.Sleep) return;
            PowerCapabilities capabilities = ReadCapabilities();
            if (action == PowerAction.Hibernate && !capabilities.HibernateAvailable)
                throw new InvalidOperationException("此电脑未启用或不支持休眠。可在管理员终端执行 powercfg /hibernate on 后重试。程序不会自行修改系统设置。");
            if (action == PowerAction.Sleep && !capabilities.SleepAvailable)
                throw new InvalidOperationException("此电脑当前不支持睡眠。请检查 Windows 电源设置或选择其他操作。");
        }
        public static PowerCapabilities ReadCapabilities()
        {
            PowerCapabilities capabilities;
            if (!GetPwrCapabilities(out capabilities)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取系统电源能力。");
            return capabilities;
        }
        public static void KeepAwake(bool enabled)
        {
            if (SetThreadExecutionState(enabled ? 0x80000001u : 0x80000000u) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法更新自动睡眠设置。");
        }
        // Restart explicitly forces apps closed, matching the user's "Restart anyway" choice.
        public static string ShutdownArguments(PowerAction action)
        {
            if (action == PowerAction.Shutdown) return "/s /t 0";
            if (action == PowerAction.Restart) return "/r /f /t 0";
            if (action == PowerAction.Logoff) return "/l";
            throw new ArgumentException("此操作不使用 shutdown.exe。");
        }
        public static void Execute(PowerAction action)
        {
            if (action == PowerAction.StopServices) throw new ArgumentException("停止服务由播放器控制器执行。");
            Validate(action);
            if (action == PowerAction.Sleep || action == PowerAction.Hibernate)
            {
                Suspend(action == PowerAction.Hibernate);
                return;
            }
            if (action == PowerAction.Lock)
            {
                if (!LockWorkStation()) throw new Win32Exception(Marshal.GetLastWin32Error());
                return;
            }
            var info = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "shutdown.exe"), ShutdownArguments(action));
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardError = true;
            using (var process = Process.Start(info))
            {
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Windows 拒绝了电源操作（错误码 " + process.ExitCode + "）。" : error.Trim());
            }
        }
        private static void Suspend(bool hibernate)
        {
            IntPtr token;
            using (Process current = Process.GetCurrentProcess())
            {
                if (!OpenProcessToken(current.Handle, 0x28, out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            bool adjusted = false;
            TokenPrivileges previous = new TokenPrivileges();
            try
            {
                Luid luid;
                if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out luid)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
                uint required;
                if (!AdjustTokenPrivileges(token, false, ref privileges, (uint)Marshal.SizeOf(typeof(TokenPrivileges)), out previous, out required))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                int error = Marshal.GetLastWin32Error();
                adjusted = true;
                if (error != 0) throw new Win32Exception(error);
                if (!SetSuspendState(hibernate, false, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (adjusted)
                {
                    TokenPrivileges ignored;
                    uint required;
                    AdjustTokenPrivileges(token, false, ref previous, 0, out ignored, out required);
                }
                CloseHandle(token);
            }
        }
    }
}
