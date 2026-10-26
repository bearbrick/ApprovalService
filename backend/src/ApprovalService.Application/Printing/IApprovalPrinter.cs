using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Application.Printing;

/// <summary>
/// 仅接收已裁剪的安全详情，渲染器不能自行读取原始业务数据。
/// </summary>
public interface IApprovalPrinter
{
    /// <summary>
    /// 生成带嵌入字体的审批单 PDF。
    /// </summary>
    /// <param name="input">应用层完成权限检查的打印快照。</param>
    /// <returns>完整 PDF 字节。</returns>
    byte[] Render(ApprovalPrintInput input);

    /// <summary>
    /// 检查字体可被渲染器解析并包含基本中文字形。
    /// </summary>
    /// <param name="bytes">字体内容。</param>
    void ValidateFont(byte[] bytes);
}

/// <summary>
/// 单次打印的不可共享上下文。
/// </summary>
public sealed class ApprovalPrintInput
{
    /// <summary>
    /// 已按查看人字段权限裁剪的详情。
    /// </summary>
    public CenterDetailResponse Detail { get; set; } = new();
    /// <summary>
    /// 锁定版本的打印设置。
    /// </summary>
    public FlowPrintSettings Settings { get; set; } = new();
    /// <summary>
    /// 单位上传且允许嵌入的 TrueType 字体。
    /// </summary>
    public byte[] FontBytes { get; set; } = [];
    /// <summary>
    /// 公司名称。
    /// </summary>
    public string CompanyName { get; set; } = string.Empty;
    /// <summary>
    /// 发起部门显示名称或历史部门标识。
    /// </summary>
    public string DepartmentName { get; set; } = string.Empty;
    /// <summary>
    /// 可选 PNG Logo。
    /// </summary>
    public byte[]? Logo
    {
        get; set;
    }
    /// <summary>
    /// 当前打印人的显示名称。
    /// </summary>
    public string PrintedBy { get; set; } = string.Empty;
    /// <summary>
    /// 生成时间。
    /// </summary>
    public DateTimeOffset PrintedAt
    {
        get; set;
    }
    /// <summary>
    /// 原子审计对应的打印次数。
    /// </summary>
    public int PrintNumber
    {
        get; set;
    }
    /// <summary>
    /// 需要登录和权限的线上审批单地址。
    /// </summary>
    public string DetailUrl { get; set; } = string.Empty;
    /// <summary>
    /// 已授权人员显示名称。
    /// </summary>
    public IReadOnlyDictionary<string, string> People { get; set; } = new Dictionary<string, string>();
    /// <summary>
    /// 已授权签名 PNG，按资产 ID 查找。
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> Signatures { get; set; } = new Dictionary<string, byte[]>();
    /// <summary>
    /// 已授权附件图片，按附件 ID 查找。
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> Receipts { get; set; } = new Dictionary<string, byte[]>();
}
