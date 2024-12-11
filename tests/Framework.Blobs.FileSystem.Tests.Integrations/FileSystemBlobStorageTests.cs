// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Blobs;
using Framework.Blobs.FileSystem;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class FileSystemBlobStorageTests(ITestOutputHelper output) : BlobStorageTestsBase(output)
{
    private readonly string _baseDirectoryPath = Directory.CreateTempSubdirectory().FullName;

    protected override IBlobStorage GetStorage()
    {
        var options = new FileSystemBlobStorageOptions { BaseDirectoryPath = _baseDirectoryPath };

        var optionsWrapper = new OptionsWrapper<FileSystemBlobStorageOptions>(options);

        return new FileSystemBlobStorage(optionsWrapper);
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

    [Fact(Skip = "Directory.EnumerateFiles does not support nested folder wildcards")]
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
        var container = Container;
        var containerName = ContainerName;
        using var storage = (FileSystemBlobStorage)GetStorage();
        await ResetAsync(storage);

        var result = await storage.GetPagedListAsync(container);
        result.HasMore.Should().BeFalse();
        result.Blobs.Should().BeEmpty();
        (await result.NextPageAsync()).Should().BeFalse();
        result.HasMore.Should().BeFalse();
        result.Blobs.Should().BeEmpty();

        const string directory = "EmptyDirectory/";
        Directory.CreateDirectory(Path.Combine(_baseDirectoryPath, containerName, directory));

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
        Directory.Exists(Path.Combine(_baseDirectoryPath, containerName, directory)).Should().BeFalse();

        info = await storage.GetBlobInfoAsync(container, directory);
        info.Should().BeNull();
    }

    [Fact]
    public override Task CanCallDeleteWithEmptyContainerAsync()
    {
        return base.CanCallDeleteWithEmptyContainerAsync();
    }

    [Fact]
    public override Task CanCallBulkDeleteWithEmptyContainerAsync()
    {
        return base.CanCallBulkDeleteWithEmptyContainerAsync();
    }

    [Fact]
    public override Task CanCallDeleteAllAsyncWithEmptyContainerAsync()
    {
        return base.CanCallDeleteAllAsyncWithEmptyContainerAsync();
    }

    [Fact]
    public override Task CanCallCopyWithEmptyContainerAsync()
    {
        return base.CanCallCopyWithEmptyContainerAsync();
    }

    [Fact]
    public override Task CanCallRenameWithEmptyContainerAsync()
    {
        return base.CanCallRenameWithEmptyContainerAsync();
    }

    [Fact]
    public override Task CanCallExistsWithEmptyContainerAsync()
    {
        return base.CanCallExistsWithEmptyContainerAsync();
    }

    [Fact]
    public override Task CanCallDownloadWithEmptyContainerAsync()
    {
        return base.CanCallDownloadWithEmptyContainerAsync();
    }

    [Fact]
    public override Task CanCallGetBlobInfoWithEmptyContainerAsync()
    {
        return base.CanCallGetBlobInfoWithEmptyContainerAsync();
    }

    [Fact]
    public override Task CanCallGetPagedListWithEmptyContainerAsync()
    {
        return base.CanCallGetPagedListWithEmptyContainerAsync();
    }
}
