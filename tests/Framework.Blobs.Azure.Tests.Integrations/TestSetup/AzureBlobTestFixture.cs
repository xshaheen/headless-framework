// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Azurite;
using Testcontainers.Xunit;

namespace Tests.TestSetup;

[UsedImplicitly]
public sealed class AzureBlobTestFixture(IMessageSink messageSink)
    : ContainerFixture<AzuriteBuilder, AzuriteContainer>(messageSink)
{
    protected override AzuriteBuilder Configure(AzuriteBuilder builder)
    {
        return builder.WithImage("mcr.microsoft.com/azure-storage/azurite:latest");
    }
}

[CollectionDefinition(nameof(AzureBlobTestFixture))]
public sealed class AzureBlobTestFixtureCollection : ICollectionFixture<AzureBlobTestFixture>;
