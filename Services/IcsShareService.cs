using CampusHotspotFix.Interop;
using CampusHotspotFix.Models;

namespace CampusHotspotFix.Services
{
    /// <summary>
    /// ICS 共享绑定服务。
    /// 核心功能:将 PPPoE 拨号连接设为 ICS 公用连接,将虚拟热点设为专用连接,
    /// 使连接热点的设备能通过 PPPoE 上网。
    ///
    /// 对应 PRD 问题: P2_IcsShareNotBound
    /// </summary>
    public class IcsShareService
    {
        /// <summary>
        /// 检测系统是否安装了 ICS 共享组件。
        /// 部分精简版 Windows (N/KN/LTSC) 可能缺少此组件。
        /// </summary>
        public bool IsIcsAvailableOnSystem()
        {
            return ComHelper.IsIcsAvailable();
        }

        /// <summary>
        /// 将指定适配器绑定为 ICS 公用连接(互联网来源)。
        /// 典型调用:传入 PPPoE 拨号适配器的 GUID。
        /// </summary>
        public FixResult SetAsPublicConnection(Guid adapterGuid)
        {
            try
            {
                var config = ComHelper.GetConfigForAdapterGuid(adapterGuid);
                if (config == null)
                    return FixResult.Fail(ProblemCode.P2_IcsShareNotBound,
                        "未找到对应适配器的 ICS 配置对象。确认适配器已连接且 ICS 服务可用。",
                        detail: $"查询 GUID={adapterGuid} 的 ICS 配置失败");

                config.EnableSharing(SharingConnectionType.Public);

                return FixResult.Ok(ProblemCode.P2_IcsShareNotBound,
                    $"已将适配器 [{adapterGuid}] 设为 ICS 公用连接");
            }
            catch (UnauthorizedAccessException)
            {
                return FixResult.Fail(ProblemCode.P2_IcsShareNotBound,
                    "权限不足:需要管理员权限才能修改 ICS 共享配置。");
            }
            catch (Exception ex)
            {
                return FixResult.Fail(ProblemCode.P2_IcsShareNotBound,
                    "设置 ICS 公用连接失败",
                    detail: $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// 将指定适配器绑定为 ICS 专用连接(接收共享的 LAN 侧)。
        /// 典型调用:传入虚拟热点适配器的 GUID。
        /// </summary>
        public FixResult SetAsPrivateConnection(Guid adapterGuid)
        {
            try
            {
                var config = ComHelper.GetConfigForAdapterGuid(adapterGuid);
                if (config == null)
                    return FixResult.Fail(ProblemCode.P2_IcsShareNotBound,
                        "未找到对应适配器的 ICS 配置对象。确认虚拟热点已创建且启用。",
                        detail: $"查询 GUID={adapterGuid} 的 ICS 配置失败");

                config.EnableSharing(SharingConnectionType.Private);

                return FixResult.Ok(ProblemCode.P2_IcsShareNotBound,
                    $"已将适配器 [{adapterGuid}] 设为 ICS 专用连接");
            }
            catch (UnauthorizedAccessException)
            {
                return FixResult.Fail(ProblemCode.P2_IcsShareNotBound,
                    "权限不足:需要管理员权限才能修改 ICS 共享配置。");
            }
            catch (Exception ex)
            {
                return FixResult.Fail(ProblemCode.P2_IcsShareNotBound,
                    "设置 ICS 专用连接失败",
                    detail: $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// 完整 ICS 绑定流程:先设公用(PPPoE),再设专用(热点)。
        /// 这是"一键修复"调用的主要方法。
        /// </summary>
        /// <returns>绑定结果列表(Guid + FixResult 对)</returns>
        public List<(Guid AdapterGuid, FixResult Result)> BindSharing(
            Guid publicAdapterGuid, Guid privateAdapterGuid)
        {
            var results = new List<(Guid, FixResult)>();

            // 第一步:验证 ICS 可用
            if (!ComHelper.IsIcsAvailable())
            {
                var fail = FixResult.Fail(ProblemCode.P2_IcsShareNotBound,
                    "系统未安装 ICS 共享组件,无法继续。",
                    detail: "INetSharingManager COM 组件不可用,常见于 Windows N/KN/LTSC 版本。");
                results.Add((publicAdapterGuid, fail));
                results.Add((privateAdapterGuid, fail));
                return results;
            }

            // 第二步:设置公用连接(PPPoE)
            var publicResult = SetAsPublicConnection(publicAdapterGuid);
            results.Add((publicAdapterGuid, publicResult));

            // 第三步:设置专用连接(热点)
            // 即使公用连接失败也尝试设置专用连接(两者独立)
            var privateResult = SetAsPrivateConnection(privateAdapterGuid);
            results.Add((privateAdapterGuid, privateResult));

            return results;
        }

        /// <summary>
        /// 移除指定适配器的 ICS 共享配置。
        /// </summary>
        public FixResult DisableSharing(Guid adapterGuid)
        {
            try
            {
                var config = ComHelper.GetConfigForAdapterGuid(adapterGuid);
                if (config == null)
                    return FixResult.Ok(ProblemCode.P2_IcsShareNotBound,
                        "适配器未配置 ICS 共享,无需清理。");

                config.DisableSharing();
                return FixResult.Ok(ProblemCode.P2_IcsShareNotBound,
                    $"已移除适配器 [{adapterGuid}] 的 ICS 共享配置");
            }
            catch (Exception ex)
            {
                return FixResult.Fail(ProblemCode.P2_IcsShareNotBound,
                    "移除 ICS 共享失败",
                    detail: $"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
