// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3.Model;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Tests.TestSetup;
using tusdotnet.Interfaces;

namespace Tests.Flows;

/// <summary>
/// The expiry sweep removes abandoned uploads with all their objects and nothing else: not a live upload, not one a
/// request changed after the sweep read it, not a completed upload, and not another writer's multipart upload. Each
/// test owns a key prefix, because a sweep covers its whole prefix.
/// </summary>
public abstract class ExpirationSweepTestsBase(IS3TestBackend backend) : TestBase
{
    private const int _MiB = 1024 * 1024;

    private readonly S3TestClient _client = backend.CreateClient();

    private readonly TusS3StoreOptions _options = new()
    {
        Bucket = TusS3StoreFactory.Bucket,
        Prefix = $"sweep-{Guid.NewGuid():N}/",
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
        _client.Gate.Disarm();
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_remove_only_the_expired_upload_and_never_read_a_completed_one()
    {
        // given - an expired upload holding a part and a tail segment, a live upload, and a completed upload
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = _CreateStore(clock);
        var expired = await _CreateIncompleteAsync(store, 20 * _MiB, Faker.Random.Bytes(6 * _MiB));
        await store.SetExpirationAsync(expired, clock.GetUtcNow().AddMinutes(10), AbortToken);
        var expiredUploadId = (await _raw.ReadStateAsync(expired, AbortToken))!.MultipartUploadId;
        expiredUploadId.Should().NotBeNull();

        var live = await _CreateIncompleteAsync(store, 1000, Faker.Random.Bytes(300));
        await store.SetExpirationAsync(live, clock.GetUtcNow().AddHours(2), AbortToken);

        var completedContent = Faker.Random.Bytes(400);
        var completed = await _CreateIncompleteAsync(store, completedContent.Length, completedContent);
        clock.Advance(TimeSpan.FromMinutes(30));
        _client.Gate.Reset();

        // when
        var removed = await store.RemoveExpiredFilesAsync(AbortToken);

        // then - the sweep read only the two in-progress states, never the completed upload's record or object
        var reads = _client.Gate.Count(S3Operation.GetObject);
        var heads = _client.Gate.Count(S3Operation.HeadObject);
        reads.Should().Be(2);
        heads.Should().Be(0);

        // and only the expired upload is gone, with every object it owned
        removed.Should().Be(1);
        (await _ListKeysAsync(_raw.Keys.WorkingSet(expired))).Should().BeEmpty();
        (await _ListMultipartUploadIdsAsync(_raw.Keys.FinishedObject(expired))).Should().BeEmpty();
        (await _raw.ReadFinishedObjectAsync(expired, AbortToken)).Should().BeNull();

        (await store.GetUploadOffsetAsync(live, AbortToken)).Should().Be(300);
        (await _raw.ReadFinishedObjectAsync(completed, AbortToken)).Should().Equal(completedContent);
    }

    public virtual async Task should_keep_an_upload_a_patch_commits_to_after_the_sweep_read_it()
    {
        // given - an upload with a learned sliding window, and a sweep whose clock is past its expiry
        using var host = await TusS3TestHost.StartAsync(
            _client,
            _options,
            LoggerFactory,
            timeProvider: null,
            AbortToken
        );
        var location = await host.CreateUploadAsync(20 * _MiB, metadata: null, AbortToken);
        var fileId = TusS3TestHost.FileIdOf(location);
        await _PatchAsync(host, location, Faker.Random.Bytes(1000), offset: 0);
        var before = (await _raw.ReadStateAsync(fileId, AbortToken))!;

        var sweeper = _CreateStore(new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(2)));
        _client.Gate.Arm(S3Operation.PutObject, $"{fileId}/info");

        // when - the sweep reads the upload as expired and is held before its claim; a PATCH commits meanwhile
        var sweep = sweeper.RemoveExpiredFilesAsync(AbortToken);
        await _client.Gate.WaitForArrivalAsync(AbortToken);
        _client.Gate.Disarm();
        await _PatchAsync(host, location, Faker.Random.Bytes(1000), offset: 1000);
        _client.Gate.ReleaseOne();
        var removed = await sweep;

        // then - the claim was conditional on the state the sweep read, so it failed and the upload survives intact
        removed.Should().Be(0);
        var after = (await _raw.ReadStateAsync(fileId, AbortToken))!;
        after.Status.Should().Be(TusS3UploadStatus.Active);
        after.Offset.Should().Be(2000);
        after.ExpiresAt.Should().BeAfter(before.ExpiresAt!.Value);
        (await _ListKeysAsync(_raw.Keys.Segments(fileId))).Should().HaveCount(after.Segments.Count);

        using var head = await host.HeadAsync(location, AbortToken);
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("2000");
    }

