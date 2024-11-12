using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Framework.Blobs;
using Framework.Blobs.SshNet;
using Framework.Blobs.Tests.Harness;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection("SshBlobStorageIntegrationTests")]
public sealed class SshBlobStorageTests : FileStorageTestsBase, IAsyncLifetime
{
    private readonly IContainer _sftpContainer;
    private readonly IContainer _awaiterContainer;

    public SshBlobStorageTests(ITestOutputHelper output)
        : base(output)
    {
        _sftpContainer = new ContainerBuilder()
            .WithImage("atmoz/sftp:latest")
            .WithPortBinding(2222, 22)
            .WithCommand("framework:password:::storage")
            .Build();

        _awaiterContainer = new ContainerBuilder()
            .DependsOn(_sftpContainer)
            .WithImage("andrewlock/wait-for-dependencies:latest")
            .WithCommand("sftp:22")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _sftpContainer.StartAsync();
        await _awaiterContainer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _sftpContainer.StopAsync();
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
}
