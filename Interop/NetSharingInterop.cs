using System.Runtime.InteropServices;

namespace CampusHotspotFix.Interop
{
    // ============================================================
    // ICS (Internet Connection Sharing) COM 互操作定义
    //
    // 重要:这些 GUID 是在 Windows 11 Build 26200 上实测得到的,
    // 与 MSDN 上旧版本的 GUID **不同**。
    // 如果要在其他 Windows 版本上使用,请先检查注册表中的实际 GUID。
    //
    // 验证命令(管理员 PowerShell):
    //   Get-ChildItem HKLM:\SOFTWARE\Classes\Interface |
    //     Where-Object {$_.PSChildName -match 'C08956|33C4643C'}
    //
    // 组件: hnetcfg.dll (Home Networking Sharing Configuration Manager)
    // ============================================================

    // ---- CLSID (组件类) ----

    /// <summary>
    /// NetSharingManager COM 类 CLSID。
    /// Windows 11 Build 26200 实测 ID。
    /// </summary>
    [ComImport]
    [Guid("5C63C1AD-3956-4FF8-8486-40034758315B")]
    internal class NetSharingManager { }


    // ---- 主接口 ----

    /// <summary>
    /// INetSharingManager — ICS 共享管理器入口。
    /// ProxyStub: OLE Automation (PSOAInterface)
    /// </summary>
    [ComImport]
    [Guid("C08956B7-1CD3-11D1-B1C5-00805FC1270E")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    internal interface INetSharingManager
    {
        [DispId(1)]
        INetSharingEveryConnectionCollection EnumEveryConnection { get; }

        [DispId(2)]
        INetSharingConfiguration INetSharingConfigurationForINetConnection(
            [MarshalAs(UnmanagedType.IUnknown)] object connection);

        [DispId(3)]
        bool SharingInstalled { get; }
    }

    /// <summary>
    /// INetSharingEveryConnectionCollection — 连接集合。
    /// ProxyStub: netshell.dll (PSFactoryBuffer)
    /// </summary>
    [ComImport]
    [Guid("33C4643C-7811-46FA-A89A-768597BD7223")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    internal interface INetSharingEveryConnectionCollection
    {
        [DispId(1)]
        int Count { get; }

        [DispId(0)]
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object Item(object index);
    }

    /// <summary>
    /// INetConnection — 网络连接 COM 接口。
    /// 来自 netcon.h 标准定义。
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

    /// <summary>
    /// INetSharingConfiguration — 单条连接的 ICS 共享配置。
    /// ProxyStub: OLE Automation (PSOAInterface)
    /// </summary>
    [ComImport]
    [Guid("C08956B6-1CD3-11D1-B1C5-00805FC1270E")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    internal interface INetSharingConfiguration
    {
        [DispId(1)]
        SharingConnectionType Type { get; }

        [DispId(2)]
        bool SharingEnabled { get; }

        [DispId(3)]
        void EnableSharing(SharingConnectionType type);

        [DispId(4)]
        void DisableSharing();
    }


    // ---- 枚举 ----

    internal enum SharingConnectionType
    {
        Private = 0,  // 专用连接(接收共享的 LAN 侧)
        Public = 1,   // 公用连接(提供互联网的 WAN 侧)
    }


    // ---- 结构体 ----

    /// <summary>
    /// NETCON_PROPERTIES — 连接属性结构体。
    /// 由 INetConnection.GetProperties() 返回,使用后需用 Marshal.FreeCoTaskMem 释放。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NetConProperties
    {
        public Guid GuidId;                             // 匹配 AdapterInfo.Id
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string DeviceName;
        public int Status;
        public int MediaType;
        public int Characteristics;
        public Guid ClsidThisObject;
        public Guid ClsidUiObject;
        [MarshalAs(UnmanagedType.LPWStr)] public string Description;
    }


    // ---- 工具 ----

    internal static class ComHelper
    {
        /// <summary>
        /// 检查系统上 ICS 功能是否可用
        /// </summary>
        internal static bool IsIcsAvailable()
        {
            try
            {
                var mgr = (INetSharingManager)new NetSharingManager();
                return mgr.SharingInstalled;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ICS COM 创建失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取指定 GUID 对应的 INetSharingConfiguration
        /// 返回 null 表示未找到匹配连接或目标不支持 ICS
        /// </summary>
        internal static INetSharingConfiguration? GetConfigForAdapterGuid(Guid targetGuid)
        {
            if (!IsIcsAvailable())
                return null;

            var mgr = (INetSharingManager)new NetSharingManager();
            var collection = mgr.EnumEveryConnection;
            int count = collection.Count;

            for (int i = 1; i <= count; i++)
            {
                try
                {
                    object rawConn = collection.Item(i);
                    if (rawConn == null) continue;

                    IntPtr pConn = Marshal.GetIUnknownForObject(rawConn);
                    try
                    {
                        Guid iid = typeof(INetConnection).GUID;
                        int hr = Marshal.QueryInterface(pConn, in iid, out IntPtr pNetConn);
                        if (hr != 0) continue;

                        var netConn = Marshal.GetObjectForIUnknown(pNetConn) as INetConnection;
                        Marshal.Release(pNetConn);

                        if (netConn == null) continue;

                        IntPtr pProps = IntPtr.Zero;
                        try
                        {
                            hr = netConn.GetProperties(out pProps);
                            if (hr != 0 || pProps == IntPtr.Zero) continue;

                            var props = Marshal.PtrToStructure<NetConProperties>(pProps);
                            if (props.GuidId == targetGuid)
                            {
                                return mgr.INetSharingConfigurationForINetConnection(rawConn);
                            }
                        }
                        finally
                        {
                            if (pProps != IntPtr.Zero)
                                Marshal.FreeCoTaskMem(pProps);
                        }
                    }
                    finally
                    {
                        Marshal.Release(pConn);
                    }
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }
    }
}
