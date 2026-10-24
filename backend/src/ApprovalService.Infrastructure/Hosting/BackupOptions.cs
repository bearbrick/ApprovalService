namespace ApprovalService.Infrastructure.Hosting;

/// <summary>
/// SQLite 每日备份设置；时间按服务器本地时区解释。
/// </summary>
public sealed class BackupOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "ApprovalService:Backup";

    /// <summary>
    /// 是否自动执行每日备份。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 每日开始时间，格式 HH:mm，默认与标签服务一致。
    /// </summary>
    public string Time { get; set; } = "02:30";

    /// <summary>
    /// 保留最近的每日快照数量；升级前快照由部署人员确认后清理。
    /// </summary>
    public int Keep { get; set; } = 7;
}
