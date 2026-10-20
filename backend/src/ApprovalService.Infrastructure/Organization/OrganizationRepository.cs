using System.Data;
using System.Text.Json;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Assignees;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Organization;

/// <summary>
/// 组织主数据与绑定仓储；每次批量变更在一个数据库事务中留审计记录。
/// </summary>
/// <param name="factory">数据库上下文工厂。</param>
public sealed class OrganizationRepository(IDbContextFactory<ApprovalDbContext> factory) : IOrganizationRepository
{
    /// <inheritdoc />
    public async Task<OrganizationImportBatch> LoadMasterDataAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return new OrganizationImportBatch
        {
            Departments = await db.Departments.AsNoTracking().ToListAsync(cancellationToken),
            People = await db.People.AsNoTracking().ToListAsync(cancellationToken),
            Memberships = await db.PersonDepartments.AsNoTracking().ToListAsync(cancellationToken),
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> LoadClientAppCodesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ClientApps.AsNoTracking().Select(item => item.Code).ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OrganizationImportWriteResult> SaveTrustedImportAsync(OrganizationImportBatch batch, string actorId,
        DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var issues = await ValidateCurrentAsync(db, batch, cancellationToken);
        if (issues.Count > 0)
        {
            return new OrganizationImportWriteResult { Issues = issues };
        }

        foreach (var department in batch.Departments)
        {
            var existing = await db.Departments.SingleOrDefaultAsync(item => item.Id == department.Id, cancellationToken);
            var before = existing is null ? null : JsonSerializer.Serialize(existing);
            department.TrustedSource = "approvalService";
            department.Version = (existing?.Version ?? 0) + 1;
            if (existing is null)
            {
                db.Departments.Add(department);
            }
            else
            {
                db.Entry(existing).CurrentValues.SetValues(department);
            }

            Record(db, "department", department.Id, actorId, "approvalService", before,
                JsonSerializer.Serialize(department), occurredAt);
        }

        foreach (var person in batch.People)
        {
            var existing = await db.People.SingleOrDefaultAsync(item => item.Id == person.Id, cancellationToken);
            var before = existing is null ? null : JsonSerializer.Serialize(existing);
            person.TrustedSource = "approvalService";
            person.Version = (existing?.Version ?? 0) + 1;
            if (existing is null)
            {
                db.People.Add(person);
            }
            else
            {
                var wasActive = existing.IsActive;
                db.Entry(existing).CurrentValues.SetValues(person);
                if (wasActive && !person.IsActive)
                {
                    await AddDepartureNoticeAsync(db, person.Id, occurredAt, cancellationToken);
                }
            }

            Record(db, "person", person.Id, actorId, "approvalService", before,
                JsonSerializer.Serialize(person), occurredAt);
        }

        foreach (var membership in batch.Memberships)
        {
            var existing = await db.PersonDepartments.SingleOrDefaultAsync(item =>
                item.PersonId == membership.PersonId && item.DepartmentId == membership.DepartmentId,
                cancellationToken);
            var before = existing is null ? null : JsonSerializer.Serialize(existing);
            if (existing is null)
            {
                db.PersonDepartments.Add(membership);
            }
            else
            {
                existing.IsPrimary = membership.IsPrimary;
            }

            Record(db, "membership", membership.PersonId + ":" + membership.DepartmentId,
                actorId, "approvalService", before,
                JsonSerializer.Serialize(membership), occurredAt);
        }

        foreach (var binding in batch.PersonBindings)
        {
            var existing = await db.ExternalPersonBindings.SingleOrDefaultAsync(item =>
                item.ClientAppCode == binding.ClientAppCode && item.ExternalUserId == binding.ExternalUserId,
                cancellationToken);
            var before = existing is null ? null : JsonSerializer.Serialize(existing);
            if (existing is null)
            {
                db.ExternalPersonBindings.Add(binding);
            }
            else
            {
                db.Entry(existing).CurrentValues.SetValues(binding);
            }

            Record(db, "personBinding", binding.ClientAppCode + ":" + binding.ExternalUserId,
                actorId, "approvalService", before, JsonSerializer.Serialize(binding), occurredAt);
        }

        foreach (var binding in batch.DepartmentBindings)
        {
            var existing = await db.ExternalDepartmentBindings.SingleOrDefaultAsync(item =>
                item.ClientAppCode == binding.ClientAppCode && item.ExternalDepartmentId == binding.ExternalDepartmentId,
                cancellationToken);
            var before = existing is null ? null : JsonSerializer.Serialize(existing);
            if (existing is null)
            {
                db.ExternalDepartmentBindings.Add(binding);
            }
            else
            {
                db.Entry(existing).CurrentValues.SetValues(binding);
            }

            Record(db, "departmentBinding", binding.ClientAppCode + ":" + binding.ExternalDepartmentId,
                actorId, "approvalService", before, JsonSerializer.Serialize(binding), occurredAt);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OrganizationImportWriteResult();
    }

    /// <inheritdoc />
    public async Task<OrganizationImportWriteResult> SavePushedChangesAsync(OrganizationImportBatch batch,
        string clientAppCode, string actorId, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var issues = await ValidateCurrentAsync(db, batch, cancellationToken);
        if (issues.Count > 0)
        {
            return new OrganizationImportWriteResult { Issues = issues };
        }

        var departed = new List<string>();
        foreach (var department in batch.Departments)
        {
            var existing = await db.Departments.SingleOrDefaultAsync(item => item.Id == department.Id, cancellationToken);
            if (existing is null)
            {
                department.TrustedSource = clientAppCode;
                department.Version = 1;
                db.Departments.Add(department);
                Record(db, "department", department.Id, actorId, clientAppCode, null,
                    JsonSerializer.Serialize(department), occurredAt);
            }
            else if (existing.TrustedSource == clientAppCode)
            {
                var before = JsonSerializer.Serialize(existing);
                department.TrustedSource = clientAppCode;
                department.Version = existing.Version + 1;
                db.Entry(existing).CurrentValues.SetValues(department);
                Record(db, "department", department.Id, actorId, clientAppCode, before,
                    JsonSerializer.Serialize(department), occurredAt);
            }
        }

        foreach (var person in batch.People)
        {
            var existing = await db.People.SingleOrDefaultAsync(item => item.Id == person.Id, cancellationToken);
            if (existing is null)
            {
                person.TrustedSource = clientAppCode;
                person.Version = 1;
                db.People.Add(person);
                Record(db, "person", person.Id, actorId, clientAppCode, null,
                    JsonSerializer.Serialize(person), occurredAt);
            }
            else if (existing.TrustedSource == clientAppCode)
            {
                var before = JsonSerializer.Serialize(existing);
                var wasActive = existing.IsActive;
                person.TrustedSource = clientAppCode;
                person.Version = existing.Version + 1;
                db.Entry(existing).CurrentValues.SetValues(person);
                Record(db, "person", person.Id, actorId, clientAppCode, before,
                    JsonSerializer.Serialize(person), occurredAt);
                if (wasActive && !person.IsActive)
                {
                    departed.Add(person.Id);
                    await AddDepartureNoticeAsync(db, person.Id, occurredAt, cancellationToken);
                }
            }
            // 其他来源只能维护绑定，不能覆盖可信主数据。
        }

        foreach (var membership in batch.Memberships)
        {
            var person = await db.People.FindAsync([membership.PersonId], cancellationToken);
            var department = await db.Departments.FindAsync([membership.DepartmentId], cancellationToken);
            if (person is null || person.TrustedSource != clientAppCode ||
                department is null || department.TrustedSource != clientAppCode)
            {
                continue;
            }

            var existing = await db.PersonDepartments.SingleOrDefaultAsync(item =>
                item.PersonId == membership.PersonId && item.DepartmentId == membership.DepartmentId,
                cancellationToken);
            var before = existing is null ? null : JsonSerializer.Serialize(existing);
            if (existing is null)
            {
                db.PersonDepartments.Add(membership);
            }
            else
            {
                existing.IsPrimary = membership.IsPrimary;
            }

            Record(db, "membership", membership.PersonId + ":" + membership.DepartmentId,
                actorId, clientAppCode, before, JsonSerializer.Serialize(membership), occurredAt);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OrganizationImportWriteResult { DepartedPersonIds = departed };
    }

    /// <inheritdoc />
    public async Task<PersonEntity?> MatchPersonAsync(string? employeeNumber, string? phone,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(employeeNumber))
        {
            return await db.People.AsNoTracking().SingleOrDefaultAsync(item =>
                item.EmployeeNumber == employeeNumber, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var matches = await db.People.AsNoTracking().Where(item => item.Phone == phone)
            .Take(2).ToArrayAsync(cancellationToken);
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <inheritdoc />
    public async Task<PersonEntity?> GetPersonAsync(string personId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.People.AsNoTracking().SingleOrDefaultAsync(item => item.Id == personId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task BindPersonAsync(ExternalPersonBindingEntity binding, string actorId,
        DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (!await db.People.AnyAsync(item => item.Id == binding.PersonId, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "绑定的人员不存在。");
        }

        var existing = await db.ExternalPersonBindings.SingleOrDefaultAsync(item =>
            item.ClientAppCode == binding.ClientAppCode && item.ExternalUserId == binding.ExternalUserId,
            cancellationToken);
        var before = existing is null ? null : JsonSerializer.Serialize(existing);
        if (existing is null)
        {
            db.ExternalPersonBindings.Add(binding);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(binding);
        }

        Record(db, "personBinding", binding.ClientAppCode + ":" + binding.ExternalUserId,
            actorId, binding.ClientAppCode, before, JsonSerializer.Serialize(binding), occurredAt);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task BindDepartmentAsync(ExternalDepartmentBindingEntity binding, string actorId,
        DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (!await db.Departments.AnyAsync(item => item.Id == binding.DepartmentId, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "绑定的部门不存在。");
        }

        var existing = await db.ExternalDepartmentBindings.SingleOrDefaultAsync(item =>
            item.ClientAppCode == binding.ClientAppCode && item.ExternalDepartmentId == binding.ExternalDepartmentId,
            cancellationToken);
        var before = existing is null ? null : JsonSerializer.Serialize(existing);
        if (existing is null)
        {
            db.ExternalDepartmentBindings.Add(binding);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(binding);
        }

        Record(db, "departmentBinding", binding.ClientAppCode + ":" + binding.ExternalDepartmentId,
            actorId, binding.ClientAppCode, before, JsonSerializer.Serialize(binding), occurredAt);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> ResolvePersonAsync(string clientAppCode, string externalUserId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ExternalPersonBindings.AsNoTracking()
            .Where(item => item.ClientAppCode == clientAppCode && item.ExternalUserId == externalUserId)
            .Select(item => item.PersonId).SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> ResolveDepartmentAsync(string clientAppCode, string externalDepartmentId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ExternalDepartmentBindings.AsNoTracking()
            .Where(item => item.ClientAppCode == clientAppCode && item.ExternalDepartmentId == externalDepartmentId)
            .Select(item => item.DepartmentId).SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AssigneeSnapshot> LoadAssigneeSnapshotAsync(string initiatorId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var people = await db.People.AsNoTracking().ToArrayAsync(cancellationToken);
        var departments = await db.Departments.AsNoTracking().ToArrayAsync(cancellationToken);
        var memberships = await db.PersonDepartments.AsNoTracking().ToArrayAsync(cancellationToken);
        var roles = await db.PersonRoles.AsNoTracking().ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var primary = memberships.FirstOrDefault(item => item.PersonId == initiatorId && item.IsPrimary);
        return new AssigneeSnapshot
        {
            InitiatorId = initiatorId,
            InitiatingDepartmentId = primary?.DepartmentId ?? string.Empty,
            People = people.Select(item => new AssigneePerson
            {
                Id = item.Id,
                DepartmentIds = memberships.Where(link => link.PersonId == item.Id)
                    .Select(link => link.DepartmentId).ToList(),
                ManagerId = item.ManagerId,
                Roles = roles.Where(link => link.PersonId == item.Id)
                    .Select(link => link.RoleCode).ToList(),
                Position = item.Position,
                Grade = item.Grade,
                GradeRank = item.GradeRank,
                IsActive = item.IsActive,
            }).ToList(),
            Departments = departments.Select(item => new AssigneeDepartment
            {
                Id = item.Id,
                ParentId = item.ParentId,
                HeadId = item.HeadPersonId,
                DivisionLeaderIds = JsonSerializer.Deserialize<List<string>>(item.DivisionLeaderIdsJson) ?? [],
            }).ToList(),
        };
    }

    /// <inheritdoc />
    public async Task<bool> IsOrganizationAdminAsync(string personId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.People.AsNoTracking().AnyAsync(person => person.Id == personId && person.IsActive,
            cancellationToken) && await db.PersonRoles.AsNoTracking().AnyAsync(role =>
            role.PersonId == personId && role.RoleCode == "organization-admin", cancellationToken);
    }

    /// <summary>
    /// 在写入事务当前快照内重验所有组织关系，避免预览与保存之间发生并发变化。
    /// </summary>
    /// <param name="db">已开启可串行化事务的上下文。</param>
    /// <param name="batch">待写入批次。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>当前快照下的全部校验问题。</returns>
    private static async Task<IReadOnlyList<OrganizationImportIssue>> ValidateCurrentAsync(ApprovalDbContext db,
        OrganizationImportBatch batch, CancellationToken cancellationToken)
    {
        var departments = await db.Departments.AsNoTracking().ToArrayAsync(cancellationToken);
        var people = await db.People.AsNoTracking().ToArrayAsync(cancellationToken);
        var memberships = await db.PersonDepartments.AsNoTracking().ToArrayAsync(cancellationToken);
        var clientAppCodes = await db.ClientApps.AsNoTracking().Select(item => item.Code)
            .ToArrayAsync(cancellationToken);
        return OrganizationImportRules.Validate(batch, departments, people, memberships, clientAppCodes);
    }

    /// <summary>
    /// 记录来源、操作者及前后快照；空前快照表示新增。
    /// </summary>
    /// <param name="db">同一事务的上下文。</param>
    /// <param name="kind">实体种类。</param>
    /// <param name="entityId">实体 ID。</param>
    /// <param name="actorId">操作者 ID。</param>
    /// <param name="source">来源。</param>
    /// <param name="before">变更前快照。</param>
    /// <param name="after">变更后快照。</param>
    /// <param name="occurredAt">发生时间。</param>
    private static void Record(ApprovalDbContext db, string kind, string entityId, string actorId,
        string source, string? before, string after, DateTimeOffset occurredAt) =>
        db.OrganizationChanges.Add(new OrganizationChangeEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityKind = kind,
            EntityId = entityId,
            ActorId = actorId,
            Source = source,
            Kind = before is null ? "created" : "updated",
            BeforeJson = before,
            AfterJson = after,
            OccurredAt = occurredAt,
        });

    /// <summary>
    /// 留下离职提醒和当前未办任务数；具体改派遵循节点离职规则，由运行用例接续。
    /// </summary>
    /// <param name="db">同一事务的上下文。</param>
    /// <param name="personId">离职人员 ID。</param>
    /// <param name="occurredAt">发生时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    private static async Task AddDepartureNoticeAsync(ApprovalDbContext db, string personId,
        DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var pending = await db.ApprovalTasks.CountAsync(item => item.AssigneeId == personId &&
            item.Status == "pending", cancellationToken);
        var notice = await db.DepartureNotices.SingleOrDefaultAsync(item => item.PersonId == personId,
            cancellationToken);
        if (notice is null)
        {
            db.DepartureNotices.Add(new DepartureNoticeEntity
            {
                PersonId = personId,
                CreatedAt = occurredAt,
                PendingTaskCount = pending,
            });
        }
        else
        {
            notice.CreatedAt = occurredAt;
            notice.PendingTaskCount = pending;
            notice.AcknowledgedAt = null;
        }
    }
}
