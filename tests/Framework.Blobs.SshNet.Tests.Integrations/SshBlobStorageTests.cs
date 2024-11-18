// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Framework.Blobs;
using Framework.Blobs.SshNet;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SshBlobStorageTests(ITestOutputHelper output) : BlobStorageTestsBase(output), IAsyncLifetime
{
    private readonly IContainer _sftpContainer = new ContainerBuilder()
        .WithImage("atmoz/sftp:latest")
        .WithPortBinding(2222, 22)
        .WithCommand("framework:password:::storage")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(22))
        .Build();

    /// <summary>This runs before all the test run and Called just after the constructor</summary>
    public Task InitializeAsync()
    {
        return _sftpContainer.StartAsync();
    }

    /// <summary>This runs after all the test run and Called before Dispose()</summary>
    public Task DisposeAsync()
    {
        return _sftpContainer.StopAsync();
    }

    protected override IBlobStorage GetStorage()
    {
        var options = new SshBlobStorageOptions { ConnectionString = "sftp://framework:password@localhost:2222" };
        var optionsWrapper = new OptionsWrapper<SshBlobStorageOptions>(options);

        return new SshBlobStorage(optionsWrapper);
    }

    [Fact]
    public void CanCreateSshNetFileStorageWithoutConnectionStringPassword()
    {
        // given
        var options = new SshBlobStorageOptions { ConnectionString = "sftp://framework@localhost:2222" };
        var optionsWrapper = new OptionsWrapper<SshBlobStorageOptions>(options);

        // when
        using var storage = new SshBlobStorage(optionsWrapper);
    }

    [Fact]
    public void CanCreateSshNetFileStorageWithoutProxyPassword()
    {
        // given
        var options = new SshBlobStorageOptions
        {
            ConnectionString = "sftp://username@host",
            Proxy = "proxy://username@host",
        };
        var optionsWrapper = new OptionsWrapper<SshBlobStorageOptions>(options);

        // when
        using var storage = new SshBlobStorage(optionsWrapper);
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

    [Fact]
    public override Task CanGetFileListForSingleFileAsync()
    {
        return base.CanGetFileListForSingleFileAsync();
    }

    [Fact]
    public override Task CanGetPagedFileListForSingleFolderAsync()
    {
        return base.CanGetPagedFileListForSingleFolderAsync();
    }

    [Fact]
    public override Task CanGetFileInfoAsync()
    {
        return base.CanGetFileInfoAsync();
    }

    [Fact]
    public override Task CanGetNonExistentFileInfoAsync()
    {
        return base.CanGetNonExistentFileInfoAsync();
    }

    [Fact]
    public override Task CanManageFilesAsync()
    {
        return base.CanManageFilesAsync();
    }

    [Fact]
    public override Task CanRenameFilesAsync()
    {
        return base.CanRenameFilesAsync();
    }

    [Fact(Skip = "Doesn't work well with SFTP")]
    public override Task CanConcurrentlyManageFilesAsync()
    {
        return base.CanConcurrentlyManageFilesAsync();
    }

    [Fact]
    public override Task CanDeleteEntireFolderAsync()
    {
        return base.CanDeleteEntireFolderAsync();
    }

    [Fact]
    public override Task CanDeleteEntireFolderWithWildcardAsync()
    {
        return base.CanDeleteEntireFolderWithWildcardAsync();
    }

    [Fact]
    public override Task CanDeleteFolderWithMultiFolderWildcardsAsync()
    {
        return base.CanDeleteFolderWithMultiFolderWildcardsAsync();
    }

    [Fact]
    public override Task CanDeleteSpecificFilesAsync()
    {
        return base.CanDeleteSpecificFilesAsync();
    }

    [Fact]
    public override Task CanDeleteNestedFolderAsync()
    {
        return base.CanDeleteNestedFolderAsync();
    }

    [Fact]
    public override Task CanDeleteSpecificFilesInNestedFolderAsync()
    {
        return base.CanDeleteSpecificFilesInNestedFolderAsync();
    }

    [Fact]
    public override Task CanRoundTripSeekableStreamAsync()
    {
        return base.CanRoundTripSeekableStreamAsync();
    }

    [Fact]
    public override Task WillRespectStreamOffsetAsync()
    {
        return base.WillRespectStreamOffsetAsync();
    }

    [Fact]
    public override Task CanSaveOverExistingStoredContent()
    {
        return base.CanSaveOverExistingStoredContent();
    }

    [Fact]
    public async Task WillNotReturnDirectoryInGetPagedFileListAsync()
    {
        var storage = GetStorage();

        await ResetAsync(storage);

        using (storage)
        {
            var container = Container;

            var result = await storage.GetPagedListAsync(container);
            result.HasMore.Should().BeFalse();
            result.Blobs.Should().BeEmpty();
            (await result.NextPageAsync()).Should().BeFalse();
            result.HasMore.Should().BeFalse();
            result.Blobs.Should().BeEmpty();

            const string directory = "EmptyDirectory";
            var client = storage is SshBlobStorage sshStorage ? await sshStorage.GetClientAsync() : null;
            client.Should().NotBeNull();

            await client!.CreateDirectoryAsync($"storage/{directory}");

            result = await storage.GetPagedListAsync(container);
            result.HasMore.Should().BeFalse();
            result.Blobs.Should().BeEmpty();
            (await result.NextPageAsync()).Should().BeFalse();
            result.HasMore.Should().BeFalse();
            result.Blobs.Should().BeEmpty();

            // Ensure the directory will not be returned via get file info
            var info = await storage.GetBlobInfoAsync(container, directory);
            info.Should().BeNull();

            // Ensure delete files can remove all files including fake folders
            await storage.DeleteAllAsync(container, "*");

            // Assert folder was removed by Delete Files
            // count.Should().Be(1);
            (await client.ExistsAsync($"storage/{directory}"))
                .Should()
                .BeFalse();

            (await storage.GetBlobInfoAsync(container, directory)).Should().BeNull();
        }
    }
}
