using System.Diagnostics;

namespace CampusHotspotFix.Services
{
    /// <summary>
    /// Windows 移动热点服务。
    /// 方法:
    ///   1. 自动: 通过 PowerShell 调用 WinRT API 尝试开启
    ///   2. 手动: 启动 ms-settings:network-mobilehotspot 设置页面
    /// </summary>
    public class MobileHotspotService
    {
        /// <summary>
        /// 启动移动热点(通过 PowerShell 调用 WinRT API)
        /// </summary>
        public (bool Success, string Message) StartHotspot()
        {
            var script = @"
$null = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager,Windows.Networking.NetworkOperators,ContentType=WindowsRuntime]
$profile = [Windows.Networking.Connectivity.NetworkInformation]::GetInternetConnectionProfile()
$mgr = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager]::CreateFromConnectionProfile($profile)
$asyncOp = $mgr.StartTetheringAsync()
$asyncOp.AsTask().GetAwaiter().GetResult()
";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{script.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi)!;
                string output = proc.StandardOutput.ReadToEnd();
                string error = proc.StandardError.ReadToEnd();
                proc.WaitForExit(15000);

                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    return (true, "移动热点已开启");
                else
                    return (false, $"PowerShell 启动失败: {error.Trim()}");
            }
            catch (Exception ex)
            {
                return (false, $"异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 检测移动热点是否已开启(通过 PowerShell)
        /// </summary>
        public bool IsHotspotRunning()
        {
            var script = @"
$null = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager,Windows.Networking.NetworkOperators,ContentType=WindowsRuntime]
$profile = [Windows.Networking.Connectivity.NetworkInformation]::GetInternetConnectionProfile()
$mgr = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager]::CreateFromConnectionProfile($profile)
if ($mgr.TetheringOperationalState -eq 1) { Write-Host 'ON' }
";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{script.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi)!;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                return output.Trim().Equals("ON", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检测移动热点硬件/系统是否支持
        /// </summary>
        public bool IsHotspotSupported()
        {
            var script = @"
$null = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager,Windows.Networking.NetworkOperators,ContentType=WindowsRuntime]
$profile = [Windows.Networking.Connectivity.NetworkInformation]::GetInternetConnectionProfile()
$mgr = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager]::CreateFromConnectionProfile($profile)
$cap = $mgr.TetheringCapability
if ($cap -eq 0) { Write-Host 'SUPPORTED' }
";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{script.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi)!;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                return output.Trim().Equals("SUPPORTED", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 打开 Windows 设置 → 移动热点 页面(引导用户手动开启)
        /// </summary>
        public static void OpenSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:network-mobilehotspot",
                    UseShellExecute = true,
                });
            }
            catch { }
        }
    }
}
