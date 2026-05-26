// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixtures;

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
        services.AddHeadlessDbContext<
            TestIdentityDbContext,
            TestUser,
            TestRole,
            string,
            IdentityUserClaim<string>,
            IdentityUserRole<string>,
            IdentityUserLogin<string>,
            IdentityRoleClaim<string>,
            IdentityUserToken<string>,
            IdentityUserPasskey<string>
        >(options => options.UseNpgsql(SqlConnectionString));
    }
}
