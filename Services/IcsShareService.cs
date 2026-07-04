using CampusHotspotFix.Interop;
using CampusHotspotFix.Models;

namespace CampusHotspotFix.Services
{
    /// <summary>
    /// ICS 共享绑定服务。
    /// 核心功能:将 PPPoE 拨号连接设为 ICS 公用连接,将虚拟热点设为专用连接。
    ///
    /// 策略:不依赖 GUID 匹配(避免 INetConnection QI 兼容问题),
    /// 改为枚举所有 COM 连接,对每个连接尝试 EnableSharing。
    /// 只有正确的连接会接受共享配置,错误的会静默拒绝。
    /// </summary>
    public class IcsShareService
    {
        public bool IsIcsAvailableOnSystem() => ComHelper.IsIcsAvailable();
        public (bool Available, string? ErrorMessage, string? ErrorDetail) DiagnoseIcs()
            => ComHelper.DiagnoseIcs();

        /// <summary>
        /// 完整 ICS 绑定流程:
        ///   1. 枚举所有 COM 连接,对每个尝试 EnableSharing(Public) → PPPoE 会接受
        ///   2. 枚举所有 COM 连接,对每个尝试 EnableSharing(Private) → 虚拟热点会接受
        /// </summary>
        public List<(Guid AdapterGuid, FixResult Result)> BindSharing(
            Guid publicAdapterGuid, Guid privateAdapterGuid)
        {
            var results = new List<(Guid, FixResult)>();

            if (!ComHelper.IsIcsAvailable())
            {
                var diag = ComHelper.DiagnoseIcs();
                var fail = FixResult.Fail(ProblemCode.P2_IcsShareNotBound,
                    "ICS 共享组件不可用",
                    detail: $"原因: {diag.ErrorMessage}\r\n{diag.ErrorDetail}");
                results.Add((publicAdapterGuid, fail));
                results.Add((privateAdapterGuid, fail));
                return results;
            }

            // 设置公用连接: 对所有连接尝试 Public
            var (pubOk, pubTotal, pubDetails) = ComHelper.TrySetPublicOnAll();
            results.Add((publicAdapterGuid, WrapResult(
                pubOk > 0,
                $"ICS 公用连接设置: {pubOk}/{pubTotal} 个连接成功",
                string.Join("\r\n", pubDetails))));

            // 设置专用连接: 对所有连接尝试 Private
            var (privOk, privTotal, privDetails) = ComHelper.TrySetPrivateOnAll();
            results.Add((privateAdapterGuid, WrapResult(
                privOk > 0,
                $"ICS 专用连接设置: {privOk}/{privTotal} 个连接成功",
                string.Join("\r\n", privDetails))));

            return results;
        }

        private static FixResult WrapResult(bool success, string message, string? detail)
        {
            return success
                ? FixResult.Ok(ProblemCode.P2_IcsShareNotBound, message)
                : FixResult.Fail(ProblemCode.P2_IcsShareNotBound, message, detail: detail);
        }
    }
}
