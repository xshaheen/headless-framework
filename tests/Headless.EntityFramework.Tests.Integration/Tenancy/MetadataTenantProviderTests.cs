// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Tenancy;

[CollectionDefinition]
public sealed class MetadataTenantCollection
    : ICollectionFixture<PostgreSqlMetadataTenantFixture>,
        ICollectionFixture<SqlServerMetadataTenantFixture>,
        ICollectionFixture<PostgreSqlPlacedMetadataTenantFixture>;

public sealed class PostgreSqlMetadataTenantFixture() : MetadataTenantFixture(TenantDatabaseProvider.PostgreSql);

public sealed class SqlServerMetadataTenantFixture() : MetadataTenantFixture(TenantDatabaseProvider.SqlServer);

// A non-default database and schema, standing in for the placement a per-tenant topology would choose.
public sealed class PostgreSqlPlacedMetadataTenantFixture()
    : MetadataTenantFixture(TenantDatabaseProvider.PostgreSql, new TenantDataPlacement("tenant_placed", "placed"));

[Collection<MetadataTenantCollection>]
public sealed class PostgreSqlMetadataTenantConformanceTests(PostgreSqlMetadataTenantFixture fixture)
    : MetadataTenantConformanceTests<PostgreSqlMetadataTenantFixture>(fixture);

[Collection<MetadataTenantCollection>]
public sealed class SqlServerMetadataTenantConformanceTests(SqlServerMetadataTenantFixture fixture)
    : MetadataTenantConformanceTests<SqlServerMetadataTenantFixture>(fixture);

[Collection<MetadataTenantCollection>]
public sealed class PostgreSqlTenantIsolationKitTests(PostgreSqlMetadataTenantFixture fixture)
    : TenantIsolationKitConformanceTests<PostgreSqlMetadataTenantFixture>(fixture);

[Collection<MetadataTenantCollection>]
public sealed class SqlServerTenantIsolationKitTests(SqlServerMetadataTenantFixture fixture)
    : TenantIsolationKitConformanceTests<SqlServerMetadataTenantFixture>(fixture);

[Collection<MetadataTenantCollection>]
public sealed class PlacedPostgreSqlTenantIsolationKitTests(PostgreSqlPlacedMetadataTenantFixture fixture)
    : TenantIsolationKitConformanceTests<PostgreSqlPlacedMetadataTenantFixture>(fixture);

[Collection<MetadataTenantCollection>]
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
            db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [tenancy].[HostRows] ([Id], [Code], [TenantId]) VALUES ({Guid.NewGuid()}, {code}, NULL)",
                AbortToken
            );
        await insert();
        await insert.Should().ThrowAsync<SqlException>();
    }
}
