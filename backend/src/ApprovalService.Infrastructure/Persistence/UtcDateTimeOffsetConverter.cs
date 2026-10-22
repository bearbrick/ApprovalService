using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// 将带时区时间统一存成 UTC DateTime，使 SQLite 也能在数据库中比较到期与租约时间。
/// </summary>
internal sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTime>
{
    /// <summary>
    /// 建立 UTC 时间的双向转换。
    /// </summary>
    public UtcDateTimeOffsetConverter()
        : base(value => value.UtcDateTime, value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)))
    {
    }
}
