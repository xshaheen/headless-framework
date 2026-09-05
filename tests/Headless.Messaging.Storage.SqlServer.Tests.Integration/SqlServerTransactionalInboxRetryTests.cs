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
    protected override void ConfigureContext(DbContextOptionsBuilder options) =>
        options.UseSqlServer(fixture.ConnectionString);

    protected override void ConfigureStorage(MessagingSetupBuilder setup) =>
        setup.UseEntityFramework<InboxScopeDbContext>(options => options.Schema = "inbox_retry_tests");

    protected override string CreateEffectsTableSql =>
        "IF OBJECT_ID(N'InboxScopeEffects', N'U') IS NULL CREATE TABLE [InboxScopeEffects] ([Id] uniqueidentifier PRIMARY KEY);";
}
