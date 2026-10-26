using ApprovalService.Domain.Center;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// 审批运行库中的个人委托规则集合。
/// </summary>
public partial class ApprovalDbContext
{
    /// <summary>
    /// 审批人的个人委托规则。
    /// </summary>
    public DbSet<DelegationRuleEntity> DelegationRules => Set<DelegationRuleEntity>();
}
