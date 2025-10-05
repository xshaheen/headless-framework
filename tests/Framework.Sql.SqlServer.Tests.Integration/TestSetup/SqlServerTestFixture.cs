// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.MsSql;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests.TestSetup;

[CollectionDefinition]
public sealed class SqlServerTestFixture(IMessageSink messageSink)
    : ContainerFixture<MsSqlBuilder, MsSqlContainer>(messageSink),
        ICollectionFixture<SqlServerTestFixture>;
