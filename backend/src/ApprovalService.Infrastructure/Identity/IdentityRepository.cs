using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Identity;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Identity;

/// <summary>
/// 使用数据库条件更新保证刷新和嵌入令牌只能消费一次。
/// </summary>
/// <param name="factory">按已配置提供程序创建上下文。</param>
public sealed class IdentityRepository(IDbContextFactory<ApprovalDbContext> factory) : IIdentityRepository
{
    /// <inheritdoc />
    public async Task<IdentityAccountSnapshot?> FindByEmployeeNumberAsync(string employeeNumber,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
            item => item.EmployeeNumber == employeeNumber, cancellationToken);
        return person is null ? null : await LoadSnapshotAsync(db, person, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IdentityAccountSnapshot?> FindByPersonIdAsync(string personId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == personId, cancellationToken);
        return person is null ? null : await LoadSnapshotAsync(db, person, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsEmbedSubjectActiveAsync(string clientAppCode, string externalUserId, string personId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await (from binding in db.ExternalPersonBindings.AsNoTracking()
                      join app in db.ClientApps.AsNoTracking() on binding.ClientAppCode equals app.Code
                      where binding.ClientAppCode == clientAppCode && binding.ExternalUserId == externalUserId &&
                          binding.PersonId == personId && app.IsEnabled
                      select binding.PersonId).AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task CreateAccountAsync(LocalAccountEntity account, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.LocalAccounts.Add(account);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // 并发开通只能有一个成功；只把已存在账号的冲突转为使用者可修正的错误。
            await using var checkDb = await factory.CreateDbContextAsync(cancellationToken);
            if (await checkDb.LocalAccounts.AnyAsync(item => item.PersonId == account.PersonId, cancellationToken))
            {
                throw new UserFriendlyException(ErrorCodes.Conflict, "账号已开通。");
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> RecordFailedLoginAsync(string personId, DateTimeOffset now, int maxAttempts,
        TimeSpan lockoutDuration, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var until = now.Add(lockoutDuration);
        await db.LocalAccounts.Where(item => item.PersonId == personId &&
            (item.LockedUntil == null || item.LockedUntil <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.FailedLogins,
                    item => item.LockedUntil != null ? 1 : item.FailedLogins + 1)
                .SetProperty(item => item.LockedUntil,
                    item => item.LockedUntil != null
                        ? (maxAttempts <= 1 ? until : (DateTimeOffset?)null)
                        : item.FailedLogins + 1 >= maxAttempts ? until : (DateTimeOffset?)null), cancellationToken);
        return await db.LocalAccounts.AsNoTracking().Where(item => item.PersonId == personId)
            .Select(item => item.LockedUntil).SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ResetFailedLoginsAsync(string personId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var changed = await db.LocalAccounts.Where(item => item.PersonId == personId &&
            (item.LockedUntil == null || item.LockedUntil <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.FailedLogins, 0)
                .SetProperty(item => item.LockedUntil, (DateTimeOffset?)null), cancellationToken);
        return changed == 1;
    }

    /// <inheritdoc />
    public async Task<bool> ChangePasswordAsync(string personId, long expectedVersion, string passwordHash,
        string passwordSalt, bool mustChangePassword, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var count = await db.LocalAccounts.Where(item => item.PersonId == personId &&
            item.TokenVersion == expectedVersion).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.PasswordHash, passwordHash)
                .SetProperty(item => item.PasswordSalt, passwordSalt)
                .SetProperty(item => item.MustChangePassword, mustChangePassword)
                .SetProperty(item => item.TokenVersion, item => item.TokenVersion + 1), cancellationToken);
        return count == 1;
    }

    /// <inheritdoc />
    public async Task<long?> RevokeAllTokensAsync(string personId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var count = await db.LocalAccounts.Where(item => item.PersonId == personId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.TokenVersion,
                item => item.TokenVersion + 1), cancellationToken);
        return count == 0 ? null : await db.LocalAccounts.AsNoTracking()
            .Where(item => item.PersonId == personId)
            .Select(item => (long?)item.TokenVersion)
            .SingleAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddRefreshTokenAsync(RefreshTokenEntity token, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RefreshTokenEntity?> ConsumeRefreshTokenAsync(string tokenHash, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var changed = await db.RefreshTokens.Where(item => item.TokenHash == tokenHash &&
            item.ConsumedAt == null && item.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConsumedAt, now), cancellationToken);
        return changed == 1 ? await db.RefreshTokens.AsNoTracking().SingleAsync(
            item => item.TokenHash == tokenHash, cancellationToken) : null;
    }

    /// <inheritdoc />
    public async Task AddEmbedTokenAsync(EmbedTokenEntity token, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.EmbedTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmbedTokenEntity?> ConsumeEmbedTokenAsync(string tokenHash, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var changed = await db.EmbedTokens.Where(item => item.TokenHash == tokenHash &&
            item.ConsumedAt == null && item.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConsumedAt, now), cancellationToken);
        return changed == 1 ? await db.EmbedTokens.AsNoTracking().SingleAsync(
            item => item.TokenHash == tokenHash, cancellationToken) : null;
    }

    /// <summary>
    /// 将一次人员查询补上可能尚未开通的账号。
    /// </summary>
    /// <param name="db">当前上下文。</param>
    /// <param name="person">已找到的人员。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>人员与账号快照。</returns>
    private static async Task<IdentityAccountSnapshot> LoadSnapshotAsync(ApprovalDbContext db, PersonEntity person,
        CancellationToken cancellationToken) => new()
        {
            Person = person,
            Account = await db.LocalAccounts.AsNoTracking().SingleOrDefaultAsync(
                item => item.PersonId == person.Id, cancellationToken),
        };
}
