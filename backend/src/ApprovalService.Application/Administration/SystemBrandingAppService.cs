using System.Security.Cryptography;
using ApprovalService.Application.Center;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Administration;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Administration;

/// <summary>
/// 校验打印页眉的公司名称与 Logo，并协调版本化保存。
/// </summary>
/// <param name="repository">持久化和审计仓储。</param>
public sealed class SystemBrandingAppService(ISystemBrandingRepository repository)
{
    /// <summary>
    /// 读取当前系统信息摘要。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>空库也有修订零的摘要。</returns>
    public async Task<SystemBrandingResponse> GetAsync(CancellationToken cancellationToken = default) =>
        ToResponse(await repository.GetAsync(cancellationToken));

    /// <summary>
    /// 根据预期修订保存公司名称。
    /// </summary>
    /// <param name="request">名称、修订和原因。</param>
    /// <param name="actorId">当前已授权人员。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>更新后的摘要。</returns>
    public Task<SystemBrandingResponse> SaveNameAsync(UpdateSystemBrandingRequest request, string actorId,
        CancellationToken cancellationToken = default)
    {
        var name = (request.CompanyName ?? string.Empty).Trim();
        if (name.Length is < 1 or > 60 || name.Any(char.IsControl))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "单位名称须为 1–60 个不含控制字符的文字。");
        }
        return UpdateAsync(request.ExpectedRevision, actorId, request.Reason, "companyName",
            value => value.CompanyName = name, cancellationToken);
    }

    /// <summary>
    /// 校验并更换至多 1 MiB 的 PNG Logo。
    /// </summary>
    /// <param name="request">Base64 图片、修订和原因。</param>
    /// <param name="actorId">当前已授权人员。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>更新后的摘要。</returns>
    public Task<SystemBrandingResponse> SaveLogoAsync(UploadSystemLogoRequest request, string actorId,
        CancellationToken cancellationToken = default)
    {
        if (request.ContentBase64 is null || request.ContentBase64.Length is < 4 or > 1400000)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "Logo 不得超过 1 MiB。");
        }
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(request.ContentBase64);
        }
        catch (FormatException)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "Logo 图片内容不是有效的 Base64。");
        }
        if (bytes.Length is < 16 or > 1024 * 1024)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "Logo 不得超过 1 MiB。");
        }
        try
        {
            SignaturePngValidator.Validate(bytes);
        }
        catch (UserFriendlyException)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "Logo 须为完整的 PNG 图片，尺寸不超过 4096×4096。");
        }
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return UpdateAsync(request.ExpectedRevision, actorId, request.Reason, "logoUpload", value =>
        {
            value.LogoBytes = bytes;
            value.LogoContentType = "image/png";
            value.LogoSha256 = sha;
        }, cancellationToken);
    }

    /// <summary>
    /// 移除已上传的 Logo。
    /// </summary>
    /// <param name="request">修订和原因。</param>
    /// <param name="actorId">当前已授权人员。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>更新后的摘要。</returns>
    public Task<SystemBrandingResponse> DeleteLogoAsync(DeleteSystemLogoRequest request, string actorId,
        CancellationToken cancellationToken = default) => UpdateAsync(request.ExpectedRevision, actorId,
        request.Reason, "logoDelete", value =>
        {
            value.LogoBytes = null;
            value.LogoContentType = null;
            value.LogoSha256 = null;
        }, cancellationToken);

    /// <summary>
    /// 读取原始 Logo；由路由统一执行管理员鉴权。
    /// </summary>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>图片字节及可信内容类型。</returns>
    public async Task<(byte[] Bytes, string ContentType)> ReadLogoAsync(
        CancellationToken cancellationToken = default)
    {
        var value = await repository.GetAsync(cancellationToken);
        if (value?.LogoBytes is null || value.LogoContentType is null)
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "尚未上传单位 Logo。");
        }
        return (value.LogoBytes, value.LogoContentType);
    }

    /// <summary>
    /// 统一检查版本和原因后保存任一设置。
    /// </summary>
    /// <param name="revision">预期修订。</param>
    /// <param name="actorId">当前已授权人员。</param>
    /// <param name="reason">操作原因。</param>
    /// <param name="action">审计操作类别。</param>
    /// <param name="change">设置修改器。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>更新后的摘要。</returns>
    private async Task<SystemBrandingResponse> UpdateAsync(long revision, string actorId, string reason,
        string action, Action<SystemBrandingEntity> change, CancellationToken cancellationToken)
    {
        if (revision < 0 || string.IsNullOrWhiteSpace(actorId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "系统信息修订或操作人无效。");
        }
        reason = (reason ?? string.Empty).Trim();
        if (reason.Length is < 2 or > 200 || reason.Any(char.IsControl))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "修改原因须为 2–200 个字，且不能换行。");
        }
        var value = await repository.GetAsync(cancellationToken) ?? new SystemBrandingEntity();
        if (value.Revision != revision)
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "系统信息已被其他管理员修改，请刷新后重试。");
        }
        change(value);
        if (!await repository.SaveAsync(value, revision, actorId, reason, action, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "系统信息已被其他管理员修改，请刷新后重试。");
        }
        value.Revision = revision + 1;
        return ToResponse(value);
    }

    /// <summary>
    /// 只返回管理页与打印配置需要的安全元数据。
    /// </summary>
    /// <param name="value">已保存的信息或空库。</param>
    /// <returns>安全摘要。</returns>
    private static SystemBrandingResponse ToResponse(SystemBrandingEntity? value) => new()
    {
        CompanyName = value?.CompanyName ?? string.Empty,
        LogoSha256 = value?.LogoSha256,
        Revision = value?.Revision ?? 0,
    };
}
