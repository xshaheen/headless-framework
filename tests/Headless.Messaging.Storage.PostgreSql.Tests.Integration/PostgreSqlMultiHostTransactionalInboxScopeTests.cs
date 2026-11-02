// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>
/// The EF storage's transactional inbox on a multi-host connection string: the storage reads its connection from the
/// context, so both report no data source until opened, and the attempt must still join the storage.
/// </summary>
[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlMultiHostTransactionalInboxScopeTests(PostgreSqlTestFixture fixture)
    : TransactionalInboxScopeConformanceTests
{
    private static readonly string _Schema = $"inbox_scope_multi_host_{Guid.NewGuid():N}";

    protected override void ConfigureContext(DbContextOptionsBuilder options) =>
        options.UseNpgsql(fixture.MultiHostConnectionString);

    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.ConfigureStorage(storage => storage.Schema = _Schema);
        setup.UsePostgreSqlEntityFramework<InboxScopeDbContext>();
    }

    protected override string CreateEffectsTableSql =>
        "CREATE TABLE IF NOT EXISTS \"TenantInboxScopeEffects\" (\"Id\" uuid PRIMARY KEY, \"TenantId\" text NULL);";

    protected override string ReplaceAttemptSql(string receivedTable) =>
        $"UPDATE {receivedTable} SET \"attempt_id\"=@attempt WHERE \"id\"=@id;";
}
