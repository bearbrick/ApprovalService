using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Organization;

/// <summary>
/// 在组织写入事务中单调推进相关账号的令牌代际。
/// </summary>
internal static class OrganizationIdentityGeneration
{
    /// <summary>
    /// 递增已有账号的令牌版本；尚无账号的人员不可能持有有效身份令牌。
    /// </summary>
    /// <param name="db">已开启组织写入事务的上下文。</param>
    /// <param name="personIds">状态或绑定变化影响的人员 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    public static async Task AdvanceAsync(ApprovalDbContext db, IEnumerable<string?> personIds,
        CancellationToken cancellationToken)
    {
        foreach (var personId in personIds.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct())
        {
            // 条件更新与组织变化同事务提交，旧会话即使恢复成原绑定也不能复活。
            await db.LocalAccounts.Where(item => item.PersonId == personId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.TokenVersion,
                    item => item.TokenVersion + 1), cancellationToken);
        }
    }
}
