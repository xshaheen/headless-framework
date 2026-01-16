// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Azurite;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition]
public sealed class AzureBlobStorageFixture(IMessageSink messageSink)
    : ContainerFixture<AzuriteBuilder, AzuriteContainer>(messageSink),
        ICollectionFixture<AzureBlobStorageFixture>
{
    protected override AzuriteBuilder Configure()
    {
        return base.Configure().WithImage("mcr.microsoft.com/azure-storage/azurite:latest");
    }
}
