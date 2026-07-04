using System.Text.RegularExpressions;
using CampusHotspotFix.Models;
using CampusHotspotFix.Utils;

namespace CampusHotspotFix.Services
{
    public enum HostedNetworkStatus
    {
        Unknown,
        NotStarted,
        Started,
    }

    public class HostedNetworkQueryResult
    {
        public HostedNetworkStatus Status { get; init; }
        public int ConnectedClientCount { get; init; }
        public string RawOutput { get; init; } = string.Empty;
    }

    /// <summary>
    /// 虚拟热点(承载网络)管理服务,对应PRD第2.1节的P1修复方案。
    ///
    /// 关键设计决策:不依赖"设置→网络→移动热点"这个UI路径(那个UI本身识别不了PPPoE拨号连接,
    /// 是P1问题的根源之一),而是直接用netsh命令行创建虚拟无线网络,绕开UI层的判断逻辑。
    /// </summary>
    public class HostedNetworkService
    {
        /// <summary>
        /// 创建并启动虚拟热点。
        /// 先在 raw 输出中找成功标志, 找不到时再用失败标志兜底。
        /// </summary>
        public FixResult Enable(string ssid, string key)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                return FixResult.Fail(ProblemCode.P1_HostedNetworkNotAvailable, "热点名称不能为空");
            if (key.Length < 8)
                return FixResult.Fail(ProblemCode.P1_HostedNetworkNotAvailable, "热点密码至少需要8位字符(WPA2要求)");

            // 第一步:配置承载网络参数
            var setResult = CommandRunner.Run("netsh",
                $"wlan set hostednetwork mode=allow ssid=\"{ssid}\" key=\"{key}\"");

            var setOutput = setResult.StandardOutput;
            bool setOk = IsSuccessOutput(setOutput);

            if (!setOk)
            {
                return FixResult.Fail(
                    ProblemCode.P1_HostedNetworkNotAvailable,
                    "配置虚拟热点参数失败,请检查网卡是否支持承载网络",
                    detail: $"ExitCode={setResult.ExitCode}\r\nstdout:\r\n{setOutput}\r\nstderr:\r\n{setResult.StandardError}");
            }

            // 第二步:启动热点
            var startResult = CommandRunner.Run("netsh", "wlan start hostednetwork");

            var startOutput = startResult.StandardOutput;
            bool startOk = IsSuccessOutput(startOutput);

            if (!startOk)
            {
                return FixResult.Fail(
                    ProblemCode.P1_HostedNetworkNotAvailable,
                    "虚拟热点启动失败,常见原因是网卡驱动不支持承载网络,或WLAN服务未运行",
                    detail: $"ExitCode={startResult.ExitCode}\r\nstdout:\r\n{startOutput}\r\nstderr:\r\n{startResult.StandardError}");
            }

            return FixResult.Ok(ProblemCode.P1_HostedNetworkNotAvailable, "虚拟热点已成功启动");
        }

        public FixResult Disable()
        {
            var result = CommandRunner.Run("netsh", "wlan stop hostednetwork");

            bool acceptable = result.ProcessExitedNormally
                || result.StandardOutput.Contains("没有启动")
                || result.StandardOutput.Contains("not started", StringComparison.OrdinalIgnoreCase);

            return acceptable
                ? FixResult.Ok(ProblemCode.P1_HostedNetworkNotAvailable, "虚拟热点已停止")
                : FixResult.Fail(ProblemCode.P1_HostedNetworkNotAvailable, "停止虚拟热点失败",
                    detail: result.StandardOutput + result.StandardError);
        }

        public HostedNetworkQueryResult QueryStatus()
        {
            var result = CommandRunner.Run("netsh", "wlan show hostednetwork");

            if (!result.ProcessExitedNormally)
                return new HostedNetworkQueryResult { Status = HostedNetworkStatus.Unknown, RawOutput = result.StandardError };

            var output = result.StandardOutput;

            var status = output.Contains("已启动") || output.Contains("Started", StringComparison.OrdinalIgnoreCase)
                ? HostedNetworkStatus.Started
                : HostedNetworkStatus.NotStarted;

            var clientCountMatch = Regex.Match(output, @"(个数|Number of clients)\s*[:：]\s*(\d+)");
            int clientCount = clientCountMatch.Success ? int.Parse(clientCountMatch.Groups[2].Value) : 0;

            return new HostedNetworkQueryResult
            {
                Status = status,
                ConnectedClientCount = clientCount,
                RawOutput = output,
            };
        }

        /// <summary>
        /// 判断 netsh 输出是否表示操作成功。
        /// 兼容中文/英文关键词变体。
        /// </summary>
        private static bool IsSuccessOutput(string output)
        {
            // 成功关键词
            if (output.Contains("已成功", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("已启动", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("The hosted network started", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("The hosted network", StringComparison.OrdinalIgnoreCase))
                return true;

            // 如果没有明确失败关键词就算成功(部分 Windows 版本 netsh 不输出明确成功消息)
            var failKeywords = new[] { "失败", "错误", "error", "fail", "not supported", "is not" };
            foreach (var kw in failKeywords)
            {
                if (output.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // ExitCode==0 且非空输出 → 视为成功
            return !string.IsNullOrWhiteSpace(output);
        }
    }
}
