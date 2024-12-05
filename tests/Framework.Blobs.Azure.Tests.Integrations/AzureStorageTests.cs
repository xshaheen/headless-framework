// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Blobs;
using Framework.Blobs.Azure;
using Framework.BuildingBlocks.Abstractions;
using Microsoft.Extensions.Options;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(AzureBlobTestFixture))]
public sealed class AzureStorageTests(ITestOutputHelper output) : BlobStorageTestsBase(output)
{
    protected override IBlobStorage GetStorage()
    {
        var mimeTypeProvider = new MimeTypeProvider();
        var clock = new Clock(TimeProvider.System);

        var options = new AzureStorageOptions { AccountName = "", AccountKey = "" };
        var optionsAccessor = new OptionsSnapshotWrapper<AzureStorageOptions>(options);

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
