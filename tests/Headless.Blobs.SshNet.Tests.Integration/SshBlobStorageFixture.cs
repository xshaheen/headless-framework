// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Headless.Blobs;
using Headless.Blobs.SshNet;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

[CollectionDefinition(DisableParallelization = true)]
public sealed class SshBlobStorageFixture : ICollectionFixture<SshBlobStorageFixture>, IAsyncLifetime
{
    private readonly IContainer _sftpContainer = new ContainerBuilder("atmoz/sftp:latest")
        .WithPortBinding(22, true)
        .WithCommand("framework:password:::storage")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server listening on"))
        .Build();

    public SftpClientPool Pool { get; private set; } = null!;

    public CrossOsNamingNormalizer CrossOsNamingNormalizer { get; } = new();

    public SshBlobStorageOptions Options { get; private set; } = null!;

    public OptionsMonitorWrapper<SshBlobStorageOptions> OptionsMonitor { get; private set; } = null!;

    public string GetConnectionString() =>
        $"sftp://framework:password@{_sftpContainer.Hostname}:{_sftpContainer.GetMappedPublicPort(22)}";

    /// <summary>This runs before all the test run and Called just after the constructor</summary>
    public async ValueTask InitializeAsync()
    {
        await _sftpContainer.StartAsync();

        Options = new SshBlobStorageOptions
        {
            ConnectionString = GetConnectionString(),
            MaxPoolSize = 20, // Support concurrent test with 10 parallel operations
            MaxConcurrentOperations = 10,
        };
        OptionsMonitor = new OptionsMonitorWrapper<SshBlobStorageOptions>(Options);
        var optionsWrapper = new OptionsWrapper<SshBlobStorageOptions>(Options);
        Pool = new SftpClientPool(optionsWrapper, NullLogger<SftpClientPool>.Instance);
    }

    /// <summary>This runs after all the test run and Called before Dispose()</summary>
    public async ValueTask DisposeAsync()
    {
        Pool.Dispose();
        await _sftpContainer.StopAsync();
        await _sftpContainer.DisposeAsync();
    }
}
