using CampusHotspotFix.Models;
using CampusHotspotFix.Services;

namespace CampusHotspotFix.Forms
{
    /// <summary>
    /// 主界面 — MVP 版本。
    /// "诊断"→"一键修复"→ 查看结果的简单流程。
    /// </summary>
    public class MainForm : Form
    {
        private readonly NetworkAdapterService _adapterService = new();
        private readonly HostedNetworkService _hostedNetworkService = new();
        private readonly IcsShareService _icsShareService = new();
        private readonly PowerManagementService _powerManagementService = new();

        private readonly TextBox _outputBox;
        private readonly TextBox _ssidBox;
        private readonly TextBox _keyBox;
        private readonly Button _diagnoseButton;
        private readonly Button _fixButton;
        private readonly Button _generateButton;
        private readonly Button _stopButton;
        private readonly Label _statusLabel;

        private CancellationTokenSource? _fixCts;

        public MainForm()
        {
            Text = "校园宽带热点修复工具";
            Width = 750;
            Height = 600;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9);
            BackColor = Color.White;

            // ---- 标题 ----
            var titleLabel = new Label
            {
                Text = "校园宽带热点修复工具 v1.0",
                Font = new Font("Microsoft YaHei UI", 14, FontStyle.Bold),
            };
            titleLabel.SetBounds(20, 12, 400, 30);
            Controls.Add(titleLabel);

            // ---- 热点参数区域 ----
            var ssidLabel = new Label { Text = "热点名称:" };
            ssidLabel.SetBounds(20, 55, 75, 24);
            Controls.Add(ssidLabel);

            _ssidBox = new TextBox { Text = $"CampusHotspot_{Environment.MachineName}" };
            _ssidBox.SetBounds(95, 53, 260, 24);
            Controls.Add(_ssidBox);

            var keyLabel = new Label { Text = "热点密码:" };
            keyLabel.SetBounds(20, 85, 75, 24);
            Controls.Add(keyLabel);

            _keyBox = new TextBox { Text = GenerateRandomPassword(12), PasswordChar = '*' };
            _keyBox.SetBounds(95, 83, 260, 24);
            Controls.Add(_keyBox);

            _generateButton = new Button { Text = "随机密码" };
            _generateButton.SetBounds(365, 82, 90, 26);
            _generateButton.Click += (_, _) => _keyBox.Text = GenerateRandomPassword(12);
            Controls.Add(_generateButton);

            // ---- 状态栏 ----
            _statusLabel = new Label
            {
                Text = "就绪",
                ForeColor = Color.Gray,
            };
            _statusLabel.SetBounds(20, 118, 400, 20);
            Controls.Add(_statusLabel);

            // ---- 按钮 ----
            _diagnoseButton = new Button
            {
                Text = "🔍 诊断网络状态",
                BackColor = Color.FromArgb(0xE8, 0xF0, 0xFE),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
            };
            _diagnoseButton.SetBounds(20, 145, 150, 38);
            _diagnoseButton.Click += OnDiagnoseClicked;
            Controls.Add(_diagnoseButton);

            _fixButton = new Button
            {
                Text = "🔧 一键修复",
                BackColor = Color.FromArgb(0x00, 0x7A, 0xCC),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand,
            };
            _fixButton.SetBounds(185, 145, 150, 38);
            _fixButton.Click += OnFixClicked;
            Controls.Add(_fixButton);

            _stopButton = new Button
            {
                Text = "■ 停止",
                BackColor = Color.FromArgb(0xFF, 0x44, 0x44),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Visible = false,
            };
            _stopButton.SetBounds(350, 145, 100, 38);
            _stopButton.Click += (_, _) => _fixCts?.Cancel();
            Controls.Add(_stopButton);

            // ---- 输出区域 ----
            _outputBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.FromArgb(0x1E, 0x1E, 0x2E),
                ForeColor = Color.FromArgb(0xD4, 0xD4, 0xD4),
                Font = new Font("Consolas", 9),
            };
            _outputBox.SetBounds(20, 195, 700, 350);
            Controls.Add(_outputBox);

