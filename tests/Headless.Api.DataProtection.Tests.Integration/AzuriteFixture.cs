// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Azurite;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition]
public sealed class AzuriteFixture(IMessageSink messageSink)
    : ContainerFixture<AzuriteBuilder, AzuriteContainer>(messageSink),
        ICollectionFixture<AzuriteFixture>
{
    protected override AzuriteBuilder Configure()
    {
        return base.Configure().WithImage("mcr.microsoft.com/azure-storage/azurite:latest");
    }
}
