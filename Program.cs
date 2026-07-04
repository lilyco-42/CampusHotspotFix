using CampusHotspotFix.Forms;
using CampusHotspotFix.Utils;

namespace CampusHotspotFix
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // 必须在程序最开始就注册,任何用到CommandRunner的代码之前
            CommandRunner.RegisterEncodings();

            // 二次防御:正常情况下app.manifest已经强制要求管理员权限,
            // 这里再检查一次,理由见AdminChecker.cs里的注释
            if (!AdminChecker.IsRunningAsAdmin())
            {
                AdminChecker.RelaunchAsAdmin(args);
                return; // RelaunchAsAdmin内部会Environment.Exit,这里的return实际不会被执行,只是让编译器满意
            }

            bool isSilentMode = args.Contains("--silent-fix", StringComparer.OrdinalIgnoreCase);

            if (isSilentMode)
            {
                RunSilentFix();
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }

        /// <summary>
        /// 静默修复模式:不显示任何窗口,直接依次执行修复逻辑后退出。
        /// 专门给TaskSchedulerService注册的"用户登录时"开机任务调用,
        /// 对应PRD里P4(重启后配置失效)的解决方案。
        ///
        /// 注意:这里目前只是占位骨架,具体调用哪些Service的Fix方法,
        /// 等HostedNetworkService/IcsShareService/PowerManagementService全部完成后再补上,
        /// 保持和正常模式下"一键修复"按钮调用同一套Service方法,避免逻辑分叉维护两份。
        /// </summary>
        private static void RunSilentFix()
        {
            // TODO: 依次调用 HostedNetworkService.Enable() -> IcsShareService.BindSharing()
            //       -> PowerManagementService.DisableAllPowerSaving()
            // 每一步结果写入日志文件(建议路径:%LOCALAPPDATA%\CampusHotspotFix\silent-fix.log),
            // 方便用户重启后如果没生效,能自己查日志,或者发给你远程排查。
        }
    }
}