    public virtual async Task should_remove_a_gone_upload_whatever_its_expiry()
    {
        // given - an upload the provider lost the parts of, with an expiry a day away
        var store = _CreateStore(timeProvider: null);
        var gone = await _CreateIncompleteAsync(store, 1000, Faker.Random.Bytes(300));
        await store.SetExpirationAsync(gone, DateTimeOffset.UtcNow.AddDays(1), AbortToken);
        await _raw.RewriteStateAsync(gone, state => state.Status = TusS3UploadStatus.Gone, AbortToken);

        // when
        var listed = (await store.GetExpiredFilesAsync(AbortToken)).ToList();
        var removed = await store.RemoveExpiredFilesAsync(AbortToken);

        // then
        listed.Should().Equal(gone);
        removed.Should().Be(1);
        (await _ListKeysAsync(_raw.Keys.WorkingSet(gone))).Should().BeEmpty();
    }

    public virtual async Task should_leave_a_completing_upload_until_its_pre_completion_expiry_passes()
    {
        // given - two completing uploads, one whose expiry from before completing is still ahead and one whose
        // expiry has passed and whose multipart upload already produced the finished object
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = _CreateStore(clock);
        var fresh = await _CreateIncompleteAsync(store, 1000, Faker.Random.Bytes(300));
        await _MarkCompletingAsync(fresh, clock.GetUtcNow().AddHours(1));

        var stale = await _CreateIncompleteAsync(store, 1000, Faker.Random.Bytes(300));
        await _MarkCompletingAsync(stale, clock.GetUtcNow().AddMinutes(-1));
        await _PutAsync(_raw.Keys.FinishedObject(stale), Faker.Random.Bytes(1000));

        // when
        var removed = await store.RemoveExpiredFilesAsync(AbortToken);

        // then - the stale one is destroyed as abandoned, never finished
        removed.Should().Be(1);
        (await _ListKeysAsync(_raw.Keys.WorkingSet(stale))).Should().BeEmpty();
        (await _raw.ReadFinishedObjectAsync(stale, AbortToken)).Should().BeNull();
        (await _raw.ReadCompletedRecordAsync(stale, AbortToken)).Should().BeNull();

        // and the fresh one is untouched, left for the client's re-sent final PATCH
        var freshState = (await _raw.ReadStateAsync(fresh, AbortToken))!;
        freshState.Status.Should().Be(TusS3UploadStatus.Completing);
        (await _ListKeysAsync(_raw.Keys.Segments(fresh))).Should().HaveCount(freshState.Segments.Count);
    }

    public virtual async Task should_abort_only_an_old_multipart_upload_its_upload_does_not_record()
    {
        // given - a tracked upload with a recorded multipart upload, an unrecorded one on the same key, and another
        // writer's multipart upload under the same prefix
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = _CreateStore(clock);
        var tracked = await _CreateIncompleteAsync(store, 20 * _MiB, Faker.Random.Bytes(6 * _MiB));
        await store.SetExpirationAsync(tracked, clock.GetUtcNow().AddDays(1), AbortToken);
        var recorded = (await _raw.ReadStateAsync(tracked, AbortToken))!.MultipartUploadId!;
        var leaked = await _InitiateMultipartUploadAsync(_raw.Keys.FinishedObject(tracked));
        var foreignKey = _options.NormalizedPrefix + "another-writer-object";
        var foreign = await _InitiateMultipartUploadAsync(foreignKey);

        // when - a sweep while the leak is younger than the grace period
        clock.Advance(TimeSpan.FromMinutes(30));
        (await store.RemoveExpiredFilesAsync(AbortToken)).Should().Be(0);

        // then - it is left alone
        (await _ListMultipartUploadIdsAsync(_raw.Keys.FinishedObject(tracked)))
            .Should()
            .BeEquivalentTo([recorded, leaked]);

        // when - a sweep once it is older
        clock.Advance(TimeSpan.FromHours(1));
        (await store.RemoveExpiredFilesAsync(AbortToken)).Should().Be(0);

        // then - only the unrecorded one on the tracked key is aborted
        (await _ListMultipartUploadIdsAsync(_raw.Keys.FinishedObject(tracked)))
            .Should()
            .Equal(recorded);
        (await _ListMultipartUploadIdsAsync(foreignKey)).Should().Equal(foreign);
    }

