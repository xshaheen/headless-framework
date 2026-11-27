// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Tenancy;

// One collection per provider: a collection fails every test in it when any of its fixtures fails to start, so a
// SQL Server container that cannot start must not take the PostgreSQL classes down with it.
[CollectionDefinition]
public sealed class PostgreSqlMetadataTenantCollection
    : ICollectionFixture<PostgreSqlMetadataTenantFixture>,
        ICollectionFixture<PostgreSqlPlacedMetadataTenantFixture>;

[CollectionDefinition]
public sealed class SqlServerMetadataTenantCollection : ICollectionFixture<SqlServerMetadataTenantFixture>;

public sealed class PostgreSqlMetadataTenantFixture() : MetadataTenantFixture(TenantDatabaseProvider.PostgreSql);

public sealed class SqlServerMetadataTenantFixture() : MetadataTenantFixture(TenantDatabaseProvider.SqlServer);

// A non-default database and schema, standing in for the placement a per-tenant topology would choose.
public sealed class PostgreSqlPlacedMetadataTenantFixture()
    : MetadataTenantFixture(TenantDatabaseProvider.PostgreSql, new TenantDataPlacement("tenant_placed", "placed"));

[Collection<PostgreSqlMetadataTenantCollection>]
public sealed class PostgreSqlMetadataTenantConformanceTests(PostgreSqlMetadataTenantFixture fixture)
    : MetadataTenantConformanceTests<PostgreSqlMetadataTenantFixture>(fixture);

[Collection<SqlServerMetadataTenantCollection>]
public sealed class SqlServerMetadataTenantConformanceTests(SqlServerMetadataTenantFixture fixture)
    : MetadataTenantConformanceTests<SqlServerMetadataTenantFixture>(fixture);

[Collection<PostgreSqlMetadataTenantCollection>]
public sealed class PostgreSqlTenantIsolationKitTests(PostgreSqlMetadataTenantFixture fixture)
    : TenantIsolationKitConformanceTests<PostgreSqlMetadataTenantFixture>(fixture);

[Collection<SqlServerMetadataTenantCollection>]
public sealed class SqlServerTenantIsolationKitTests(SqlServerMetadataTenantFixture fixture)
    : TenantIsolationKitConformanceTests<SqlServerMetadataTenantFixture>(fixture);

[Collection<PostgreSqlMetadataTenantCollection>]
public sealed class PlacedPostgreSqlTenantIsolationKitTests(PostgreSqlPlacedMetadataTenantFixture fixture)
    : TenantIsolationKitConformanceTests<PostgreSqlPlacedMetadataTenantFixture>(fixture);

[Collection<SqlServerMetadataTenantCollection>]
public sealed class SqlServerTenantIndexTests(SqlServerMetadataTenantFixture fixture) : TestBase
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await fixture.ResetAsync(AbortToken);
    }

    [Fact]
    public async Task should_preserve_absent_and_conventional_filters_when_scoping_nullable_tenant_indexes()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataTenantContext>();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(HostTenantRow))!;
        var indexes = entity.GetIndexes().ToArray();
        indexes
            .Single(x => string.Equals(x.Properties[0].Name, nameof(HostTenantRow.Code), StringComparison.Ordinal))
            .GetFilter()
            .Should()
            .BeNull();
        indexes
            .Single(x =>
                string.Equals(x.Properties[0].Name, nameof(HostTenantRow.OptionalCode), StringComparison.Ordinal)
            )
            .GetFilter()
            .Should()
            .Be("[OptionalCode] IS NOT NULL");
    }

    [Fact]
    public async Task should_reject_duplicate_host_business_keys_after_scoping_index()
    {
        fixture.CurrentTenant.Id = null;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataTenantContext>();
        // Interpolation holes become SQL parameters here, so the code stays a hole rather than inline text.
        const string code = "host-code";
        Func<Task<int>> insert = () =>
            db.Database.ExecuteSqlAsync(
                $"INSERT INTO [tenancy].[HostRows] ([Id], [Code], [TenantId]) VALUES ({Guid.NewGuid()}, {code}, NULL)",
                AbortToken
            );
        await insert();
        await insert.Should().ThrowAsync<SqlException>();
    }
}
