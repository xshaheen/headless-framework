// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Tests.TestSetup;

[CollectionDefinition]
public sealed class SshBlobTestFixture : ICollectionFixture<SshBlobTestFixture>, IAsyncLifetime
{
    private readonly IContainer _sftpContainer = new ContainerBuilder()
        .WithImage("atmoz/sftp:latest")
        .WithPortBinding(2222, 22)
        .WithCommand("framework:password:::storage")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(22))
        .Build();

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
