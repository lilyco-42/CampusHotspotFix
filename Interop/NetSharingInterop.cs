using System.Runtime.InteropServices;

namespace CampusHotspotFix.Interop
{
    internal enum SharingConnectionType
    {
        Private = 0,
        Public = 1,
    }

    // IEnumVARIANT — 用于手动枚举 COM 集合
    [ComImport]
    [Guid("00020404-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumVARIANT
    {
        [PreserveSig] int Next(int celt, IntPtr rgvar, out int pceltFetched);
        [PreserveSig] int Skip(int celt);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumVARIANT ppenum);
    }

    internal static class ComHelper
    {
        private static readonly Guid ClsidNetSharingMgr = new("5C63C1AD-3956-4FF8-8486-40034758315B");
        private static Type? _mgrType;

        private static Type MgrType =>
            _mgrType ??= Type.GetTypeFromCLSID(ClsidNetSharingMgr)
                ?? throw new InvalidOperationException($"NetSharingManager CLSID 不可用");

        private static dynamic CreateManager()
            => Activator.CreateInstance(MgrType)!;

        // ==================== 公开 API ====================

        internal static bool IsIcsAvailable()
        {
            try { return (bool)CreateManager().SharingInstalled; }
            catch { return false; }
        }

        internal static (bool Available, string? ErrorMessage, string? ErrorDetail) DiagnoseIcs()
        {
            try { return ((bool)CreateManager().SharingInstalled, null, null); }
            catch (Exception ex) { return (false, $"{ex.GetType().Name}: {ex.Message}", ex.ToString()); }
        }

        internal static (int Succeeded, int Total, List<string> Details) TrySetPublicOnAll()
            => TrySetSharingOnAll(SharingConnectionType.Public);

        internal static (int Succeeded, int Total, List<string> Details) TrySetPrivateOnAll()
            => TrySetSharingOnAll(SharingConnectionType.Private);

        private static (int Succeeded, int Total, List<string> Details) TrySetSharingOnAll(SharingConnectionType type)
        {
            var details = new List<string>();
            int succeeded = 0;
            var connections = new List<object>();

            try
            {
                dynamic mgr = CreateManager();
                dynamic collection = mgr.EnumEveryConnection;
                int count = (int)collection.Count;
                details.Add($"共 {count} 个连接");

                // 枚举: 通过 _NewEnum → IEnumVARIANT
                connections = EnumConnectionsViaNewEnum(collection, details);
                details.Add($"实际取到 {connections.Count} 个连接对象");

                foreach (var rawConn in connections)
                {
                    try
                    {
                        details.Add($"  尝试 {(type == SharingConnectionType.Public ? "Public" : "Private")}...");
                        dynamic config = mgr.INetSharingConfigurationForINetConnection(rawConn);
                        config.EnableSharing((int)type);
                        succeeded++;
                        details.Add($"  ✓ 成功");
                    }
                    catch (Exception ex)
                    {
                        details.Add($"  ✗ {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                details.Add($"枚举失败: {ex.Message}");
            }

            return (succeeded, connections.Count, details);
        }

        /// <summary>
        /// 通过 _NewEnum → IEnumVARIANT 枚举集合中的连接
        /// </summary>
        private static List<object> EnumConnectionsViaNewEnum(dynamic collection, List<string> log)
        {
            var result = new List<object>();

            try
            {
                object? newEnumObj;
                try
                {
                    newEnumObj = collection._NewEnum;
                }
                catch (Exception ex)
                {
                    log.Add($"_NewEnum 不可用: {ex.Message}");
                    return result;
                }

                if (newEnumObj == null)
                {
                    log.Add("_NewEnum 返回 null");
                    return result;
                }

                // QI: _NewEnum (IUnknown) → IEnumVARIANT
                IntPtr pUnk = Marshal.GetIUnknownForObject(newEnumObj);
                try
                {
                    Guid iidEnumVariant = typeof(IEnumVARIANT).GUID;
                    int hr = Marshal.QueryInterface(pUnk, in iidEnumVariant, out IntPtr pEnum);
                    if (hr != 0)
                    {
                        log.Add($"QI IEnumVARIANT 失败 hr=0x{hr:X8}");
                        return result;
                    }

                    try
                    {
                        var enumVar = Marshal.GetObjectForIUnknown(pEnum) as IEnumVARIANT;
                        if (enumVar == null)
                        {
                            log.Add("IEnumVARIANT 转换失败");
                            return result;
                        }

                        // 迭代
                        const int batchSize = 1;
                        while (true)
                        {
                            IntPtr variant = Marshal.AllocCoTaskMem(16); // VARIANT = 16 bytes
                            try
                            {
                                int fetched;
                                hr = enumVar.Next(batchSize, variant, out fetched);

                                if (hr != 0 || fetched == 0)
                                    break; // S_FALSE or error → 枚举结束

                                object? obj = Marshal.GetObjectForNativeVariant(variant);
                                if (obj != null)
                                    result.Add(obj);
                            }
                            finally
                            {
                                // 清除 VARIANT (释放 BSTR 等)
                                try { Marshal.DestroyStructure(variant, typeof(InnerVariant)); }
                                catch { }
                                Marshal.FreeCoTaskMem(variant);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.Release(pEnum);
                    }
                }
                finally
                {
                    Marshal.Release(pUnk);
                }
            }
            catch (Exception ex)
            {
                log.Add($"枚举异常: {ex.Message}");
            }

            return result;
        }

        // 仅供 DestroyStructure 使用的占位类型
        [StructLayout(LayoutKind.Sequential)]
        private struct InnerVariant
        {
            public ushort vt;
            public ushort reserved1;
            public ushort reserved2;
            public ushort reserved3;
            public IntPtr data1;
            public IntPtr data2;
        }
    }
}
