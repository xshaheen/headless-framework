// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Tests.TestSetup;

public sealed class AwsBlobTestFixture : IAsyncLifetime
{
    private readonly IContainer _localstackContainer = new ContainerBuilder()
        .WithImage("localstack/localstack:3.0.2")
        .WithPortBinding("4563-4599", "4563-4599")
        .WithPortBinding(8055, 8080)
        .WithEnvironment("SERVICES", "s3")
        .WithEnvironment("DEBUG", "1")
        .WithBindMount("localstackdata", "/var/lib/localstack")
        .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(8080))
        .Build();

    /// <summary>This runs before all the test run and Called just after the constructor</summary>
    public Task InitializeAsync()
    {
        return _localstackContainer.StartAsync();
    }

    /// <summary>This runs after all the test run and Called before Dispose()</summary>
    public Task DisposeAsync()
    {
        return _localstackContainer.StopAsync();
    }
}

[CollectionDefinition(nameof(AwsBlobTestFixture))]
public sealed class AwsBlobTestFixtureCollection : ICollectionFixture<AwsBlobTestFixture>;
