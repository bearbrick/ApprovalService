using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Administration;

/// <summary>
/// 修正唯一匹配的存量流程管理员标识，并保存需人工处理的事项。
/// </summary>
/// <param name="factory">当前数据库提供程序的上下文工厂。</param>
/// <param name="timeProvider">记录首次发现时间的时钟。</param>
public sealed class FlowAdministratorIdentityReconciler(IDbContextFactory<ApprovalDbContext> factory,
    TimeProvider timeProvider)
{
    /// <summary>
    /// 本机命令执行前把选定数据库升级到当前模型，不在 HTTP 启动时运行。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    public async Task PrepareDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// 在只读一致快照上预览唯一匹配的改写和需人工处理的歧义。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>变更计划和并发指纹。</returns>
    public async Task<FlowAdministratorIdentityPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var flows = await db.Flows.AsNoTracking().ToArrayAsync(cancellationToken);
        var people = await db.People.AsNoTracking().ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return BuildPreview(flows, people);
    }

    /// <summary>
    /// 仅在输入仍与预览相同时完成唯一匹配修正和歧义事项持久化。
    /// </summary>
    /// <param name="expectedFingerprint">最近一次人工预览返回的输入指纹。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    public async Task ReconcileAsync(string expectedFingerprint, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var flows = await db.Flows.ToArrayAsync(cancellationToken);
        var people = await db.People.AsNoTracking().ToArrayAsync(cancellationToken);
        if (BuildPreview(flows, people).Fingerprint != expectedFingerprint)
        {
            throw new UserFriendlyException(ErrorCodes.Conflict,
                "预览后流程或人员主数据已变化，请重新预览再提交。");
        }

        var issues = (await db.FlowAdministratorIdentityIssues.ToArrayAsync(cancellationToken))
            .ToDictionary(item => item.FlowId, StringComparer.Ordinal);

        foreach (var flow in flows)
        {
            if (string.IsNullOrWhiteSpace(flow.AdministratorId))
            {
                // 管理员已清除时，旧待办事项不再对应当前流程状态。
                if (issues.TryGetValue(flow.Id, out var staleIssue))
                {
                    db.FlowAdministratorIdentityIssues.Remove(staleIssue);
                }

                continue;
            }

            var result = FlowAdministratorIdentityRules.Resolve(flow.AdministratorId, people);
            if (result.PersonId is not null)
            {
                // 匹配对象只有一个时，即使原值也是另一个人的工号，也统一写内部 ID。
                flow.AdministratorId = result.PersonId;
                if (issues.TryGetValue(flow.Id, out var resolvedIssue))
                {
                    db.FlowAdministratorIdentityIssues.Remove(resolvedIssue);
                }

                continue;
            }

            // 零匹配与多匹配都留在库中，不能猜测值的真实含义。
            if (!issues.TryGetValue(flow.Id, out var issue))
            {
                db.FlowAdministratorIdentityIssues.Add(new FlowAdministratorIdentityIssueEntity
                {
                    FlowId = flow.Id,
                    LegacyValue = flow.AdministratorId,
                    MatchCount = result.MatchCount,
                    DetectedAt = timeProvider.GetUtcNow(),
                });
            }
            else if (issue.LegacyValue != flow.AdministratorId || issue.MatchCount != result.MatchCount)
            {
                issue.LegacyValue = flow.AdministratorId;
                issue.MatchCount = result.MatchCount;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 对同一输入生成可显示的变更计划及稳定指纹。
    /// </summary>
    /// <param name="flows">流程元数据。</param>
    /// <param name="people">人员主数据。</param>
    /// <returns>变更计划。</returns>
    private static FlowAdministratorIdentityPreview BuildPreview(IReadOnlyCollection<FlowEntity> flows,
        IReadOnlyCollection<PersonEntity> people)
    {
        var entries = flows.Where(flow => !string.IsNullOrWhiteSpace(flow.AdministratorId))
            .OrderBy(flow => flow.Code, StringComparer.Ordinal)
            .Select(flow =>
            {
                var match = FlowAdministratorIdentityRules.Resolve(flow.AdministratorId!, people);
                return new FlowAdministratorIdentityPreviewEntry
                {
                    FlowCode = flow.Code,
                    LegacyValue = flow.AdministratorId!,
                    ResolvedPersonId = match.PersonId,
                    MatchCount = match.MatchCount,
                };
            }).ToArray();
        var fingerprintJson = JsonSerializer.Serialize(new
        {
            Flows = flows.OrderBy(flow => flow.Id, StringComparer.Ordinal)
                .Select(flow => new { flow.Id, flow.Code, flow.AdministratorId }),
            People = people.OrderBy(person => person.Id, StringComparer.Ordinal)
                .Select(person => new { person.Id, person.EmployeeNumber }),
        });
        return new FlowAdministratorIdentityPreview
        {
            Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintJson)))
                .ToLowerInvariant(),
            PlannedUpdates = entries.Where(entry => entry.ResolvedPersonId is not null &&
                entry.ResolvedPersonId != entry.LegacyValue).ToArray(),
            Unresolved = entries.Where(entry => entry.ResolvedPersonId is null).ToArray(),
        };
    }
}

/// <summary>
/// 本机一次性命令提交前显示的流程管理员 ID 对账计划。
/// </summary>
public sealed class FlowAdministratorIdentityPreview
{
    /// <summary>
    /// 所有参与解析的流程和人员标识的 SHA-256 指纹。
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>
    /// 唯一匹配且需要写回内部 ID 的流程。
    /// </summary>
    public IReadOnlyList<FlowAdministratorIdentityPreviewEntry> PlannedUpdates { get; set; } = [];

    /// <summary>
    /// 零匹配或多匹配、必须人工处理的流程。
    /// </summary>
    public IReadOnlyList<FlowAdministratorIdentityPreviewEntry> Unresolved { get; set; } = [];
}

/// <summary>
/// 一条流程管理员标识的预览结果。
/// </summary>
public sealed class FlowAdministratorIdentityPreviewEntry
{
    /// <summary>
    /// 稳定流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 原管理员标识。
    /// </summary>
    public string LegacyValue { get; set; } = string.Empty;

    /// <summary>
    /// 唯一匹配时的内部人员 ID。
    /// </summary>
    public string? ResolvedPersonId
    {
        get; set;
    }

    /// <summary>
    /// 去重后的匹配人数。
    /// </summary>
    public int MatchCount
    {
        get; set;
    }
}
