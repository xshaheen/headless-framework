// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.LocalStack;
using Testcontainers.Xunit;

namespace Tests.TestSetup;

public sealed class AwsBlobTestFixture(IMessageSink messageSink)
    : ContainerFixture<LocalStackBuilder, LocalStackContainer>(messageSink)
{
    protected override LocalStackBuilder Configure(LocalStackBuilder builder)
    {
        return builder
            .WithImage("localstack/localstack:3.0.2")
            .WithEnvironment("SERVICES", "s3")
            .WithEnvironment("DEBUG", "1")
            .WithPortBinding(8055, 8080);
    }
}

[CollectionDefinition(nameof(AwsBlobTestFixture))]
public sealed class AwsBlobTestFixtureCollection : ICollectionFixture<AwsBlobTestFixture>;
