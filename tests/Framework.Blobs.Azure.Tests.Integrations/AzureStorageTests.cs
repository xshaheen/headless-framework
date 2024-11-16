// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Framework.Blobs;
using Framework.Blobs.Azure;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class AzureStorageTests(ITestOutputHelper output) : BlobStorageTestsBase(output), IAsyncLifetime
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
        var mimeTypeProvider = new MimeTypeProvider();
        var clock = new Clock(TimeProvider.System);

        var options = new AzureStorageSettings { AccountName = "", AccountKey = "" };
        var optionsAccessor = new OptionsSnapshotWrapper<AzureStorageSettings>(options);

        return new AzureBlobStorage(mimeTypeProvider, clock, optionsAccessor);
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

    [Fact]
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
}
