using System.Security.Cryptography;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Center;

/// <summary>
/// 本人签名资产的上传、明确选用和按审批单授权读取。
/// </summary>
/// <param name="assets">不可变签名资产仓储。</param>
/// <param name="center">审批单的参与人和嵌入应用授权仓储。</param>
/// <param name="timeProvider">固定上传时间。</param>
public sealed class SignatureAppService(ISignatureAssetRepository assets, ICenterQueryRepository center,
    TimeProvider timeProvider)
{
    /// <summary>
    /// 签名原始字节上限。
    /// </summary>
    public const int MaxPngBytes = 2 * 1024 * 1024;

    /// <summary>
    /// 上传本人手写签名，嵌入会话仅能上传单次资产。
    /// </summary>
    /// <param name="scope">可信人员会话。</param>
    /// <param name="content">原始 PNG 请求体。</param>
    /// <param name="personal">是否在本人的签名设置中可重复选择。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>新资产的安全元数据。</returns>
    public async Task<CenterSignatureResponse> UploadAsync(CenterScope scope, Stream content, bool personal,
        CancellationToken cancellationToken = default)
    {
        RequireScope(scope);
        ArgumentNullException.ThrowIfNull(content);
        if (personal && scope.ClientAppCode is not null)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "嵌入会话不能修改个人签名设置。");
        }
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await content.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length > MaxPngBytes - read)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, "手写签名 PNG 不能超过 2 MiB。");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        var png = buffer.ToArray();
        SignaturePngValidator.Validate(png);
        var asset = new SignatureAssetEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            PersonId = scope.PersonId,
            IsPersonal = personal,
            PngBytes = png,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(png)),
            UploadedAt = timeProvider.GetUtcNow(),
        };
        await assets.AddAsync(asset, cancellationToken);
        return Describe(asset);
    }

    /// <summary>
    /// 读取本人最近上传的个人签名；返回值不代表自动使用。
    /// </summary>
    /// <param name="scope">可信人员会话。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>元数据或空。</returns>
    public async Task<CenterSignatureResponse?> LatestPersonalAsync(CenterScope scope,
        CancellationToken cancellationToken = default)
    {
        RequireScope(scope);
        if (scope.ClientAppCode is not null)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "嵌入会话不能读取个人签名设置。");
        }
        var asset = await assets.FindLatestPersonalAsync(scope.PersonId, cancellationToken);
        return asset is null ? null : Describe(asset);
    }

    /// <summary>
    /// 只有实际操作人本人才能选用签名资产。
    /// </summary>
    /// <param name="scope">可信人员会话。</param>
    /// <param name="assetId">本次明确选择的资产 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>验证完成。</returns>
    public async Task EnsureOwnedAsync(CenterScope scope, string assetId,
        CancellationToken cancellationToken = default)
    {
        RequireScope(scope);
        if (string.IsNullOrWhiteSpace(assetId) || assetId.Length > 64 ||
            await assets.FindAsync(assetId, cancellationToken) is not { } asset || asset.PersonId != scope.PersonId)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "只能选用本人上传的手写签名。");
        }
    }

    /// <summary>
    /// 本人或已授权审批单参与者读取不可变签名内容。
    /// </summary>
    /// <param name="scope">可信人员会话。</param>
    /// <param name="assetId">签名资产 ID。</param>
    /// <param name="instanceId">历史审批单 ID；读取本人未使用签名时为空。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>原始 PNG 字节。</returns>
    public async Task<byte[]> ReadPngAsync(CenterScope scope, string assetId, string? instanceId,
        CancellationToken cancellationToken = default)
    {
        RequireScope(scope);
        if (string.IsNullOrWhiteSpace(assetId) || assetId.Length > 64)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "签名资产标识不正确。");
        }
        var asset = await assets.FindAsync(assetId, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "签名资产不存在。");
        if (instanceId is null)
        {
            if (scope.ClientAppCode is not null || asset.PersonId != scope.PersonId)
            {
                throw new UserFriendlyException(ErrorCodes.Forbidden, "无权读取这份个人签名。");
            }
            return asset.PngBytes;
        }
        var record = await center.FindAsync(scope, instanceId, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "审批单不存在或当前人员无权查看。");
        var detail = CenterProjection.Detail(record, scope.PersonId);
        if (!detail.Tasks.Any(task => task.SignatureFileId == assetId) &&
            !detail.Events.Any(item => item.SignatureFileId == assetId))
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "该签名未用于这张审批单。");
        }
        return asset.PngBytes;
    }

    /// <summary>
    /// 检查不能由请求正文冒充的可信人员身份。
    /// </summary>
    /// <param name="scope">会话范围。</param>
    private static void RequireScope(CenterScope scope)
    {
        if (scope is null || string.IsNullOrWhiteSpace(scope.PersonId))
        {
            throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录后再操作签名。");
        }
    }

    /// <summary>
    /// 仅映射可对外展示的元数据。
    /// </summary>
    /// <param name="asset">已保存资产。</param>
    /// <returns>安全响应。</returns>
    private static CenterSignatureResponse Describe(SignatureAssetEntity asset) => new()
    {
        Id = asset.Id,
        Sha256 = asset.Sha256,
        IsPersonal = asset.IsPersonal,
        UploadedAt = asset.UploadedAt,
    };
}
