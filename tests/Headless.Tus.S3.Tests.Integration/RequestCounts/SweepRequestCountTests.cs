// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Tests.TestSetup;

namespace Tests.RequestCounts;

/// <summary>
/// Pins what an expiry sweep costs: one listing per 1,000 in-progress uploads and one read per in-progress upload,
/// however many completed uploads share the prefix.
/// </summary>
public abstract class SweepRequestCountTestsBase(IS3TestBackend backend) : TestBase
{
    private const int _InProgressUploads = 3;

    private readonly S3TestClient _client = backend.CreateClient();

    private readonly TusS3StoreOptions _options = new()
    {
        Bucket = TusS3StoreFactory.Bucket,
        Prefix = $"sweep-counts-{Guid.NewGuid():N}/",
    };

    private TusS3Store _store = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _options.Provider = _client.Provider;
        await new TusS3BucketProvisioner(_client.S3, _options, LoggerFactory).ProvisionAsync(AbortToken);
        _store = new TusS3Store(_client.S3, _options, loggerFactory: LoggerFactory);

        // In-progress uploads that have not expired, and completed uploads the sweep must never touch.
        for (var i = 0; i < _InProgressUploads; i++)
        {
            var fileId = await _UploadAsync(length: 1000, Faker.Random.Bytes(100));
            await _store.SetExpirationAsync(fileId, DateTimeOffset.UtcNow.AddHours(1), AbortToken);
        }

        for (var i = 0; i < 4; i++)
        {
            await _UploadAsync(length: 200, Faker.Random.Bytes(200));
        }

        _client.Gate.Reset();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_list_once_and_read_each_in_progress_upload_once()
    {
        // when
        var expired = await _store.GetExpiredFilesAsync(AbortToken);

        // then
        expired.Should().BeEmpty();
        _client.Gate.Count(S3Operation.ListObjectsV2).Should().Be(1);
        _client.Gate.Count(S3Operation.GetObject).Should().Be(_InProgressUploads);
        _client.Gate.TotalRequests.Should().Be(1 + _InProgressUploads, "counts: {0}", _Describe());
    }

    public virtual async Task should_add_only_the_multipart_listing_when_removing()
    {
        // when
        var removed = await _store.RemoveExpiredFilesAsync(AbortToken);

        // then - the listing and the reads, plus one page of multipart uploads checked for leaks
        removed.Should().Be(0);
        _client.Gate.Count(S3Operation.ListObjectsV2).Should().Be(1);
        _client.Gate.Count(S3Operation.GetObject).Should().Be(_InProgressUploads);
        _client.Gate.Count(S3Operation.ListMultipartUploads).Should().Be(1);
        _client.Gate.TotalRequests.Should().Be(2 + _InProgressUploads, "counts: {0}", _Describe());
    }

    private async Task<string> _UploadAsync(long length, byte[] content)
    {
        var fileId = await _store.CreateFileAsync(length, metadata: null, AbortToken);

        await using var stream = new MemoryStream(content);
        await _store.AppendDataAsync(fileId, stream, AbortToken);

        return fileId;
    }

    private string _Describe()
    {
        return string.Join(", ", _client.Gate.Counts.Select(pair => $"{pair.Key}={pair.Value}"));
    }
}

[Collection<TusS3MinioFixture>]
public sealed class MinioSweepRequestCountTests(TusS3MinioFixture fixture) : SweepRequestCountTestsBase(fixture)
{
    [Fact]
    public override Task should_list_once_and_read_each_in_progress_upload_once() =>
        base.should_list_once_and_read_each_in_progress_upload_once();

    [Fact]
    public override Task should_add_only_the_multipart_listing_when_removing() =>
        base.should_add_only_the_multipart_listing_when_removing();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackSweepRequestCountTests(TusS3LocalStackFixture fixture)
    : SweepRequestCountTestsBase(fixture)
{
    [Fact]
    public override Task should_list_once_and_read_each_in_progress_upload_once() =>
        base.should_list_once_and_read_each_in_progress_upload_once();

    [Fact]
    public override Task should_add_only_the_multipart_listing_when_removing() =>
        base.should_add_only_the_multipart_listing_when_removing();
}
