using System.Data;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// 在同一数据库快照中复核流程管理权限并读取导出版本。
/// </summary>
/// <param name="factory">当前数据库上下文工厂。</param>
public sealed class FlowTransferRepository(IDbContextFactory<ApprovalDbContext> factory) : IFlowTransferRepository
{
    /// <inheritdoc />
    public async Task<string> LoadDefinitionAsync(string code, string source, int? version, string actorId,
        bool isLocalSession, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var flow = await db.Flows.AsNoTracking().SingleOrDefaultAsync(item => item.Code == code,
            cancellationToken) ?? throw new UserFriendlyException(ErrorCodes.NotFound, "流程不存在。");
        try
        {
            await FlowManagementRepository.RequireManagementAsync(db, actorId, isLocalSession, flow,
                cancellationToken);
        }
        catch (UserFriendlyException exception) when (exception.Code == ErrorCodes.Forbidden)
        {
            // 单个流程的只读接口统一隐藏无权流程的存在性。
            throw new UserFriendlyException(ErrorCodes.NotFound, "流程不存在。");
        }
        string definition;
        if (source == AdminFlowTransferSources.Draft)
        {
            var draft = await db.FlowDrafts.AsNoTracking().SingleOrDefaultAsync(item => item.FlowId == flow.Id,
                cancellationToken);
            if (draft is not null)
            {
                definition = draft.DefinitionJson;
            }
            else
            {
                // 旧库没有独立草稿时，导出当前发布内容作为只读修订零快照。
                var current = await db.FlowVersions.AsNoTracking().SingleOrDefaultAsync(item =>
                    item.Id == flow.PublishedVersionId, cancellationToken);
                definition = current?.DefinitionJson ?? throw new UserFriendlyException(ErrorCodes.Conflict,
                    "流程尚无可导出的草稿。");
            }
        }
        else
        {
            definition = await db.FlowVersions.AsNoTracking().Where(item =>
                    item.FlowId == flow.Id && item.Version == version)
                .Select(item => item.DefinitionJson).SingleOrDefaultAsync(cancellationToken)
                ?? throw new UserFriendlyException(ErrorCodes.NotFound, "指定流程发布版本不存在。");
        }

        await transaction.CommitAsync(cancellationToken);
        return definition;
    }
}
