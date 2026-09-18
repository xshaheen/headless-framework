// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerTransactionalInboxScopeTests(SqlServerTestFixture fixture)
    : TransactionalInboxScopeConformanceTests
{
    private static readonly string _Schema = $"inbox_scope_{Guid.NewGuid():N}";

    protected override void ConfigureContext(DbContextOptionsBuilder options) =>
        options.UseSqlServer(fixture.ConnectionString);

    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.ConfigureStorage(storage => storage.Schema = _Schema);
        setup.UseEntityFramework<InboxScopeDbContext>();
    }

    protected override string CreateEffectsTableSql =>
        "IF OBJECT_ID(N'TenantInboxScopeEffects', N'U') IS NULL CREATE TABLE [TenantInboxScopeEffects] ([Id] uniqueidentifier PRIMARY KEY, [TenantId] nvarchar(64) NULL);";

    protected override string ReplaceAttemptSql(string receivedTable) =>
        $"UPDATE {receivedTable} SET [AttemptId]=@attempt WHERE [Id]=@id;";
}
