// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Sql;
using Headless.Sql.PostgreSql;
using Headless.Sql.Sqlite;
using Headless.Sql.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tests;

public sealed class WorkItem
{
    public Guid Id { get; set; }

    public string Status { get; set; } = "new";

    public RowLeaseSnapshot Lease { get; set; }
}

/// <summary>A context that keeps a lease on its own work rows; each provider's subclass names the columns its way.</summary>
public abstract class WorkContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    public abstract ISqlDialect Dialect { get; }

    protected abstract void Map(EntityTypeBuilder<WorkItem> item);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkItem>(item =>
        {
            item.Property(w => w.Id).ValueGeneratedNever();
            item.Property(w => w.Status).HasMaxLength(50);
            Map(item);
        });
        modelBuilder.UseFencingDatabaseClock(Dialect);
    }

    protected static void MapSnakeCase(EntityTypeBuilder<WorkItem> item)
    {
        item.ToTable("work_items");
        item.Property(w => w.Id).HasColumnName("id");
        item.Property(w => w.Status).HasColumnName("status");
        item.HasRowLease(w => w.Lease);
    }
}

public sealed class PostgreSqlWorkContext(DbContextOptions<PostgreSqlWorkContext> options) : WorkContext(options)
{
    public override ISqlDialect Dialect => PostgreSqlDialect.Instance;

    protected override void Map(EntityTypeBuilder<WorkItem> item) => MapSnakeCase(item);
}

public sealed class SqliteWorkContext(DbContextOptions<SqliteWorkContext> options) : WorkContext(options)
{
    public override ISqlDialect Dialect => SqliteDialect.Instance;

    protected override void Map(EntityTypeBuilder<WorkItem> item) => MapSnakeCase(item);
}

/// <summary>EF Core's own PascalCase names, to prove the descriptor follows the model rather than a fixed naming.</summary>
public sealed class SqlServerWorkContext(DbContextOptions<SqlServerWorkContext> options) : WorkContext(options)
{
    public override ISqlDialect Dialect => SqlServerDialect.Instance;

    protected override void Map(EntityTypeBuilder<WorkItem> item)
    {
        item.ToTable("WorkItems");
        item.HasRowLease(
            w => w.Lease,
            new FencedRowColumns
            {
                Generation = "LeaseGeneration",
                State = "LeaseState",
                ExpiresAt = "LeaseExpiresAt",
                Holder = "LeaseHolder",
                TakeoverCount = "LeaseTakeoverCount",
            }
        );
    }
}
