// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Blobs;
using Headless.Blobs.Azure;
using Headless.IO;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class AzureBlobStorageDelegationKeyTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly BlobServiceClient _serviceClient = Substitute.For<BlobServiceClient>();
    private readonly List<DateTimeOffset> _requestedKeyExpiries = [];

    public AzureBlobStorageDelegationKeyTests()
    {
        // An anonymous container client cannot sign locally, so the presigned path needs a user-delegation key.
        _serviceClient
            .GetBlobContainerClient(Arg.Any<string>())
            .Returns(call => new BlobContainerClient(
                new Uri($"https://account.blob.core.windows.net/{call.Arg<string>()}")
            ));
        _serviceClient.AccountName.Returns("account");
        _serviceClient
            .GetUserDelegationKeyAsync(
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => _KeyResponseAsync(call.ArgAt<DateTimeOffset>(1)));
    }

    private async Task<Response<UserDelegationKey>> _KeyResponseAsync(DateTimeOffset expiresOn)
    {
        lock (_requestedKeyExpiries)
        {
            _requestedKeyExpiries.Add(expiresOn);
        }

        // Yield so concurrent callers overlap the in-flight request instead of running one after another.
        await Task.Yield();

        // Positional: the factory's overloads share parameter names, so named arguments are ambiguous.
        // (signedObjectId, signedTenantId, signedStartsOn, signedExpiresOn, signedService, signedVersion, value)
        var key = BlobsModelFactory.UserDelegationKey(
            Guid.Empty.ToString(),
            Guid.Empty.ToString(),
            _timeProvider.GetUtcNow(),
            expiresOn,
            "b",
            "2025-01-05",
            Convert.ToBase64String(new byte[32])
        );

        return Response.FromValue(key, Substitute.For<Response>());
    }

    private AzureBlobStorage _CreateStorage()
    {
        return new AzureBlobStorage(
            _serviceClient,
            new MimeTypeProvider(),
            _timeProvider,
            new OptionsWrapper<AzureStorageOptions>(new AzureStorageOptions()),
            new AzureBlobNamingNormalizer(),
            NullLogger<AzureBlobStorage>.Instance
        );
    }

    [Fact]
    public async Task should_reuse_the_delegation_key_when_it_outlives_the_requested_expiry()
    {
        // given
        var sut = _CreateStorage();

        // when
        var first = await sut.GetPresignedDownloadUrlAsync(
            new BlobLocation("mycontainer", "a.png"),
            TimeSpan.FromMinutes(5),
            cancellationToken: AbortToken
        );
        _timeProvider.Advance(TimeSpan.FromMinutes(30));
        var second = await sut.GetPresignedUploadUrlAsync(
            new BlobLocation("mycontainer", "b.png"),
            TimeSpan.FromMinutes(5),
            cancellationToken: AbortToken
        );

        // then
        _requestedKeyExpiries.Should().ContainSingle();
        _requestedKeyExpiries[0].Should().Be(new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero));
        first.Query.Should().Contain("skoid=");
        second.Query.Should().Contain("skoid=");
    }

    [Fact]
    public async Task should_request_a_new_key_when_the_requested_expiry_outlives_the_cached_key()
    {
        // given
        var sut = _CreateStorage();
        var location = new BlobLocation("mycontainer", "a.png");

        // when
        await sut.GetPresignedDownloadUrlAsync(location, TimeSpan.FromMinutes(5), cancellationToken: AbortToken);
        await sut.GetPresignedDownloadUrlAsync(location, TimeSpan.FromHours(3), cancellationToken: AbortToken);
        await sut.GetPresignedDownloadUrlAsync(location, TimeSpan.FromHours(2), cancellationToken: AbortToken);

        // then
        _requestedKeyExpiries
            .Should()
            .Equal(
                new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 1, 1, 3, 0, 0, TimeSpan.Zero)
            );
    }

    [Fact]
    public async Task should_request_a_new_key_when_the_cached_key_expired()
    {
        // given
        var sut = _CreateStorage();
        var location = new BlobLocation("mycontainer", "a.png");

        // when
        await sut.GetPresignedDownloadUrlAsync(location, TimeSpan.FromMinutes(5), cancellationToken: AbortToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(56));
        await sut.GetPresignedDownloadUrlAsync(location, TimeSpan.FromMinutes(5), cancellationToken: AbortToken);

        // then
        _requestedKeyExpiries.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_request_one_key_when_urls_are_minted_concurrently()
    {
        // given
        var sut = _CreateStorage();

        // when
        var urls = await Task.WhenAll(
            Enumerable
                .Range(0, 20)
                .Select(i =>
                    sut.GetPresignedDownloadUrlAsync(
                            new BlobLocation("mycontainer", $"item-{i}.png"),
                            TimeSpan.FromMinutes(5),
                            cancellationToken: AbortToken
                        )
                        .AsTask()
                )
        );

        // then
        urls.Should().HaveCount(20);
        _requestedKeyExpiries.Should().ContainSingle();
    }

    [Fact]
    public async Task should_request_the_key_again_when_the_previous_request_failed()
    {
        // given
        var sut = _CreateStorage();
        var location = new BlobLocation("mycontainer", "a.png");
        _serviceClient
            .GetUserDelegationKeyAsync(
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                _ => Task.FromException<Response<UserDelegationKey>>(new RequestFailedException(403, "denied")),
                call => _KeyResponseAsync(call.ArgAt<DateTimeOffset>(1))
            );

        // when
        var failed = async () =>
            await sut.GetPresignedDownloadUrlAsync(location, TimeSpan.FromMinutes(5), cancellationToken: AbortToken);
        await failed
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unable to generate a presigned URL*");
        var url = await sut.GetPresignedDownloadUrlAsync(
            location,
            TimeSpan.FromMinutes(5),
            cancellationToken: AbortToken
        );

        // then
        url.Query.Should().Contain("skoid=");
        _requestedKeyExpiries.Should().ContainSingle();
    }
}
