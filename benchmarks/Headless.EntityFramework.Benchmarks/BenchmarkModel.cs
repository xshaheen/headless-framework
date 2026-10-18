// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Domain;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Headless.EntityFramework.Benchmarks;

/// <summary>An <see cref="IEntity"/> with two collection navigations, the shape the navigation tracker reacts to.</summary>
public sealed class Parent : Entity<Guid>
{
    public string Name { get; set; } = "";

    public List<Child> Children { get; set; } = [];

    public List<Tag> Tags { get; set; } = [];
}

public sealed class Child : Entity<Guid>
{
    public Guid ParentId { get; set; }
}

public sealed class Tag : Entity<Guid>
{
    public Guid ParentId { get; set; }
}

/// <summary>An audited order with a client-generated key, the common case for domain aggregates.</summary>
public sealed class AuditedOrder : Entity<Guid>
{
    public string CustomerName { get; set; } = "";

    public decimal Amount { get; set; }
}

public sealed class BenchmarkDbContext(DbContextOptions options, IOptions<AuditLogStorageOptions> auditLogStorage)
    : HeadlessDbContext(options)
{
    public DbSet<Parent> Parents => Set<Parent>();

    public DbSet<AuditedOrder> Orders => Set<AuditedOrder>();

    public override string DefaultSchema => "";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Parent>(b =>
        {
            b.Property(e => e.Id).ValueGeneratedNever();
            b.HasMany(e => e.Children).WithOne().HasForeignKey(e => e.ParentId);
            b.HasMany(e => e.Tags).WithOne().HasForeignKey(e => e.ParentId);
        });
        modelBuilder.Entity<Child>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<Tag>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<AuditedOrder>(b =>
        {
            b.IsAudited();
            b.Property(e => e.Id).ValueGeneratedNever();
        });
        modelBuilder.ConfigureHeadlessAuditLog(
            auditLogStorage.Value,
            HeadlessStorageNaming.ForProvider(Database.ProviderName)
        );

        // SQLite has no sequence support for the composite audit key; a single-column key lets ROWID generate it.
        if (Database.IsSqlite())
        {
            modelBuilder.Entity<AuditLogEntry>(b =>
            {
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).ValueGeneratedOnAdd();
            });
        }
    }
}
