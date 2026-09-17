// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Tenancy;

[CollectionDefinition(DisableParallelization = true)]
public sealed class MetadataTenantCollection
    : ICollectionFixture<PostgreSqlMetadataTenantFixture>,
        ICollectionFixture<SqlServerMetadataTenantFixture>;

public sealed class PostgreSqlMetadataTenantFixture() : MetadataTenantFixture(TenantDatabaseProvider.PostgreSql);

public sealed class SqlServerMetadataTenantFixture() : MetadataTenantFixture(TenantDatabaseProvider.SqlServer);

[Collection<MetadataTenantCollection>]
public sealed class PostgreSqlMetadataTenantConformanceTests(PostgreSqlMetadataTenantFixture fixture)
    : MetadataTenantConformanceTests<PostgreSqlMetadataTenantFixture>(fixture);

[Collection<MetadataTenantCollection>]
public sealed class SqlServerMetadataTenantConformanceTests(SqlServerMetadataTenantFixture fixture)
    : MetadataTenantConformanceTests<SqlServerMetadataTenantFixture>(fixture);

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
        Func<Task<int>> insert = () =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [tenancy].[HostRows] ([Id], [Code], [TenantId]) VALUES ({Guid.NewGuid()}, {"host-code"}, NULL)",
                AbortToken
            );
        await insert();
        await insert.Should().ThrowAsync<SqlException>();
    }
}