            AppendOutput("校园宽带热点修复工具 v1.0 (MVP)\r\n");
            AppendOutput("点击「诊断网络状态」检测系统状态, 或直接点击「一键修复」自动修复。\r\n");
        }

        // ---- 诊断 ----

        private async void OnDiagnoseClicked(object? sender, EventArgs e)
        {
            _diagnoseButton.Enabled = false;
            SetStatus("诊断中...", Color.FromArgb(0x00, 0x7A, 0xCC));

            try
            {
                await Task.Run(() => RunDiagnose());
            }
            catch (Exception ex)
            {
                AppendOutput($"[错误] 诊断过程异常: {ex.Message}");
            }
            finally
            {
                _diagnoseButton.Enabled = true;
                SetStatus("就绪", Color.Gray);
            }
        }

        private void RunDiagnose()
        {
            // 1. 检测承载网络支持
            var supported = _adapterService.IsHostedNetworkSupported(out var rawDriver);
            string supportText = supported switch
            {
                true => "✅ 支持承载网络",
                false => "❌ 不支持承载网络",
                null => "⚠️ 无法确定(请查看原始 netsh 输出)",
            };
            SafeAppend($"[P1] 驱动检测: {supportText}");

            // 如果正则未匹配到,显示 raw netsh 输出片段供排查
            if (supported == null && !string.IsNullOrWhiteSpace(rawDriver))
            {
                SafeAppend("── netsh wlan show drivers 关键片段 ──");
                // 取包含 "Hosted"、"承载"、"Virtual"、"支持" 的行
                foreach (var line in rawDriver.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Contains("Hosted", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("承载", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("支持", StringComparison.OrdinalIgnoreCase))
                    {
                        SafeAppend($"  {trimmed}");
                    }
                }
                SafeAppend("─────────────────────────");
            }

            // 2. 枚举真实适配器(已过滤 NDIS 过滤器等噪音)
            var realAdapters = _adapterService.GetRealAdapters();
            SafeAppend($"[信息] 真实网络接口共 {realAdapters.Count} 个:");

            foreach (var a in realAdapters)
            {
                string status = a.IsUp ? "🟢" : "⚪";
                string tag = a.IsHostedNetworkVirtualAdapter ? "(虚拟热点)" : "";
                SafeAppend($"  {status} {a.Name} {tag} | {a.InterfaceType}");
            }

            // 可选:显示全部适配器(含过滤器),但折叠
            var allAdapters = _adapterService.GetAllAdapters(includeFilterDrivers: true);
            int filterCount = allAdapters.Count - realAdapters.Count;
            if (filterCount > 0)
            {
                SafeAppend($"  (已忽略 {filterCount} 个 NDIS 过滤器/协议驱动)");
            }

            // 3. 检测 PPPoE 拨号
            var pppoe = _adapterService.GetDialupAdapters();
            if (pppoe.Count == 0)
            {
                SafeAppend("[P2] ⚠️ 未检测到活动的 PPPoE 拨号连接,请先拨号上网");
            }
            else
            {
                SafeAppend($"[P2] ✅ 检测到 {pppoe.Count} 个拨号连接: {pppoe[0].Name} | ID={pppoe[0].Id}");
            }

            // 4. 检测虚拟热点
            var virtualAdapters = _adapterService.GetHostedNetworkAdapters();
            if (virtualAdapters.Count == 0)
            {
                SafeAppend("[P1] ℹ️ 虚拟热点适配器未出现(需先创建热点)");
            }
            else
            {
                SafeAppend($"[P1] ✅ 虚拟热点适配器已存在: {virtualAdapters[0].Name} | ID={virtualAdapters[0].Id}");
            }

            // 5. 检测热点状态
            var hotspot = _hostedNetworkService.QueryStatus();
            SafeAppend($"[P1] 热点状态: {hotspot.Status} | 已连接客户端: {hotspot.ConnectedClientCount}");

            // 6. 检测 ICS 可用性 —— 带详细错误
            try
            {
                bool icsOk = _icsShareService.IsIcsAvailableOnSystem();
                SafeAppend(icsOk
                    ? "[P2] ✅ ICS 共享组件可用"
                    : "[P2] ⚠️ ICS 共享组件不可用");
                if (!icsOk)
                {
                    SafeAppend("  原因: INetSharingManager COM 组件创建失败。常见原因:");
                    SafeAppend("  - 系统为 Windows N/KN/LTSC 精简版");
                    SafeAppend("  - hnetcfg.dll 未注册 (尝试: regsvr32 hnetcfg.dll)");
                }
            }
            catch (Exception ex)
            {
                SafeAppend($"[P2] ⚠️ ICS 检测异常: {ex.GetType().Name}: {ex.Message}");
            }

            // 7. 电源计划
            SafeAppend("[P5] 电源管理配置将在「一键修复」时自动处理");
        }

        // ---- 一键修复 ----

        private async void OnFixClicked(object? sender, EventArgs e)
        {
            if (_fixCts != null)
            {
                AppendOutput("[提示] 修复任务已在运行中");
                return;
            }

            string ssid = _ssidBox.Text.Trim();
            string key = _keyBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(ssid))
            {
                AppendOutput("[错误] 请输入热点名称");
                return;
            }
            if (key.Length < 8)
            {
                AppendOutput("[错误] 热点密码至少需要 8 个字符");
                return;
            }

            _fixCts = new CancellationTokenSource();
            var token = _fixCts.Token;

            SetControlsDuringFix(false);
            SetStatus("修复中...", Color.FromArgb(0x00, 0x7A, 0xCC));
            AppendOutput("========== 开始一键修复 ==========");

            try
            {
                await Task.Run(() => RunFix(ssid, key, token), token);
                SetStatus("修复完成", Color.Green);
            }
            catch (OperationCanceledException)
            {
                AppendOutput("[已取消] 用户中断修复流程");
                SetStatus("已取消", Color.Orange);
            }
            catch (Exception ex)
            {
                AppendOutput($"[错误] 修复过程异常: {ex.Message}");
                SetStatus("修复出错", Color.Red);
            }
            finally
            {
                _fixCts.Dispose();
                _fixCts = null;
                SetControlsDuringFix(true);
            }
        }

        private void RunFix(string ssid, string key, CancellationToken token)
        {
            var results = new List<FixResult>();

            // Step 1: 检测承载网络支持
            SafeAppend("[步骤 1/5] 检测网卡驱动...");
            var supported = _adapterService.IsHostedNetworkSupported(out _);
            if (supported != true)
            {
                SafeAppend("[P1] ❌ 网卡不支持承载网络,无法创建热点");
                SafeAppend("[建议] 回滚网卡驱动版本,或使用外置 USB 无线网卡");
                return;
            }
            SafeAppend("[P1] ✅ 网卡支持承载网络");
            token.ThrowIfCancellationRequested();

            // Step 2: 创建并启动虚拟热点
            SafeAppend("[步骤 2/5] 创建虚拟热点...");
            var enableResult = _hostedNetworkService.Enable(ssid, key);
            results.Add(enableResult);
            SafeAppend(enableResult.Success
                ? $"[P1] ✅ 虚拟热点已启动 (SSID: {ssid})"
                : $"[P1] ❌ 启动失败: {enableResult.Message}");

            // 即使已创建过也继续
            token.ThrowIfCancellationRequested();

            // 等待虚拟适配器出现
            SafeAppend("[信息] 等待虚拟适配器初始化...");
            Thread.Sleep(3000);

            // Step 3: 查找适配器并绑定 ICS
            SafeAppend("[步骤 3/5] 绑定 ICS 共享...");

            var pppoeAdapters = _adapterService.GetDialupAdapters();
            var virtualAdapters = _adapterService.GetHostedNetworkAdapters();

            if (pppoeAdapters.Count == 0)
            {
                SafeAppend("[P2] ❌ 未找到 PPPoE 拨号连接,请先拨号后再试");
            }
            else if (virtualAdapters.Count == 0)
            {
                SafeAppend("[P2] ❌ 虚拟热点适配器未出现,请检查驱动或重新创建");
            }
            else
            {
                var pppoeGuid = Guid.Parse(pppoeAdapters[0].Id);
                var virtualGuid = Guid.Parse(virtualAdapters[0].Id);

                SafeAppend($"[信息] PPPoE 适配器 GUID: {pppoeGuid}");
                SafeAppend($"[信息] 虚拟热点适配器 GUID: {virtualGuid}");

                var icsResults = _icsShareService.BindSharing(pppoeGuid, virtualGuid);
                foreach (var (guid, result) in icsResults)
                {
                    results.Add(result);
                    SafeAppend(result.Success
                        ? $"[P2] ✅ ICS 绑定成功: {result.Message}"
                        : $"[P2] ❌ {result.Message}");
                }

                token.ThrowIfCancellationRequested();
            }

            // Step 4: 电源管理优化
            SafeAppend("[步骤 4/5] 优化电源管理...");
            var powerResult = _powerManagementService.DisableAllPowerSaving();
            results.Add(powerResult);
            SafeAppend(powerResult.Success
                ? $"[P5] ✅ {powerResult.Message}"
                : $"[P5] ⚠️ {powerResult.Message}");

            token.ThrowIfCancellationRequested();

            // Step 5: 汇总
            SafeAppend("[步骤 5/5] 生成结果汇总...");
            int successCount = results.Count(r => r.Success);
            int totalCount = results.Count;

            SafeAppend("");
            SafeAppend("========== 修复结果汇总 ==========");
            SafeAppend($"成功: {successCount}/{totalCount} 项");
            SafeAppend("");

            if (successCount == totalCount)
            {
                SafeAppend("🎉 全部修复完成! 现在可以尝试用手机连接热点测试上网。");
                SafeAppend("⚠️ 如重启后配置失效,请在完成全部修复后使用任务计划功能(待实现)。");
            }
            else if (successCount >= totalCount - 2)
            {
                SafeAppend("大部分项目已修复,请检查以上失败项。");
            }
            else
            {
                SafeAppend("部分项目修复失败,请根据错误信息排查。");
            }

            SafeAppend($"热点名称: {ssid}");
            SafeAppend($"热点密码: {key}");
        }

        // ---- UI 辅助 ----

        private void SetControlsDuringFix(bool enabled)
        {
            if (InvokeRequired)
            {
                Invoke(() => SetControlsDuringFix(enabled));
                return;
            }
            _diagnoseButton.Enabled = enabled;
            _fixButton.Enabled = enabled;
            _generateButton.Enabled = enabled;
            _ssidBox.ReadOnly = !enabled;
            _keyBox.ReadOnly = !enabled;
            _stopButton.Visible = !enabled;
        }

        private void SetStatus(string text, Color color)
        {
            if (InvokeRequired)
            {
                Invoke(() => SetStatus(text, color));
                return;
            }
            _statusLabel.Text = text;
            _statusLabel.ForeColor = color;
        }

        private void SafeAppend(string text)
        {
            if (InvokeRequired)
            {
                Invoke(() => AppendOutput(text));
                return;
            }
            AppendOutput(text);
        }

        private void AppendOutput(string text)
        {
            _outputBox.Text += text + "\r\n";
            _outputBox.SelectionStart = _outputBox.Text.Length;
            _outputBox.ScrollToCaret();
        }

        private static string GenerateRandomPassword(int length)
        {
            const string chars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
            var random = Random.Shared;
            return new string(Enumerable.Range(0, length)
                .Select(_ => chars[random.Next(chars.Length)])
                .ToArray());
        }
    }
}
