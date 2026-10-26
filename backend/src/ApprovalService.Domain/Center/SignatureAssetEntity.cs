namespace ApprovalService.Domain.Center;

/// <summary>
/// 本人上传的不可变 PNG 签名资产；审批操作只引用其稳定 ID。
/// </summary>
public sealed class SignatureAssetEntity
{
    /// <summary>
    /// 资产 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 签名本人的内部人员 ID。
    /// </summary>
    public string PersonId { get; set; } = string.Empty;

    /// <summary>
    /// 是否列入本人可再次明确选择的签名；嵌入会话只能创建单次资产。
    /// </summary>
    public bool IsPersonal
    {
        get; set;
    }

    /// <summary>
    /// 已完整解码校验的原始 PNG 字节，插入后不可修改。
    /// </summary>
    public byte[] PngBytes { get; set; } = [];

    /// <summary>
    /// 原始 PNG 的 SHA-256 摘要。
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// 上传时间。
    /// </summary>
    public DateTimeOffset UploadedAt
    {
        get; set;
    }
}

/// <summary>
/// 不可变签名资产的持久化访问。
/// </summary>
public interface ISignatureAssetRepository
{
    /// <summary>
    /// 插入一次签名资产。
    /// </summary>
    /// <param name="asset">已验证的资产。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>写入完成。</returns>
    Task AddAsync(SignatureAssetEntity asset, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 ID 读取资产，调用方须自行完成当前场景的授权。
    /// </summary>
    /// <param name="id">签名资产 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>资产或空。</returns>
    Task<SignatureAssetEntity?> FindAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取本人最近上传的可重复选择签名。
    /// </summary>
    /// <param name="personId">可信内部人员 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>最近个人签名或空。</returns>
    Task<SignatureAssetEntity?> FindLatestPersonalAsync(string personId, CancellationToken cancellationToken = default);
}
