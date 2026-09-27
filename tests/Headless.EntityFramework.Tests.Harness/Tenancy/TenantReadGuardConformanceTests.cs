// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Tenancy;

public sealed class ReadGuardTenantContext(
    HeadlessDbContextServices services,
    DbContextOptions<ReadGuardTenantContext> options
) : HeadlessDbContext(services, options)
{
    public override string DefaultSchema => "read_guard";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var parent = modelBuilder.Entity<ReadGuardParent>();
        parent.ToTable("Parents");
        parent.HasMany(x => x.Rows).WithOne().HasForeignKey(x => x.ParentId);
        var row = modelBuilder.Entity<ReadGuardRow>();
        row.ToTable("Rows");
        row.IsTenantOwned();
        row.Property(x => x.TenantId).HasMaxLength(41);
        var host = modelBuilder.Entity<ReadGuardHostRow>();
        host.ToTable("HostRows");
        host.Property(x => x.TenantId).HasMaxLength(41);
    }
}

/// <summary>A non-tenant principal whose collection holds tenant-owned rows, to exercise navigation loads.</summary>
public sealed class ReadGuardParent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<ReadGuardRow> Rows { get; set; } = [];
}

/// <summary>Tenant-owned through the model builder, so its tenant column is required.</summary>
public sealed class ReadGuardRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ParentId { get; set; }
    public string? TenantId { get; set; }
}

/// <summary>Tenant-owned through <see cref="IMultiTenant"/>, so its tenant column is nullable and holds host rows.</summary>
public sealed class ReadGuardHostRow : IMultiTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? TenantId { get; set; }
}

public abstract class ReadGuardTenantFixture(TenantDatabaseProvider provider) : TenantDatabaseFixture(provider)
{
    protected override void ConfigureTenancy(HeadlessEntityFrameworkTenancyBuilder ef) =>
        ef.GuardTenantWrites().GuardTenantReads();

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddDbContext<ReadGuardTenantContext>(ConfigureOptions);

    protected override DbContext GetContext(IServiceProvider services) =>
        services.GetRequiredService<ReadGuardTenantContext>();
}

public abstract class TenantReadGuardConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : ReadGuardTenantFixture
{
    private static readonly Func<ReadGuardTenantContext, Task<int>> _CompiledRowCount = EF.CompileAsyncQuery(
        (ReadGuardTenantContext db) => db.Set<ReadGuardRow>().Count()
    );

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await fixture.ResetAsync(AbortToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task should_throw_unwrapped_when_listing_required_tenant_rows_without_tenant(string? tenant)
    {
        await _SeedAsync("tenant-a");
        fixture.CurrentTenant.Id = tenant;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadGuardTenantContext>();

        var act = () => db.Set<ReadGuardRow>().ToListAsync(AbortToken);

        // An exact-type match on the outer exception proves EF surfaced the guard failure as-is, not wrapped.
        await act.Should().ThrowExactlyAsync<MissingTenantContextException>();
    }

    [Fact]
    public async Task should_throw_unwrapped_when_bulk_deleting_required_tenant_rows_without_tenant()
    {
        await _SeedAsync("tenant-a");
        fixture.CurrentTenant.Id = null;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReadGuardTenantContext>();

            var act = () => db.Set<ReadGuardRow>().ExecuteDeleteAsync(AbortToken);

            await act.Should().ThrowExactlyAsync<MissingTenantContextException>();
        }

        (await _CountAllRowsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_throw_unwrapped_when_bulk_updating_required_tenant_rows_without_tenant()
    {
        await _SeedAsync("tenant-a");
        fixture.CurrentTenant.Id = null;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadGuardTenantContext>();

        var act = () =>
            db.Set<ReadGuardRow>()
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TenantId, "tenant-b"), AbortToken);

        await act.Should().ThrowExactlyAsync<MissingTenantContextException>();
    }

    [Fact]
    public async Task should_check_tenant_on_each_execution_of_a_compiled_query()
    {
        await _SeedAsync("tenant-a");
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadGuardTenantContext>();
        fixture.CurrentTenant.Id = "tenant-a";
        (await _CompiledRowCount(db)).Should().Be(1);

        // A compiled query skips the tenant-keyed query cache, so only per-execution evaluation can throw here.
        fixture.CurrentTenant.Id = null;
        var act = async () => await _CompiledRowCount(db);

        await act.Should().ThrowExactlyAsync<MissingTenantContextException>();
    }

