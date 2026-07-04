using System.Diagnostics;
using System.Text;

namespace CampusHotspotFix.Utils
{
    /// <summary>
    /// 命令执行结果。所有字段都保留,方便上层Service自己判断成功/失败,
    /// 而不是CommandRunner替它们下结论——不同命令的"成功"标准不一样,
    /// 判断逻辑应该留在各自的Service里。
    /// </summary>
    public class CommandResult
    {
        public int ExitCode { get; init; }
        public string StandardOutput { get; init; } = string.Empty;
        public string StandardError { get; init; } = string.Empty;

        /// <summary>
        /// 只是"进程是否正常退出(ExitCode==0)"的快捷判断,
        /// 不代表命令的业务逻辑一定成功——比如netsh某些子命令即使配置失败也可能返回0。
        /// </summary>
        public bool ProcessExitedNormally => ExitCode == 0;
    }

    /// <summary>
    /// 统一封装外部命令行工具调用(netsh、powercfg等)。
    ///
    /// 关键设计决策:
    /// 1. 显式指定GBK(codepage 936)编码读取输出——这是中文Windows下netsh类工具的实际输出编码,
    ///    不指定会导致中文关键字("已启动承载网络"、"支持的承载网络"等)全部乱码,后续所有关键字匹配都会失效。
    /// 2. 同时捕获stdout和stderr,很多命令行工具的错误信息只出现在stderr。
    /// 3. 设置超时,避免个别命令卡死导致整个诊断/修复流程假死。
    /// </summary>
    public static class CommandRunner
    {
        /// <summary>
        /// 程序启动时必须调用一次,注册GBK等旧版编码支持。
        /// .NET Core/5+默认裁掉了这些编码,不注册的话Encoding.GetEncoding(936)会直接抛异常。
        /// </summary>
        public static void RegisterEncodings()
        {
            Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }

        /// <param name="fileName">可执行文件名,如 "netsh"、"powercfg"</param>
        /// <param name="arguments">命令行参数,如 "wlan show drivers"</param>
        /// <param name="timeoutMs">超时时间(毫秒),默认15秒,足够覆盖正常场景,同时避免假死</param>
        public static CommandResult Run(string fileName, string arguments, int timeoutMs = 15000)
        {
            // 936 = GBK,简体中文Windows命令行工具默认输出编码。
            // 如果目标机器系统语言不是中文(极少数情况),这里会读到乱码,
            // 但考虑到产品目标用户群(校园网学生)几乎100%是中文Windows,这个假设是合理的权衡。
            var gbk = Encoding.GetEncoding(936);

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = gbk,
                StandardErrorEncoding = gbk,
            };

            using var process = new Process { StartInfo = startInfo };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            bool finishedInTime = process.WaitForExit(timeoutMs);

            if (!finishedInTime)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* 进程可能已自行退出,忽略 */ }

                return new CommandResult
                {
                    ExitCode = -1,
                    StandardOutput = stdout.ToString(),
                    StandardError = $"命令执行超时(超过{timeoutMs}ms),已强制终止。原始stderr: {stderr}",
                };
            }

            return new CommandResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.ToString(),
            };
        }
    }
}
