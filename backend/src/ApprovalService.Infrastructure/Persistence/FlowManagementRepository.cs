using System.Data;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// 在当前组织快照下授权并原子保存流程草稿与不可变发布版。
/// </summary>
/// <param name="factory">已选数据库提供程序的上下文工厂。</param>
public sealed class FlowManagementRepository(IDbContextFactory<ApprovalDbContext> factory) : IFlowManagementRepository
{
    /// <inheritdoc />
    public async Task<FlowManagerIdentity> LoadIdentityAsync(string personId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await ReadIdentityAsync(db, personId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ManagedFlowSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var flows = await db.Flows.AsNoTracking().OrderBy(item => item.Code).ToArrayAsync(cancellationToken);
        var drafts = await db.FlowDrafts.AsNoTracking().ToDictionaryAsync(item => item.FlowId, cancellationToken);
        var currentIds = flows.Where(item => item.PublishedVersionId is not null)
            .Select(item => item.PublishedVersionId!).ToArray();
        var published = await db.FlowVersions.AsNoTracking().Where(item => currentIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        return flows.Select(flow => new ManagedFlowSnapshot
        {
            Flow = flow,
            Draft = drafts.GetValueOrDefault(flow.Id),
            Published = flow.PublishedVersionId is not null
                ? published.GetValueOrDefault(flow.PublishedVersionId) : null,
        }).ToArray();
    }

    /// <inheritdoc />
    public async Task<ManagedFlowSnapshot?> LoadAsync(string code, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var flow = await db.Flows.AsNoTracking().SingleOrDefaultAsync(item => item.Code == code,
            cancellationToken);
        return flow is null ? null : await SnapshotAsync(db, flow, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ManagedFlowSnapshot> CreateAsync(FlowDraftWrite input,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var flow = new FlowEntity
        {
            Id = input.FlowId,
            Code = input.Code,
            Name = input.Name,
            Group = input.Group,
            AdministratorId = input.AdministratorId,
            OwnerDepartmentId = input.OwnerDepartmentId,
            CreatedAt = input.OccurredAt,
        };
        await RequireManagementAsync(db, input.ActorId, input.IsLocalSession, flow, cancellationToken);
        await ValidateReferencesAsync(db, flow, cancellationToken);
        if (await db.Flows.AnyAsync(item => item.Code == input.Code, cancellationToken))
        {
            throw Conflict("流程编码已存在，请换一个编码。");
        }

        var draft = new FlowDraftEntity
        {
            FlowId = flow.Id,
            Revision = 1,
            DefinitionJson = input.DefinitionJson,
            UpdatedAt = input.OccurredAt,
        };
        db.Flows.Add(flow);
        db.FlowDrafts.Add(draft);
        db.FlowManagementAudits.Add(Audit(input, "create", draft.Revision));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConflict(exception))
        {
            throw Conflict("流程编码已被其他管理员创建，请刷新列表。");
        }

        return new ManagedFlowSnapshot { Flow = flow, Draft = draft };
    }

    /// <inheritdoc />
    public async Task<ManagedFlowSnapshot> ReplaceDraftAsync(FlowDraftWrite input,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var flow = await db.Flows.SingleOrDefaultAsync(item => item.Code == input.Code, cancellationToken)
            ?? throw NotFound();
        var draft = await db.FlowDrafts.SingleOrDefaultAsync(item => item.FlowId == flow.Id, cancellationToken);
        if (draft is null)
        {
            var oldVersion = flow.PublishedVersionId is not null
                ? await LoadVersionAsync(db, flow.PublishedVersionId, cancellationToken) : null;
            if (oldVersion is null)
            {
                throw Conflict("流程草稿不存在，请先恢复草稿。");
            }

            // 旧库只有发布版时，修订零表示尚未保存独立草稿；首次编辑再正式插入。
            draft = new FlowDraftEntity
            {
                FlowId = flow.Id,
                Revision = 0,
                DefinitionJson = oldVersion.DefinitionJson,
                UpdatedAt = oldVersion.PublishedAt,
            };
            db.FlowDrafts.Add(draft);
        }
        await RequireManagementAsync(db, input.ActorId, input.IsLocalSession, flow, cancellationToken);
        if (draft.Revision != input.ExpectedRevision)
        {
            throw Conflict("流程草稿已被其他人修改，请刷新后重试。");
        }

        var newScope = new FlowEntity { OwnerDepartmentId = input.OwnerDepartmentId };
        await RequireManagementAsync(db, input.ActorId, input.IsLocalSession, newScope, cancellationToken);
        await ValidateReferencesAsync(db, newScope, input.AdministratorId, cancellationToken);
        flow.Name = input.Name;
        flow.Group = input.Group;
        flow.OwnerDepartmentId = input.OwnerDepartmentId;
        flow.AdministratorId = input.AdministratorId;
        draft.DefinitionJson = input.DefinitionJson;
        draft.Revision++;
        draft.UpdatedAt = input.OccurredAt;
        db.FlowManagementAudits.Add(Audit(input, "save-draft", draft.Revision, flow.Id));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict("流程草稿已被其他人修改，请刷新后重试。");
        }
        catch (DbUpdateException exception) when (IsUniqueConflict(exception))
        {
            throw Conflict("流程草稿已由其他人创建，请刷新后重试。");
        }

        return new ManagedFlowSnapshot
        {
            Flow = flow,
            Draft = draft,
            Published = flow.PublishedVersionId is not null
                ? await LoadVersionAsync(db, flow.PublishedVersionId, cancellationToken) : null,
        };
    }

    /// <inheritdoc />
    public async Task<ManagedFlowSnapshot> PublishAsync(FlowPublishWrite input,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var flow = await db.Flows.SingleOrDefaultAsync(item => item.Code == input.Code, cancellationToken)
            ?? throw NotFound();
        var draft = await db.FlowDrafts.SingleOrDefaultAsync(item => item.FlowId == flow.Id, cancellationToken)
            ?? throw Conflict("流程草稿不存在，无法发布。");
        await RequireManagementAsync(db, input.ActorId, input.IsLocalSession, flow, cancellationToken);
        if (draft.Revision != input.ExpectedRevision || draft.DefinitionJson != input.DefinitionJson ||
            flow.PublishedVersionId != input.ExpectedPublishedVersionId)
        {
            throw Conflict("流程草稿或当前发布版已变化，请刷新后重试。");
        }

        var lastVersion = await db.FlowVersions.Where(item => item.FlowId == flow.Id)
            .MaxAsync(item => (int?)item.Version, cancellationToken) ?? 0;
        var version = new FlowVersionEntity
        {
            Id = input.VersionId,
            FlowId = flow.Id,
            Version = checked(lastVersion + 1),
            SchemaVersion = input.SchemaVersion,
            DefinitionJson = draft.DefinitionJson,
            GraphJson = input.GraphJson,
            PublishedAt = input.OccurredAt,
        };
        db.FlowVersions.Add(version);
        db.FlowManagementAudits.Add(new FlowManagementAuditEntity
        {
            Id = input.AuditId,
            FlowId = flow.Id,
            ActorId = input.ActorId,
            Action = "publish",
            Reason = input.Reason,
            DraftRevision = draft.Revision,
            PublishedVersion = version.Version,
            ContentSha256 = input.ContentSha256,
            OccurredAt = input.OccurredAt,
        });
        // 对草稿做一次带修订号的条件写，拦住发布与编辑并发交错。
        draft.UpdatedAt = input.OccurredAt;
        db.Entry(draft).Property(item => item.UpdatedAt).IsModified = true;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            var switched = await db.Flows.Where(item => item.Id == flow.Id &&
                    item.PublishedVersionId == input.ExpectedPublishedVersionId)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.PublishedVersionId, version.Id),
                    cancellationToken);
            if (switched != 1)
            {
                throw Conflict("当前发布版已变化，请刷新后重试。");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict("流程草稿已被其他人修改，请刷新后重试。");
        }
        catch (DbUpdateException exception) when (IsUniqueConflict(exception))
        {
            throw Conflict("流程版本已被其他管理员发布，请刷新后重试。");
        }

        flow.PublishedVersionId = version.Id;
        return new ManagedFlowSnapshot { Flow = flow, Draft = draft, Published = version };
    }

