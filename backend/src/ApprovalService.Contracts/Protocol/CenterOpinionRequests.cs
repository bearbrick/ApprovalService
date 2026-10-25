namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 新增或修改个人常用意见的请求。
/// </summary>
public sealed class CenterOpinionRequest
{
    /// <summary>
    /// 常用意见正文，最多五百字。
    /// </summary>
    public string? Content
    {
        get; set;
    }
}

/// <summary>
/// 当前人员的一条已保存常用意见。
/// </summary>
public sealed class CenterOpinionResponse
{
    /// <summary>
    /// 常用意见标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 意见正文。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间，使用 UTC。
    /// </summary>
    public DateTime CreatedAtUtc
    {
        get; set;
    }
}
