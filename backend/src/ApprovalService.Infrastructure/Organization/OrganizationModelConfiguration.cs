using ApprovalService.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Organization;

/// <summary>
/// 三种数据库共用的组织模型约束和索引。
/// </summary>
internal static class OrganizationModelConfiguration
{
    /// <summary>
    /// 配置组织主数据、绑定和变更记录。
    /// </summary>
    /// <param name="modelBuilder">统一模型构造器。</param>
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DepartmentEntity>(entity =>
        {
            entity.ToTable("Departments");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(64);
            entity.Property(item => item.Code).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Name).HasMaxLength(256).IsRequired();
            entity.Property(item => item.ParentId).HasMaxLength(64);
            entity.Property(item => item.HeadPersonId).HasMaxLength(64);
            entity.Property(item => item.TrustedSource).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => item.Code).IsUnique();
        });

        modelBuilder.Entity<PersonEntity>(entity =>
        {
            entity.ToTable("People");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(64);
            entity.Property(item => item.EmployeeNumber).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Name).HasMaxLength(256).IsRequired();
            entity.Property(item => item.Phone).HasMaxLength(64);
            entity.Property(item => item.Email).HasMaxLength(256);
            entity.Property(item => item.ManagerId).HasMaxLength(64);
            entity.Property(item => item.Position).HasMaxLength(128);
            entity.Property(item => item.Grade).HasMaxLength(128);
            entity.Property(item => item.TrustedSource).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => item.EmployeeNumber).IsUnique();
            entity.HasIndex(item => item.Phone);
        });

        modelBuilder.Entity<PersonDepartmentEntity>(entity =>
        {
            entity.ToTable("PersonDepartments");
            entity.HasKey(item => new { item.PersonId, item.DepartmentId });
            entity.Property(item => item.PersonId).HasMaxLength(64);
            entity.Property(item => item.DepartmentId).HasMaxLength(64);
            entity.HasIndex(item => item.DepartmentId);
        });

        modelBuilder.Entity<OrganizationRoleEntity>(entity =>
        {
            entity.ToTable("OrganizationRoles");
            entity.Property(item => item.Revision).IsConcurrencyToken();
            entity.HasKey(item => item.Code);
            entity.Property(item => item.Code).HasMaxLength(128);
            entity.Property(item => item.Name).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<PersonRoleEntity>(entity =>
        {
            entity.ToTable("PersonRoles");
            entity.HasKey(item => new { item.PersonId, item.RoleCode });
            entity.Property(item => item.PersonId).HasMaxLength(64);
            entity.Property(item => item.RoleCode).HasMaxLength(128);
            entity.HasIndex(item => item.RoleCode);
        });

        modelBuilder.Entity<ExternalPersonBindingEntity>(entity =>
        {
            entity.ToTable("ExternalPersonBindings");
            entity.HasKey(item => new { item.ClientAppCode, item.ExternalUserId });
            entity.Property(item => item.ClientAppCode).HasMaxLength(128);
            entity.Property(item => item.ExternalUserId).HasMaxLength(128);
            entity.Property(item => item.PersonId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Method).HasMaxLength(32).IsRequired();
            entity.HasIndex(item => item.PersonId);
        });

        modelBuilder.Entity<ExternalDepartmentBindingEntity>(entity =>
        {
            entity.ToTable("ExternalDepartmentBindings");
            entity.HasKey(item => new { item.ClientAppCode, item.ExternalDepartmentId });
            entity.Property(item => item.ClientAppCode).HasMaxLength(128);
            entity.Property(item => item.ExternalDepartmentId).HasMaxLength(128);
            entity.Property(item => item.DepartmentId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Method).HasMaxLength(32).IsRequired();
            entity.HasIndex(item => item.DepartmentId);
        });

        modelBuilder.Entity<OrganizationChangeEntity>(entity =>
        {
            entity.ToTable("OrganizationChanges");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(64);
            entity.Property(item => item.EntityKind).HasMaxLength(48).IsRequired();
            entity.Property(item => item.EntityId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.ActorId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Source).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Kind).HasMaxLength(48).IsRequired();
            entity.HasIndex(item => new { item.EntityKind, item.EntityId, item.OccurredAt });
        });

        modelBuilder.Entity<DepartureNoticeEntity>(entity =>
        {
            entity.ToTable("DepartureNotices");
            entity.HasKey(item => item.PersonId);
            entity.Property(item => item.PersonId).HasMaxLength(64);
        });
    }
}
