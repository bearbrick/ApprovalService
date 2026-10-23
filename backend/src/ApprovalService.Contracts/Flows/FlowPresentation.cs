namespace ApprovalService.Contracts.Flows;

/// <summary>
/// 审批数据字段及待办摘要的定义；服务不创建业务单据。
/// </summary>
public sealed class FlowDataDefinition
{
    /// <summary>
    /// 业务系统需要提交及审批人可以填写的字段。
    /// </summary>
    public List<FlowFieldDefinition> Fields { get; set; } = [];

    /// <summary>
    /// 在待办、通知等摘要中显示的字段 key，通常选三至五个。
    /// </summary>
    public List<string> SummaryFields { get; set; } = [];
}

/// <summary>
/// 一个审批数据字段，明细表可包含子字段。
/// </summary>
public sealed class FlowFieldDefinition
{
    /// <summary>
    /// 同一层级内唯一的字段 key。
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 向审批人展示的字段名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 字段种类，见 <see cref="FlowFieldTypes"/>。
    /// </summary>
    public string Type { get; set; } = FlowFieldTypes.Text;

    /// <summary>
    /// 业务系统提交或审批人填写时是否必传。
    /// </summary>
    public bool Required
    {
        get; set;
    }

    /// <summary>
    /// 计算字段表达式；只计算审批快照，不写回业务单据。
    /// </summary>
    public string? Formula
    {
        get; set;
    }

    /// <summary>
    /// 字段是否由审批人在获授权的节点填写。
    /// </summary>
    public bool ApproverInput
    {
        get; set;
    }

    /// <summary>
    /// 明细表的子字段；普通字段保持空集合。
    /// </summary>
    public List<FlowFieldDefinition> Children { get; set; } = [];
}

/// <summary>
/// 审批详情展示方式，可自动排版、拖拽排版或嵌入业务系统只读原单。
/// </summary>
public sealed class FlowDetailLayout
{
    /// <summary>
    /// 展示方式：auto、custom 或 embed。
    /// </summary>
    public string Mode { get; set; } = "auto";

    /// <summary>
    /// 拖拽排版的分组；其他方式保持空集合。
    /// </summary>
    public List<FlowLayoutGroup> Groups { get; set; } = [];

    /// <summary>
    /// 嵌入只读原单时使用的接入应用页面编码，地址由业务系统提供。
    /// </summary>
    public string? EmbedPageCode
    {
        get; set;
    }
}

/// <summary>
/// 拖拽排版中的字段分组。
/// </summary>
public sealed class FlowLayoutGroup
{
    /// <summary>
    /// 分组标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 当前分组一行的字段列数。
    /// </summary>
    public int Columns { get; set; } = 2;

    /// <summary>
    /// 按展示顺序排列的字段和明细表。
    /// </summary>
    public List<FlowLayoutItem> Items { get; set; } = [];
}

/// <summary>
/// 一个字段或明细表的详情展示配置。
/// </summary>
public sealed class FlowLayoutItem
{
    /// <summary>
    /// 字段 key。
    /// </summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>
    /// 占用的布局列数。
    /// </summary>
    public int ColumnSpan { get; set; } = 1;

    /// <summary>
    /// 明细表子字段的列宽百分比；键是子字段 key。
    /// </summary>
    public Dictionary<string, int> DetailColumnWidths { get; set; } = [];

    /// <summary>
    /// 金额旁是否显示人民币大写。
    /// </summary>
    public bool ShowMoneyUppercase
    {
        get; set;
    }
}

/// <summary>
/// 每个流程版本固定的审批单打印设置。
/// </summary>
public sealed class FlowPrintSettings
{
    /// <summary>
    /// 纸张方向：portrait 或 landscape；纸张固定 A4。
    /// </summary>
    public string Orientation { get; set; } = "portrait";

    /// <summary>
    /// 按顺序打印的审批数据字段 key；空集合表示按数据定义顺序。
    /// </summary>
    public List<string> FieldKeys { get; set; } = [];

    /// <summary>
    /// 是否打印审批意见。
    /// </summary>
    public bool ShowComments { get; set; } = true;

    /// <summary>
    /// 是否打印审批人签名。
    /// </summary>
    public bool ShowSignatures { get; set; } = true;

    /// <summary>
    /// 是否在后续页面附票据图片。
    /// </summary>
    public bool IncludeReceiptPages
    {
        get; set;
    }

    /// <summary>
    /// 是否在页眉显示单位 Logo。
    /// </summary>
    public bool ShowLogo { get; set; } = true;

    /// <summary>
    /// 是否显示二维码，扫码打开线上审批单需登录和权限。
    /// </summary>
    public bool ShowQrCode { get; set; } = true;

    /// <summary>
    /// 是否显示打印人和打印时间水印。
    /// </summary>
    public bool ShowWatermark
    {
        get; set;
    }
}
