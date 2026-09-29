// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Hosting.Validation;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tests.Fixture;

namespace Tests.Tenancy;

[Collection<TenantPlacementCollection>]
public sealed class TenantDataPlacementRoutingTests(TenantPlacementDbContextTestFixture fixture) : TestBase
{
    private const string _A = TenantPlacementDbContextTestFixture.SchemaTenantA;
    private const string _B = TenantPlacementDbContextTestFixture.SchemaTenantB;
    private const string _DatabaseA = TenantPlacementDbContextTestFixture.DatabaseTenantA;
    private const string _DatabaseB = TenantPlacementDbContextTestFixture.DatabaseTenantB;
    private const string _SharedA = TenantPlacementDbContextTestFixture.SharedTenantA;
    private const string _SharedB = TenantPlacementDbContextTestFixture.SharedTenantB;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await fixture.ResetAsync();
    }

    [Fact]
    public async Task should_keep_each_schema_tenant_rows_in_its_own_schema()
    {
        // given
        await _AddRowAsync(_A, "row-a");
        await _AddRowAsync(_B, "row-b");

        // when
        var aRows = await _ReadNamesAsync(_A);
        var bRows = await _ReadNamesAsync(_B);

        // then
        aRows.Should().Equal("row-a");
        bRows.Should().Equal("row-b");
        (await _CountRawAsync(fixture.SharedConnectionString, "tenant_a")).Should().Be(1);
        (await _CountRawAsync(fixture.SharedConnectionString, "tenant_b")).Should().Be(1);
        (await _CountRawAsync(fixture.SharedConnectionString, "app")).Should().Be(0);
    }

    [Fact]
    public async Task should_keep_each_database_tenant_rows_in_its_own_database()
    {
        // given: a query first, then a save, so the second open reuses the pinned connection check
        using (fixture.CurrentTenant.Change(_DatabaseA))
        {
            await using var db = await fixture.CreateAsync<PlacementDbContext>(AbortToken);
            (await db.Rows.CountAsync(AbortToken)).Should().Be(0);
            db.Rows.Add(new PlacedRow { Name = "row-da" });
            await db.SaveChangesAsync(AbortToken);
            new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database.Should().Be("tenant_da");
        }

        await _AddRowAsync(_DatabaseB, "row-db");

        // then
        (await _CountRawAsync(fixture.TenantDatabaseConnectionString("tenant_da"), "app"))
            .Should()
            .Be(1);
        (await _CountRawAsync(fixture.TenantDatabaseConnectionString("tenant_db"), "app")).Should().Be(1);
        (await _CountRawAsync(fixture.SharedConnectionString, "app")).Should().Be(0);
    }

    [Fact]
    public async Task should_share_one_model_per_schema_and_build_one_per_distinct_schema()
    {
        var a1 = await _ModelAsync(_A);
        var a2 = await _ModelAsync(_A);
        var b = await _ModelAsync(_B);

        a1.Should().BeSameAs(a2);
        b.Should().NotBeSameAs(a1);
    }

    [Fact]
    public async Task should_keep_models_cached_across_many_schemas()
    {
        // given: more schemas than one EF internal service provider keeps without an explicit cache budget
        var first = await _ModelAsync(TenantPlacementDbContextTestFixture.ManySchemaTenant(0));

        for (var i = 1; i < TenantPlacementDbContextTestFixture.ManySchemaTenantCount; i++)
        {
            await _ModelAsync(TenantPlacementDbContextTestFixture.ManySchemaTenant(i));
        }

        // when
        var again = await _ModelAsync(TenantPlacementDbContextTestFixture.ManySchemaTenant(0));

        // then: still cached, and building 45+ tenant models did not create a service provider per tenant
        again.Should().BeSameAs(first);
    }

    [Fact]
    public async Task should_keep_hybrid_shared_tenants_in_the_shared_schema_separated_by_the_filter()
    {
        // given: two tenants whose placement names only the shared database, next to schema tenant a
        await _AddRowAsync(_SharedA, "row-sa");
        await _AddRowAsync(_SharedB, "row-sb");
        await _AddRowAsync(_A, "row-a");

        // when
        var sharedARows = await _ReadNamesAsync(_SharedA);
        var sharedBRows = await _ReadNamesAsync(_SharedB);

        // then
        sharedARows.Should().Equal("row-sa");
        sharedBRows.Should().Equal("row-sb");
        (await _CountRawAsync(fixture.SharedConnectionString, "app")).Should().Be(2);
        (await _CountRawAsync(fixture.SharedConnectionString, "tenant_a")).Should().Be(1);
        (await _ModelAsync(_SharedA)).FindEntityType(typeof(PlacedRow))!.GetSchema().Should().Be("app");
    }

    [Fact]
    public async Task should_use_registration_placement_without_an_ambient_tenant()
    {
        // given
        await using var scope = fixture.Services.CreateAsyncScope();

        // when: injected directly, no tenant
        var db = scope.ServiceProvider.GetRequiredService<PlacementDbContext>();

        // then
        new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString())
            .Database.Should()
            .Be("placement_shared");
        db.Model.FindEntityType(typeof(PlacedRow))!.GetSchema().Should().Be("app");
        (await db.Notes.CountAsync(AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task should_refuse_a_query_after_the_ambient_tenant_changes()
    {
        // given
        using var tenant = fixture.CurrentTenant.Change(_A);
        await using var db = await fixture.CreateAsync<PlacementDbContext>(AbortToken);

        db.Rows.Add(new PlacedRow { Name = "added-under-a" });

        // when
        using var other = fixture.CurrentTenant.Change(_B);

        // then
        await FluentActions
            .Awaiting(() => db.Rows.ToListAsync(AbortToken))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*pinned to tenant 'a'*ambient tenant is now 'b'*");
        FluentActions
            .Invoking(() => db.Rows.Add(new PlacedRow { Name = "added-under-b" }))
            .Should()
            .Throw<InvalidOperationException>();
        await FluentActions
            .Awaiting(() => db.SaveChangesAsync(AbortToken))
            .Should()
            .ThrowAsync<InvalidOperationException>();
        await FluentActions
            .Awaiting(() => db.Database.OpenConnectionAsync(AbortToken))
            .Should()
            .ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task should_refuse_commands_on_an_open_connection_after_the_ambient_tenant_changes()
    {
        // given
        using var tenant = fixture.CurrentTenant.Change(_A);
        await using var db = await fixture.CreateAsync<PlacementDbContext>(AbortToken);
        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);

        // when
        using var other = fixture.CurrentTenant.Change(_B);

        // then: neither raw SQL nor a query over a non-tenant-owned entity reads the context's tenant id
        await FluentActions
            .Awaiting(() => db.Database.ExecuteSqlRawAsync("SELECT 1", AbortToken))
            .Should()
            .ThrowAsync<InvalidOperationException>();
        await FluentActions
            .Awaiting(() => db.Notes.ToListAsync(AbortToken))
            .Should()
            .ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task should_refuse_a_tenant_without_placement()
    {
        // given
        using var tenant = fixture.CurrentTenant.Change(TenantPlacementDbContextTestFixture.UnplacedTenant);

        // when
        var act = () => fixture.CreateAsync<PlacementDbContext>(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*'unplaced' has no data placement*never fall back to the shared database*");
    }

    [Fact]
    public async Task should_refuse_direct_injection_and_synchronous_creation_under_a_tenant()
    {
        // given
        using var tenant = fixture.CurrentTenant.Change(_A);
        await using var scope = fixture.Services.CreateAsyncScope();

        // when
        var inject = () => scope.ServiceProvider.GetRequiredService<PlacementDbContext>();
        var createSync = () =>
            fixture.Services.GetRequiredService<IDbContextFactory<PlacementDbContext>>().CreateDbContext();

        // then
        inject.Should().Throw<InvalidOperationException>().WithMessage("*CreateDbContextAsync*");
        createSync.Should().Throw<InvalidOperationException>().WithMessage("*CreateDbContextAsync*");
    }

    [Fact]
    public async Task should_leave_an_unrouted_context_on_the_live_ambient_tenant()
    {
        // given
        using var tenant = fixture.CurrentTenant.Change(_A);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<UnroutedDbContext>();

        // when
        using var other = fixture.CurrentTenant.Change(_B);

        // then
        db.TenantId.Should().Be(_B);
        new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database.Should().Be("placement_shared");
        (await db.Notes.CountAsync(AbortToken)).Should().Be(0);
    }

    [Theory]
    [InlineData(_A, "tenant_a")]
    [InlineData(_DatabaseA, "app")]
    public async Task should_apply_query_filter_and_write_guard_under_tenant_placement(string tenantId, string schema)
    {
        // given: a row stamped for another tenant, planted in this tenant's own store
        using var tenant = fixture.CurrentTenant.Change(tenantId);
        await using (var seed = await fixture.CreateAsync<PlacementDbContext>(AbortToken))
        {
            var insert =
                "INSERT INTO \""
                + schema
                + "\".\"Rows\" (\"Id\", \"Name\", \"TenantId\") VALUES ({0}, 'planted', 'intruder')";
            await seed.Database.ExecuteSqlRawAsync(insert, [Guid.NewGuid()], AbortToken);
        }

        await using var db = await fixture.CreateAsync<PlacementDbContext>(AbortToken);

        // when
        var visible = await db.Rows.CountAsync(AbortToken);
        var planted = await db.Rows.IgnoreMultiTenancyFilter().SingleAsync(AbortToken);
        planted.Name = "changed";
        var save = () => db.SaveChangesAsync(AbortToken);

        // then
        visible.Should().Be(0);
        await save.Should().ThrowAsync<CrossTenantWriteException>();
    }

    [Fact]
    public async Task should_apply_read_guard_through_a_host_pinned_routed_context()
    {
        // given
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlacementDbContext>();

        // when
        var act = () => db.Rows.ToListAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<MissingTenantContextException>();
    }

    [Theory]
    [InlineData(_A)]
    [InlineData(_DatabaseA)]
    public async Task should_refuse_a_routed_context_that_skips_base_configuration(string tenantId)
    {
        // given: without base.OnConfiguring a schema placement would share one model across schemas and a
        // database placement would silently stay on the registration database
        using var tenant = fixture.CurrentTenant.Change(tenantId);

        // when
        var act = () => fixture.CreateAsync<SkippingBaseConfigurationDbContext>(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*base.OnConfiguring*");
    }

    [Fact]
    public async Task should_refuse_a_tenant_owned_entity_in_an_explicit_schema()
    {
        using var tenant = fixture.CurrentTenant.Change(_A);

        var act = () => fixture.CreateAsync<ExplicitTenantSchemaDbContext>(AbortToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*schema 'audit'*tenant schema 'tenant_a'*");
    }

    [Fact]
    public async Task should_refuse_a_shared_entity_in_an_explicit_schema()
    {
        using var tenant = fixture.CurrentTenant.Change(_A);

        var act = () => fixture.CreateAsync<ExplicitSharedSchemaDbContext>(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*migrations would create it again*");
    }

    [Fact]
    public async Task should_keep_explicit_schemas_under_a_database_only_placement()
    {
        using var tenant = fixture.CurrentTenant.Change(_DatabaseA);

        await using var db = await fixture.CreateAsync<ExplicitSharedSchemaDbContext>(AbortToken);

        db.Model.FindEntityType(typeof(PlacedNote))!.GetSchema().Should().Be("audit");
    }

    [Fact]
    public async Task should_route_a_data_source_configured_context_to_the_tenant_database()
    {
        using var tenant = fixture.CurrentTenant.Change(_DatabaseA);
        await using var db = await fixture.CreateAsync<DataSourceDbContext>(AbortToken);

        await db.Database.OpenConnectionAsync(AbortToken);

        db.Database.GetDbConnection().Database.Should().Be("tenant_da");
    }

    [Fact]
    public async Task should_refuse_a_connection_swapped_away_from_the_tenant_database_without_leaking_it()
    {
        // given
        using var tenant = fixture.CurrentTenant.Change(_DatabaseA);
        await using var db = await fixture.CreateAsync<PlacementDbContext>(AbortToken);
        db.Database.SetConnectionString(fixture.SharedConnectionString);

        // when
        var act = () => db.Database.OpenConnectionAsync(AbortToken);

        // then
        var error = await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenant 'da'*");
        error.Which.Message.Should().NotContain(fixture.SharedConnectionString);
        error.Which.Message.Should().NotContain(fixture.TenantDatabaseConnectionString("tenant_da"));
    }

    [Fact]
    public async Task should_open_a_tenant_database_through_a_provider_managed_data_source()
    {
        using var tenant = fixture.CurrentTenant.Change(_DatabaseA);
        await using var db = await fixture.CreateAsync<EnumMappedDbContext>(AbortToken);

        await db.Database.OpenConnectionAsync(AbortToken);
        await db.Database.CloseConnectionAsync();
        await db.Database.OpenConnectionAsync(AbortToken);

        db.Database.GetDbConnection().Database.Should().Be("tenant_da");
    }

    [Fact]
    public async Task should_fail_startup_when_routing_without_a_placement_source()
    {
        // given
        await using var services = fixture.CreateServices(_ => { });

        // when
        async Task validateAsync()
        {
            foreach (var validator in services.GetServices<IHeadlessStartupValidator>())
            {
                await validator.ValidateAsync(AbortToken);
            }
        }

        // then
        (await FluentActions.Awaiting(validateAsync).Should().ThrowAsync<HeadlessTenancyValidationException>())
            .Which.Message.Should()
            .Contain("HEADLESS_TENANCY_EF_ROUTING_WITHOUT_PLACEMENT");
    }

    private async Task _AddRowAsync(string tenantId, string name)
    {
        using var tenant = fixture.CurrentTenant.Change(tenantId);
        await using var db = await fixture.CreateAsync<PlacementDbContext>(AbortToken);
        db.Rows.Add(new PlacedRow { Name = name });
        await db.SaveChangesAsync(AbortToken);
    }

    private async Task<List<string>> _ReadNamesAsync(string tenantId)
    {
        using var tenant = fixture.CurrentTenant.Change(tenantId);
        await using var db = await fixture.CreateAsync<PlacementDbContext>(AbortToken);

        return await db.Rows.Select(x => x.Name).ToListAsync(AbortToken);
    }

    private async Task<IModel> _ModelAsync(string tenantId)
    {
        using var tenant = fixture.CurrentTenant.Change(tenantId);
        await using var db = await fixture.CreateAsync<PlacementDbContext>(AbortToken);

        return db.Model;
    }

    private async Task<long> _CountRawAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT count(*) FROM "{schema}"."Rows" """;

        return (long)(await command.ExecuteScalarAsync(AbortToken))!;
    }
}
