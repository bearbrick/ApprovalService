using System.Data;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Domain.Center;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Center;

/// <summary>
/// 将委托规则保存在审批数据库，并在串行化事务中防止范围重叠。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class DelegationRuleRepository(IDbContextFactory<ApprovalDbContext> factory) : IDelegationRuleRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DelegationRuleEntity>> ListAsync(string principalId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rules = await db.DelegationRules.AsNoTracking().Where(item => item.PrincipalId == principalId)
            .OrderBy(item => item.StartsAtUtc).ThenBy(item => item.Id).ToArrayAsync(cancellationToken);
        return rules.Select(Decode).ToArray();
    }

    /// <inheritdoc />
    public async Task CreateAsync(DelegationRuleEntity rule, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await db.DelegationRules.Where(item => item.PrincipalId == rule.PrincipalId &&
            item.StartsAtUtc < rule.EndsAtUtc && rule.StartsAtUtc < item.EndsAtUtc)
            .ToArrayAsync(cancellationToken);
        DelegationRules.Validate(rule, existing.Select(Decode).ToArray());
        rule.FlowCodesJson = JsonSerializer.Serialize(rule.FlowCodes, ApprovalJson.Options);
        db.DelegationRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string principalId, string id, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rule = await db.DelegationRules.SingleOrDefaultAsync(item => item.Id == id &&
            item.PrincipalId == principalId, cancellationToken);
        if (rule is null)
        {
            return false;
        }
        db.DelegationRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DelegationRuleEntity>> ListActiveAsync(string flowCode, DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rules = await db.DelegationRules.AsNoTracking().Where(item =>
            item.StartsAtUtc <= occurredAt && occurredAt < item.EndsAtUtc).ToArrayAsync(cancellationToken);
        return rules.Select(Decode).Where(item => item.FlowCodes.Count == 0 ||
            item.FlowCodes.Contains(flowCode, StringComparer.Ordinal)).ToArray();
    }

    /// <summary>
    /// 将数据库范围 JSON 还原成领域列表。
    /// </summary>
    /// <param name="rule">数据库实体。</param>
    /// <returns>可用于领域校验和引擎快照的实体。</returns>
    private static DelegationRuleEntity Decode(DelegationRuleEntity rule)
    {
        rule.FlowCodes = JsonSerializer.Deserialize<List<string>>(rule.FlowCodesJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("委托规则的流程范围为空。");
        return rule;
    }
}
