// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Abstractions;
using Headless.Blobs;
using Headless.Blobs.Azure;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class AzureBlobStoragePresignedTests : TestBase
{
    private static AzureBlobStorage _CreateStorageWithoutSigningCredentials()
    {
        // An anonymous client (no account key / user delegation key) cannot generate a SAS.
        var blobServiceClient = new BlobServiceClient(new Uri("https://account.blob.core.windows.net"));

        return new AzureBlobStorage(
            blobServiceClient,
            new MimeTypeProvider(),
            TimeProvider.System,
            new OptionsWrapper<AzureStorageOptions>(new AzureStorageOptions()),
            new AzureBlobNamingNormalizer(),
            NullLogger<AzureBlobStorage>.Instance
        );
    }

    [Fact]
    public void implements_presigned_url_capability()
    {
        _CreateStorageWithoutSigningCredentials().Should().BeAssignableTo<IPresignedUrlBlobStorage>();
    }

    [Fact]
    public async Task download_presigned_throws_when_client_cannot_sign()
    {
        var sut = _CreateStorageWithoutSigningCredentials();

        var act = async () =>
            await sut.GetPresignedDownloadUrlAsync(
                new BlobLocation("mycontainer", "file.txt"),
                TimeSpan.FromMinutes(5)
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Unable to generate a presigned URL*");
    }

    [Fact]
    public async Task upload_presigned_throws_when_client_cannot_sign()
    {
        var sut = _CreateStorageWithoutSigningCredentials();

        var act = async () =>
            await sut.GetPresignedUploadUrlAsync(new BlobLocation("mycontainer", "file.txt"), TimeSpan.FromMinutes(5));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Unable to generate a presigned URL*");
    }

    [Fact]
    public async Task presigned_throws_on_non_positive_expiry()
    {
        var sut = _CreateStorageWithoutSigningCredentials();

        var act = async () =>
            await sut.GetPresignedDownloadUrlAsync(new BlobLocation("mycontainer", "file.txt"), TimeSpan.Zero);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
