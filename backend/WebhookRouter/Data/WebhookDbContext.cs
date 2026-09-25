using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using WebhookRouter.Models;

namespace WebhookRouter.Data;

public sealed class WebhookDbContext(DbContextOptions<WebhookDbContext> options) : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.EventType).HasMaxLength(64);
            entity.Property(x => x.ActorUsername).HasMaxLength(256);
            entity.Property(x => x.EntityType).HasMaxLength(64);
            entity.Property(x => x.EntityId).HasMaxLength(128);
            entity.Property(x => x.EntityName).HasMaxLength(256);
            entity.Property(x => x.Metadata)
                .HasColumnType("nvarchar(max)").HasDefaultValueSql("N'{}'");
            entity.ToTable("ActivityLogs", table => table.HasCheckConstraint("CK_ActivityLogs_Metadata_Json", "ISJSON([Metadata]) = 1"));
            entity.HasIndex(x => new { x.OccurredAt, x.Id });
            entity.HasIndex(x => new { x.EntityType, x.EntityId, x.OccurredAt });
            // Snapshot IDs/names, no cascading FK: account removal must not erase history.
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Users");
            entity.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.FirstName).HasMaxLength(100);
            entity.Property(x => x.LastName).HasMaxLength(100);
            entity.Property(x => x.IsEnabled).HasDefaultValue(true);
            entity.Property(x => x.AllowPasswordAuthentication).HasDefaultValue(false);
        });
        modelBuilder.Entity<IdentityRole<Guid>>(entity =>
        {
            entity.ToTable("Roles");
            entity.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        });
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.HasOne(x => x.CreatedByUser)
                .WithMany(x => x.Tenants)
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Topics)
                .WithOne(x => x.Tenant)
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Topic>(entity =>
        {
            entity.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
            entity.Property(x => x.Key).HasMaxLength(100);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.FullyQualifiedNamespace).HasMaxLength(300);
            entity.Property(x => x.ServiceBusConnectionString).HasMaxLength(2000);
            entity.Property(x => x.ServiceBusEntityType).HasMaxLength(10).HasDefaultValue("Topic");
            entity.Property(x => x.ServiceBusEntityName).HasMaxLength(260);
        });
    }
}
