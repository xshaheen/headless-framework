// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.EntityFramework;
using Headless.EntityFramework.Testing;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Tenancy;

/// <summary>
/// Proves the tenant-isolation assertions against a real database with the tenant write guard on, under whatever
/// <see cref="TenantDataPlacement"/> the fixture was built with.
/// </summary>
public abstract class TenantIsolationKitConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : MetadataTenantFixture
{
    private readonly TenantWorld _world = new(fixture.CurrentTenant);
    private AsyncServiceScope _scope;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await fixture.ResetAsync(AbortToken);
        _scope = fixture.Services.CreateAsyncScope();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _scope.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_isolate_a_clr_owned_row()
    {
        var id = await _SeedAsync();

        await TenantIsolationDbAssertions.ShouldNotSeeAcrossTenantsAsync<TenantRow>(_world, _Create, id, AbortToken);
    }

    [Fact]
    public async Task should_isolate_a_shadow_owned_row()
    {
        var id = await _SeedAsync();

        await TenantIsolationDbAssertions.ShouldNotSeeAcrossTenantsAsync<ShadowTenantRow>(
            _world,
            _Create,
            id,
            AbortToken
        );
    }

    [Fact]
    public async Task should_leave_the_victim_row_unchanged_after_refused_writes()
    {
        var id = await _SeedAsync();

        await TenantIsolationDbAssertions.ShouldRefuseWritesAcrossTenantsAsync<TenantRow>(
            _world,
            _Create,
            id,
            row => row.Name = "compromised",
            AbortToken
        );

        using var _ = _world.AsTenantA();
        await using var db = _Create();
        var row = await db.Set<TenantRow>().SingleAsync(x => x.Id == id, AbortToken);
        row.Name.Should().Be("original");
        row.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task should_let_the_owner_write_its_own_row()
    {
        // The refusal only proves isolation if the guard discriminates by tenant rather than refusing every write.
        var id = await _SeedAsync();

        using var _ = _world.AsTenantA();
        await using var db = _Create();
        var row = await db.Set<TenantRow>().SingleAsync(x => x.Id == id, AbortToken);
        row.Name = "renamed";
        await db.SaveChangesAsync(AbortToken);

        db.ChangeTracker.Clear();
        (await db.Set<TenantRow>().SingleAsync(x => x.Id == id, AbortToken)).Name.Should().Be("renamed");
    }

    [Fact]
    public async Task should_store_rows_in_the_fixture_placement()
    {
        await _SeedAsync();

        await using var db = _Create();
        db.Model.FindEntityType(typeof(TenantRow))!.GetSchema().Should().Be(fixture.Placement.Schema);
        var sql = db.GetService<ISqlGenerationHelper>();
        await db.Database.OpenConnectionAsync(AbortToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {sql.DelimitIdentifier("Rows", fixture.Placement.Schema)}";
        Convert.ToInt32(await command.ExecuteScalarAsync(AbortToken), CultureInfo.InvariantCulture).Should().Be(1);
        db.Database.GetDbConnection().Database.Should().Be(fixture.Placement.Database);
    }

    private MetadataTenantContext _Create()
    {
        var options = new DbContextOptionsBuilder<MetadataTenantContext>();
        fixture.ConfigureOptions(options);

        return new MetadataTenantContext(
            _scope.ServiceProvider.GetRequiredService<HeadlessDbContextServices>(),
            options.Options,
            fixture.Placement
        );
    }

    private async Task<Guid> _SeedAsync()
    {
        using var _ = _world.AsTenantA();
        await using var db = _Create();
        var row = new TenantRow();
        db.Add(row);
        db.Add(new ShadowTenantRow { Id = row.Id });
        await db.SaveChangesAsync(AbortToken);

        return row.Id;
    }
}
