using System.Runtime.InteropServices;

namespace CampusHotspotFix.Interop
{
    // ============================================================
    // ICS (Internet Connection Sharing) COM 互操作
    //
    // 全部使用 dynamic 模式调用,避免 [ComImport] 接口定义
    // 与当前系统 COM 接口 vtable 不匹配的问题。
    //
    // CLSID: Windows 11 Build 26200 实测
    // ============================================================

    // ============================================================
    // INetConnection — IUnknown 接口, vtable 稳定
    // 用于 QI 后读取连接属性(GUID),从而匹配 AdapterInfo.Id
    // ============================================================

    /// <summary>NETCON_PROPERTIES 结构体</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NetConProperties
    {
        public Guid GuidId;
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string DeviceName;
        public int Status;
        public int MediaType;
        public int Characteristics;
        public Guid ClsidThisObject;
        public Guid ClsidUiObject;
        [MarshalAs(UnmanagedType.LPWStr)] public string Description;
    }

    /// <summary>
    /// INetConnection — IUnknown 接口,稳定 vtable
    /// 来自 Windows SDK netcon.h (GUID 自 Windows 2000 以来未变)
    /// </summary>
    [ComImport]
    [Guid("C08956A0-1CD3-11D1-B1C5-00805FC1270E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface INetConnection
    {
        [PreserveSig] int Connect();
        [PreserveSig] int Disconnect();
        [PreserveSig] int GetProperties(out IntPtr ppProps);
    }

    internal enum SharingConnectionType
    {
        Private = 0,
        Public = 1,
    }

    internal static class ComHelper
    {
        private static readonly Guid ClsidNetSharingMgr = new("5C63C1AD-3956-4FF8-8486-40034758315B");
        private static readonly Guid IidNetConnection = typeof(INetConnection).GUID;
        private static Type? _mgrType;

        private static Type MgrType =>
            _mgrType ??= Type.GetTypeFromCLSID(ClsidNetSharingMgr)
                ?? throw new InvalidOperationException($"NetSharingManager CLSID {ClsidNetSharingMgr} 不可用");

        private static dynamic CreateManager() => Activator.CreateInstance(MgrType)!;

        // ---- 公开 API ----

        internal static bool IsIcsAvailable()
        {
            try
            {
                dynamic mgr = CreateManager();
                return (bool)mgr.SharingInstalled;
            }
            catch
            {
                return false;
            }
        }

        internal static (bool Available, string? ErrorMessage, string? ErrorDetail) DiagnoseIcs()
        {
            try
            {
                dynamic mgr = CreateManager();
                bool installed = (bool)mgr.SharingInstalled;
                return (installed, null, null);
            }
            catch (Exception ex)
            {
                return (false, $"{ex.GetType().Name}: {ex.Message}", ex.ToString());
            }
        }

        /// <summary>
        /// 枚举所有连接,通过 QI → INetConnection.GetProperties()
        /// 读取 GUID,匹配后返回其 INetSharingConfiguration
        /// </summary>
        internal static dynamic? GetConfigForAdapterGuid(Guid targetGuid)
        {
            if (!IsIcsAvailable())
                return null;

            dynamic mgr = CreateManager();
            dynamic collection = mgr.EnumEveryConnection;
            int count = (int)collection.Count;

            for (int i = 1; i <= count; i++)
            {
                try
                {
                    dynamic rawConn = collection.Item(i);
                    if (rawConn == null) continue;

                    // QI: rawConn (IUnknown) → INetConnection
                    IntPtr pUnk = Marshal.GetIUnknownForObject(rawConn);
                    try
                    {
                        int hr = Marshal.QueryInterface(pUnk, in IidNetConnection, out IntPtr pConn);
                        if (hr != 0) continue;

                        try
                        {
                            var netConn = Marshal.GetObjectForIUnknown(pConn) as INetConnection;
                            if (netConn == null) continue;

                            hr = netConn.GetProperties(out IntPtr pProps);
                            if (hr != 0 || pProps == IntPtr.Zero) continue;

                            try
                            {
                                var props = Marshal.PtrToStructure<NetConProperties>(pProps);
                                if (props.GuidId == targetGuid)
                                {
                                    return mgr.INetSharingConfigurationForINetConnection(rawConn);
                                }
                            }
                            finally
                            {
                                Marshal.FreeCoTaskMem(pProps);
                            }
                        }
                        finally
                        {
                            Marshal.Release(pConn);
                        }
                    }
                    finally
                    {
                        Marshal.Release(pUnk);
                    }
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }

        // ---- 业务方法 ----

        internal static (bool Success, string Message, string? Detail) SetPublicConnection(Guid adapterGuid)
        {
            return SetSharing(adapterGuid, SharingConnectionType.Public);
        }

        internal static (bool Success, string Message, string? Detail) SetPrivateConnection(Guid adapterGuid)
        {
            return SetSharing(adapterGuid, SharingConnectionType.Private);
        }

        internal static (bool Success, string Message, string? Detail) DisableSharing(Guid adapterGuid)
        {
            try
            {
                dynamic? config = GetConfigForAdapterGuid(adapterGuid);
                if (config == null)
                    return (true, "适配器未配置 ICS 共享,无需清理", null);

                config.DisableSharing();
                return (true, $"已移除 ICS 共享配置", null);
            }
            catch (Exception ex)
            {
                return (false, "移除 ICS 共享失败", $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static (bool Success, string Message, string? Detail) SetSharing(Guid adapterGuid, SharingConnectionType type)
        {
            try
            {
                dynamic? config = GetConfigForAdapterGuid(adapterGuid);
                if (config == null)
                    return (false, $"未找到适配器 [{adapterGuid}] 的 ICS 配置对象", null);

                config.EnableSharing((int)type);
                return (true, $"已设为 {(type == SharingConnectionType.Public ? "公用" : "专用")} 连接", null);
            }
            catch (UnauthorizedAccessException ex)
            {
                return (false, "权限不足: 需要管理员权限", ex.Message);
            }
            catch (Exception ex)
            {
                return (false, $"设置 ICS 共享失败", $"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
