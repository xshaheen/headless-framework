// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging;
using Microsoft.Data.SqlClient;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerRawTransactionalInboxTests(SqlServerTestFixture fixture)
    : RawTransactionalInboxConformanceTests
{
    private static readonly string _Schema = $"raw_inbox_{Guid.NewGuid():N}";

    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.ConfigureStorage(storage => storage.Schema = _Schema);
        setup.UseSqlServer(fixture.ConnectionString);
    }

    protected override DbConnection CreateConnection() => new SqlConnection(fixture.ConnectionString);

    protected override string CreateEffectsTableSql =>
        "IF OBJECT_ID(N'RawInboxEffects', N'U') IS NULL CREATE TABLE [RawInboxEffects] ([Id] uniqueidentifier NOT NULL, [Entry] int NOT NULL);";

    protected override string InsertEffectSql => "INSERT INTO [RawInboxEffects] ([Id], [Entry]) VALUES (@id, @entry);";

    protected override string SelectEffectEntriesSql =>
        "SELECT [Entry] FROM [RawInboxEffects] WHERE [Id] = @id ORDER BY [Entry];";

    protected override string ReplaceAttemptSql(string receivedTable) =>
        $"UPDATE {receivedTable} SET [AttemptId]=@attempt WHERE [Id]=@id;";
}
