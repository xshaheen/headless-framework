// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;

namespace Tests;

/// <summary>
/// Collection fixture providing a LocalStack container for AWS SQS/SNS integration tests.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class LocalStackTestFixture : HeadlessLocalStackFixture, ICollectionFixture<LocalStackTestFixture>
{
    /// <summary>Gets the LocalStack connection string (service URL).</summary>
    public string ConnectionString => Container.GetConnectionString();
}
