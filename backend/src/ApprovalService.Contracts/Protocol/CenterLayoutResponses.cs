namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 当前查看人可见的锁定版本详情排版，嵌入原单尚未接入时使用自动排版。
/// </summary>
public sealed class CenterDetailLayoutResponse
{
    /// <summary>
    /// auto 或 custom。
    /// </summary>
    public string Mode { get; set; } = "auto";

    /// <summary>
    /// 仅包含至少一个可见字段的分组。
    /// </summary>
    public IReadOnlyList<CenterLayoutGroupResponse> Groups { get; set; } = [];
}

/// <summary>
/// 当前人员可见的详情分组。
/// </summary>
public sealed class CenterLayoutGroupResponse
{
    /// <summary>
    /// 安全展示标题，若部分字段隐藏则使用通用标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 每行列数，一至三列。
    /// </summary>
    public int Columns
    {
        get; set;
    }

    /// <summary>
    /// 按发布顺序排列的可见字段。
    /// </summary>
    public IReadOnlyList<CenterLayoutItemResponse> Items { get; set; } = [];
}

/// <summary>
/// 一个可见字段的排版参数。
/// </summary>
public sealed class CenterLayoutItemResponse
{
    /// <summary>
    /// 已授权详情数据的字段编码。
    /// </summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>
    /// 占用的分组列数。
    /// </summary>
    public int ColumnSpan
    {
        get; set;
    }

    /// <summary>
    /// 仅含可见明细子字段的列宽百分比。
    /// </summary>
    public IReadOnlyDictionary<string, int> DetailColumnWidths { get; set; } = new Dictionary<string, int>();

    /// <summary>
    /// 可见金额字段换算后的人民币大写，未启用或无法换算时为空。
    /// </summary>
    public string? MoneyUppercase
    {
        get; set;
    }
}
