using System.Data;
using System.Text.Json;
using ApprovalService.Application.Identity;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Identity;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Options;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApprovalService.Infrastructure.Identity;

/// <summary>
/// 只供服务器本机命令调用的一次性首位组织管理员初始化器。
/// </summary>
/// <param name="factory">使用部署配置创建数据库上下文的工厂。</param>
/// <param name="passwords">与日常账号开通共用的密码摘要服务。</param>
/// <param name="options">用于定位 SQLite 数据目录的部署配置。</param>
public sealed class FirstAdminBootstrapper(IDbContextFactory<ApprovalDbContext> factory,
    IIdentityPasswordService passwords, IOptions<DataOptions> options)
{
    /// <summary>
    /// 初始化命令所选数据库的结构，已有数据库只应用未执行的迁移。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    public async Task PrepareDatabaseAsync(CancellationToken cancellationToken = default)
    {
        if (options.Value.Provider == "Sqlite")
        {
            var directory = options.Value.Directory;
            var absolute = Path.IsPathRooted(directory) ? directory : Path.Combine(AppContext.BaseDirectory, directory);
            Directory.CreateDirectory(absolute);
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// 首次创建在职人员或选用现有人员，并原子开通账号与管理员角色。
    /// </summary>
    /// <param name="personId">内部人员 ID。</param>
    /// <param name="employeeNumber">新人员工号；已有人员可留空。</param>
    /// <param name="personName">新人员姓名；已有人员可留空。</param>
    /// <param name="temporaryPassword">仅本次使用的临时密码。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    public async Task BootstrapAsync(string personId, string? employeeNumber, string? personName,
        string temporaryPassword, CancellationToken cancellationToken = default)
    {
        personId = personId?.Trim() ?? string.Empty;
        employeeNumber = employeeNumber?.Trim();
        personName = personName?.Trim();
        if (personId.Length is < 1 or > 64 || employeeNumber?.Length > 128 || personName?.Length > 256 ||
            string.IsNullOrWhiteSpace(temporaryPassword) || temporaryPassword.Length is < 10 or > 128)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                "人员 ID 不超过 64 位，工号不超过 128 位，姓名不超过 256 位，临时密码须为 10 到 128 位。");
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        // 管理员角色一旦分配，命令永久失效；同一事务的串行化隔离防止并发首开。
        if (await db.PersonRoles.AnyAsync(item => item.RoleCode == "organization-admin", cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "首位管理员已开通，不能再次运行初始化命令。");
        }

        var person = await db.People.SingleOrDefaultAsync(item => item.Id == personId, cancellationToken);
        if (person is null)
        {
            if (string.IsNullOrWhiteSpace(employeeNumber) || string.IsNullOrWhiteSpace(personName))
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, "新人员必须填写工号和姓名。");
            }

            if (await db.People.AnyAsync(item => item.EmployeeNumber == employeeNumber, cancellationToken))
            {
                throw new UserFriendlyException(ErrorCodes.Conflict, "工号已存在，请输入该人员的内部 ID。");
            }

            person = new PersonEntity
            {
                Id = personId,
                EmployeeNumber = employeeNumber,
                Name = personName,
                IsActive = true,
                TrustedSource = "approvalService",
            };
            db.People.Add(person);
            db.OrganizationChanges.Add(new OrganizationChangeEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                EntityKind = "person",
                EntityId = personId,
                ActorId = personId,
                Source = "local-bootstrap",
                Kind = "create",
                AfterJson = JsonSerializer.Serialize(person),
                OccurredAt = DateTimeOffset.UtcNow,
            });
        }
        else if (!person.IsActive ||
            (!string.IsNullOrEmpty(employeeNumber) && employeeNumber != person.EmployeeNumber) ||
            (!string.IsNullOrEmpty(personName) && personName != person.Name))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "人员已停用，或输入的工号和姓名与现有人员不一致。");
        }

        if (await db.LocalAccounts.AnyAsync(item => item.PersonId == personId, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "该人员已有本地账号。");
        }

        var digest = passwords.Hash(temporaryPassword);
        db.LocalAccounts.Add(new LocalAccountEntity
        {
            PersonId = personId,
            PasswordHash = digest.Hash,
            PasswordSalt = digest.Salt,
            MustChangePassword = true,
        });
        if (!await db.OrganizationRoles.AnyAsync(item => item.Code == "organization-admin", cancellationToken))
        {
            db.OrganizationRoles.Add(new OrganizationRoleEntity
            {
                Code = "organization-admin",
                Name = "组织管理员",
            });
        }

        db.PersonRoles.Add(new PersonRoleEntity { PersonId = personId, RoleCode = "organization-admin" });
        db.OrganizationChanges.Add(new OrganizationChangeEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityKind = "person-role",
            EntityId = personId + ":organization-admin",
            ActorId = personId,
            Source = "local-bootstrap",
            Kind = "create",
            AfterJson = JsonSerializer.Serialize(new { PersonId = personId, RoleCode = "organization-admin" }),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
