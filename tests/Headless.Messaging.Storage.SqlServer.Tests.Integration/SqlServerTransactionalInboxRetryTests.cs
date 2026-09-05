// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Microsoft.EntityFrameworkCore;
using InboxScopeDbContext = Tests.TransactionalInboxScopeConformanceTests.InboxScopeDbContext;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerTransactionalInboxRetryTests(SqlServerTestFixture fixture)
    : TransactionalInboxRetryConformanceTests
{
    private static readonly string _Schema = $"inbox_retry_{Guid.NewGuid():N}";

    protected override void ConfigureContext(DbContextOptionsBuilder options) =>
        options.UseSqlServer(fixture.ConnectionString);

    protected override void ConfigureStorage(MessagingSetupBuilder setup) =>
        setup.UseEntityFramework<InboxScopeDbContext>(options => options.Schema = _Schema);

    protected override string CreateEffectsTableSql =>
        "IF OBJECT_ID(N'InboxScopeEffects', N'U') IS NULL CREATE TABLE [InboxScopeEffects] ([Id] uniqueidentifier PRIMARY KEY);";
}
