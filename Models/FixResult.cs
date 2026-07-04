namespace CampusHotspotFix.Models
{
    /// <summary>
    /// 单个问题项的诊断结论。
    /// DiagnosticService对P1-P10逐项检测后,每一项都会产出一条这个结构,
    /// 汇总成DiagnosticResult返回给UI层展示。
    /// </summary>
    public class DiagnosticItem
    {
        public ProblemCode Code { get; init; }

        public ResolutionType Resolution { get; init; }

        /// <summary>
        /// true = 检测到这个问题确实存在;false = 检测过,但没发现这个问题。
        /// 注意这个字段和Resolution是两个独立维度——
        /// 比如P6(运营商检测)即使IsDetected=true,Resolution也只能是RiskOnly,不能是AutoFixable。
        /// </summary>
        public bool IsDetected { get; init; }

        /// <summary>给用户看的人话说明,不要在这里堆技术术语</summary>
        public string UserMessage { get; init; } = string.Empty;

        /// <summary>给你自己或技术支持看的详细诊断依据(命令输出片段、判断依据等)</summary>
        public string TechnicalDetail { get; init; } = string.Empty;
    }

    public class DiagnosticResult
    {
        public DateTime CheckedAt { get; init; } = DateTime.Now;

        public List<DiagnosticItem> Items { get; init; } = new();

        /// <summary>只返回IsDetected=true且Resolution=AutoFixable的项——这些才是"一键修复"按钮实际要处理的范围</summary>
        public List<DiagnosticItem> GetAutoFixableIssues() =>
            Items.Where(i => i.IsDetected && i.Resolution == ResolutionType.AutoFixable).ToList();

        /// <summary>只返回IsDetected=true且Resolution=RiskOnly的项——这些只能展示告知文案,不能给修复按钮</summary>
        public List<DiagnosticItem> GetRiskOnlyIssues() =>
            Items.Where(i => i.IsDetected && i.Resolution == ResolutionType.RiskOnly).ToList();
    }

    /// <summary>
    /// 单个修复动作的执行结果。
    /// 每个Service的Fix方法(HostedNetworkService.Enable()、IcsShareService.BindSharing()等)
    /// 都应该返回这个结构,而不是简单的bool——出问题时,ExceptionDetail对排查至关重要。
    /// </summary>
    public class FixResult
    {
        public ProblemCode TargetCode { get; init; }

        public bool Success { get; init; }

        public string Message { get; init; } = string.Empty;

        /// <summary>失败时的异常信息/命令行原始输出,写入日志文件,不直接展示给用户</summary>
        public string? ExceptionDetail { get; init; }

        public static FixResult Ok(ProblemCode code, string message) =>
            new() { TargetCode = code, Success = true, Message = message };

        public static FixResult Fail(ProblemCode code, string message, string? detail = null) =>
            new() { TargetCode = code, Success = false, Message = message, ExceptionDetail = detail };
    }
}
