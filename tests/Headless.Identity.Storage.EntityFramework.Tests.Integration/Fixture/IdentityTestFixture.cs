// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Orm.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixtures;
using Xunit;

namespace Tests.Fixture;

/// <summary>
/// Test fixture for Identity DbContext integration tests with PostgreSQL.
/// </summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class IdentityTestFixture
    : PostgreSqlDbContextTestFixture<TestIdentityDbContext>,
        ICollectionFixture<IdentityTestFixture>
{
    protected override void ConfigureDbContext(IServiceCollection services)
    {
        services.AddDbContext<TestIdentityDbContext>(options =>
            options.UseNpgsql(SqlConnectionString).AddHeadlessExtension()
        );
    }
}
