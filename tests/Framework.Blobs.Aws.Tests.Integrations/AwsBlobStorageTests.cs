// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Framework.Blobs;
using Framework.Blobs.Aws;
using Framework.BuildingBlocks.Abstractions;
using Microsoft.Extensions.Options;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(AwsBlobTestFixture))]
public sealed class AwsBlobStorageTests(ITestOutputHelper output) : BlobStorageTestsBase(output)
{
    protected override IBlobStorage GetStorage()
    {
        var s3Config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.USEast1,
            ServiceURL = "http://localhost:4566",
            ForcePathStyle = true,
        };

        var awsCredentials = new BasicAWSCredentials("xxx", "xxx");
#pragma warning disable CA2000
        var amazonS3Client = new AmazonS3Client(awsCredentials, s3Config);
#pragma warning restore CA2000

        var mimeTypeProvider = new MimeTypeProvider();
        var clock = new Clock(TimeProvider.System);

        var options = new AwsBlobStorageOptions();
        var optionsWrapper = new OptionsWrapper<AwsBlobStorageOptions>(options);

        return new AwsBlobStorage(amazonS3Client, mimeTypeProvider, clock, optionsWrapper);
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
}
