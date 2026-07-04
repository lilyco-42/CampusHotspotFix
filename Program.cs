using CampusHotspotFix.Forms;
using CampusHotspotFix.Models;
using CampusHotspotFix.Services;
using CampusHotspotFix.Utils;

namespace CampusHotspotFix
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            CommandRunner.RegisterEncodings();

            if (!AdminChecker.IsRunningAsAdmin())
            {
                AdminChecker.RelaunchAsAdmin(args);
                return;
            }

            bool isSilentMode = args.Contains("--silent-fix", StringComparer.OrdinalIgnoreCase);

            if (isSilentMode)
            {
                RunSilentFix();
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }

        /// <summary>
        /// 静默修复模式:由 TaskScheduler 在用户登录时调用,
        /// 无窗口,自动执行完整修复并写入日志。
        /// </summary>
        private static void RunSilentFix()
        {
            var adapterService = new NetworkAdapterService();
            var hostedNetwork = new HostedNetworkService();
            var icsShare = new IcsShareService();
            var powerMgmt = new PowerManagementService();

            string ssid = $"CampusHotspot_{Environment.MachineName}";
            string key = GenerateRandomPassword(12);

            var logLines = new List<string>();
            logLines.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 校园宽带热点修复工具 - 静默修复模式");
            logLines.Add("========================================");

            try
            {
                // Step 1: 检测承载网络
                logLines.Add("[1/4] 检测网卡驱动...");
                var supported = adapterService.IsHostedNetworkSupported(out _);
                if (supported != true)
                {
                    logLines.Add("[失败] 网卡不支持承载网络,退出修复");
                    WriteLog(logLines);
                    return;
                }
                logLines.Add("[OK] 网卡支持承载网络");

                // Step 2: 创建并启动热点
                logLines.Add($"[2/4] 创建虚拟热点 (SSID: {ssid})...");
                var enableResult = hostedNetwork.Enable(ssid, key);
                logLines.Add(enableResult.Success
                    ? $"[OK] 虚拟热点已启动"
                    : $"[失败] {enableResult.Message}");
                if (!enableResult.Success && enableResult.ExceptionDetail != null)
                    logLines.Add($"  详情: {enableResult.ExceptionDetail}");

                // 等待虚拟适配器出现
                Thread.Sleep(3000);

                // Step 3: ICS 共享绑定
                logLines.Add("[3/4] 绑定 ICS 共享...");
                var pppoeAdapters = adapterService.GetDialupAdapters();
                var virtualAdapters = adapterService.GetHostedNetworkAdapters();

                if (pppoeAdapters.Count > 0 && virtualAdapters.Count > 0)
                {
                    var icsResults = icsShare.BindSharing(
                        Guid.Parse(pppoeAdapters[0].Id),
                        Guid.Parse(virtualAdapters[0].Id));

                    foreach (var (_, result) in icsResults)
                    {
                        logLines.Add(result.Success
                            ? $"[OK] ICS: {result.Message}"
                            : $"[失败] ICS: {result.Message}");
                    }
                }
                else
                {
                    logLines.Add("[跳过] PPPoE 或虚拟适配器未找到,跳过 ICS 绑定");
                }

                // Step 4: 电源管理
                logLines.Add("[4/4] 优化电源管理...");
                var powerResult = powerMgmt.DisableAllPowerSaving();
                logLines.Add(powerResult.Success
                    ? $"[OK] {powerResult.Message}"
                    : $"[失败] {powerResult.Message}");

                logLines.Add("========================================");
                logLines.Add("静默修复完成。");
                logLines.Add($"热点名称: {ssid}");
                logLines.Add($"热点密码: {key}");
            }
            catch (Exception ex)
            {
                logLines.Add($"[严重错误] {ex.GetType().Name}: {ex.Message}");
                logLines.Add(ex.StackTrace ?? "");
            }

            WriteLog(logLines);
        }

        private static void WriteLog(List<string> lines)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CampusHotspotFix");

                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "silent-fix.log");

                File.AppendAllLines(logFile, lines);
                File.AppendAllText(logFile, "\r\n");
            }
            catch
            {
                // 日志写失败也不应导致程序崩溃
            }
        }

        private static string GenerateRandomPassword(int length)
        {
            const string chars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
            var random = Random.Shared;
            return new string(Enumerable.Range(0, length)
                .Select(_ => chars[random.Next(chars.Length)])
                .ToArray());
        }
    }
}
