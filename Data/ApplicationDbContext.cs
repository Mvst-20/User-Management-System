using Microsoft.EntityFrameworkCore;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User entity configuration
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

        // EmailVerificationToken entity configuration
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

        // Permission entity configuration
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
        });

        // UserGroup entity configuration
        modelBuilder.Entity<UserGroup>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // UserGroupMember entity configuration
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

        // UserGroupPermission entity configuration
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
    }
}
