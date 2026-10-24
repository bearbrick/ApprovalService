using System.Data;
using ApprovalService.Application.Reporting;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Reporting;
using ApprovalService.Domain.Shared;
using MiniExcelLibs;

namespace ApprovalService.Infrastructure.Reporting;

/// <summary>
/// 只根据应用层已授权的汇总指标生成四张中文工作表。
/// </summary>
internal sealed class ReportingWorkbookExporter : IReportingWorkbookExporter
{
    /// <summary>
    /// 最多导出的流程、节点和人员汇总行数。
    /// </summary>
    internal const int MaximumMetricRows = 10000;

    /// <summary>
    /// 单个维度文本的最大字符数。
    /// </summary>
    internal const int MaximumDimensionLength = 256;

    /// <summary>
    /// 工作簿字节上限。
    /// </summary>
    internal const int MaximumWorkbookBytes = 16 * 1024 * 1024;

    /// <inheritdoc />
    public byte[] Export(ReportingQueryDto report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var metrics = report.VisibleMetrics;
        var rowCount = (long)metrics.Flows.Count + metrics.Nodes.Count + metrics.People.Count;
        if (rowCount > MaximumMetricRows)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                $"报表汇总超过 {MaximumMetricRows} 行，请缩小筛选范围后重试。");
        }

        var sheets = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["流程汇总"] = BuildFlows(metrics.Flows),
            ["节点汇总"] = BuildNodes(metrics.Nodes),
            ["人员汇总"] = BuildPeople(metrics.People),
            ["保密匿名总量"] = BuildConfidential(report),
        };
        using var stream = new MemoryStream();
        stream.SaveAs(sheets, excelType: ExcelType.XLSX);
        if (stream.Length > MaximumWorkbookBytes)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                "报表工作簿超过 16 MB，请缩小筛选范围后重试。");
        }

        return stream.ToArray();
    }

    /// <summary>
    /// 构造按流程聚合的中文列。
    /// </summary>
    /// <param name="items">已去除保密流程的指标。</param>
    /// <returns>流程工作表。</returns>
    private static DataTable BuildFlows(IReadOnlyList<ReportingFlowMetric> items)
    {
        var table = NewTable("流程编码", "发起量", "结束量", "通过量", "拒绝量", "撤回量", "平均耗时（分钟）", "P90耗时（分钟）");
        foreach (var item in items)
        {
            table.Rows.Add(SafeText(item.FlowCode), item.StartedCount, item.CompletedCount,
                item.ApprovedCount, item.RejectedCount, item.WithdrawnCount,
                Value(item.AverageDurationMinutes), Value(item.P90DurationMinutes));
        }

        return table;
    }

    /// <summary>
    /// 构造按流程节点聚合的中文列。
    /// </summary>
    /// <param name="items">已去除保密流程的指标。</param>
    /// <returns>节点工作表。</returns>
    private static DataTable BuildNodes(IReadOnlyList<ReportingNodeMetric> items)
    {
        var table = NewTable("流程编码", "节点ID", "完成任务数", "平均停留（分钟）", "已到期任务数", "逾期任务数");
        foreach (var item in items)
        {
            table.Rows.Add(SafeText(item.FlowCode), SafeText(item.NodeId), item.CompletedCount,
                Value(item.AverageDurationMinutes), item.DueCount, item.OverdueCount);
        }

        return table;
    }

    /// <summary>
    /// 构造按实际审批人聚合的中文列。
    /// </summary>
    /// <param name="items">已去除保密流程的指标。</param>
    /// <returns>人员工作表。</returns>
    private static DataTable BuildPeople(IReadOnlyList<ReportingPersonMetric> items)
    {
        var table = NewTable("处理人ID", "当前待办数", "完成任务数", "平均处理（分钟）", "逾期次数");
        foreach (var item in items)
        {
            table.Rows.Add(SafeText(item.AssigneeId), item.PendingCount, item.CompletedCount,
                Value(item.AverageDurationMinutes), item.OverdueCount);
        }

        return table;
    }

    /// <summary>
    /// 构造只含匿名数量的保密表，不写入任何流程或人员维度。
    /// </summary>
    /// <param name="report">已授权的报表结果。</param>
    /// <returns>保密匿名汇总表。</returns>
    private static DataTable BuildConfidential(ReportingQueryDto report)
    {
        var table = NewTable("匿名发起量", "匿名结束量");
        table.Rows.Add(report.ConfidentialStartedCount, report.ConfidentialCompletedCount);
        return table;
    }

    /// <summary>
    /// 生成有固定中文列的工作表，包括空结果时的表头。
    /// </summary>
    /// <param name="columns">按导出顺序排列的列名。</param>
    /// <returns>空工作表。</returns>
    private static DataTable NewTable(params string[] columns)
    {
        var table = new DataTable();
        foreach (var column in columns)
        {
            table.Columns.Add(column, typeof(object));
        }

        return table;
    }

    /// <summary>
    /// 将空耗时写为空单元格。
    /// </summary>
    /// <param name="number">可空耗时。</param>
    /// <returns>数值或数据库空值。</returns>
    private static object Value(double? number) => number.HasValue ? number.Value : DBNull.Value;

    /// <summary>
    /// 限制维度长度并防止表格程序将外部标识识别为公式。
    /// </summary>
    /// <param name="text">流程、节点或人员标识。</param>
    /// <returns>安全文本。</returns>
    private static string SafeText(string text)
    {
        if (text.Length > MaximumDimensionLength)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                "报表维度文本过长，请检查数据后重试。");
        }

        return text.Length > 0 && text[0] is '=' or '+' or '-' or '@' ? "'" + text : text;
    }
}