    [Fact]
    public async Task should_throw_unwrapped_when_loading_required_tenant_navigation_without_tenant()
    {
        var parentId = await _SeedAsync("tenant-a");
        fixture.CurrentTenant.Id = null;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadGuardTenantContext>();
        var parent = await db.Set<ReadGuardParent>().SingleAsync(x => x.Id == parentId, AbortToken);

        var act = () => db.Entry(parent).Collection(x => x.Rows).LoadAsync(AbortToken);

        await act.Should().ThrowExactlyAsync<MissingTenantContextException>();
    }

    [Fact]
    public async Task should_throw_unwrapped_when_including_required_tenant_navigation_without_tenant()
    {
        await _SeedAsync("tenant-a");
        fixture.CurrentTenant.Id = null;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadGuardTenantContext>();

        var act = () => db.Set<ReadGuardParent>().Include(x => x.Rows).ToListAsync(AbortToken);

        await act.Should().ThrowExactlyAsync<MissingTenantContextException>();
    }

    [Fact]
    public async Task should_check_tenant_on_each_execution_when_same_query_was_cached_with_tenant()
    {
        await _SeedAsync("tenant-a");
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadGuardTenantContext>();
        fixture.CurrentTenant.Id = "tenant-a";
        (await db.Set<ReadGuardRow>().CountAsync(AbortToken)).Should().Be(1);

        fixture.CurrentTenant.Id = null;
        var act = () => db.Set<ReadGuardRow>().CountAsync(AbortToken);

        await act.Should().ThrowExactlyAsync<MissingTenantContextException>();
    }

    [Fact]
    public async Task should_read_all_tenants_when_multi_tenancy_filter_is_ignored_without_tenant()
    {
        await _SeedAsync("tenant-a");
        await _SeedAsync("tenant-b");
        fixture.CurrentTenant.Id = null;

        (await _CountAllRowsAsync()).Should().Be(2);
    }

    [Fact]
    public async Task should_keep_read_guard_when_write_guard_bypass_is_active()
    {
        await _SeedAsync("tenant-a");
        fixture.CurrentTenant.Id = null;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadGuardTenantContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ITenantWriteGuardBypass>().BeginBypass();

        var act = () => db.Set<ReadGuardRow>().ToListAsync(AbortToken);

        await act.Should().ThrowExactlyAsync<MissingTenantContextException>();
    }

    [Fact]
    public async Task should_return_host_rows_for_nullable_tenant_column_without_tenant()
    {
        fixture.CurrentTenant.Id = "tenant-a";
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReadGuardTenantContext>();
            db.Add(new ReadGuardHostRow());
            await db.SaveChangesAsync(AbortToken);
        }

        fixture.CurrentTenant.Id = null;
        var host = new ReadGuardHostRow();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReadGuardTenantContext>();
            // The write guard refuses an untenanted Added entry; the bypass lets the host row keep its null tenant.
            using var bypass = scope.ServiceProvider.GetRequiredService<ITenantWriteGuardBypass>().BeginBypass();
            db.Add(host);
            await db.SaveChangesAsync(AbortToken);
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReadGuardTenantContext>();

            var rows = await db.Set<ReadGuardHostRow>().ToListAsync(AbortToken);

            rows.Should().ContainSingle().Which.Id.Should().Be(host.Id);
        }
    }

    [Fact]
    public async Task should_return_only_current_tenant_rows_when_tenant_is_set()
    {
        await _SeedAsync("tenant-a");
        await _SeedAsync("tenant-b");
        fixture.CurrentTenant.Id = "tenant-a";
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadGuardTenantContext>();

        var rows = await db.Set<ReadGuardRow>().ToListAsync(AbortToken);

        rows.Should().ContainSingle().Which.TenantId.Should().Be("tenant-a");
    }

    private async Task<int> _CountAllRowsAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadGuardTenantContext>();
        return await db.Set<ReadGuardRow>().IgnoreMultiTenancyFilter().CountAsync(AbortToken);
    }

    private async Task<Guid> _SeedAsync(string tenant)
    {
        fixture.CurrentTenant.Id = tenant;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadGuardTenantContext>();
        var parent = new ReadGuardParent { Rows = [new ReadGuardRow()] };
        db.Add(parent);
        await db.SaveChangesAsync(AbortToken);
        return parent.Id;
    }
}