    public virtual async Task should_remove_a_working_set_folder_whose_state_object_is_gone()
    {
        // given - tail segments left behind with no state object
        var store = _CreateStore(timeProvider: null);
        var fileId = Guid.NewGuid().ToString("N");
        await _PutAsync(_raw.Keys.Segments(fileId) + "leftover", Faker.Random.Bytes(100));

        // when
        var removed = await store.RemoveExpiredFilesAsync(AbortToken);

        // then - the leftovers are deleted, and no upload is counted
        removed.Should().Be(0);
        (await _ListKeysAsync(_raw.Keys.WorkingSet(fileId))).Should().BeEmpty();
    }

    public virtual async Task should_never_move_an_absolute_expiry()
    {
        // given
        using var host = await TusS3TestHost.StartAsync(
            _client,
            _options,
            LoggerFactory,
            timeProvider: null,
            AbortToken,
            absoluteExpiration: true
        );
        var location = await host.CreateUploadAsync(20 * _MiB, metadata: null, AbortToken);
        var fileId = TusS3TestHost.FileIdOf(location);
        var created = (await _raw.ReadStateAsync(fileId, AbortToken))!.ExpiresAt;
        created.Should().NotBeNull();

        // when
        await _PatchAsync(host, location, Faker.Random.Bytes(1000), offset: 0);
        await _PatchAsync(host, location, Faker.Random.Bytes(1000), offset: 1000);

        // then
        var state = (await _raw.ReadStateAsync(fileId, AbortToken))!;
        state.ExpiresAt.Should().Be(created);
        state.SlidingExpirationWindow.Should().BeNull();
    }

    public virtual async Task should_fold_the_sliding_refresh_into_the_append_after_the_first_patch()
    {
        // given - a first PATCH, after which tusdotnet's refresh shows the 30-minute window
        using var host = await TusS3TestHost.StartAsync(
            _client,
            _options,
            LoggerFactory,
            timeProvider: null,
            AbortToken
        );
        var location = await host.CreateUploadAsync(20 * _MiB, metadata: null, AbortToken);
        var fileId = TusS3TestHost.FileIdOf(location);
        await _PatchAsync(host, location, Faker.Random.Bytes(1000), offset: 0);
        var first = (await _raw.ReadStateAsync(fileId, AbortToken))!;
        first.SlidingExpirationWindow.Should().BeCloseTo(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(5));
        _client.Gate.Reset();

        // when
        using var patch = await host.PatchAsync(location, Faker.Random.Bytes(1000), 1000, AbortToken);

        // then - read the state, write the segment, commit: the refresh made no write of its own
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _client.Gate.Count(S3Operation.PutObject).Should().Be(2);
        _client.Gate.TotalRequests.Should().Be(3);

        // and the commit moved the expiry to what tusdotnet reported
        var second = (await _raw.ReadStateAsync(fileId, AbortToken))!;
        second.ExpiresAt.Should().BeAfter(first.ExpiresAt!.Value);
        var reported = DateTimeOffset.Parse(
            patch.Headers.GetValues("Upload-Expires").Single(),
            CultureInfo.InvariantCulture
        );
        second.ExpiresAt.Should().BeCloseTo(reported, TimeSpan.FromSeconds(2));
    }

