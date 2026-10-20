using ApprovalService.Domain.Runtime;
using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Persistence;

/// <summary>
/// 审批运行库的统一模型；三种数据库使用同一实体配置、各自生成迁移。
/// </summary>
public class ApprovalDbContext : DbContext
{
    /// <summary>
    /// 供检查程序或直接创建上下文的调用方使用。
    /// </summary>
    /// <param name="options">数据库提供程序和连接配置。</param>
    public ApprovalDbContext(DbContextOptions<ApprovalDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// 供数据库专属迁移上下文使用。
    /// </summary>
    /// <param name="options">数据库提供程序和连接配置。</param>
    protected ApprovalDbContext(DbContextOptions options) : base(options)
    {
    }

    /// <summary>
    /// 可编辑流程元数据。
    /// </summary>
    public DbSet<FlowEntity> Flows => Set<FlowEntity>();

    /// <summary>
    /// 发布后的流程版本。
    /// </summary>
    public DbSet<FlowVersionEntity> FlowVersions => Set<FlowVersionEntity>();

    /// <summary>
    /// 审批单状态。
    /// </summary>
    public DbSet<InstanceEntity> Instances => Set<InstanceEntity>();

    /// <summary>
    /// 待办任务投影。
    /// </summary>
    public DbSet<ApprovalTaskEntity> ApprovalTasks => Set<ApprovalTaskEntity>();

    /// <summary>
    /// 只追加的操作记录。
    /// </summary>
    public DbSet<OperationEventEntity> OperationEvents => Set<OperationEventEntity>();

    /// <summary>
    /// 事务待发队列。
    /// </summary>
    public DbSet<OutboxItemEntity> OutboxItems => Set<OutboxItemEntity>();

    /// <summary>
    /// 可重启的定时任务。
    /// </summary>
    public DbSet<ScheduledJobEntity> ScheduledJobs => Set<ScheduledJobEntity>();

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardImmutableRecords();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardImmutableRecords();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// 已发布版本和审计事件只能插入，不能被 EF 的普通更新或删除改写。
    /// </summary>
    private void GuardImmutableRecords()
    {
        if (ChangeTracker.Entries<FlowVersionEntity>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<OperationEventEntity>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("已发布版本和操作记录只能追加，不能修改或删除。");
        }
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // 三种提供程序均存 UTC DateTime；SQLite 因而可以在 SQL 中比较租约和到期时间。
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FlowEntity>(entity =>
        {
            entity.ToTable("Flows");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(64);
            entity.Property(item => item.Code).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Name).HasMaxLength(256).IsRequired();
            entity.Property(item => item.Group).HasMaxLength(128);
            entity.Property(item => item.AdministratorId).HasMaxLength(128);
            entity.Property(item => item.PublishedVersionId).HasMaxLength(64);
            entity.HasIndex(item => item.Code).IsUnique();
        });

        modelBuilder.Entity<FlowVersionEntity>(entity =>
        {
            entity.ToTable("FlowVersions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(64);
            entity.Property(item => item.FlowId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.DefinitionJson).IsRequired();
            entity.Property(item => item.GraphJson).IsRequired();
            entity.HasOne<FlowEntity>().WithMany().HasForeignKey(item => item.FlowId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.FlowId, item.Version }).IsUnique();
        });

        modelBuilder.Entity<InstanceEntity>(entity =>
        {
            entity.ToTable("Instances");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(64);
            entity.Property(item => item.SerialNo).HasMaxLength(64).IsRequired();
            entity.Property(item => item.FlowId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.FlowVersionId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.FlowCode).HasMaxLength(128).IsRequired();
            entity.Property(item => item.BusinessKey).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Title).HasMaxLength(256).IsRequired();
            entity.Property(item => item.InitiatorId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.InitiatorDepartmentId).HasMaxLength(128);
            entity.Property(item => item.SubmittedById).HasMaxLength(128);
            entity.Property(item => item.ClientAppCode).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(48).IsRequired();
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.Property(item => item.ParentInstanceId).HasMaxLength(64);
            entity.HasOne<FlowVersionEntity>().WithMany().HasForeignKey(item => item.FlowVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.SerialNo).IsUnique();
            entity.HasIndex(item => new { item.FlowCode, item.BusinessKey }).IsUnique();
            entity.HasIndex(item => new { item.Status, item.StartedAt });
        });

        modelBuilder.Entity<ApprovalTaskEntity>(entity =>
        {
            entity.ToTable("ApprovalTasks");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(64);
            entity.Property(item => item.InstanceId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.NodeId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.AssigneeId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.DelegatedForId).HasMaxLength(128);
            entity.Property(item => item.Status).HasMaxLength(48).IsRequired();
            entity.Property(item => item.SignatureFileId).HasMaxLength(64);
            entity.HasOne<InstanceEntity>().WithMany().HasForeignKey(item => item.InstanceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.AssigneeId, item.Status, item.DueAt });
            entity.HasIndex(item => new { item.InstanceId, item.NodeId });
        });

        modelBuilder.Entity<OperationEventEntity>(entity =>
        {
            entity.ToTable("OperationEvents");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(64);
            entity.Property(item => item.InstanceId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.RequestId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Kind).HasMaxLength(64).IsRequired();
            entity.Property(item => item.PreviousHash).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Hash).HasMaxLength(64).IsRequired();
            entity.HasOne<InstanceEntity>().WithMany().HasForeignKey(item => item.InstanceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.InstanceId, item.Sequence }).IsUnique();
        });

        modelBuilder.Entity<OutboxItemEntity>(entity =>
        {
            entity.ToTable("OutboxItems");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(64);
            entity.Property(item => item.InstanceId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.EventId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Kind).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(48).IsRequired();
            entity.Property(item => item.LeaseOwner).HasMaxLength(128);
            entity.Property(item => item.LastError).HasMaxLength(1024);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.Status, item.AvailableAt, item.LeaseUntil });
            entity.HasIndex(item => item.EventId);
        });

        modelBuilder.Entity<ScheduledJobEntity>(entity =>
        {
            entity.ToTable("ScheduledJobs");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(64);
            entity.Property(item => item.InstanceId).HasMaxLength(64);
            entity.Property(item => item.Kind).HasMaxLength(64).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(48).IsRequired();
            entity.Property(item => item.LeaseOwner).HasMaxLength(128);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.Status, item.DueAt, item.LeaseUntil });
        });
    }
}
