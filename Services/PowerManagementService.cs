using CampusHotspotFix.Models;
using CampusHotspotFix.Utils;

namespace CampusHotspotFix.Services
{
    /// <summary>
    /// 电源管理服务。
    /// 修复 P5(电源/驱动超时导致约 30 分钟断连)：
    ///   - 关闭网卡的"允许计算机关闭此设备以节约电源"
    ///   - 设置电源计划为高性能,防止系统休眠导致断连
    ///   - 关闭 USB 选择性暂停
    ///
    /// 核心工具: powercfg 命令行 + WMI 网卡电源管理
    /// </summary>
    public class PowerManagementService
    {
        // 高性能电源计划 GUID (Windows 内置)
        private const string HighPerformancePlanGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        /// <summary>
        /// 执行完整的 P5 修复:
        /// 1. 激活高性能电源计划
        /// 2. 禁用系统休眠/待机(交流电下)
        /// 3. 禁用网卡电源节省(WMI)
        /// </summary>
        public FixResult DisableAllPowerSaving()
        {
            var steps = new List<FixResult>();
            int successCount = 0;

            // Step 1: 激活高性能电源计划
            var step1 = SetHighPerformancePlan();
            steps.Add(step1);
            if (step1.Success) successCount++;

            // Step 2: 禁用系统休眠/待机
            var step2 = DisableSystemSleep();
            steps.Add(step2);
            if (step2.Success) successCount++;

            // Step 3: 禁用网卡电源节省
            var step3 = DisableNicPowerSaving();
            steps.Add(step3);
            if (step3.Success) successCount++;

            // Step 4: 禁用 USB 选择性暂停
            var step4 = DisableUsbSelectiveSuspend();
            steps.Add(step4);
            if (step4.Success) successCount++;

            // 汇总结果
            bool allOk = successCount == steps.Count;
            var details = string.Join(" | ", steps.Select(s =>
                $"{(s.Success ? "✓" : "✗")} {s.Message}"));

            return allOk
                ? FixResult.Ok(ProblemCode.P5_PowerSavingDisconnect, $"电源管理修复完成。{details}")
                : FixResult.Fail(ProblemCode.P5_PowerSavingDisconnect,
                    $"电源管理修复部分完成({successCount}/{steps.Count})。",
                    detail: details);
        }

        /// <summary>
        /// 激活高性能电源计划
        /// </summary>
        public FixResult SetHighPerformancePlan()
        {
            var result = CommandRunner.Run("powercfg", $"/setactive {HighPerformancePlanGuid}");
            return result.ProcessExitedNormally
                ? FixResult.Ok(ProblemCode.P5_PowerSavingDisconnect, "已启用高性能电源计划")
                : FixResult.Fail(ProblemCode.P5_PowerSavingDisconnect, "设置高性能电源计划失败",
                    detail: result.StandardError);
        }

        /// <summary>
        /// 禁用系统休眠和待机
        /// </summary>
        public FixResult DisableSystemSleep()
        {
            var errors = new List<string>();

            // 禁用待机(交流电)
            var r1 = CommandRunner.Run("powercfg", "/change standby-timeout-ac 0");
            if (!r1.ProcessExitedNormally) errors.Add($"standby: {r1.StandardError}");

            // 禁用休眠
            var r2 = CommandRunner.Run("powercfg", "/change hibernate-timeout-ac 0");
            if (!r2.ProcessExitedNormally) errors.Add($"hibernate: {r2.StandardError}");

            if (errors.Count == 0)
                return FixResult.Ok(ProblemCode.P5_PowerSavingDisconnect, "已禁用系统休眠和待机");

            return FixResult.Fail(ProblemCode.P5_PowerSavingDisconnect,
                "部分电源设置失败", detail: string.Join("; ", errors));
        }

        /// <summary>
        /// 遍历当前活动的网络适配器,禁用其电源节省(通过 WMI 设置 PowerManagementEnabled=false)
        /// </summary>
        public FixResult DisableNicPowerSaving()
        {
            try
            {
                var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT * FROM Win32_NetworkAdapter WHERE NetEnabled = TRUE " +
                    "AND (AdapterTypeId = 0 OR AdapterTypeId = 1)");

                int found = 0, modified = 0;
                foreach (System.Management.ManagementObject adapter in searcher.Get())
                {
                    found++;
                    try
                    {
                        var powerSupported = adapter["PowerManagementSupported"];
                        if (powerSupported is bool supported && supported)
                        {
                            // SetPowerState(PowerState, Enable)
                            // 传 false,false 表示禁用电源管理
                            adapter.InvokeMethod("SetPowerState",
                                [false, false]);
                            modified++;
                        }
                    }
                    catch
                    {
                        // 单个适配器设置失败不影响其他适配器
                    }
                }

                return FixResult.Ok(ProblemCode.P5_PowerSavingDisconnect,
                    $"已处理 {modified}/{found} 个网卡的电源管理设置");
            }
            catch (System.Management.ManagementException ex)
            {
                return FixResult.Fail(ProblemCode.P5_PowerSavingDisconnect,
                    "WMI 查询失败,无法修改网卡电源设置",
                    detail: $"{ex.GetType().Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                return FixResult.Fail(ProblemCode.P5_PowerSavingDisconnect,
                    "禁用网卡电源节省时发生异常",
                    detail: $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// 禁用 USB 选择性暂停(通过 powercfg)
        /// 部分 USB 无线网卡会因此断连
        /// </summary>
        public FixResult DisableUsbSelectiveSuspend()
        {
            // USB 选择性暂停的电源设置 GUID
            const string usbSuspendGuid = "{2A40A19C-97A4-4B12-A5F9-71A8FA90A888}";
            const string subUsbGuid = "{48E6B7A5-4372-45E5-85B5-7B1E7CFF8D4F}";

            var r1 = CommandRunner.Run("powercfg",
                $"/setacvalueindex SCHEME_CURRENT {subUsbGuid} {usbSuspendGuid} 0");
            var r2 = CommandRunner.Run("powercfg",
                $"/setdcvalueindex SCHEME_CURRENT {subUsbGuid} {usbSuspendGuid} 0");

            bool ok = r1.ProcessExitedNormally && r2.ProcessExitedNormally;

            return ok
                ? FixResult.Ok(ProblemCode.P5_PowerSavingDisconnect, "已禁用 USB 选择性暂停")
                : FixResult.Fail(ProblemCode.P5_PowerSavingDisconnect, "禁用 USB 选择性暂停失败",
                    detail: r1.StandardError + r2.StandardError);
        }
    }
}
