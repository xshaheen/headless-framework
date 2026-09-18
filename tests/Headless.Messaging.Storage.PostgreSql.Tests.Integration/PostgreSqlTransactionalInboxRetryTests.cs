// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Microsoft.EntityFrameworkCore;

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
        setup.UseEntityFramework<InboxRetryDbContext>();
    }

    protected override string CreateEffectsTableSql =>
        "CREATE TABLE IF NOT EXISTS \"InboxScopeEffects\" (\"Id\" uuid PRIMARY KEY);";
}
