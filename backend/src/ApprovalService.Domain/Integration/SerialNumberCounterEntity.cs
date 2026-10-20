namespace ApprovalService.Domain.Integration;

/// <summary>
/// 审批编号前缀与日期桶的持久流水号；先分配再发起，失败时允许留空号。
/// </summary>
public sealed class SerialNumberCounterEntity
{
    /// <summary>
    /// 编号前缀。
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// 按配置日期格式生成的日期桶。
    /// </summary>
    public string DateBucket { get; set; } = string.Empty;

    /// <summary>
    /// 下一次可分配的正流水号。
    /// </summary>
    public long NextSequence
    {
        get; set;
    }

    /// <summary>
    /// 乐观并发版本。
    /// </summary>
    public long Version
    {
        get; set;
    }
}
