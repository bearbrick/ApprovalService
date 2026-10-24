using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Administration;

/// <summary>
/// 展示不可变版本历史，并将旧版内容复制为待重新发布的草稿。
/// </summary>
/// <param name="repository">在同一数据库事务内复核范围和并发前提的仓储。</param>
/// <param name="timeProvider">统一操作时间。</param>
public sealed class FlowVersionAppService(IFlowVersionRepository repository, TimeProvider timeProvider)
{
    /// <summary>
    /// 读取当前本地流程管理员可见的发布版本摘要。
    /// </summary>
    /// <param name="code">稳定流程编码。</param>
    /// <param name="actorId">已认证内部人员 ID。</param>
    /// <param name="isLocalSession">是否本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>按版本号倒序的摘要。</returns>
    public async Task<IReadOnlyList<AdminFlowVersionResponse>> ListAsync(string code, string actorId,
        bool isLocalSession, CancellationToken cancellationToken = default)
    {
        var versions = await repository.ListAsync(code, actorId, isLocalSession, cancellationToken);
        return versions.Select(item => new AdminFlowVersionResponse
        {
            Id = item.Id,
            Version = item.Version,
            PublishedAt = item.PublishedAt,
            IsCurrent = item.IsCurrent,
        }).ToArray();
    }

    /// <summary>
    /// 按双重并发前提把旧版内容复制为下一修订草稿，不改变当前发布版。
    /// </summary>
    /// <param name="code">稳定流程编码。</param>
    /// <param name="version">来源不可变发布版本号。</param>
    /// <param name="request">草稿/发布版前提与原因。</param>
    /// <param name="actorId">已认证内部人员 ID。</param>
    /// <param name="isLocalSession">是否本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>只变更草稿的流程详情。</returns>
    public async Task<AdminFlowResponse> RestoreAsync(string code, int version,
        AdminFlowVersionRestoreRequest request, string actorId, bool isLocalSession,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (version <= 0 || request.ExpectedRevision < 0 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 2 or > 200)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                "历史版本号、草稿修订或操作原因不正确；原因须为 2–200 字。");
        }

        var snapshot = await repository.RestoreAsync(new FlowVersionRestoreWrite
        {
            Code = code,
            SourceVersion = version,
            ExpectedRevision = request.ExpectedRevision,
            ExpectedPublishedVersionId = request.ExpectedPublishedVersionId,
            ActorId = actorId,
            IsLocalSession = isLocalSession,
            Reason = request.Reason.Trim(),
            AuditId = Guid.NewGuid().ToString("N"),
            OccurredAt = timeProvider.GetUtcNow(),
        }, cancellationToken);
        return FlowManagementAppService.Response(snapshot, true);
    }
}
