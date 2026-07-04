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
        /// 过滤掉 NDIS 过滤器、协议驱动等非真实网络接口。
        /// 典型的需要过滤的名称模式:
        ///   - WFP Native MAC Layer LightWeight Filter
        ///   - QoS Packet Scheduler
        ///   - Npcap Packet Driver
        ///   - VirtualBox NDIS Light-Weight Filter
        ///   - Huorong / 火绒 等杀毒软件网络驱动
        ///   - 任何包含 "-0000" 尾缀的 (系统自动编号的过滤器实例)
        /// </summary>
        private static readonly string[] FilterDriverKeywords =
        [
            "LightWeight Filter",
            "Light-Weight Filter",
            "WFP",
            "QoS Packet Scheduler",
            "Npcap",
            "NPCAP",
            "VirtualBox",
            "Huorong",
            "NDIS Filter",
            "VMware",
            "-0000",
        ];

        /// <summary>
        /// 枚举所有真实的网络接口(以太网、WLAN、PPPoE拨号等)。
        /// 过滤掉 Loopback、Tunnel 以及各种 NDIS 过滤器/协议驱动。
        /// </summary>
        public List<AdapterInfo> GetAllAdapters(bool includeFilterDrivers = false)
        {
            var result = new List<AdapterInfo>();

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Loopback、Tunnel等系统内部接口对我们的场景没有意义
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                // 过滤 NDIS 过滤器、协议驱动等非真实网络接口
                if (!includeFilterDrivers && IsFilterDriver(nic.Name, nic.Description))
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

        private static bool IsFilterDriver(string name, string description)
        {
            foreach (var keyword in FilterDriverKeywords)
            {
                if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 找到当前的拨号连接(PPPoE)。NetworkInterfaceType.Ppp覆盖了PPPoE场景。
        /// </summary>
        public List<AdapterInfo> GetDialupAdapters()
        {
            return GetAllAdapters()
                .Where(a => a.InterfaceType == NetworkInterfaceType.Ppp.ToString())
                .ToList();
        }

        /// <summary>
        /// 找到虚拟热点适配器。
        /// 注意:开热点之前调这个方法大概率是空列表(适配器还未创建)。
        /// </summary>
        public List<AdapterInfo> GetHostedNetworkAdapters()
        {
            return GetAllAdapters()
                .Where(a => a.IsHostedNetworkVirtualAdapter)
                .ToList();
        }

        private static bool IsHostedNetworkAdapterName(string description)
        {
            return description.Contains("Virtual WiFi", StringComparison.OrdinalIgnoreCase)
                || description.Contains("Wi-Fi Direct Virtual Adapter", StringComparison.OrdinalIgnoreCase)
                || description.Contains("Microsoft Hosted Network Virtual Adapter", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 检测网卡驱动是否支持承载网络(Hosted Network)。
        /// 先用 netsh 正则匹配,如果匹配不到但系统中已经存在虚拟热点适配器,
        /// 说明硬件肯定是支持的,直接返回 true 作为兜底。
        /// </summary>
        public bool? IsHostedNetworkSupported(out string rawOutput)
        {
            var result = CommandRunner.Run("netsh", "wlan show drivers");
            rawOutput = result.StandardOutput;

            if (!result.ProcessExitedNormally)
            {
                return null;
            }

            // 尝试正则匹配 —— 兼容中英文
            var match = Regex.Match(
                result.StandardOutput,
                @"(支持的承载网络|Hosted network supported|支持托管网络)\s*[:：]?\s*(是|Yes|否|No|True|False)",
                RegexOptions.IgnoreCase);

            if (match.Success)
            {
                var value = match.Groups[2].Value;
                return value is "是" or "Yes" or "True";
            }

            // 正则没匹配到 → 检查系统中是否已有 Wi-Fi Direct 虚拟适配器
            // 只要有这个适配器在,就说明驱动肯定是支持的,这比 netsh 文本解析更可靠
            var hasVirtualAdapter = GetHostedNetworkAdapters().Count > 0;
            if (hasVirtualAdapter)
            {
                return true;
            }

            // 最终兜底:返回 null——不确定
            return null;
        }

        /// <summary>
        /// 简化版诊断输出(只显示真正的物理/虚拟网络接口和拨号连接)
        /// </summary>
        public List<AdapterInfo> GetRealAdapters()
        {
            return GetAllAdapters(includeFilterDrivers: false)
                .Where(a => a.InterfaceType is "Ethernet" or "Wireless80211" or "Ppp"
                            || a.IsHostedNetworkVirtualAdapter)
                .ToList();
        }
    }
}
