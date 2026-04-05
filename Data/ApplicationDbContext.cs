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
            entity.HasIndex(e => e.Role);
            entity.HasIndex(e => e.LastLoginAt);
            entity.HasIndex(e => e.CreatedAt);

            entity.Property(e => e.Status)
                .HasDefaultValue(UserStatus.Unverified);
            entity.Property(e => e.Role)
                .HasDefaultValue(UserRole.User);
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
    }
}
