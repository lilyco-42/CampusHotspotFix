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
        /// 调用前提:NetworkAdapterService.IsHostedNetworkSupported() 必须已经确认返回true,
        /// 否则这里的命令大概率会失败——但即便调用方没检查,这里也会根据命令的实际返回结果
        /// 给出明确的FixResult,不会假装成功。
        /// </summary>
        /// <param name="ssid">自定义热点名称,建议由UI层生成一个不易冲突的默认值,同时允许用户自定义</param>
        /// <param name="key">热点密码,WPA2要求至少8位字符</param>
        public FixResult Enable(string ssid, string key)
        {
            if (string.IsNullOrWhiteSpace(ssid))
            {
                return FixResult.Fail(ProblemCode.P1_HostedNetworkNotAvailable, "热点名称不能为空");
            }
            if (key.Length < 8)
            {
                return FixResult.Fail(ProblemCode.P1_HostedNetworkNotAvailable, "热点密码至少需要8位字符(WPA2要求)");
            }

            // 第一步:配置承载网络参数(mode=allow 表示允许创建,不是立即启动)
            var setResult = CommandRunner.Run("netsh",
                $"wlan set hostednetwork mode=allow ssid=\"{ssid}\" key=\"{key}\"");

            if (!setResult.ProcessExitedNormally || !setResult.StandardOutput.Contains("已成功"))
            {
                return FixResult.Fail(
                    ProblemCode.P1_HostedNetworkNotAvailable,
                    "配置虚拟热点参数失败,请检查网卡是否支持承载网络",
                    detail: setResult.StandardOutput + setResult.StandardError);
            }

            // 第二步:真正启动
            var startResult = CommandRunner.Run("netsh", "wlan start hostednetwork");

            if (!startResult.ProcessExitedNormally || !startResult.StandardOutput.Contains("已启动承载网络"))
            {
                return FixResult.Fail(
                    ProblemCode.P1_HostedNetworkNotAvailable,
                    "虚拟热点启动失败,常见原因是网卡驱动不支持承载网络,或WLAN服务未运行",
                    detail: startResult.StandardOutput + startResult.StandardError);
            }

            return FixResult.Ok(ProblemCode.P1_HostedNetworkNotAvailable, "虚拟热点已成功启动");
        }

        public FixResult Disable()
        {
            var result = CommandRunner.Run("netsh", "wlan stop hostednetwork");

            // "承载网络没有启动"也算是一种可接受的结果(说明本来就没开,目标状态已达成)
            bool acceptable = result.ProcessExitedNormally
                || result.StandardOutput.Contains("没有启动")
                || result.StandardOutput.Contains("not started", StringComparison.OrdinalIgnoreCase);

            return acceptable
                ? FixResult.Ok(ProblemCode.P1_HostedNetworkNotAvailable, "虚拟热点已停止")
                : FixResult.Fail(ProblemCode.P1_HostedNetworkNotAvailable, "停止虚拟热点失败",
                    detail: result.StandardOutput + result.StandardError);
        }

        /// <summary>
        /// 查询当前热点状态和连接客户端数量。
        /// ConnectedClientCount这个数据是给DisconnectMonitorService用的——
        /// 后台定时调用这个方法,记录"客户端数量从>0变为0"的时间点,
        /// 用于后续判断P5(电源相关)还是P6(运营商检测)。
        /// </summary>
        public HostedNetworkQueryResult QueryStatus()
        {
            var result = CommandRunner.Run("netsh", "wlan show hostednetwork");

            if (!result.ProcessExitedNormally)
            {
                return new HostedNetworkQueryResult { Status = HostedNetworkStatus.Unknown, RawOutput = result.StandardError };
            }

            var output = result.StandardOutput;

            var status = output.Contains("已启动") || output.Contains("Started", StringComparison.OrdinalIgnoreCase)
                ? HostedNetworkStatus.Started
                : HostedNetworkStatus.NotStarted;

            // 输出里有"个数     : N"这一行表示当前连接的客户端数量
            var clientCountMatch = Regex.Match(output, @"(个数|Number of clients)\s*[:：]\s*(\d+)");
            int clientCount = clientCountMatch.Success ? int.Parse(clientCountMatch.Groups[2].Value) : 0;

            return new HostedNetworkQueryResult
            {
                Status = status,
                ConnectedClientCount = clientCount,
                RawOutput = output,
            };
        }
    }
}
