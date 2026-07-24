using Microsoft.EntityFrameworkCore;

namespace Netvium.Services.Account.DirectSql;

public sealed class DirectIdentityDbContext(DbContextOptions<DirectIdentityDbContext> options) : DbContext(options)
{
    public DbSet<DirectUser> Users => Set<DirectUser>();
    public DbSet<DirectRefreshToken> RefreshTokens => Set<DirectRefreshToken>();
    public DbSet<DirectPlan> Plans => Set<DirectPlan>();
    public DbSet<DirectUserLicense> UserLicenses => Set<DirectUserLicense>();
    public DbSet<DirectUserDevice> UserDevices => Set<DirectUserDevice>();
    public DbSet<DirectAuthAuditLog> AuthAuditLogs => Set<DirectAuthAuditLog>();
    public DbSet<DirectActivationToken> ActivationTokens => Set<DirectActivationToken>();
    public DbSet<DirectAccountUsageEvent> AccountUsageEvents => Set<DirectAccountUsageEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");

        modelBuilder.Entity<DirectUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<DirectRefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
            entity.Property(x => x.DeviceIdentifierHash).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(x => x.CreatedByIpHash).HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.RevokedByIpHash).HasMaxLength(64).IsUnicode(false);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasOne(x => x.User).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<DirectPlan>(entity =>
        {
            entity.ToTable("Plans");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PlanCode).HasMaxLength(30).IsUnicode(false).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(60).IsRequired();
            entity.HasIndex(x => x.PlanCode).IsUnique();
        });

        modelBuilder.Entity<DirectUserLicense>(entity =>
        {
            entity.ToTable("UserLicenses");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.HasOne(x => x.User).WithMany(x => x.Licenses).HasForeignKey(x => x.UserId);
            entity.HasOne(x => x.Plan).WithMany(x => x.UserLicenses).HasForeignKey(x => x.PlanId);
        });

        modelBuilder.Entity<DirectUserDevice>(entity =>
        {
            entity.ToTable("UserDevices");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DeviceIdentifierHash).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(x => x.DeviceName).HasMaxLength(150);
            entity.HasIndex(x => new { x.UserId, x.DeviceIdentifierHash }).IsUnique();
        });

        modelBuilder.Entity<DirectAuthAuditLog>(entity =>
        {
            entity.ToTable("AuthAuditLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.EventType).HasMaxLength(50).IsUnicode(false).IsRequired();
            entity.Property(x => x.DetailCode).HasMaxLength(100).IsUnicode(false);
            entity.Property(x => x.IpAddressHash).HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.DeviceIdentifierHash).HasMaxLength(64).IsUnicode(false);
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasOne<DirectUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DirectActivationToken>(entity =>
        {
            entity.ToTable("ActivationTokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(x => x.PlanDurationDays).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(250);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.RedeemedAtUtc, x.RevokedAtUtc });
            entity.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DirectAccountUsageEvent>(entity =>
        {
            entity.ToTable("AccountUsageEvents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.EventType).HasMaxLength(80).IsUnicode(false).IsRequired();
            entity.Property(x => x.Detail).HasMaxLength(500);
            entity.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        });
    }
}
