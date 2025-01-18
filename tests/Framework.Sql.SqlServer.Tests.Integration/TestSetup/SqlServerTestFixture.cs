// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.MsSql;
using Testcontainers.Xunit;

namespace Tests.TestSetup;

[CollectionDefinition(nameof(SqlServerTestFixture))]
public sealed class SqlServerTestFixture(IMessageSink messageSink)
    : ContainerFixture<MsSqlBuilder, MsSqlContainer>(messageSink),
        ICollectionFixture<SqlServerTestFixture>;
