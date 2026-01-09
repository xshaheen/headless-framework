// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Tests.TestSetup;

[CollectionDefinition]
public sealed class SshBlobTestFixture : ICollectionFixture<SshBlobTestFixture>, IAsyncLifetime
{
    private readonly IContainer _sftpContainer = new ContainerBuilder("atmoz/sftp:latest")
        .WithPortBinding(22, true)
        .WithCommand("framework:password:::storage")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server listening on"))
        .Build();

    public string GetConnectionString() =>
        $"sftp://framework:password@{_sftpContainer.Hostname}:{_sftpContainer.GetMappedPublicPort(22)}";

    /// <summary>This runs before all the test run and Called just after the constructor</summary>
    public async ValueTask InitializeAsync()
    {
        await _sftpContainer.StartAsync();
    }

    /// <summary>This runs after all the test run and Called before Dispose()</summary>
    public async ValueTask DisposeAsync()
    {
        await _sftpContainer.StopAsync();
        await _sftpContainer.DisposeAsync();
    }
}
