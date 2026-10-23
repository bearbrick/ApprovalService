using ApprovalService.Domain.Integration;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Integration;

/// <summary>
/// 用唯一键和乐观版本逐次分配编号；并发创建同日期桶时抢输者重读。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class ApprovalSerialNumberRepository(IDbContextFactory<ApprovalDbContext> factory)
    : IApprovalSerialNumberRepository
{
    /// <inheritdoc />
    public async Task<long> AllocateAsync(string prefix, string dateBucket, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var counter = await db.SerialNumberCounters.SingleOrDefaultAsync(item =>
                item.Prefix == prefix && item.DateBucket == dateBucket, cancellationToken);
            long allocated;
            if (counter is null)
            {
                allocated = 1;
                db.SerialNumberCounters.Add(new SerialNumberCounterEntity
                {
                    Prefix = prefix,
                    DateBucket = dateBucket,
                    NextSequence = 2,
                    Version = 1,
                });
            }
            else
            {
                allocated = counter.NextSequence;
                counter.NextSequence = checked(allocated + 1);
                counter.Version = checked(counter.Version + 1);
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return allocated;
            }
            catch (DbUpdateConcurrencyException)
            {
                // 同日期桶被并发分配时重读，不重复发放同一号码。
            }
            catch (DbUpdateException) when (counter is null)
            {
                // 并发首次创建日期桶时只有一方能插入主键，败者重读。
            }
        }

        throw new InvalidOperationException("审批编号分配持续遇到并发冲突，请稍后重试。");
    }
}
