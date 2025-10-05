// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Azurite;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests.TestSetup;

[UsedImplicitly]
[CollectionDefinition]
public sealed class TusAzureFixture(IMessageSink messageSink)
    : ContainerFixture<AzuriteBuilder, AzuriteContainer>(messageSink),
        ICollectionFixture<TusAzureFixture>
{
    protected override AzuriteBuilder Configure(AzuriteBuilder builder)
    {
        return builder.WithImage("mcr.microsoft.com/azure-storage/azurite:latest");
    }
}
