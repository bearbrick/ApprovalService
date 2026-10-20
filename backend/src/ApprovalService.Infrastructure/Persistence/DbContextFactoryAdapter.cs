using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// 将所选数据库的工厂适配为统一运行库工厂。
/// </summary>
/// <typeparam name="TProvider">数据库专属上下文类型。</typeparam>
/// <param name="inner">EF Core 创建的数据库专属工厂。</param>
internal sealed class DbContextFactoryAdapter<TProvider>(IDbContextFactory<TProvider> inner) : IDbContextFactory<ApprovalDbContext>
    where TProvider : ApprovalDbContext
{
    /// <inheritdoc />
    public ApprovalDbContext CreateDbContext() => inner.CreateDbContext();

    /// <inheritdoc />
    public async Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        await inner.CreateDbContextAsync(cancellationToken);
}
