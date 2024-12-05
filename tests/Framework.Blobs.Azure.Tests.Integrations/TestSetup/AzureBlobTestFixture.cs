// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Tests.TestSetup;

public sealed class AzureBlobTestFixture : IAsyncLifetime
{
    private readonly IContainer _azuriteContainer = new ContainerBuilder()
        .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
        .WithPortBinding(10000, 10000)
        .WithPortBinding(10001, 10001)
        .WithPortBinding(10002, 10002)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(10000))
        .Build();

    /// <summary>This runs before all the test run and Called just after the constructor</summary>
    public Task InitializeAsync()
    {
        return _azuriteContainer.StartAsync();
    }

    /// <summary>This runs after all the test run and Called before Dispose()</summary>
    public Task DisposeAsync()
    {
        return _azuriteContainer.StopAsync();
    }
}

[CollectionDefinition(nameof(AzureBlobTestFixture))]
public sealed class AzureBlobTestFixtureCollection : ICollectionFixture<AzureBlobTestFixture>;
