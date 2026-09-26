// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Tests.Fixture;

/// <summary>
/// The context the tenant-migration tests migrate. Its migrations under <c>Fixture/Migrations</c> were scaffolded
/// with <c>dotnet ef migrations add</c> against its own default schema <c>app</c>, with seed data and a later
/// alteration of an indexed column, so the per-tenant schema rewrite is exercised on real scaffold output.
/// </summary>
public sealed class TenantMigrationDbContext(
    HeadlessDbContextServices services,
    DbContextOptions<TenantMigrationDbContext> options
) : HeadlessDbContext(services, options)
{
    public static readonly Guid SeedRowId = new("0198f6a4-0000-7000-8000-000000000001");

    public DbSet<MigratedRow> Rows => Set<MigratedRow>();

    public DbSet<MigratedNote> Notes => Set<MigratedNote>();

    public override string DefaultSchema => "app";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var row = modelBuilder.Entity<MigratedRow>();
        row.ToTable("Rows");
        row.IsTenantOwned();
        row.Property(x => x.TenantId).HasMaxLength(64);
        row.Property(x => x.Name).HasMaxLength(200);
        row.HasIndex(x => x.Name);
        row.HasData(
            new MigratedRow
            {
                Id = SeedRowId,
                Name = "seeded",
                TenantId = "seed",
            }
        );
        modelBuilder.Entity<MigratedNote>().ToTable("Notes");
    }
}

public sealed class MigratedRow
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    public string? TenantId { get; set; }
}

/// <summary>Not tenant-owned; it still lives in each tenant's schema.</summary>
public sealed class MigratedNote
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Text { get; set; }
}