    public virtual async Task should_remove_an_expired_upload_through_the_registered_cleanup_job()
    {
        // given - an expired upload, and a host wired as an application wires the store and the cleanup job
        var fileId = await _CreateIncompleteAsync(_CreateStore(timeProvider: null), 1000, Faker.Random.Bytes(300));
        await _CreateStore(timeProvider: null)
            .SetExpirationAsync(fileId, DateTimeOffset.UtcNow.AddMinutes(-10), AbortToken);
        var firstPass = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var builder = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(_client.S3);
            // Registered first, so the store's own forwarding (a TryAdd) keeps this one, which reports the pass.
            services.AddSingleton<ITusExpirationStore>(provider => new PassReportingExpirationStore(
                provider.GetRequiredService<TusS3Store>(),
                firstPass
            ));
            services.AddTusS3Store(options =>
            {
                options.Bucket = _options.Bucket;
                options.Prefix = _options.Prefix;
                options.Provider = _options.Provider;
            });
            services.AddTusExpiredUploadsCleanup(options => options.Interval = TimeSpan.FromHours(1));
        });

        using var host = builder.Build();

        // when - the job's first pass runs at startup
        await host.StartAsync(AbortToken);
        var removed = await firstPass.Task.WaitAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        removed.Should().Be(1);
        (await _ListKeysAsync(_raw.Keys.WorkingSet(fileId))).Should().BeEmpty();
    }

    private TusS3Store _CreateStore(TimeProvider? timeProvider)
    {
        return new TusS3Store(_client.S3, _options, loggerFactory: LoggerFactory, timeProvider: timeProvider);
    }

    private static async Task<string> _CreateIncompleteAsync(TusS3Store store, long length, byte[] content)
    {
        var fileId = await store.CreateFileAsync(length, metadata: null, AbortToken);

        await using var stream = new MemoryStream(content);
        await store.AppendDataAsync(fileId, stream, AbortToken);

        return fileId;
    }

    private static async Task _PatchAsync(TusS3TestHost host, string location, byte[] body, long offset)
    {
        using var response = await host.PatchAsync(location, body, offset, AbortToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private Task _MarkCompletingAsync(string fileId, DateTimeOffset expiresBeforeCompleting)
    {
        return _raw.RewriteStateAsync(
            fileId,
            state =>
            {
                state.Status = TusS3UploadStatus.Completing;
                state.PreFinalOffset = state.Offset;
                state.ExpiresAtBeforeCompleting = expiresBeforeCompleting;
                state.ExpiresAt = expiresBeforeCompleting;
            },
            AbortToken
        );
    }

    private async Task _PutAsync(string key, byte[] content)
    {
        await using var body = new MemoryStream(content);
        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = body,
            AutoCloseStream = false,
        };
        TusS3RequestPresets.Apply(request, _client.Provider);

        await _client.S3.PutObjectAsync(request, AbortToken);
    }

    private async Task<string> _InitiateMultipartUploadAsync(string key)
    {
        var response = await _client.S3.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest { BucketName = _options.Bucket, Key = key },
            AbortToken
        );

        return response.UploadId;
    }

    private async Task<List<string>> _ListKeysAsync(string prefix)
    {
        var response = await _client.S3.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = _options.Bucket, Prefix = prefix },
            AbortToken
        );

        return [.. (response.S3Objects ?? []).Select(o => o.Key)];
    }

    private async Task<List<string>> _ListMultipartUploadIdsAsync(string key)
    {
        var response = await _client.S3.ListMultipartUploadsAsync(
            new ListMultipartUploadsRequest { BucketName = _options.Bucket, Prefix = key },
            AbortToken
        );

        return
        [
            .. (response.MultipartUploads ?? [])
                .Where(u => string.Equals(u.Key, key, StringComparison.Ordinal))
                .Select(u => u.UploadId),
        ];
    }

    // Forwards to the store and reports the first pass's result, so the test waits on the job instead of a delay.
    private sealed class PassReportingExpirationStore(ITusExpirationStore inner, TaskCompletionSource<int> firstPass)
        : ITusExpirationStore
    {
        public Task SetExpirationAsync(string fileId, DateTimeOffset expires, CancellationToken cancellationToken) =>
            inner.SetExpirationAsync(fileId, expires, cancellationToken);

        public Task<DateTimeOffset?> GetExpirationAsync(string fileId, CancellationToken cancellationToken) =>
            inner.GetExpirationAsync(fileId, cancellationToken);

        public Task<IEnumerable<string>> GetExpiredFilesAsync(CancellationToken cancellationToken) =>
            inner.GetExpiredFilesAsync(cancellationToken);

        public async Task<int> RemoveExpiredFilesAsync(CancellationToken cancellationToken)
        {
            var removed = await inner.RemoveExpiredFilesAsync(cancellationToken);
            firstPass.TrySetResult(removed);

            return removed;
        }
    }
}

