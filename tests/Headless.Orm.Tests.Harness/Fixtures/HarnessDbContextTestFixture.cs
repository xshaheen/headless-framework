// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Orm.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tests.Fixtures;

/// <summary>
/// Test fixture for HarnessDbContext integration tests with PostgreSQL.
/// </summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class HarnessDbContextTestFixture
    : PostgreSqlDbContextTestFixture<HarnessDbContext>,
        ICollectionFixture<HarnessDbContextTestFixture>
{
    protected override void ConfigureDbContext(IServiceCollection services)
    {
        services.AddDbContext<HarnessDbContext>(options =>
            options.UseNpgsql(SqlConnectionString).AddHeadlessExtension()
        );
    }
}
