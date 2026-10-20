using ApprovalService.Domain.Integration;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Integration;

/// <summary>
/// 从流程与版本表读取当前已发布版本，不读取草稿。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class FlowCatalogRepository(IDbContextFactory<ApprovalDbContext> factory) : IFlowCatalogRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PublishedFlowSnapshot>> ListPublishedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await (from flow in db.Flows.AsNoTracking()
                      join version in db.FlowVersions.AsNoTracking() on flow.PublishedVersionId equals version.Id
                      orderby flow.Code
                      select new PublishedFlowSnapshot { Flow = flow, Version = version }).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PublishedFlowSnapshot?> LoadPublishedAsync(string code, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await (from flow in db.Flows.AsNoTracking()
                      join version in db.FlowVersions.AsNoTracking() on flow.PublishedVersionId equals version.Id
                      where flow.Code == code
                      select new PublishedFlowSnapshot { Flow = flow, Version = version })
            .SingleOrDefaultAsync(cancellationToken);
    }
}