[Collection<TusS3MinioFixture>]
public sealed class MinioExpirationSweepTests(TusS3MinioFixture fixture) : ExpirationSweepTestsBase(fixture)
{
    [Fact]
    public override Task should_remove_only_the_expired_upload_and_never_read_a_completed_one() =>
        base.should_remove_only_the_expired_upload_and_never_read_a_completed_one();

    [Fact]
    public override Task should_keep_an_upload_a_patch_commits_to_after_the_sweep_read_it() =>
        base.should_keep_an_upload_a_patch_commits_to_after_the_sweep_read_it();

    [Fact]
    public override Task should_remove_a_gone_upload_whatever_its_expiry() =>
        base.should_remove_a_gone_upload_whatever_its_expiry();

    [Fact]
    public override Task should_leave_a_completing_upload_until_its_pre_completion_expiry_passes() =>
        base.should_leave_a_completing_upload_until_its_pre_completion_expiry_passes();

    // No leak scenario here: MinIO lists multipart uploads only under an exact object key, so the sweep's listing
    // over the whole prefix finds none and leaves leaks to the bucket's own abort rule. Termination lists the exact
    // key and still aborts them.

    [Fact]
    public override Task should_remove_a_working_set_folder_whose_state_object_is_gone() =>
        base.should_remove_a_working_set_folder_whose_state_object_is_gone();

    [Fact]
    public override Task should_never_move_an_absolute_expiry() => base.should_never_move_an_absolute_expiry();

    [Fact]
    public override Task should_fold_the_sliding_refresh_into_the_append_after_the_first_patch() =>
        base.should_fold_the_sliding_refresh_into_the_append_after_the_first_patch();

    [Fact]
    public override Task should_remove_an_expired_upload_through_the_registered_cleanup_job() =>
        base.should_remove_an_expired_upload_through_the_registered_cleanup_job();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackExpirationSweepTests(TusS3LocalStackFixture fixture) : ExpirationSweepTestsBase(fixture)
{
    [Fact]
    public override Task should_remove_only_the_expired_upload_and_never_read_a_completed_one() =>
        base.should_remove_only_the_expired_upload_and_never_read_a_completed_one();

    [Fact]
    public override Task should_keep_an_upload_a_patch_commits_to_after_the_sweep_read_it() =>
        base.should_keep_an_upload_a_patch_commits_to_after_the_sweep_read_it();

    [Fact]
    public override Task should_remove_a_gone_upload_whatever_its_expiry() =>
        base.should_remove_a_gone_upload_whatever_its_expiry();

    [Fact]
    public override Task should_leave_a_completing_upload_until_its_pre_completion_expiry_passes() =>
        base.should_leave_a_completing_upload_until_its_pre_completion_expiry_passes();

    [Fact]
    public override Task should_abort_only_an_old_multipart_upload_its_upload_does_not_record() =>
        base.should_abort_only_an_old_multipart_upload_its_upload_does_not_record();

    [Fact]
    public override Task should_remove_a_working_set_folder_whose_state_object_is_gone() =>
        base.should_remove_a_working_set_folder_whose_state_object_is_gone();

    [Fact]
    public override Task should_never_move_an_absolute_expiry() => base.should_never_move_an_absolute_expiry();

    [Fact]
    public override Task should_fold_the_sliding_refresh_into_the_append_after_the_first_patch() =>
        base.should_fold_the_sliding_refresh_into_the_append_after_the_first_patch();

    [Fact]
    public override Task should_remove_an_expired_upload_through_the_registered_cleanup_job() =>
        base.should_remove_an_expired_upload_through_the_registered_cleanup_job();
}
