using System.Text.Json;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 组织管理员维护接入应用管理员名单，并为后续测试回调提供可信授权检查。
/// </summary>
/// <param name="repository">管理员名单仓储。</param>
/// <param name="organization">组织权限仓储。</param>
/// <param name="timeProvider">操作时间来源。</param>
public sealed class ClientAppAdminAppService(IClientAppAdminRepository repository,
    IOrganizationRepository organization, TimeProvider timeProvider)
{
    /// <summary>
    /// 读取指定应用的内部人员管理员名单。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="actorId">当前人员 ID。</param>
    /// <param name="isLocalSession">是否为本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>不含密钥的管理员名单。</returns>
    public async Task<AdminClientAppAdministratorsResponse> GetAsync(string code, string actorId,
        bool isLocalSession, CancellationToken cancellationToken = default)
    {
        await RequireOrganizationAdminAsync(actorId, isLocalSession, cancellationToken);
        var json = await repository.GetAdministratorIdsJsonAsync(code, cancellationToken) ??
            throw new UserFriendlyException(ErrorCodes.NotFound, "接入应用不存在。");
        return new AdminClientAppAdministratorsResponse { Code = code, PersonIds = ParseIds(json) };
    }

    /// <summary>
    /// 按预期旧名单替换应用管理员，并在同一事务写入审计。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="request">旧名单、新名单和原因。</param>
    /// <param name="actorId">当前人员 ID。</param>
    /// <param name="isLocalSession">是否为本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>保存后的管理员名单。</returns>
    public async Task<AdminClientAppAdministratorsResponse> ReplaceAsync(string code,
        UpdateAdminClientAppAdministratorsRequest request, string actorId, bool isLocalSession,
        CancellationToken cancellationToken = default)
    {
        await RequireOrganizationAdminAsync(actorId, isLocalSession, cancellationToken);
        ArgumentNullException.ThrowIfNull(request);
        var expected = ValidateIds(request.ExpectedPersonIds);
        var updated = ValidateIds(request.PersonIds);
        if ((request.Reason?.Trim().Length ?? 0) is < 2 or > 200)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "请填写 2 到 200 个字符的变更原因。");
        }

        var currentJson = await repository.GetAdministratorIdsJsonAsync(code, cancellationToken) ??
            throw new UserFriendlyException(ErrorCodes.NotFound, "接入应用不存在。");
        var current = ParseIds(currentJson);
        if (!current.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "管理员名单已变化，请刷新后重试。");
        }

        var result = await repository.ReplaceAsync(code, currentJson, JsonSerializer.Serialize(updated),
            updated, actorId, request.Reason!.Trim(), timeProvider.GetUtcNow(), cancellationToken);
        return result switch
        {
            ClientAppAdminWriteResult.Updated => new AdminClientAppAdministratorsResponse
            {
                Code = code,
                PersonIds = updated,
            },
            ClientAppAdminWriteResult.AppNotFound => throw new UserFriendlyException(ErrorCodes.NotFound,
                "接入应用不存在。"),
            ClientAppAdminWriteResult.InvalidPerson => throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                "管理员名单只能包含在职的内部人员 ID。"),
            _ => throw new UserFriendlyException(ErrorCodes.Conflict, "管理员名单已变化，请刷新后重试。"),
        };
    }

    /// <summary>
    /// 为测试回调用例核验当前本地人员对已启用应用的管理权限。
    /// </summary>
    /// <param name="code">接入应用编码。</param>
    /// <param name="actorId">JWT 中的内部人员 ID。</param>
    /// <param name="isLocalSession">JWT 是否由本地登录签发。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>当前人员有权管理该已启用应用时为真。</returns>
    public async Task<bool> CanSendTestCallbackAsync(string code, string actorId, bool isLocalSession,
        CancellationToken cancellationToken = default)
    {
        if (!isLocalSession || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(actorId) ||
            !await repository.IsAppEnabledAsync(code, cancellationToken))
        {
            return false;
        }

        return await organization.IsOrganizationAdminAsync(actorId, cancellationToken) ||
            await repository.IsActiveAdministratorAsync(code, actorId, cancellationToken);
    }

    /// <summary>
    /// 重新读取组织权限，防止仅靠前端或旧令牌里的角色授予权限。
    /// </summary>
    /// <param name="actorId">人员 ID。</param>
    /// <param name="isLocalSession">是否为本地会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    private async Task RequireOrganizationAdminAsync(string actorId, bool isLocalSession,
        CancellationToken cancellationToken)
    {
        if (!isLocalSession || !await organization.IsOrganizationAdminAsync(actorId, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "只有本地组织管理员可以维护接入应用管理员名单。");
        }
    }

    /// <summary>
    /// 读取持久化名单，损坏时拒绝管理操作。
    /// </summary>
    /// <param name="json">数据库中的 JSON。</param>
    /// <returns>按 ID 排序的名单。</returns>
    private static IReadOnlyList<string> ParseIds(string json)
    {
        try
        {
            return ValidateIds(JsonSerializer.Deserialize<string[]>(json));
        }
        catch (JsonException)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "接入应用管理员名单配置无效。");
        }
    }

    /// <summary>
    /// 限定人员 ID 数量、格式和重复项，避免无限长的授权名单。
    /// </summary>
    /// <param name="ids">待校验的名单。</param>
    /// <returns>按内部人员 ID 排序的名单。</returns>
    private static IReadOnlyList<string> ValidateIds(IReadOnlyList<string>? ids)
    {
        if (ids is null || ids.Count > 50 || ids.Any(id => string.IsNullOrWhiteSpace(id) ||
            id.Length > 128 || id != id.Trim()) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "管理员名单最多 50 人，且须使用不重复的内部人员 ID。");
        }

        return ids.OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }
}
