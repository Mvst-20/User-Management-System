using Microsoft.EntityFrameworkCore;
using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<EmailVerificationToken> EmailVerificationTokens { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<UserGroup> UserGroups { get; set; }
    public DbSet<UserGroupMember> UserGroupMembers { get; set; }
    public DbSet<UserGroupPermission> UserGroupPermissions { get; set; }
    public DbSet<SystemSetting> SystemSettings { get; set; }
    public DbSet<RevokedToken> RevokedTokens { get; set; }
    public DbSet<RateLimitEntry> RateLimitEntries { get; set; }
    public DbSet<UserSession> UserSessions { get; set; }
    public DbSet<LoginLog> LoginLogs { get; set; }
    public DbSet<SmtpConfig> SmtpConfigs { get; set; }
    public DbSet<Subsystem> Subsystems { get; set; }
    public DbSet<ServerMetric> ServerMetrics { get; set; }
    public DbSet<CacheVersion> CacheVersions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.Phone).IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.LastLoginAt);
            entity.HasIndex(e => e.CreatedAt);

            entity.Property(e => e.Status)
                .HasDefaultValue(UserStatus.Unverified);
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime")
                .ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime")
                .ValueGeneratedOnAdd();

            entity.HasOne(e => e.User)
                .WithMany(u => u.VerificationTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.IsSystem);
            entity.HasIndex(e => e.CreatorId);
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime")
                .ValueGeneratedOnAdd();

            entity.HasOne(e => e.Creator)
                .WithMany()
                .HasForeignKey(e => e.CreatorId)
                .OnDelete(DeleteBehavior.SetNull);

            // 归属子系统：子系统删除时其注册的权限一并删除（级联），组授权记录由
            // user_group_permissions.permission_id 的既有 Cascade 外键一并清理
            entity.HasIndex(e => e.SubsystemId);

            entity.HasOne(e => e.Subsystem)
                .WithMany(s => s.Permissions)
                .HasForeignKey(e => e.SubsystemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserGroup>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();

            // 新建权限组默认为最低级别（数值越大权限越低），避免误建成高权限组
            entity.Property(e => e.Level)
                .HasDefaultValue(PermissionCodes.GroupLevel.Default);
        });

        modelBuilder.Entity<UserGroupMember>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.GroupId }).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.GroupId);
            entity.Property(e => e.JoinedAt)
                .HasColumnType("datetime")
                .ValueGeneratedOnAdd();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Group)
                .WithMany(g => g.Members)
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserGroupPermission>(entity =>
        {
            entity.HasIndex(e => new { e.GroupId, e.PermissionId }).IsUnique();
            entity.HasIndex(e => e.GroupId);
            entity.HasIndex(e => e.PermissionId);
            entity.Property(e => e.GrantedAt)
                .HasColumnType("datetime")
                .ValueGeneratedOnAdd();

            entity.HasOne(e => e.Group)
                .WithMany(g => g.GroupPermissions)
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Permission)
                .WithMany()
                .HasForeignKey(e => e.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasIndex(e => e.Key).IsUnique();
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("datetime");
        });

        modelBuilder.Entity<RevokedToken>(entity =>
        {
            entity.HasIndex(e => e.ExpiresAt);
        });

        modelBuilder.Entity<RateLimitEntry>(entity =>
        {
            entity.HasIndex(e => e.ExpiresAt);
        });

        // 会话表：按用户查列表、按设备键做"同类设备只留最近一个"、按活跃时间统计在线
        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.RefreshTokenHash).IsUnique();
            entity.HasIndex(e => e.PreviousRefreshTokenHash);
            entity.HasIndex(e => new { e.UserId, e.DeviceKey });
            entity.HasIndex(e => e.LastActiveAt);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.LastActiveAt).HasColumnType("datetime");
            entity.Property(e => e.RefreshTokenExpiresAt).HasColumnType("datetime");
            entity.Property(e => e.RevokedAt).HasColumnType("datetime");

            // 用户删除时会话级联清理，避免孤儿会话
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 登录审计：按用户 + 时间分页；按时间做保留期清理
        modelBuilder.Entity<LoginLog>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Ip);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
        });

        // 单行配置表，主键固定为 1，不自增
        modelBuilder.Entity<SmtpConfig>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<Subsystem>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.ApiKey).IsUnique();
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime");
            entity.Property(e => e.LastUsedAt).HasColumnType("datetime");
            entity.Property(e => e.ExpiresAt).HasColumnType("datetime");
        });

        // 指标采样表：按采样时间查询，60s × 8 天 ≈ 1.2 万行
        modelBuilder.Entity<ServerMetric>(entity =>
        {
            entity.HasIndex(e => e.RecordedAt);
            entity.Property(e => e.RecordedAt).HasColumnType("datetime");
        });

        // 缓存版本号：每个作用域一行，写入为单条 UPSERT
        modelBuilder.Entity<CacheVersion>(entity =>
        {
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime");
        });
    }
}
