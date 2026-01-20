// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.LocalStack;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition]
public sealed class LocalStackTestFixture(IMessageSink messageSink)
    : ContainerFixture<LocalStackBuilder, LocalStackContainer>(messageSink),
        ICollectionFixture<LocalStackTestFixture>
{
    protected override LocalStackBuilder Configure()
    {
        return base.Configure().WithImage("localstack/localstack:latest");
    }
}
