using System.Runtime.InteropServices;

namespace CampusHotspotFix.Interop
{
    // ============================================================
    // ICS (Internet Connection Sharing) COM 互操作定义
    //
    // 组件: hnetcfg.dll / NetSharingManager
    //
    // 调用流程:
    //   1. 实例化 NetSharingManager
    //   2. EnumEveryConnection → 枚举所有连接
    //   3. 按 GUID 匹配目标适配器
    //   4. 获取 INetSharingConfiguration
    //   5. EnableSharing(Public/Private) → 绑定 ICS
    //
    // 参考资料: Windows SDK netcfgn.h / netcon.h
    // ============================================================

    [ComImport]
    [Guid("C1A400A4-3E45-11D2-A870-00C04FB990A2")]
    internal class NetSharingManager { }

    [ComImport]
    [Guid("C1A400A0-3E45-11D2-A870-00C04FB990A2")]
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

    [ComImport]
    [Guid("E707C1E0-27C0-11D2-A8C0-00C04FB990A2")]
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
    /// INetConnection — 网络连接 COM 接口
    /// 用于获取连接 GUID 以匹配 AdapterInfo.Id
    /// </summary>
    [ComImport]
    [Guid("C08956A0-1CD3-11D1-B1C5-00805FC1270E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface INetConnection
    {
        // 不用的方法定义为 PreserveSig 占位保持 vtable 顺序
        [PreserveSig] int Connect();
        [PreserveSig] int Disconnect();
        [PreserveSig] int GetProperties(out IntPtr ppProps);
    }

    [ComImport]
    [Guid("C5E8E7D0-2520-11D2-A8C4-00C04FB990A2")]
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

    internal enum SharingConnectionType
    {
        Private = 0,  // 专用连接(接收共享的 LAN 侧)
        Public = 1,   // 公用连接(提供互联网的 WAN 侧)
    }

    /// <summary>
    /// NETCON_PROPERTIES — 连接属性结构体
    /// 必须保持字段顺序与 C 结构体完全一致
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

    /// <summary>
    /// ComHelper — 简化 COM 枚举与属性读取
    /// </summary>
    internal static class ComHelper
    {
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
                        // QI for INetConnection
                        Guid iid = typeof(INetConnection).GUID;
                        int hr = Marshal.QueryInterface(pConn, in iid, out IntPtr pNetConn);
                        if (hr != 0) continue;

                        var netConn = Marshal.GetObjectForIUnknown(pNetConn) as INetConnection;
                        Marshal.Release(pNetConn);

                        if (netConn == null) continue;

                        // 读取 GUID
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

        internal static bool IsIcsAvailable()
        {
            try
            {
                var mgr = (INetSharingManager)new NetSharingManager();
                return mgr.SharingInstalled;
            }
            catch
            {
                return false;
            }
        }
    }
}
