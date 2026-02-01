// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.LocalStack;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests;

/// <summary>
/// Collection fixture providing a LocalStack container for AWS SQS/SNS integration tests.
/// Uses Testcontainers.LocalStack for container lifecycle management.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class LocalStackTestFixture(IMessageSink messageSink)
    : ContainerFixture<LocalStackBuilder, LocalStackContainer>(messageSink),
        ICollectionFixture<LocalStackTestFixture>
{
    /// <summary>Gets the LocalStack connection string (service URL).</summary>
    public string ConnectionString => Container.GetConnectionString();

    protected override LocalStackBuilder Configure()
    {
        return base.Configure().WithImage("localstack/localstack:latest");
    }
}