    /// <inheritdoc />
    public async Task<ManagedFlowSnapshot> SetEnabledAsync(FlowEnabledWrite input,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var flow = await db.Flows.AsNoTracking().SingleOrDefaultAsync(item => item.Code == input.Code,
            cancellationToken) ?? throw NotFound();
        await RequireManagementAsync(db, input.ActorId, input.IsLocalSession, flow, cancellationToken);
        if (flow.IsEnabled != input.ExpectedEnabled)
        {
            throw Conflict("流程启用状态已变化，请刷新后重试。");
        }

        if (flow.IsEnabled == input.Enabled)
        {
            await transaction.CommitAsync(cancellationToken);
            return await SnapshotAsync(db, flow, cancellationToken);
        }

        var switched = await db.Flows.Where(item => item.Id == flow.Id &&
                item.IsEnabled == input.ExpectedEnabled)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.IsEnabled, input.Enabled),
                cancellationToken);
        if (switched != 1)
        {
            throw Conflict("流程启用状态已变化，请刷新后重试。");
        }

        var published = flow.PublishedVersionId is not null
            ? await LoadVersionAsync(db, flow.PublishedVersionId, cancellationToken) : null;
        db.FlowManagementAudits.Add(new FlowManagementAuditEntity
        {
            Id = input.AuditId,
            FlowId = flow.Id,
            ActorId = input.ActorId,
            Action = input.Enabled ? "enable" : "disable",
            Reason = input.Reason,
            PublishedVersion = published?.Version,
            OccurredAt = input.OccurredAt,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        flow.IsEnabled = input.Enabled;
        return await SnapshotAsync(db, flow, cancellationToken);
    }

    /// <summary>
    /// 在当前数据库快照内读取人员、角色与直接部门关系。
    /// </summary>
    /// <param name="db">同一事务的数据库上下文。</param>
    /// <param name="personId">内部人员 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>当前身份。</returns>
    private static async Task<FlowManagerIdentity> ReadIdentityAsync(ApprovalDbContext db, string personId,
        CancellationToken cancellationToken)
    {
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(item => item.Id == personId,
            cancellationToken);
        var roles = await db.PersonRoles.AsNoTracking().Where(item => item.PersonId == personId)
            .Select(item => item.RoleCode).ToArrayAsync(cancellationToken);
        var departments = await db.PersonDepartments.AsNoTracking().Where(item => item.PersonId == personId)
            .Select(item => item.DepartmentId).ToArrayAsync(cancellationToken);
        return new FlowManagerIdentity
        {
            IsActive = person?.IsActive == true,
            IsOrganizationAdmin = roles.Contains("organization-admin", StringComparer.Ordinal),
            IsFlowAdmin = roles.Contains(FlowManagementAccessRules.FlowAdministratorRoleCode,
                StringComparer.Ordinal),
            DirectDepartmentIds = departments,
        };
    }

    /// <summary>
    /// 在写事务内重新核对本地会话、当前角色及直属部门范围。
    /// </summary>
    /// <param name="db">同一事务的上下文。</param>
    /// <param name="actorId">操作人内部 ID。</param>
    /// <param name="isLocalSession">是否为本地登录。</param>
    /// <param name="flow">待操作流程。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    internal static async Task RequireManagementAsync(ApprovalDbContext db, string actorId, bool isLocalSession,
        FlowEntity flow, CancellationToken cancellationToken)
    {
        var identity = await ReadIdentityAsync(db, actorId, cancellationToken);
        if (!FlowManagementAccessRules.CanManage(identity.IsActive, isLocalSession,
                identity.IsOrganizationAdmin, identity.IsFlowAdmin, identity.DirectDepartmentIds, flow))
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "没有管理该流程的权限。");
        }
    }

    /// <summary>
    /// 验证流程归属部门及运行期指定管理员使用内部 ID。
    /// </summary>
    /// <param name="db">同一事务的上下文。</param>
    /// <param name="flow">流程元数据。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    private static Task ValidateReferencesAsync(ApprovalDbContext db, FlowEntity flow,
        CancellationToken cancellationToken) =>
        ValidateReferencesAsync(db, flow, flow.AdministratorId, cancellationToken);

    /// <summary>
    /// 验证草稿元数据引用的部门与人员存在。
    /// </summary>
    /// <param name="db">同一事务的上下文。</param>
    /// <param name="flow">新权限归属。</param>
    /// <param name="administratorId">运行期管理员内部 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    private static async Task ValidateReferencesAsync(ApprovalDbContext db, FlowEntity flow,
        string? administratorId, CancellationToken cancellationToken)
    {
        if (flow.OwnerDepartmentId is not null && !await db.Departments.AsNoTracking()
                .AnyAsync(item => item.Id == flow.OwnerDepartmentId, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "流程所属部门不存在。");
        }

        if (administratorId is not null && !await db.People.AsNoTracking()
                .AnyAsync(item => item.Id == administratorId && item.IsActive, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                "流程管理员必须填写在职人员的内部 ID。");
        }
    }

    /// <summary>
    /// 将草稿写入生成仅包含摘要的管理审计。
    /// </summary>
    /// <param name="input">草稿写入。</param>
    /// <param name="action">管理操作种类。</param>
    /// <param name="revision">新草稿修订。</param>
    /// <param name="flowId">更新时已有的流程 ID。</param>
    /// <returns>审计记录。</returns>
    private static FlowManagementAuditEntity Audit(FlowDraftWrite input, string action, long revision,
        string? flowId = null) => new()
        {
            Id = input.AuditId,
            FlowId = flowId ?? input.FlowId,
            ActorId = input.ActorId,
            Action = action,
            Reason = input.Reason,
            DraftRevision = revision,
            ContentSha256 = input.ContentSha256,
            OccurredAt = input.OccurredAt,
        };

    /// <summary>
    /// 从当前上下文组装流程和版本读取快照。
    /// </summary>
    /// <param name="db">数据库上下文。</param>
    /// <param name="flow">流程元数据。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>流程读取快照。</returns>
    private static async Task<ManagedFlowSnapshot> SnapshotAsync(ApprovalDbContext db, FlowEntity flow,
        CancellationToken cancellationToken)
    {
        var published = flow.PublishedVersionId is not null
            ? await LoadVersionAsync(db, flow.PublishedVersionId, cancellationToken) : null;
        var draft = await db.FlowDrafts.AsNoTracking().SingleOrDefaultAsync(item => item.FlowId == flow.Id,
            cancellationToken);
        if (draft is null && published is not null)
        {
            // 存量库缺少草稿行时只构造读取视图；显式保存才会写入修订一。
            draft = new FlowDraftEntity
            {
                FlowId = flow.Id,
                Revision = 0,
                DefinitionJson = published.DefinitionJson,
                UpdatedAt = published.PublishedAt,
            };
        }

        return new ManagedFlowSnapshot { Flow = flow, Draft = draft, Published = published };
    }

    /// <summary>
    /// 按不可变版本 ID 读取发布版。
    /// </summary>
    /// <param name="db">数据库上下文。</param>
    /// <param name="versionId">当前版本 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>发布版。</returns>
    private static Task<FlowVersionEntity?> LoadVersionAsync(ApprovalDbContext db, string versionId,
        CancellationToken cancellationToken) =>
        db.FlowVersions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == versionId, cancellationToken);

    /// <summary>
    /// 创建用户可修正的并发冲突错误。
    /// </summary>
    /// <param name="message">中文说明。</param>
    /// <returns>统一冲突错误。</returns>
    private static UserFriendlyException Conflict(string message) => new(ErrorCodes.Conflict, message);

    /// <summary>
    /// 创建不存在的流程错误。
    /// </summary>
    /// <returns>统一未找到错误。</returns>
    private static UserFriendlyException NotFound() => new(ErrorCodes.NotFound, "流程不存在。");

    /// <summary>
    /// 仅把数据库唯一索引冲突转换为可修正错误，其它数据库缺陷原样暴露。
    /// </summary>
    /// <param name="exception">数据库更新异常。</param>
    /// <returns>由唯一索引拒绝时为真。</returns>
    private static bool IsUniqueConflict(DbUpdateException exception) => exception.InnerException switch
    {
        SqliteException sqlite => sqlite.SqliteErrorCode == 19,
        SqlException sqlServer => sqlServer.Number is 2601 or 2627,
        PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation,
        _ => false,
    };
}
