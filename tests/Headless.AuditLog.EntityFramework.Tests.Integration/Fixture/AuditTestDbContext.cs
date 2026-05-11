using Headless.AuditLog;
using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Tests.Fixture;

public class AuditTestDbContext(HeadlessDbContextServices services, DbContextOptions options)
    : HeadlessDbContext(services, options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<GeneratedOrder> GeneratedOrders => Set<GeneratedOrder>();

    public override string DefaultSchema => "";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Order>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<GeneratedOrder>().Property(e => e.Id).ValueGeneratedOnAdd();
        modelBuilder.ConfigureAuditLog();

        // SQLite doesn't support ValueGeneratedOnAdd on composite-key columns (no sequence support).
        // Override to use a single-column PK so SQLite ROWID auto-increment works.
        modelBuilder.Entity<AuditLogEntry>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedOnAdd();
        });
    }
}
