// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Testcontainers.LocalStack;

namespace Tests;

[CollectionDefinition]
public sealed class AwsBlobStorageFixture : HeadlessLocalStackFixture, ICollectionFixture<AwsBlobStorageFixture>
{
    protected override LocalStackBuilder Configure()
    {
        return base.Configure().WithEnvironment("SERVICES", "s3").WithEnvironment("DEBUG", "1");
    }
}
