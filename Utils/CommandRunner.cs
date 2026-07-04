using System.Diagnostics;
using System.Text;

namespace CampusHotspotFix.Utils
{
    public class CommandResult
    {
        public int ExitCode { get; init; }
        public string StandardOutput { get; init; } = string.Empty;
        public string StandardError { get; init; } = string.Empty;
        public bool ProcessExitedNormally => ExitCode == 0;
    }

    /// <summary>
    /// 统一封装外部命令行工具调用(netsh、powercfg等)。
    ///
    /// 编码处理:
    ///   旧版简体中文 Windows → GBK (codepage 936)
    ///   Windows 11 Build 26200+ → UTF-8 (codepage 65001)
    ///   自动检测,不再硬编码。
    /// </summary>
    public static class CommandRunner
    {
        private static Encoding? _cachedEncoding;

        /// <summary>
        /// 程序启动时必须调用一次,注册 CodePages 编码支持(用于 GBK 等)。
        /// </summary>
        public static void RegisterEncodings()
        {
            Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }

        /// <summary>
        /// 自动检测系统命令行输出编码。
        /// Win11 新版本(26200+) 默认 UTF-8, 旧版简体中文默认为 GBK。
        /// </summary>
        private static Encoding GetSystemEncoding()
        {
            if (_cachedEncoding != null)
                return _cachedEncoding;

            try
            {
                // 尝试获取当前控制台的输出编码
                int cp = Console.OutputEncoding.CodePage;

                // 65001 = UTF-8 (Win11 26200+)
                if (cp == 65001)
                {
                    _cachedEncoding = Encoding.UTF8;
                }
                else
                {
                    _cachedEncoding = Encoding.GetEncoding(cp);
                }
            }
            catch
            {
                // 兜底: 尝试 GBK (旧版中文 Windows)
                try { _cachedEncoding = Encoding.GetEncoding(936); }
                catch { _cachedEncoding = Encoding.UTF8; }
            }

            return _cachedEncoding;
        }

        /// <summary>
        /// 清除编码缓存(当需要在运行时切换编码检测时)
        /// </summary>
        public static void ResetEncodingCache()
        {
            _cachedEncoding = null;
        }

        public static CommandResult Run(string fileName, string arguments, int timeoutMs = 15000)
        {
            var encoding = GetSystemEncoding();

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding,
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
                try { process.Kill(entireProcessTree: true); } catch { }

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
