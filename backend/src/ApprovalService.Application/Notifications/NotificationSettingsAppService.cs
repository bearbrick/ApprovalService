using System.Globalization;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Notifications;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Notifications;

/// <summary>
/// 统一校验通知设置并委托持久化仓储。
/// </summary>
/// <param name="repository">设置仓储。</param>
public sealed class NotificationSettingsAppService(INotificationSettingsRepository repository)
    : INotificationSettingsAppService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationPreferenceDto>> GetPreferencesAsync(string personId,
        CancellationToken cancellationToken = default)
    {
        RequirePerson(personId);
        var saved = (await repository.ListAsync(personId, cancellationToken))
            .ToDictionary(item => item.Category, StringComparer.Ordinal);
        return NotificationCategories.All.Select(category => new NotificationPreferenceDto
        {
            Category = category,
            EmailEnabled = !saved.TryGetValue(category, out var value) || value.EmailEnabled,
            InAppEnabled = category != NotificationCategories.DailyDigest &&
                (!saved.TryGetValue(category, out value) || value.InAppEnabled),
        }).ToArray();
    }

    /// <inheritdoc />
    public async Task<NotificationPreferenceDto> SetPreferenceAsync(string personId, string category,
        bool emailEnabled, bool inAppEnabled, CancellationToken cancellationToken = default)
    {
        RequirePerson(personId);
        if (!NotificationCategories.All.Contains(category, StringComparer.Ordinal))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "提醒类别不受支持。");
        }

        if (category == NotificationCategories.DailyDigest && inAppEnabled)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "每日待办汇总仅支持邮件渠道。");
        }

        var value = new NotificationPreferenceEntity
        {
            PersonId = personId,
            Category = category,
            EmailEnabled = emailEnabled,
            InAppEnabled = inAppEnabled,
        };
        await repository.SetAsync(value, cancellationToken);
        return new NotificationPreferenceDto
        {
            Category = category,
            EmailEnabled = emailEnabled,
            InAppEnabled = inAppEnabled,
        };
    }

    /// <inheritdoc />
    public Task<string?> GetDailyDigestTimeAsync(CancellationToken cancellationToken = default) =>
        repository.GetDailyDigestTimeAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<string?> SetDailyDigestTimeAsync(string? localTime,
        CancellationToken cancellationToken = default)
    {
        if (localTime is not null && !TimeOnly.TryParseExact(localTime, "HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "每日待办汇总时间必须使用 HH:mm 格式。");
        }

        await repository.SetDailyDigestTimeAsync(localTime, cancellationToken);
        return localTime;
    }

    /// <summary>
    /// 设置只能作用于已认证的内部人员。
    /// </summary>
    /// <param name="personId">内部人员 ID。</param>
    private static void RequirePerson(string personId)
    {
        if (string.IsNullOrWhiteSpace(personId) || personId.Length > 128)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "人员 ID 不正确。");
        }
    }
}
