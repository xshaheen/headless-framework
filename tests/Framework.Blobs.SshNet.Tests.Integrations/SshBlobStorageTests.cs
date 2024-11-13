// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Framework.Blobs;
using Framework.Blobs.SshNet;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection("SshBlobStorageIntegrationTests")]
public sealed class SshBlobStorageTests(ITestOutputHelper output) : FileStorageTestsBase(output), IAsyncLifetime
{
    private readonly IContainer _sftpContainer = new ContainerBuilder()
        .WithImage("atmoz/sftp:latest")
        .WithPortBinding(2222, 22)
        .WithCommand("framework:password:::storage")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(22))
        .Build();

    public Task InitializeAsync()
    {
        return _sftpContainer.StartAsync();
    }

    public Task DisposeAsync()
    {
        return _sftpContainer.StopAsync();
    }

    protected override IBlobStorage GetStorage()
    {
        var options = new SshBlobStorageSettings { ConnectionString = "sftp://framework:password@localhost:2222" };
        var optionsWrapper = new OptionsWrapper<SshBlobStorageSettings>(options);

        return new SshBlobStorage(optionsWrapper);
    }

    [Fact]
    public override Task CanGetEmptyFileListOnMissingDirectoryAsync()
    {
        return base.CanGetEmptyFileListOnMissingDirectoryAsync();
    }

    [Fact]
    public override Task CanGetFileListForSingleFolderAsync()
    {
        return base.CanGetFileListForSingleFolderAsync();
    }
}
