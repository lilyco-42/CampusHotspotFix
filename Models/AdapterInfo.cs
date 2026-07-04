namespace CampusHotspotFix.Models
{
    /// <summary>
    /// 对应PRD文档《校园宽带热点共享工具-PRD.md》第1.2节的问题清单编号。
    /// 保持编号一致是故意的——诊断报告、修复日志、用户看到的文案,
    /// 都应该能直接对照PRD里的问题描述,不要在代码里另起一套命名增加认知成本。
    /// </summary>
    public enum ProblemCode
    {
        P1_HostedNetworkNotAvailable,      // 移动热点开关灰色/未识别为可共享网络
        P2_IcsShareNotBound,                // 热点能开但无法上网(ICS绑定问题)
        P3_AdapterBindingConflict,          // 适配器绑定错乱(WLAN与以太网互抢)
        P4_NotPersistedAfterReboot,         // 重启后配置失效
        P5_PowerSavingDisconnect,           // 约30分钟断开(电源/驱动超时,可修复)
        P6_IspActiveDetectionDisconnect,    // 约30分钟断开(运营商主动检测,不可保证)
        P7_8021xAuthConflict,               // 校园网802.1x认证导致其他设备无法接入
        P8_IspHotspotDetection,             // 学校主动检测热点行为
        P9_DialupErrorCode,                 // 拨号本身报错(651/691/628等)
        P10_AccountSharingPolicyRisk,       // 单账号多设备限制的政策风险
    }

    /// <summary>
    /// 每个问题项的处理结论,直接决定了UI上展示的颜色/图标和后续能不能点"一键修复"。
    /// </summary>
    public enum ResolutionType
    {
        /// <summary>还没检测到这个问题,或者检测结果是"没问题"</summary>
        NotApplicable,

        /// <summary>可以自动修复,对应PRD里明确承诺能稳定解决的部分(P1-P5, P9)</summary>
        AutoFixable,

        /// <summary>不能保证解决,只能诊断+告知风险(P6-P8, P10)——绝对不能在UI上给"修复"按钮</summary>
        RiskOnly,
    }

    /// <summary>
    /// 网卡/网络接口的基本信息。
    ///
    /// 设计决策:优先使用 System.Net.NetworkInformation.NetworkInterface 这个纯managed API
    /// 来枚举网卡(包括PPPoE拨号连接,它的NetworkInterfaceType会是Ppp类型),
    /// 而不是自己走WMI/COM去查——能用托管API解决的查询,没必要引入额外的互操作复杂度。
    /// WMI只在managed API覆盖不到的信息(比如电源管理属性)时才使用。
    /// </summary>
    public class AdapterInfo
    {
        /// <summary>系统里的接口GUID字符串,后续COM互操作绑定ICS共享时需要用它精确定位适配器</summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>网卡的连接名称,比如"WLAN"、"以太网"、"宽带连接"</summary>
        public string Name { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        /// <summary>Ethernet / Wireless80211 / Ppp(拨号,包括PPPoE) / 其他</summary>
        public string InterfaceType { get; init; } = string.Empty;

        public bool IsUp { get; init; }

        /// <summary>
        /// 是否是"netsh wlan set hostednetwork"创建出来的虚拟热点适配器。
        /// 判断方式是名称匹配"Microsoft Wi-Fi Direct Virtual Adapter"或类似关键字,
        /// 具体匹配规则在NetworkAdapterService里实现,这里只是承载判断结果。
        /// </summary>
        public bool IsHostedNetworkVirtualAdapter { get; init; }
    }
}
