using CampusHotspotFix.Services;

namespace CampusHotspotFix.Forms
{
    /// <summary>
    /// 主界面骨架。当前只是一个最小可编译版本,目的是让项目在这个阶段就能跑起来、
    /// 手动验证NetworkAdapterService和HostedNetworkService的效果,
    /// 而不是等所有Service都写完才第一次编译——那样出问题定位成本更高。
    ///
    /// 完整的"诊断→修复→报告"三步向导界面,按工作流程文档的实施顺序,
    /// 放在所有Service完成之后再实现(第9步)。
    /// </summary>
    public class MainForm : Form
    {
        private readonly NetworkAdapterService _adapterService = new();
        private readonly HostedNetworkService _hostedNetworkService = new();

        private readonly TextBox _outputBox;
        private readonly Button _checkDriverButton;
        private readonly Button _listAdaptersButton;

        public MainForm()
        {
            Text = "校园宽带热点修复工具(开发中 - 骨架版)";
            Width = 700;
            Height = 500;
            StartPosition = FormStartPosition.CenterScreen;

            _outputBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Dock = DockStyle.Bottom,
                Height = 380,
                Font = new Font("Consolas", 9),
            };

            _checkDriverButton = new Button
            {
                Text = "检测网卡是否支持承载网络",
                Left = 20,
                Top = 20,
                Width = 220,
            };
            _checkDriverButton.Click += OnCheckDriverClicked;

            _listAdaptersButton = new Button
            {
                Text = "列出所有网络接口",
                Left = 260,
                Top = 20,
                Width = 220,
            };
            _listAdaptersButton.Click += OnListAdaptersClicked;

            Controls.Add(_outputBox);
            Controls.Add(_checkDriverButton);
            Controls.Add(_listAdaptersButton);
        }

        private void OnCheckDriverClicked(object? sender, EventArgs e)
        {
            var supported = _adapterService.IsHostedNetworkSupported(out var rawOutput);

            string conclusion = supported switch
            {
                true => "✅ 支持承载网络,可以继续走P1修复流程",
                false => "❌ 不支持承载网络,建议:回滚网卡驱动版本,或使用外置USB无线网卡",
                null => "⚠️ 未能获取明确结论,请查看下方原始输出自行判断",
            };

            AppendOutput($"[驱动检测结果] {conclusion}\r\n\r\n原始输出:\r\n{rawOutput}");
        }

        private void OnListAdaptersClicked(object? sender, EventArgs e)
        {
            var adapters = _adapterService.GetAllAdapters();

            var lines = adapters.Select(a =>
                $"{a.Name} | {a.InterfaceType} | {(a.IsUp ? "已连接" : "未连接")} | {a.Description}");

            AppendOutput("[网络接口列表]\r\n" + string.Join("\r\n", lines));
        }

        private void AppendOutput(string text)
        {
            _outputBox.Text = text + "\r\n\r\n==================\r\n\r\n" + _outputBox.Text;
        }
    }
}
