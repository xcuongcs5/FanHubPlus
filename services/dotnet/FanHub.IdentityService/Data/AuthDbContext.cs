using FanHub.IdentityService.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace FanHub.IdentityService.Data;

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Email).HasMaxLength(255).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            entity.Property(u => u.AvatarUrl).HasMaxLength(500);
            entity.Property(u => u.PhoneNumber).HasMaxLength(20);
            entity.Property(u => u.Status).HasMaxLength(20).HasDefaultValue("Active");
            entity.HasQueryFilter(u => !u.IsDeleted);
        });

        // Role configuration
        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasIndex(r => r.Name).IsUnique();
            entity.Property(r => r.Name).HasMaxLength(50).IsRequired();
            entity.Property(r => r.Description).HasMaxLength(200);
        });

        // UserRole configuration (Many-to-Many)
        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(ur => new { ur.UserId, ur.RoleId });
            entity.HasOne(ur => ur.User).WithMany(u => u.UserRoles).HasForeignKey(ur => ur.UserId);
            entity.HasOne(ur => ur.Role).WithMany(r => r.UserRoles).HasForeignKey(ur => ur.RoleId);
        });

        // UserPreference configuration
        modelBuilder.Entity<UserPreference>(entity =>
        {
            entity.HasIndex(up => up.UserId).IsUnique();
            entity.HasOne(up => up.User).WithOne(u => u.Preference).HasForeignKey<UserPreference>(up => up.UserId);
            entity.Property(up => up.Theme).HasMaxLength(20).HasDefaultValue("light");
            entity.Property(up => up.Language).HasMaxLength(10).HasDefaultValue("vi");
        });

        // RefreshToken configuration
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(rt => rt.Token).IsUnique();
            entity.Property(rt => rt.Token).HasMaxLength(500).IsRequired();
            entity.HasOne(rt => rt.User).WithMany(u => u.RefreshTokens).HasForeignKey(rt => rt.UserId);
        });

        // Seed Roles
        var adminRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var userRoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var moderatorRoleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var eventOwnerRoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var seedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<Role>().HasData(
            new Role { Id = adminRoleId, Name = "Admin", Description = "Quản trị viên hệ thống", CreatedAt = seedDate },
            new Role { Id = userRoleId, Name = "User", Description = "Người dùng thông thường", CreatedAt = seedDate },
            new Role { Id = moderatorRoleId, Name = "Moderator", Description = "Kiểm duyệt viên", CreatedAt = seedDate },
            new Role { Id = eventOwnerRoleId, Name = "EventOwner", Description = "Người tổ chức sự kiện", CreatedAt = seedDate }
        );
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<FanHub.Shared.Common.Models.BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
