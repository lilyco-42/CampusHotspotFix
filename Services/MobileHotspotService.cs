using System.Diagnostics;

namespace CampusHotspotFix.Services
{
    /// <summary>
    /// Windows 移动热点服务
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
# Wait for async operation to complete
while ($asyncOp.Status -eq 3) { Start-Sleep -Milliseconds 200 }
$result = $asyncOp.GetResults()
[int]$status = $result.Status
if ($status -eq 0) { Write-Host 'OK' } else { Write-Host ('FAIL:' + $status) }
";
            return RunPowerShell(script, "OK", "移动热点已开启", "启动移动热点失败");
        }

        /// <summary>
        /// 检测移动热点是否已开启
        /// </summary>
        public bool IsHotspotRunning()
        {
            var script = @"
$null = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager,Windows.Networking.NetworkOperators,ContentType=WindowsRuntime]
$profile = [Windows.Networking.Connectivity.NetworkInformation]::GetInternetConnectionProfile()
$mgr = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager]::CreateFromConnectionProfile($profile)
[int]$state = $mgr.TetheringOperationalState
if ($state -eq 1) { Write-Host 'ON' } else { Write-Host 'OFF' }
";
            try
            {
                var output = RunPowerShellRaw(script);
                return output.Trim().Equals("ON", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
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
[int]$cap = $mgr.TetheringCapability
# 0 = Enabled (TetheringCapability.Enabled)
if ($cap -eq 0) { Write-Host 'SUPPORTED' } else { Write-Host ('NOT:' + $cap) }
";
            try
            {
                var output = RunPowerShellRaw(script);
                return output.Trim().Equals("SUPPORTED", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// 获取移动热点的 SSID 和密码
        /// </summary>
        public (string Ssid, string Password) GetHotspotCredentials()
        {
            var script = @"
$null = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager,Windows.Networking.NetworkOperators,ContentType=WindowsRuntime]
$profile = [Windows.Networking.Connectivity.NetworkInformation]::GetInternetConnectionProfile()
$mgr = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager]::CreateFromConnectionProfile($profile)
$cfg = $mgr.GetCurrentAccessPointConfiguration()
Write-Host $cfg.Ssid
Write-Host $cfg.Passphrase
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
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                };
                using var proc = Process.Start(psi)!;
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(5000);
                var lines = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length >= 2)
                    return (lines[0], lines[1]);
                return ("(未知)", "(未知)");
            }
            catch
            {
                return ("(获取失败)", "");
            }
        }

        /// <summary>
        /// 打开 Windows 设置 → 移动热点 页面
        /// </summary>
        public static void OpenSettings()
        {
            try { Process.Start(new ProcessStartInfo { FileName = "ms-settings:network-mobilehotspot", UseShellExecute = true }); }
            catch { }
        }

        // ---- 辅助方法 ----

        private (bool Success, string Message) RunPowerShell(string script, string successMarker, string successMsg, string failPrefix)
        {
            try
            {
                var (output, error) = RunPowerShellWithError(script);
                if (output.Trim().Contains(successMarker))
                    return (true, successMsg);
                return (false, $"{failPrefix}: {(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim())}");
            }
            catch (Exception ex)
            {
                return (false, $"异常: {ex.Message}");
            }
        }

        private string RunPowerShellRaw(string script)
        {
            var (output, _) = RunPowerShellWithError(script);
            return output;
        }

        private (string Stdout, string Stderr) RunPowerShellWithError(string script)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{script.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            using var proc = Process.Start(psi)!;
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(15000);
            return (stdout, stderr);
        }
    }
}
