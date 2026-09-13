// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.EntityFramework.Contexts.Runtime;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tests.Fixture;

namespace Tests;

[Collection<TenantWriteGuardCollection>]
public sealed class TenantMetadataWriteTests(
    TenantWriteGuardEnabledFixture enabledFixture,
    TenantWriteGuardDisabledFixture disabledFixture
) : TestBase
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        enabledFixture.CurrentTenant.Id = null;
        disabledFixture.CurrentTenant.Id = null;
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        await db.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS tenant_metadata CASCADE", AbortToken);
        await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript(), AbortToken);
    }

    [Fact]
    public async Task should_stamp_clr_and_shadow_tenants_before_required_alternate_key_tracking()
    {
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        var root = new MetadataRow();
        var shadow = new ShadowRow();
        db.Add(root);
        db.Add(shadow);

        root.Owner.Should().Be("tenant-a");
        db.Entry(shadow).Property("TenantId").CurrentValue.Should().Be("tenant-a");
        await db.SaveChangesAsync(AbortToken);
        db.ChangeTracker.Clear();
        (await db.Set<MetadataRow>().SingleAsync(AbortToken)).Owner.Should().Be("tenant-a");
        (await db.Set<ShadowRow>().CountAsync(AbortToken)).Should().Be(1);
    }

    [Fact]
    public void should_stamp_an_already_tracked_entry_transitioning_to_added()
    {
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        using var db = _CreateContext(scope.ServiceProvider);
        var root = new MetadataRow();
        db.Attach(root);
        db.Entry(root).State = EntityState.Added;
        root.Owner.Should().Be("tenant-a");
    }

    [Fact]
    public void should_fail_with_tenancy_error_before_null_alternate_key_registration()
    {
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        using var db = _CreateContext(scope.ServiceProvider);
        var add = () => db.Add(new ShadowRow());
        add.Should().Throw<MissingTenantContextException>();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task should_reject_add_under_one_tenant_and_save_under_another()
    {
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        var root = new MetadataRow();
        db.Add(root);
        enabledFixture.CurrentTenant.Id = "tenant-b";

        var save = () => db.SaveChangesAsync(AbortToken);
        await save.Should().ThrowAsync<CrossTenantWriteException>();
        root.Owner.Should().Be("tenant-a");
        (await db.Set<MetadataRow>().IgnoreQueryFilters().CountAsync(AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task should_preserve_explicit_tenant_and_reject_mismatched_add()
    {
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        var root = new MetadataRow { Owner = "tenant-b" };
        db.Add(root);
        root.Owner.Should().Be("tenant-b");
        var save = () => db.SaveChangesAsync(AbortToken);
        await save.Should().ThrowAsync<CrossTenantWriteException>();
    }

    [Fact]
    public async Task should_not_backfill_cleared_tenant_at_save()
    {
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        var root = new MetadataRow();
        db.Add(root);
        root.Owner = null;
        var save = () => db.SaveChangesAsync(AbortToken);
        await save.Should().ThrowAsync<MissingTenantContextException>();
        root.Owner.Should().BeNull();
    }

    [Fact]
    public async Task should_allow_same_tenant_owned_only_update_with_root_tenant_sql_predicate()
    {
        var id = await _SeedAsync("tenant-a");
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        var sql = new List<string>();
        await using var db = _CreateContext(scope.ServiceProvider, sql);
        var root = await db.Set<MetadataRow>().SingleAsync(x => x.Id == id, AbortToken);
        root.Detail!.Value = "updated";
        db.ChangeTracker.DetectChanges();
        db.Entry(root).State.Should().Be(EntityState.Unchanged);
        await db.SaveChangesAsync(AbortToken);

        sql.Should()
            .Contain(x =>
                x.Contains("UPDATE tenant_metadata.", StringComparison.Ordinal)
                && x.Contains("AND \"Owner\" =", StringComparison.Ordinal)
            );
        db.ChangeTracker.Clear();
        (await db.Set<MetadataRow>().SingleAsync(AbortToken)).Detail!.Value.Should().Be("updated");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_reject_cross_tenant_owned_only_write_even_when_root_is_unchanged(bool addedChild)
    {
        var id = await _SeedAsync("tenant-b", withDetail: !addedChild);
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        var root = await db.Set<MetadataRow>().IgnoreQueryFilters().SingleAsync(x => x.Id == id, AbortToken);
        if (addedChild)
        {
            root.Detail = new OwnedDetail { Value = "compromised" };
        }
        else
        {
            root.Detail!.Value = "compromised";
        }
        db.ChangeTracker.DetectChanges();
        db.Entry(root).State.Should().Be(EntityState.Unchanged);
        var save = () => db.SaveChangesAsync(AbortToken);
        await save.Should().ThrowAsync<CrossTenantWriteException>();
        db.ChangeTracker.Clear();
        var persisted = await db.Set<MetadataRow>().IgnoreQueryFilters().SingleAsync(AbortToken);
        if (addedChild)
        {
            persisted.Detail.Should().BeNull();
        }
        else
        {
            persisted.Detail!.Value.Should().Be("original");
        }
    }

    [Fact]
    public async Task should_check_original_root_tenant_for_added_child_of_unchanged_root()
    {
        var id = await _SeedAsync("tenant-b", withDetail: false);
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        var root = await db.Set<MetadataRow>().IgnoreQueryFilters().SingleAsync(x => x.Id == id, AbortToken);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        root.Owner = "tenant-a";
        var child = new OwnedDetail();
        db.Entry(child).Property("MetadataRowId").CurrentValue = root.Id;
        db.Entry(child).State = EntityState.Added;
        db.Entry(root).State.Should().Be(EntityState.Unchanged);
        var save = () => db.SaveChangesAsync(AbortToken);
        await save.Should().ThrowAsync<CrossTenantWriteException>();
    }

    [Fact]
    public async Task should_reject_detached_owned_update_in_sql_and_preserve_victim()
    {
        var id = await _SeedAsync("tenant-b");
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        var sql = new List<string>();
        await using var db = _CreateContext(scope.ServiceProvider, sql);
        var crafted = new MetadataRow
        {
            Id = id,
            Owner = "tenant-a",
            Detail = new OwnedDetail(),
        };
        db.Attach(crafted);
        crafted.Detail.Value = "compromised";
        db.ChangeTracker.DetectChanges();
        db.Entry(crafted).State.Should().Be(EntityState.Unchanged);

        var save = () => db.SaveChangesAsync(AbortToken);
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
        sql.Should()
            .Contain(x =>
                x.Contains("UPDATE tenant_metadata.", StringComparison.Ordinal)
                && x.Contains("AND \"Owner\" =", StringComparison.Ordinal)
            );
        db.ChangeTracker.Clear();
        (await db.Set<MetadataRow>().IgnoreQueryFilters().SingleAsync(AbortToken))
            .Detail!.Value.Should()
            .Be("original");
    }

    [Fact]
    public async Task should_reject_owned_entry_without_tracked_principal()
    {
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        var child = new OwnedDetail();
        db.Entry(child).Property("MetadataRowId").CurrentValue = Guid.NewGuid();
        db.Entry(child).State = EntityState.Modified;
        var save = () => db.SaveChangesAsync(AbortToken);
        await save.Should().ThrowAsync<CrossTenantWriteException>();
    }

    [Fact]
    public async Task should_allow_explicit_bypass_but_keep_database_predicates()
    {
        var id = await _SeedAsync("tenant-b");
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        var bypass = scope.ServiceProvider.GetRequiredService<ITenantWriteGuardBypass>();
        using (bypass.BeginBypass())
        {
            var row = await db.Set<MetadataRow>().IgnoreQueryFilters().SingleAsync(x => x.Id == id, AbortToken);
            row.Detail!.Value = "admin";
            await db.SaveChangesAsync(AbortToken);
            db.ChangeTracker.Clear();
            var crafted = new MetadataRow
            {
                Id = id,
                Owner = "tenant-a",
                Name = "crafted",
            };
            db.Attach(crafted);
            crafted.Name = "compromised";
            var save = () => db.SaveChangesAsync(AbortToken);
            await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
        }
        db.ChangeTracker.Clear();
        (await db.Set<MetadataRow>().IgnoreQueryFilters().SingleAsync(AbortToken)).Detail!.Value.Should().Be("admin");
    }

    [Fact]
    public async Task should_not_stamp_metadata_only_rows_when_guard_is_disabled()
    {
        disabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = disabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        var root = new MetadataRow();
        db.Add(root);
        root.Owner.Should().BeNull();
        var save = () => db.SaveChangesAsync(AbortToken);
        var failure = await save.Should().ThrowAsync<DbUpdateException>();
        var databaseError = failure.Which.InnerException.Should().BeOfType<PostgresException>().Subject;
        databaseError.SqlState.Should().Be(PostgresErrorCodes.NotNullViolation);
        databaseError.ColumnName.Should().Be(nameof(MetadataRow.Owner));
        root.Owner.Should().BeNull();
    }

    [Fact]
    public async Task should_respect_explicit_interface_opt_out_for_stamping_and_guard()
    {
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        var root = new ExcludedRow();
        db.Add(root);
        await db.SaveChangesAsync(AbortToken);
        root.TenantId.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_reject_trailing_space_on_write_even_under_bypass(bool bypassed)
    {
        var id = await _SeedAsync("tenant-a");
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        using var bypass = bypassed
            ? scope.ServiceProvider.GetRequiredService<ITenantWriteGuardBypass>().BeginBypass()
            : null;
        var root = await db.Set<MetadataRow>().SingleAsync(x => x.Id == id, AbortToken);
        root.Owner = "tenant-a ";
        var save = () => db.SaveChangesAsync(AbortToken);
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*U+0020*");
    }

    [Fact]
    public async Task should_remove_pre_tracking_handlers_on_runtime_disposal()
    {
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = new DbContext(
            new DbContextOptionsBuilder().UseNpgsql(enabledFixture.SqlConnectionString).Options
        );
        var runtime = new HeadlessDbContextRuntime(
            db,
            scope.ServiceProvider.GetRequiredService<HeadlessDbContextServices>()
        );
        runtime.Initialize();
        await runtime.DisposeAsync();
        await runtime.DisposeAsync();
        var handlers = typeof(Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(x => x.GetValue(db.ChangeTracker))
            .OfType<Delegate>()
            .SelectMany(x => x.GetInvocationList());
        handlers.Should().NotContain(x => ReferenceEquals(x.Target, runtime));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_fence_detached_shadow_tenant_writes_in_sql(bool delete)
    {
        enabledFixture.CurrentTenant.Id = "tenant-b";
        using var seedScope = enabledFixture.ServiceProvider.CreateScope();
        await using var seed = _CreateContext(seedScope.ServiceProvider);
        var victim = new ShadowRow();
        seed.Add(victim);
        await seed.SaveChangesAsync(AbortToken);

        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        var crafted = new ShadowRow { Id = victim.Id };
        db.Entry(crafted).Property("TenantId").CurrentValue = "tenant-a";
        db.Attach(crafted);
        if (delete)
        {
            db.Remove(crafted);
        }
        else
        {
            crafted.Name = "compromised";
        }

        var save = () => db.SaveChangesAsync(AbortToken);
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
        seed.ChangeTracker.Clear();
        (await seed.Set<ShadowRow>().IgnoreQueryFilters().SingleAsync(AbortToken)).Name.Should().Be("original");
    }

    [Fact]
    public async Task should_resolve_nested_ownership_using_binary_key_values()
    {
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        var root = new BinaryRow
        {
            Id = [1, 2, 3],
            Detail = new BinaryDetail { Nested = new NestedDetail() },
        };
        db.Add(root);
        await db.SaveChangesAsync(AbortToken);
        db.ChangeTracker.Clear();
        root = await db.Set<BinaryRow>().SingleAsync(AbortToken);
        db.Entry(root.Detail).Property("BinaryRowId").CurrentValue = root.Id.ToArray();
        root.Detail.Nested.Value = "updated";
        await db.SaveChangesAsync(AbortToken);
        db.ChangeTracker.Clear();
        root = await db.Set<BinaryRow>().SingleAsync(AbortToken);
        root.Detail.Nested.Value.Should().Be("updated");

        enabledFixture.CurrentTenant.Id = "tenant-b";
        root.Detail.Nested.Value = "compromised";
        var save = () => db.SaveChangesAsync(AbortToken);
        await save.Should().ThrowAsync<CrossTenantWriteException>();
    }

    [Fact]
    public async Task should_resolve_added_owned_graph_with_temporary_keys()
    {
        enabledFixture.CurrentTenant.Id = "tenant-a";
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        var root = new GeneratedRow { Detail = new GeneratedDetail() };
        db.Add(root);
        db.Entry(root).Property(x => x.Id).IsTemporary.Should().BeTrue();
        await db.SaveChangesAsync(AbortToken);
        root.Id.Should().BePositive();
        db.ChangeTracker.Clear();
        (await db.Set<GeneratedRow>().SingleAsync(AbortToken)).Detail.Value.Should().Be("original");
    }

    private async Task<Guid> _SeedAsync(string tenant, bool withDetail = true)
    {
        enabledFixture.CurrentTenant.Id = tenant;
        using var scope = enabledFixture.ServiceProvider.CreateScope();
        await using var db = _CreateContext(scope.ServiceProvider);
        var root = new MetadataRow { Detail = withDetail ? new OwnedDetail { Value = "original" } : null };
        db.Add(root);
        await db.SaveChangesAsync(AbortToken);
        return root.Id;
    }

    private MetadataContext _CreateContext(IServiceProvider provider, List<string>? sql = null)
    {
        var options = new DbContextOptionsBuilder<MetadataContext>()
            .UseNpgsql(enabledFixture.SqlConnectionString)
            .AddHeadlessExtension();
        if (sql is not null)
        {
            options.LogTo(sql.Add, [RelationalEventId.CommandExecuted]);
        }
        return new MetadataContext(provider.GetRequiredService<HeadlessDbContextServices>(), options.Options);
    }

    private sealed class MetadataContext(HeadlessDbContextServices services, DbContextOptions options)
        : HeadlessDbContext(services, options)
    {
        public override string DefaultSchema => "tenant_metadata";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<MetadataRow>().IsTenantOwned(nameof(MetadataRow.Owner));
            modelBuilder.Entity<MetadataRow>().OwnsOne(x => x.Detail);
            modelBuilder.Entity<ShadowRow>().IsTenantOwned();
            modelBuilder.Entity<ShadowRow>().Property<string>("TenantId");
            modelBuilder.Entity<ShadowRow>().HasAlternateKey("Id", "TenantId");
            modelBuilder.Entity<ExcludedRow>().IsNotTenantOwned();
            modelBuilder.Entity<BinaryRow>().IsTenantOwned();
            modelBuilder.Entity<BinaryRow>().OwnsOne(x => x.Detail, detail => detail.OwnsOne(x => x.Nested));
            modelBuilder.Entity<GeneratedRow>().IsTenantOwned();
            modelBuilder.Entity<GeneratedRow>().OwnsOne(x => x.Detail);
        }
    }

    private sealed class MetadataRow
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string? Owner { get; set; }
        public string Name { get; set; } = "original";
        public OwnedDetail? Detail { get; set; }
    }

    private sealed class OwnedDetail
    {
        public string Value { get; set; } = "original";
    }

    private sealed class ShadowRow
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "original";
    }

    private sealed class BinaryRow
    {
        public byte[] Id { get; set; } = [];
        public string? TenantId { get; set; }
        public BinaryDetail Detail { get; set; } = new();
    }

    private sealed class BinaryDetail
    {
        public string Value { get; set; } = "original";
        public NestedDetail Nested { get; set; } = new();
    }

    private sealed class NestedDetail
    {
        public string Value { get; set; } = "original";
    }

    private sealed class GeneratedRow
    {
        public int Id { get; set; }
        public string? TenantId { get; set; }
        public GeneratedDetail Detail { get; set; } = new();
    }

    private sealed class GeneratedDetail
    {
        public string Value { get; set; } = "original";
    }

    private sealed class ExcludedRow : IMultiTenant
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string? TenantId { get; set; }
    }
}
