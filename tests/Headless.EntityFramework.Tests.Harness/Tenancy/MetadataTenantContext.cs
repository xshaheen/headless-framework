// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Tenancy;

public sealed class MetadataTenantContext(
    HeadlessDbContextServices services,
    DbContextOptions<MetadataTenantContext> options
) : HeadlessDbContext(services, options)
{
    public override string DefaultSchema => "tenancy";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var row = modelBuilder.Entity<TenantRow>();
        row.ToTable("Rows");
        row.IsTenantOwned(nameof(TenantRow.Owner));
        row.Property(x => x.Owner).HasColumnName("tenant_key").HasMaxLength(41);
        row.Property(x => x.Stamp).IsConcurrencyToken();
        row.Property(x => x.Code).HasMaxLength(100);
        row.HasIndex(x => x.Code).IsUnique().HasDatabaseName("TenantCodeIndex").IsTenantScoped();
        row.HasQueryFilter("VisibleRows", x => !x.Hidden);
        row.OwnsOne(x => x.Detail);
        var shadow = modelBuilder.Entity<ShadowTenantRow>();
        shadow.ToTable("ShadowRows");
        shadow.IsTenantOwned();
        shadow.Property<string>("TenantId").HasColumnName("tenant_key").HasMaxLength(41);
        shadow.HasAlternateKey("TenantId", "Id");
        shadow.Property(x => x.Stamp).IsConcurrencyToken();
        var host = modelBuilder.Entity<HostTenantRow>();
        host.ToTable("HostRows");
        host.Property(x => x.TenantId).HasMaxLength(41);
        host.Property(x => x.Code).HasMaxLength(100);
        host.Property(x => x.OptionalCode).HasMaxLength(100);
        host.HasIndex(x => x.Code).IsUnique().IsTenantScoped();
        host.HasIndex(x => x.OptionalCode).IsUnique().IsTenantScoped();
    }
}

public sealed class TenantRow : IDeleteAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Owner { get; set; }
    public string Code { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "original";
    public string Stamp { get; set; } = "victim-stamp";
    public bool Hidden { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? RestoredAt { get; set; }
    public TenantDetail Detail { get; set; } = new();
}

public sealed class HostTenantRow : IMultiTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? TenantId { get; set; }
    public string Code { get; set; } = "host-code";
    public string? OptionalCode { get; set; }
}

public sealed class TenantDetail
{
    public string Value { get; set; } = "original";
}

public sealed class ShadowTenantRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "original";
    public string Stamp { get; set; } = "victim-stamp";
}

public abstract class MetadataTenantFixture(TenantDatabaseProvider provider) : TenantDatabaseFixture(provider)
{
    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddDbContext<MetadataTenantContext>(ConfigureOptions);

    protected override DbContext GetContext(IServiceProvider services) =>
        services.GetRequiredService<MetadataTenantContext>();
}
