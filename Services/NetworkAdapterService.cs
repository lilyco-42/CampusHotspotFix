using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using CampusHotspotFix.Models;
using CampusHotspotFix.Utils;

namespace CampusHotspotFix.Services
{
    /// <summary>
    /// 网卡/网络连接查询服务。这是纯查询模块,不修改任何系统状态,
    /// 对应工作流程文档里"建议实施顺序"的第2步——因为不修改状态,最容易独立验证正确性。
    /// </summary>
    public class NetworkAdapterService
    {
        /// <summary>
        /// 枚举所有网络接口(以太网、WLAN、PPPoE拨号等)。
        /// 用托管API(NetworkInterface)而不是WMI/COM,理由见AdapterInfo.cs里的注释。
        /// </summary>
        public List<AdapterInfo> GetAllAdapters()
        {
            var result = new List<AdapterInfo>();

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Loopback、Tunnel等系统内部接口对我们的场景没有意义,过滤掉,减少诊断报告里的噪音
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                result.Add(new AdapterInfo
                {
                    Id = nic.Id,
                    Name = nic.Name,
                    Description = nic.Description,
                    InterfaceType = nic.NetworkInterfaceType.ToString(),
                    IsUp = nic.OperationalStatus == OperationalStatus.Up,
                    IsHostedNetworkVirtualAdapter = IsHostedNetworkAdapterName(nic.Description),
                });
            }

            return result;
        }

        /// <summary>
        /// 找到当前的拨号连接(PPPoE)。NetworkInterfaceType.Ppp覆盖了PPPoE场景。
        /// 如果返回空列表,说明当前没有激活的拨号连接——诊断报告需要明确提示用户先完成拨号。
        /// </summary>
        public List<AdapterInfo> GetDialupAdapters()
        {
            return GetAllAdapters()
                .Where(a => a.InterfaceType == NetworkInterfaceType.Ppp.ToString())
                .ToList();
        }

        /// <summary>
        /// 找到"netsh wlan set hostednetwork"创建出来的虚拟热点适配器。
        /// 这个适配器只有在HostedNetworkService.Enable()执行过之后才会出现,
        /// 所以调用方要注意:开热点之前调这个方法,大概率是空列表,这是正常现象,不是bug。
        /// </summary>
        public List<AdapterInfo> GetHostedNetworkAdapters()
        {
            return GetAllAdapters()
                .Where(a => a.IsHostedNetworkVirtualAdapter)
                .ToList();
        }

        private static bool IsHostedNetworkAdapterName(string description)
        {
            // 微软虚拟WiFi适配器的描述文本在不同Windows版本上略有差异,
            // 用包含关键字的方式匹配,比要求完全相等更稳健
            return description.Contains("Virtual WiFi", StringComparison.OrdinalIgnoreCase)
                || description.Contains("Wi-Fi Direct Virtual Adapter", StringComparison.OrdinalIgnoreCase)
                || description.Contains("Microsoft Hosted Network Virtual Adapter", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 检测网卡驱动是否支持"承载网络"(Hosted Network)。
        /// 对应PRD里P1的前置判断——如果驱动本身不支持,后面所有基于hostednetwork命令的
        /// 修复动作都注定失败,必须在这一步就拦住,给用户"换驱动/换外置USB无线网卡"的建议,
        /// 而不是让流程继续走下去然后在后面某个环节报一个让人看不懂的错误。
        /// </summary>
        /// <returns>
        /// true = 支持;false = 不支持;null = 命令执行失败,没能获取到明确结论
        /// (比如超时、netsh输出格式在某个Windows版本上发生了变化导致关键字匹配不到)
        /// </returns>
        public bool? IsHostedNetworkSupported(out string rawOutput)
        {
            var result = CommandRunner.Run("netsh", "wlan show drivers");
            rawOutput = result.StandardOutput;

            if (!result.ProcessExitedNormally)
            {
                return null;
            }

            // 中文Windows输出关键字是"支持的承载网络"，后面跟"是"或"否"
            // 同时兼容可能的英文系统环境:"Hosted network supported"
            var match = Regex.Match(
                result.StandardOutput,
                @"(支持的承载网络|Hosted network supported)\s*[:：]\s*(是|Yes|否|No)",
                RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                // 匹配不到,说明netsh输出格式和预期不一致(可能是系统语言、netsh版本差异),
                // 明确返回null而不是猜测,让上层诊断报告如实反映"无法确定",而不是显示误导性的结论
                return null;
            }

            var value = match.Groups[2].Value;
            return value is "是" or "Yes";
        }
    }
}
