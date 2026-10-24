namespace ApprovalService.Application.Reporting;

/// <summary>
/// 将已授权且已过滤保密维度的报表结果编码为工作簿。
/// </summary>
public interface IReportingWorkbookExporter
{
    /// <summary>
    /// 生成包含流程、节点、人员和匿名保密数量四张表的 XLSX 文件。
    /// </summary>
    /// <param name="report">应用层授权后的安全报表结果。</param>
    /// <returns>XLSX 字节。</returns>
    byte[] Export(ReportingQueryDto report);
}
