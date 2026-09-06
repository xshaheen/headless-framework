// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Tests;

[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlTransactionalInboxScopeTests(PostgreSqlTestFixture fixture)
    : TransactionalInboxScopeConformanceTests
{
    private static readonly string _Schema = $"inbox_scope_{Guid.NewGuid():N}";

    protected override void ConfigureContext(DbContextOptionsBuilder options) =>
        options.UseNpgsql(fixture.ConnectionString);

    protected override void ConfigureStorage(MessagingSetupBuilder setup) =>
        setup.UseEntityFramework<InboxScopeDbContext>(options => options.Schema = _Schema);

    protected override string CreateEffectsTableSql =>
        "CREATE TABLE IF NOT EXISTS \"InboxScopeEffects\" (\"Id\" uuid PRIMARY KEY);";

    protected override string ReplaceAttemptSql(string receivedTable) =>
        $"UPDATE {receivedTable} SET \"AttemptId\"=@attempt WHERE \"Id\"=@id;";
}
