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
        /// 对每个连接尝试设置指定类型的共享。
        /// 先试 Item(index), 不行就试 _NewEnum 手动枚举。
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

                // 收集所有原始连接对象
                var connections = new List<object>();

                // 方法1: 按索引访问 (Item/DISPID 0)
                for (int i = 1; i <= count; i++)
                {
                    try
                    {
                        object? c = collection.Item(i);
                        if (c != null) { connections.Add(c); continue; }
                    }
                    catch (Exception ex)
                    {
                        details.Add($"[Item#{i}] ✗ {ex.GetType().Name}: {ex.Message}");
                    }

                    // 方法2: 试 _NewEnum 方式 (略, 仅用方法1)
                }

                // 如果 Item 一个都没取到, 试 _NewEnum
                if (connections.Count == 0)
                {
                    try
                    {
                        dynamic rawEnum = collection._NewEnum;
                        details.Add($"_NewEnum 对象类型: {rawEnum?.GetType()}");
                    }
                    catch (Exception ex)
                    {
                        details.Add($"_NewEnum 也失败: {ex.Message}");
                    }
                }

                total = connections.Count;
                details.Add($"共获取到 {total} 个连接对象");

                foreach (var rawConn in connections)
                {
                    try
                    {
                        string label = TryGetConnLabel(rawConn);
                        details.Add($"[{label}] 尝试 {(type == SharingConnectionType.Public ? "Public" : "Private")}...");

                        dynamic config = mgr.INetSharingConfigurationForINetConnection(rawConn);

                        // 检查当前状态
                        bool alreadyEnabled;
                        try { alreadyEnabled = (bool)config.SharingEnabled; }
                        catch { alreadyEnabled = false; }

                        if (alreadyEnabled)
                        {
                            details.Add($"[{label}] 已启用, 跳过");
                            succeeded++;
                            continue;
                        }

                        config.EnableSharing((int)type);
                        succeeded++;
                        details.Add($"[{label}] ✓ 成功");
                    }
                    catch (Exception ex)
                    {
                        details.Add($"[连接] ✗ {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                details.Add($"枚举失败: {ex.Message}");
            }

            return (succeeded, total, details);
        }

        /// <summary>
        /// 尝试读取连接的友好名称(仅用于日志)
        /// </summary>
        private static string TryGetConnLabel(object rawConn)
        {
            try
            {
                dynamic p = ((dynamic)rawConn).GetProperties();
                return p.Name ?? "?";
            }
            catch
            {
                return "?";
            }
        }
    }
}
