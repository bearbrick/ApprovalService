using ApprovalService.Domain.Notifications;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Notifications;

/// <summary>
/// 使用独立数据库上下文保存个人订阅与每日汇总全局设置。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class NotificationSettingsRepository(IDbContextFactory<ApprovalDbContext> factory)
    : INotificationSettingsRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationPreferenceEntity>> ListAsync(string personId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.NotificationPreferences.AsNoTracking().Where(item => item.PersonId == personId)
            .OrderBy(item => item.Category).ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsEnabledAsync(string personId, string category, string channelCode,
        CancellationToken cancellationToken = default)
    {
        if (category == NotificationCategories.DailyDigest && channelCode != NotificationChannelCodes.Email)
        {
            return false;
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var item = await db.NotificationPreferences.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.PersonId == personId && candidate.Category == category, cancellationToken);
        return item is null || (channelCode switch
        {
            NotificationChannelCodes.Email => item.EmailEnabled,
            NotificationChannelCodes.InApp => item.InAppEnabled,
            _ => false,
        });
    }

    /// <inheritdoc />
    public async Task SetAsync(NotificationPreferenceEntity preference,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var updated = await db.NotificationPreferences.Where(item => item.PersonId == preference.PersonId &&
                item.Category == preference.Category)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.EmailEnabled, preference.EmailEnabled)
                .SetProperty(item => item.InAppEnabled, preference.InAppEnabled), cancellationToken);
        if (updated != 0)
        {
            return;
        }

        db.NotificationPreferences.Add(preference);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // 首次设置并发插入时重新更新；不吞掉非主键冲突或数据库连接错误。
            await using var retry = await factory.CreateDbContextAsync(cancellationToken);
            var changed = await retry.NotificationPreferences.Where(item => item.PersonId == preference.PersonId &&
                    item.Category == preference.Category)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.EmailEnabled, preference.EmailEnabled)
                    .SetProperty(item => item.InAppEnabled, preference.InAppEnabled), cancellationToken);
            if (changed == 0)
            {
                throw;
            }
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetDailyDigestTimeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.DailyDigestSettings.AsNoTracking().Where(item => item.Id == "global")
            .Select(item => item.LocalSendTime).SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetDailyDigestTimeAsync(string? localTime, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var updated = await db.DailyDigestSettings.Where(item => item.Id == "global")
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LocalSendTime, localTime),
                cancellationToken);
        if (updated != 0)
        {
            return;
        }

        db.DailyDigestSettings.Add(new DailyDigestSettingsEntity { LocalSendTime = localTime });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await using var retry = await factory.CreateDbContextAsync(cancellationToken);
            var changed = await retry.DailyDigestSettings.Where(item => item.Id == "global")
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LocalSendTime, localTime),
                    cancellationToken);
            if (changed == 0)
            {
                throw;
            }
        }
    }
}
