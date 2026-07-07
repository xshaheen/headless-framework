// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerMembershipFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerMembershipFixture>,
        ICoordinationFixture
{
    public void ConfigureProvider(IServiceCollection services, HeadlessCoordinationSetupBuilder setup)
    {
        setup.UseSqlServer(options => options.ConnectionString = ConnectionString);
    }
}
