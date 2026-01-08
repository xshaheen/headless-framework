// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.LocalStack;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests.TestSetup;

[CollectionDefinition]
public sealed class AwsBlobTestFixture(IMessageSink messageSink)
    : ContainerFixture<LocalStackBuilder, LocalStackContainer>(messageSink),
        ICollectionFixture<AwsBlobTestFixture>
{
    protected override LocalStackBuilder Configure()
    {
        return base.Configure()
            .WithImage("localstack/localstack:4.0.3")
            .WithEnvironment("SERVICES", "s3")
            .WithEnvironment("DEBUG", "1")
            .WithPortBinding(8055, 8080)
            .WithReuse(true);
    }
}
