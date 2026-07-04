using System.Runtime.InteropServices;

namespace CampusHotspotFix.Interop
{
    // ============================================================
    // ICS (Internet Connection Sharing) COM 互操作
    // CLSID: Windows 11 Build 26200 实测
    //
    // 策略: INetSharingManager 用 dynamic (IDispatch 双接口)
    //       连接枚举和 GUID 匹配改为「对所有连接尝试 EnableSharing」
    //       避免了 INetConnection QI 的兼容问题
    // ============================================================

    internal enum SharingConnectionType
    {
        Private = 0,
        Public = 1,
    }

    internal static class ComHelper
    {
        private static readonly Guid ClsidNetSharingMgr = new("5C63C1AD-3956-4FF8-8486-40034758315B");
        private static Type? _mgrType;

        private static Type MgrType =>
            _mgrType ??= Type.GetTypeFromCLSID(ClsidNetSharingMgr)
                ?? throw new InvalidOperationException($"NetSharingManager CLSID {ClsidNetSharingMgr} 不可用");

        private static dynamic CreateManager()
        {
            var mgr = Activator.CreateInstance(MgrType);
            return mgr!;
        }

        // ==================== 公开 API ====================

        internal static bool IsIcsAvailable()
        {
            try { return (bool)CreateManager().SharingInstalled; }
            catch { return false; }
        }

        internal static (bool Available, string? ErrorMessage, string? ErrorDetail) DiagnoseIcs()
        {
            try
            {
                bool installed = (bool)CreateManager().SharingInstalled;
                return (installed, null, null);
            }
            catch (Exception ex) { return (false, $"{ex.GetType().Name}: {ex.Message}", ex.ToString()); }
        }

        /// <summary>
        /// 枚举所有 COM 连接,对每个连接尝试 EnableSharing(Public)。
        /// 返回(成功数, 总数, 详情列表)
        /// </summary>
        internal static (int Succeeded, int Total, List<string> Details) TrySetPublicOnAll()
        {
            return TrySetSharingOnAll(SharingConnectionType.Public);
        }

        /// <summary>
        /// 枚举所有 COM 连接,对每个连接尝试 EnableSharing(Private)。
        /// </summary>
        internal static (int Succeeded, int Total, List<string> Details) TrySetPrivateOnAll()
        {
            return TrySetSharingOnAll(SharingConnectionType.Private);
        }

        /// <summary>
        /// 对每个连接尝试设置指定类型的共享 —— 不依赖 GUID 匹配。
        /// </summary>
        private static (int Succeeded, int Total, List<string> Details) TrySetSharingOnAll(SharingConnectionType type)
        {
            var details = new List<string>();
            int succeeded = 0, total = 0;

            try
            {
                dynamic mgr = CreateManager();
                dynamic collection = mgr.EnumEveryConnection;
                int count = (int)collection.Count;
                details.Add($"共 {count} 个连接");

                for (int i = 1; i <= count; i++)
                {
                    total++;
                    try
                    {
                        object? rawConn = collection.Item(i);
                        if (rawConn == null)
                        {
                            details.Add($"[{i}] null");
                            continue;
                        }

                        details.Add($"[{i}] 尝试设置 {(type == SharingConnectionType.Public ? "Public" : "Private")}...");
                        dynamic config = mgr.INetSharingConfigurationForINetConnection(rawConn);
                        config.EnableSharing((int)type);
                        succeeded++;
                        details.Add($"[{i}] ✓ 成功");
                    }
                    catch (Exception ex)
                    {
                        details.Add($"[{i}] ✗ {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                details.Add($"枚举连接集合失败: {ex.Message}");
            }

            return (succeeded, total, details);
        }
    }
}
