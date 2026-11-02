// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging;
using Npgsql;

namespace Tests;

/// <summary>
/// The raw storage's transactional inbox on a multi-host connection string: Npgsql reports no data source for the
/// storage's unopened connection, so the attempt must still be recognized as running on the storage's own database.
/// </summary>
[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlMultiHostRawTransactionalInboxTests(PostgreSqlTestFixture fixture)
    : RawTransactionalInboxConformanceTests
{
    private static readonly string _Schema = $"raw_inbox_multi_host_{Guid.NewGuid():N}";

    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.ConfigureStorage(storage => storage.Schema = _Schema);
        setup.UsePostgreSql(fixture.MultiHostConnectionString);
    }

    protected override DbConnection CreateConnection() => new NpgsqlConnection(fixture.MultiHostConnectionString);

    protected override string CreateEffectsTableSql =>
        "CREATE TABLE IF NOT EXISTS \"RawInboxEffects\" (\"Id\" uuid NOT NULL, \"Entry\" integer NOT NULL);";

    protected override string InsertEffectSql =>
        "INSERT INTO \"RawInboxEffects\" (\"Id\", \"Entry\") VALUES (@id, @entry);";

    protected override string SelectEffectEntriesSql =>
        "SELECT \"Entry\" FROM \"RawInboxEffects\" WHERE \"Id\" = @id ORDER BY \"Entry\";";

    protected override string ReplaceAttemptSql(string receivedTable) =>
        $"UPDATE {receivedTable} SET \"attempt_id\"=@attempt WHERE \"id\"=@id;";
}
