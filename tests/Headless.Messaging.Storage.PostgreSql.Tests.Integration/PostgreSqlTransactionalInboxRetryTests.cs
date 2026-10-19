// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlTransactionalInboxRetryTests(PostgreSqlTestFixture fixture)
    : TransactionalInboxRetryConformanceTests
{
    private static readonly string _Schema = $"inbox_retry_{Guid.NewGuid():N}";

    protected override void ConfigureContext(DbContextOptionsBuilder options) =>
        options.UseNpgsql(fixture.ConnectionString);

    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.ConfigureStorage(storage => storage.Schema = _Schema);
        setup.UsePostgreSqlEntityFramework<InboxRetryDbContext>();
    }

    protected override string CreateEffectsTableSql =>
        "CREATE TABLE IF NOT EXISTS \"InboxScopeEffects\" (\"Id\" uuid PRIMARY KEY);";

    protected override int? DatabaseHostPort => fixture.Container.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort);

    protected override void ConfigureContextThrough(DbContextOptionsBuilder options, string host, int port) =>
        options.UseNpgsql(
            new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
            {
                Host = host,
                Port = port,
                Timeout = 5,
            }.ConnectionString
        );
}
