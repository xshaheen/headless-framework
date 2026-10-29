// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Core;
using Azure.Storage;
using Azure.Storage.Blobs;
using Headless.Blobs;
using Headless.Blobs.Azure;
using Headless.Blobs.Internal;
using Headless.IO;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class AzureBlobStoragePresignedTests : TestBase
{
    private static AzureBlobStorage _CreateStorageWithoutSigningCredentials()
    {
        // An anonymous client (no account key / user delegation key) cannot generate a SAS, so the presigned
        // path falls through to GetUserDelegationKeyAsync. The endpoint points at a local port nothing listens
        // on, with retries disabled: the key request fails at once instead of a DNS lookup and retry backoff
        // against a real account host, keeping the unit test off the network and deterministic.
        var options = new BlobClientOptions
        {
            Retry =
            {
                MaxRetries = 0,
                Mode = RetryMode.Fixed,
                Delay = TimeSpan.Zero,
                NetworkTimeout = TimeSpan.FromSeconds(5),
            },
        };
        var blobServiceClient = new BlobServiceClient(new Uri("http://127.0.0.1:9/"), options);

        return new AzureBlobStorage(
            blobServiceClient,
            new MimeTypeProvider(),
            TimeProvider.System,
            new OptionsWrapper<AzureStorageOptions>(new AzureStorageOptions()),
            new AzureBlobNamingNormalizer(),
            NullLogger<AzureBlobStorage>.Instance
        );
    }

    private static AzureBlobStorage _CreateStorageWithAccountKey(TimeProvider timeProvider)
    {
        // An account-key client signs the SAS locally, so the URL can be inspected without a server.
        var blobServiceClient = new BlobServiceClient(
            new Uri("https://account.blob.core.windows.net/"),
            new StorageSharedKeyCredential("account", Convert.ToBase64String(new byte[32]))
        );

        return new AzureBlobStorage(
            blobServiceClient,
            new MimeTypeProvider(),
            timeProvider,
            new OptionsWrapper<AzureStorageOptions>(new AzureStorageOptions()),
            new AzureBlobNamingNormalizer(),
            NullLogger<AzureBlobStorage>.Instance
        );
    }

    [Fact]
    public async Task download_presigned_signs_the_response_header_overrides_into_the_sas()
    {
        // given
        var sut = _CreateStorageWithAccountKey(new FakeTimeProvider(DateTimeOffset.UtcNow));
        const string fileName = "تقرير مارس.pdf";

        // when
        var url = await sut.GetPresignedDownloadUrlAsync(
            new BlobLocation("mycontainer", "report.bin"),
            TimeSpan.FromMinutes(5),
            new PresignedDownloadOptions { FileName = fileName, ContentType = "application/pdf" },
            AbortToken
        );

        // then
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        query["rsct"].Should().Be("application/pdf");
        var disposition = query["rscd"];
        disposition.Should().Be(BlobStorageHelpers.BuildAttachmentContentDisposition(fileName));
        System.Net.Http.Headers.ContentDispositionHeaderValue.Parse(disposition!).FileNameStar.Should().Be(fileName);
    }

    [Fact]
    public async Task download_presigned_url_is_unchanged_when_the_options_are_empty()
    {
        // given
        var sut = _CreateStorageWithAccountKey(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var location = new BlobLocation("mycontainer", "report.pdf");

        // when
        var withoutOptions = await sut.GetPresignedDownloadUrlAsync(
            location,
            TimeSpan.FromMinutes(5),
            cancellationToken: AbortToken
        );
        var withEmptyOptions = await sut.GetPresignedDownloadUrlAsync(
            location,
            TimeSpan.FromMinutes(5),
            new PresignedDownloadOptions(),
            AbortToken
        );

        // then
        withEmptyOptions.Should().Be(withoutOptions);
        var keys = System.Web.HttpUtility.ParseQueryString(withoutOptions.Query).AllKeys;
        keys.Should().NotContain(["rscd", "rsct"]);
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
    public async Task upload_presigned_ignores_constraints_a_sas_cannot_enforce()
    {
        // The constraints are ignored, so the call goes on to signing, which this unsigned client cannot do.
        var sut = _CreateStorageWithoutSigningCredentials();

        var act = async () =>
            await sut.GetPresignedUploadUrlAsync(
                new BlobLocation("mycontainer", "file.png"),
                TimeSpan.FromMinutes(5),
                new PresignedUploadConstraints { ContentType = "image/png", MaxLength = 1024 },
                AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Unable to generate a presigned URL*");
    }

    [Fact]
    public void reports_no_enforced_upload_constraints()
    {
        _CreateStorageWithoutSigningCredentials()
            .SupportedUploadConstraints.Should()
            .Be(PresignedUploadConstraintKinds.None);
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
