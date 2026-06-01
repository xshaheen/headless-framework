// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Testcontainers.PostgreSql;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlAuditLogFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlAuditLogFixture>
{
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("audit_log_storage_test").WithUsername("postgres").WithPassword("postgres");
    }
}
