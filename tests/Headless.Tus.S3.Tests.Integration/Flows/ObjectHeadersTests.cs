// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Tests.TestSetup;

namespace Tests.Flows;

/// <summary>
/// The headers a finished object carries, on each way the store writes one: in one request for an upload below one
/// part or of zero bytes, and through a multipart upload otherwise.
/// </summary>
public abstract class ObjectHeadersTestsBase(IS3TestBackend backend) : TestBase
{
    private const int _MiB = 1024 * 1024;

    private readonly S3TestClient _client = backend.CreateClient();

    private readonly TusS3StoreOptions _options = new()
    {
        Bucket = TusS3StoreFactory.Bucket,
        Prefix = $"headers-{Guid.NewGuid():N}/",
        PartSize = 5 * _MiB,
    };

    private TusS3RawObjects _raw = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _options.Provider = _client.Provider;
        await new TusS3BucketProvisioner(_client.S3, _options, LoggerFactory).ProvisionAsync(AbortToken);
        _raw = new TusS3RawObjects(_client, _options);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_store_octet_stream_on_every_write_path_whatever_the_filetype()
    {
        // given - a filetype the client chose, which the default provider must not trust
        var store = _CreateStore(headersProvider: null);
        var metadata = $"filetype {"text/html".ToBase64()}";

        // when - one upload below one part, one across parts, one of zero bytes
        var small = await _UploadAsync(store, metadata, 1_000);
        var large = await _UploadAsync(store, metadata, 6 * _MiB);
        var empty = await _UploadAsync(store, metadata, 0);

        // then
        foreach (var fileId in new[] { small, large, empty })
        {
            (await _raw.GetFinishedObjectHeadersAsync(fileId, AbortToken))
                .ContentType.Should()
                .Be("application/octet-stream");
        }
    }

    public virtual async Task should_apply_the_headers_a_custom_provider_returns()
    {
        // given - a provider that derives a download name from the metadata
        var store = _CreateStore(new CustomHeadersProvider());
        var metadata = $"filename {"clip.mp4".ToBase64()}";

        // when
        var small = await _UploadAsync(store, metadata, 1_000);
        var large = await _UploadAsync(store, metadata, 6 * _MiB);

        // then
        foreach (var fileId in new[] { small, large })
        {
            var headers = await _raw.GetFinishedObjectHeadersAsync(fileId, AbortToken);
            headers.ContentType.Should().Be("video/mp4");
            headers.CacheControl.Should().Be("public, max-age=60");
            headers.ContentDisposition.Should().Be("attachment; filename=\"clip.mp4\"");
        }
    }

    private TusS3Store _CreateStore(ITusS3ObjectHeadersProvider? headersProvider)
    {
        return new TusS3Store(_client.S3, _options, headersProvider, loggerFactory: LoggerFactory);
    }

    private async Task<string> _UploadAsync(TusS3Store store, string? metadata, int length)
    {
        var fileId = await store.CreateFileAsync(length, metadata, AbortToken);

        if (length > 0)
        {
            await using var body = new MemoryStream(Faker.Random.Bytes(length), writable: false);
            (await store.AppendDataAsync(fileId, body, AbortToken)).Should().Be(length);
        }

        return fileId;
    }

    private sealed class CustomHeadersProvider : ITusS3ObjectHeadersProvider
    {
        public ValueTask<TusS3ObjectHeaders> GetObjectHeadersAsync(Dictionary<string, string> metadata)
        {
            return ValueTask.FromResult(
                new TusS3ObjectHeaders
                {
                    ContentType = "video/mp4",
                    CacheControl = "public, max-age=60",
                    ContentDisposition = $"attachment; filename=\"{metadata["filename"]}\"",
                }
            );
        }
    }
}

[Collection<TusS3MinioFixture>]
public sealed class MinioObjectHeadersTests(TusS3MinioFixture fixture) : ObjectHeadersTestsBase(fixture)
{
    [Fact]
    public override Task should_store_octet_stream_on_every_write_path_whatever_the_filetype() =>
        base.should_store_octet_stream_on_every_write_path_whatever_the_filetype();

    [Fact]
    public override Task should_apply_the_headers_a_custom_provider_returns() =>
        base.should_apply_the_headers_a_custom_provider_returns();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackObjectHeadersTests(TusS3LocalStackFixture fixture) : ObjectHeadersTestsBase(fixture)
{
    [Fact]
    public override Task should_store_octet_stream_on_every_write_path_whatever_the_filetype() =>
        base.should_store_octet_stream_on_every_write_path_whatever_the_filetype();

    [Fact]
    public override Task should_apply_the_headers_a_custom_provider_returns() =>
        base.should_apply_the_headers_a_custom_provider_returns();
}
