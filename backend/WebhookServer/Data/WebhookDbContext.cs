using Microsoft.EntityFrameworkCore;
using WebhookServer.Models;

namespace WebhookServer.Data;

public sealed class WebhookDbContext(DbContextOptions<WebhookDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Topic> Topics => Set<Topic>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.HasMany(x => x.Topics)
                .WithOne(x => x.Tenant)
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Topic>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
            entity.HasIndex(x => x.ServiceBusTopicName).IsUnique();
            entity.Property(x => x.Key).HasMaxLength(100);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.ServiceBusTopicName).HasMaxLength(260);
        });
    }
}
