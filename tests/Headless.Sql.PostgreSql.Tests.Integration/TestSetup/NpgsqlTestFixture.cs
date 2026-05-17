// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Testcontainers.PostgreSql;

namespace Tests.TestSetup;

[UsedImplicitly]
[CollectionDefinition]
public sealed class NpgsqlTestFixture : HeadlessPostgreSqlFixture, ICollectionFixture<NpgsqlTestFixture>
{
    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("headless_test").WithUsername("postgres").WithPassword("postgres");
    }
}
