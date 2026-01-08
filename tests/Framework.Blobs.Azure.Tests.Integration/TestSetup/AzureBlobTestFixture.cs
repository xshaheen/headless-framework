// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Azurite;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests.TestSetup;

[UsedImplicitly]
[CollectionDefinition]
public sealed class AzureBlobTestFixture(IMessageSink messageSink)
    : ContainerFixture<AzuriteBuilder, AzuriteContainer>(messageSink),
        ICollectionFixture<AzureBlobTestFixture>
{
    protected override AzuriteBuilder Configure()
    {
        return base.Configure().WithImage("mcr.microsoft.com/azure-storage/azurite:latest");
    }
}
