using CampusHotspotFix.Interop;
using CampusHotspotFix.Models;

namespace CampusHotspotFix.Services
{
    /// <summary>
    /// ICS 共享绑定服务。
    /// 核心功能:将 PPPoE 拨号连接设为 ICS 公用连接,将虚拟热点设为专用连接。
    /// 对应 PRD 问题: P2_IcsShareNotBound
    /// </summary>
    public class IcsShareService
    {
        public bool IsIcsAvailableOnSystem() => ComHelper.IsIcsAvailable();

        public (bool Available, string? ErrorMessage, string? ErrorDetail) DiagnoseIcs()
            => ComHelper.DiagnoseIcs();

        /// <summary>
        /// 完整 ICS 绑定流程:先设公用(PPPoE),再设专用(热点)
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

            // 设置公用连接(PPPoE)
            var (pubOk, pubMsg, pubDetail) = ComHelper.SetPublicConnection(publicAdapterGuid);
            results.Add((publicAdapterGuid, WrapResult(pubOk, pubMsg, pubDetail)));

            // 设置专用连接(热点)
            var (privOk, privMsg, privDetail) = ComHelper.SetPrivateConnection(privateAdapterGuid);
            results.Add((privateAdapterGuid, WrapResult(privOk, privMsg, privDetail)));

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
