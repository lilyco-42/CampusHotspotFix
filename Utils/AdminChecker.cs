using System.Diagnostics;
using System.Security.Principal;

namespace CampusHotspotFix.Utils
{
    /// <summary>
    /// 管理员权限检查。
    ///
    /// 说明:配合app.manifest里的requireAdministrator设置,正常情况下
    /// 程序一启动系统就会强制弹UAC、以管理员身份运行,这里的IsRunningAsAdmin()
    /// 理论上应该总是返回true。保留这个检查是为了两种情况:
    /// 1. 用户绕过UAC(比如某些兼容模式设置)导致清单声明没生效,做二次防御
    /// 2. --silent-fix静默模式下,如果任务计划注册的运行级别配置有误导致权限不足,
    ///    能在日志里明确记录原因,而不是让后续操作静默失败导致排查困难
    /// </summary>
    public static class AdminChecker
    {
        public static bool IsRunningAsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// 以管理员权限重新启动当前程序(带上原始命令行参数),并退出当前进程。
        /// 用于AdminChecker检测到权限不足时的兜底恢复路径。
        /// </summary>
        public static void RelaunchAsAdmin(string[] originalArgs)
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("无法获取当前程序路径,无法重新以管理员身份启动。");

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = string.Join(' ', originalArgs),
                UseShellExecute = true,
                Verb = "runas", // 触发UAC提权对话框
            };

            Process.Start(startInfo);
            Environment.Exit(0);
        }
    }
}
