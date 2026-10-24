using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// 按当前管理范围读取版本摘要，并在一个事务内恢复旧版到新草稿修订。
/// </summary>
/// <param name="factory">当前数据库上下文工厂。</param>
public sealed class FlowVersionRepository(IDbContextFactory<ApprovalDbContext> factory) : IFlowVersionRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ManagedFlowVersionSummary>> ListAsync(string code, string actorId,
        bool isLocalSession, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var flow = await RequireFlowAsync(db, code, actorId, isLocalSession, cancellationToken);
        var versions = await db.FlowVersions.AsNoTracking().Where(item => item.FlowId == flow.Id)
            .OrderByDescending(item => item.Version)
            .Select(item => new ManagedFlowVersionSummary
            {
                Id = item.Id,
                Version = item.Version,
                PublishedAt = item.PublishedAt,
                IsCurrent = item.Id == flow.PublishedVersionId,
            }).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return versions;
    }

    /// <inheritdoc />
    public async Task<ManagedFlowSnapshot> RestoreAsync(FlowVersionRestoreWrite input,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var flow = await RequireFlowAsync(db, input.Code, input.ActorId, input.IsLocalSession,
            cancellationToken);
        var source = await db.FlowVersions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.FlowId == flow.Id && item.Version == input.SourceVersion, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "指定流程发布版本不存在。");
        var draft = await db.FlowDrafts.SingleOrDefaultAsync(item => item.FlowId == flow.Id,
            cancellationToken);
        if (draft is null && flow.PublishedVersionId is not null)
        {
            // 存量流程的只读修订零在首次恢复时才建立实体草稿。
            var current = await db.FlowVersions.AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == flow.PublishedVersionId, cancellationToken)
                ?? throw new InvalidOperationException("当前发布版本不存在。");
            draft = new FlowDraftEntity
            {
                FlowId = flow.Id,
                Revision = 0,
                DefinitionJson = current.DefinitionJson,
                UpdatedAt = current.PublishedAt,
            };
            db.FlowDrafts.Add(draft);
        }

        if (draft is null || draft.Revision != input.ExpectedRevision ||
            flow.PublishedVersionId != input.ExpectedPublishedVersionId)
        {
            throw new UserFriendlyException(ErrorCodes.Conflict,
                "流程草稿或当前发布版已变化，请刷新后重试。");
        }

        var definition = JsonSerializer.Deserialize<FlowDefinition>(source.DefinitionJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("历史发布版本的定义为空。");
        flow.Name = definition.Name;
        draft.DefinitionJson = source.DefinitionJson;
        draft.Revision++;
        draft.UpdatedAt = input.OccurredAt;
        db.FlowManagementAudits.Add(new FlowManagementAuditEntity
        {
            Id = input.AuditId,
            FlowId = flow.Id,
            ActorId = input.ActorId,
            Action = "restore-version",
            Reason = input.Reason,
            DraftRevision = draft.Revision,
            PublishedVersion = source.Version,
            ContentSha256 = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(source.DefinitionJson))).ToLowerInvariant(),
            OccurredAt = input.OccurredAt,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "流程草稿已被其他人修改，请刷新后重试。");
        }
        catch (DbUpdateException exception) when (IsUniqueConflict(exception))
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "流程草稿已由其他人创建，请刷新后重试。");
        }

        return await FlowManagementRepository.SnapshotAsync(db, flow, cancellationToken);
    }

    /// <summary>
    /// 在同一事务中读取流程并把越权目标隐藏为不存在。
    /// </summary>
    /// <param name="db">同一数据库上下文。</param>
    /// <param name="code">稳定流程编码。</param>
    /// <param name="actorId">已认证内部人员 ID。</param>
    /// <param name="isLocalSession">是否本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>已授权流程元数据。</returns>
    private static async Task<FlowEntity> RequireFlowAsync(ApprovalDbContext db, string code, string actorId,
        bool isLocalSession, CancellationToken cancellationToken)
    {
        var flow = await db.Flows.SingleOrDefaultAsync(item => item.Code == code, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "流程不存在。");
        try
        {
            await FlowManagementRepository.RequireManagementAsync(db, actorId, isLocalSession, flow,
                cancellationToken);
        }
        catch (UserFriendlyException exception) when (exception.Code == ErrorCodes.Forbidden)
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "流程不存在。");
        }

        return flow;
    }

    /// <summary>
    /// 判断新建存量草稿时是否碰到唯一键竞争。
    /// </summary>
    /// <param name="exception">数据库写入错误。</param>
    /// <returns>唯一约束冲突时为真。</returns>
    private static bool IsUniqueConflict(DbUpdateException exception) => exception.InnerException switch
    {
        SqliteException sqlite => sqlite.SqliteErrorCode == 19,
        SqlException sqlServer => sqlServer.Number is 2601 or 2627,
        PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation,
        _ => false,
    };
}
