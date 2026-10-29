// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Microsoft.Extensions.Time.Testing;
using Tests.TestSetup;

namespace Tests.Concurrency;

/// <summary>
/// A final PATCH still finishing an upload while the expiry sweep finds that upload expired. The sweep holds no tus
/// lock, so the request gate holds the completion at one storage call while the sweep runs. Whichever side wins, the
/// upload must end either completed with its finished object readable, or gone with no completed record: never a
/// record that tells clients the upload finished while its object is deleted.
/// </summary>
public abstract class CompletionSweepRaceTestsBase(IS3TestBackend backend) : TestBase
{
    private const int _MiB = 1024 * 1024;

    private readonly S3TestClient _client = backend.CreateClient();
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    private readonly TusS3StoreOptions _options = new()
    {
        Bucket = TusS3StoreFactory.Bucket,
        Prefix = $"race-{Guid.NewGuid():N}/",
        PartSize = 5 * _MiB,
    };

    private TusS3Store _store = null!;
    private TusS3RawObjects _raw = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _options.Provider = _client.Provider;
        await new TusS3BucketProvisioner(_client.S3, _options, LoggerFactory).ProvisionAsync(AbortToken);
        // No HTTP context accessor: the sweep and the append each read the state themselves, as on two hosts.
        _store = new TusS3Store(_client.S3, _options, loggerFactory: LoggerFactory, timeProvider: _clock);
        _raw = new TusS3RawObjects(_client, _options);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _client.Gate.Disarm();
        _client.Gate.FailWhen(null);
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_skip_an_upload_whose_completion_is_writing_its_record()
    {
        // given - a multipart upload whose final PATCH has completed the multipart upload and is held at the record
        var content = Faker.Random.Bytes(7 * _MiB);
        var fileId = await _CreateExpiringUploadAsync(content, 3 * _MiB);
        _client.Gate.Arm(S3Operation.PutObject, _raw.Keys.CompletedRecord(fileId));
        var completion = _Settle(_AppendAsync(fileId, content[(3 * _MiB)..]));
        await _client.Gate.WaitForArrivalAsync(AbortToken);

        // when - the sweep runs once the expiry the upload had before completing has passed
        _clock.Advance(TimeSpan.FromMinutes(30));
        var removed = await _store.RemoveExpiredFilesAsync(AbortToken);
        _client.Gate.Disarm();
        _client.Gate.ReleaseOne();
        var (_, error) = await completion;

        // then - the sweep left the upload to its completion, which finished it
        removed.Should().Be(0);
        error.Should().BeNull();
        await _AssertNoRecordWithoutObjectAsync(fileId);
        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().Equal(content);
        (await _raw.ReadStateAsync(fileId, AbortToken)).Should().BeNull();
    }

    public virtual async Task should_record_nothing_when_the_sweep_claimed_the_upload_first()
    {
        // given - an upload below one part whose final PATCH is held at the write of its finished object
        var content = Faker.Random.Bytes(1000);
        var fileId = await _CreateExpiringUploadAsync(content, 300);
        _client.Gate.Arm(S3Operation.PutObject, _raw.Keys.FinishedObject(fileId));
        var completion = _Settle(_AppendAsync(fileId, content[300..]));
        await _client.Gate.WaitForArrivalAsync(AbortToken);

        // when - the sweep claims and removes the expired completing upload, then the completion goes on
        _clock.Advance(TimeSpan.FromMinutes(30));
        var removed = await _store.RemoveExpiredFilesAsync(AbortToken);
        _client.Gate.Disarm();
        _client.Gate.ReleaseOne();
        var (_, error) = await completion;

        // then - the completion fails with the untranslated stale write, which tusdotnet answers with a 500, and
        // records nothing, so no client is told the upload finished
        removed.Should().Be(1);
        var staleWrite = error.Should().BeOfType<AmazonS3Exception>().Subject;
        TusS3StateStore.IsStaleWrite(staleWrite).Should().BeTrue();
        await _AssertNoRecordWithoutObjectAsync(fileId);
        (await _raw.ReadCompletedRecordAsync(fileId, AbortToken)).Should().BeNull();
        (await _store.FileExistAsync(fileId, AbortToken)).Should().BeFalse();
    }

    public virtual async Task should_finish_a_committing_upload_on_the_resent_final_patch_within_the_grace()
    {
        // given - a final PATCH that wrote the finished object, moved the state on, and then failed its record write
        var content = Faker.Random.Bytes(7 * _MiB);
        var fileId = await _CreateExpiringUploadAsync(content, 3 * _MiB);
        var recordKey = _raw.Keys.CompletedRecord(fileId);
        _client.Gate.FailWhen(request =>
            string.Equals(S3Operation.Of(request), S3Operation.PutObject, StringComparison.Ordinal)
            && (request.RequestUri?.AbsolutePath ?? "").EndsWith(recordKey, StringComparison.Ordinal)
        );

        var (_, recordError) = await _Settle(_AppendAsync(fileId, content[(3 * _MiB)..]));
        recordError.Should().BeAssignableTo<AmazonS3Exception>();
        _client.Gate.FailWhen(null);

        // then - it still reads as completing: the client is sent back to the start of its final PATCH
        (await _raw.ReadStateAsync(fileId, AbortToken))!
            .Status.Should()
            .Be(TusS3UploadStatus.Committing);
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(3 * _MiB);
        (await _store.GetExpirationAsync(fileId, AbortToken)).Should().BeNull();

        // and a sweep past its expiry, but inside the grace period, leaves it alone
        _clock.Advance(TimeSpan.FromMinutes(30));
        (await _store.RemoveExpiredFilesAsync(AbortToken)).Should().Be(0);
        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().Equal(content);

        // when - the client re-sends the final PATCH
        var appended = await _AppendAsync(fileId, content[(3 * _MiB)..]);

        // then - the upload completes with the original bytes
        appended.Should().Be(4 * _MiB);
        (await _raw.ReadCompletedRecordAsync(fileId, AbortToken))!.UploadLength.Should().Be(content.Length);
        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().Equal(content);
        (await _raw.ReadStateAsync(fileId, AbortToken)).Should().BeNull();
        (await _raw.ListSegmentKeysAsync(fileId, AbortToken)).Should().BeEmpty();
    }

    // An upload holding its first bytes, set to expire ten minutes from now on the test clock.
    public virtual async Task should_remove_a_committing_upload_once_past_its_expiry_and_the_grace()
    {
        // given - a final PATCH that wrote the finished object, moved the state on, and then failed its record write,
        // from a client that never re-sends it
        var content = Faker.Random.Bytes(7 * _MiB);
        var fileId = await _CreateExpiringUploadAsync(content, 3 * _MiB);
        var recordKey = _raw.Keys.CompletedRecord(fileId);
        _client.Gate.FailWhen(request =>
            string.Equals(S3Operation.Of(request), S3Operation.PutObject, StringComparison.Ordinal)
            && (request.RequestUri?.AbsolutePath ?? "").EndsWith(recordKey, StringComparison.Ordinal)
        );

        var (_, recordError) = await _Settle(_AppendAsync(fileId, content[(3 * _MiB)..]));
        recordError.Should().BeAssignableTo<AmazonS3Exception>();
        _client.Gate.FailWhen(null);

        // when - the sweep runs past the expiry but inside the grace period, then past both
        _clock.Advance(TimeSpan.FromMinutes(30));
        var removedWithinGrace = await _store.RemoveExpiredFilesAsync(AbortToken);
        _clock.Advance(TimeSpan.FromMinutes(31));
        var removedPastGrace = await _store.RemoveExpiredFilesAsync(AbortToken);

        // then - only the second pass removes the upload, with its finished object
        removedWithinGrace.Should().Be(0);
        removedPastGrace.Should().Be(1);
        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().BeNull();
        (await _raw.ReadCompletedRecordAsync(fileId, AbortToken)).Should().BeNull();
        (await _raw.ReadStateAsync(fileId, AbortToken)).Should().BeNull();
        (await _raw.ListSegmentKeysAsync(fileId, AbortToken)).Should().BeEmpty();
        (await _store.FileExistAsync(fileId, AbortToken)).Should().BeFalse();
    }

    private async Task<string> _CreateExpiringUploadAsync(byte[] content, int stored)
    {
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);
        (await _AppendAsync(fileId, content[..stored])).Should().Be(stored);
        await _store.SetExpirationAsync(fileId, _clock.GetUtcNow().AddMinutes(10), AbortToken);

        return fileId;
    }

    private async Task _AssertNoRecordWithoutObjectAsync(string fileId)
    {
        if (await _raw.ReadCompletedRecordAsync(fileId, AbortToken) is { } record)
        {
            var finished = await _raw.ReadFinishedObjectAsync(fileId, AbortToken);
            finished.Should().NotBeNull("a completed record must point at its finished object");
            finished!.LongLength.Should().Be(record.UploadLength);
        }
    }

    private async Task<long> _AppendAsync(string fileId, byte[] body)
    {
        // Leave the test's thread first, so the append reaches the gate before the test waits for it.
        await Task.Yield();
        await using var stream = new MemoryStream(body, writable: false);

        return await _store.AppendDataAsync(fileId, stream, AbortToken);
    }

    private static async Task<(long? Appended, Exception? Error)> _Settle(Task<long> append)
    {
        try
        {
            return (await append, null);
        }
#pragma warning disable CA1031 // The test inspects whether the completion failed and how.
        catch (Exception e)
#pragma warning restore CA1031
        {
            return (null, e);
        }
    }
}

[Collection<TusS3MinioFixture>]
public sealed class MinioCompletionSweepRaceTests(TusS3MinioFixture fixture) : CompletionSweepRaceTestsBase(fixture)
{
    [Fact]
    public override Task should_skip_an_upload_whose_completion_is_writing_its_record() =>
        base.should_skip_an_upload_whose_completion_is_writing_its_record();

    [Fact]
    public override Task should_record_nothing_when_the_sweep_claimed_the_upload_first() =>
        base.should_record_nothing_when_the_sweep_claimed_the_upload_first();

    [Fact]
    public override Task should_finish_a_committing_upload_on_the_resent_final_patch_within_the_grace() =>
        base.should_finish_a_committing_upload_on_the_resent_final_patch_within_the_grace();

    [Fact]
    public override Task should_remove_a_committing_upload_once_past_its_expiry_and_the_grace() =>
        base.should_remove_a_committing_upload_once_past_its_expiry_and_the_grace();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackCompletionSweepRaceTests(TusS3LocalStackFixture fixture)
    : CompletionSweepRaceTestsBase(fixture)
{
    [Fact]
    public override Task should_skip_an_upload_whose_completion_is_writing_its_record() =>
        base.should_skip_an_upload_whose_completion_is_writing_its_record();

    [Fact]
    public override Task should_record_nothing_when_the_sweep_claimed_the_upload_first() =>
        base.should_record_nothing_when_the_sweep_claimed_the_upload_first();

    [Fact]
    public override Task should_finish_a_committing_upload_on_the_resent_final_patch_within_the_grace() =>
        base.should_finish_a_committing_upload_on_the_resent_final_patch_within_the_grace();

    [Fact]
    public override Task should_remove_a_committing_upload_once_past_its_expiry_and_the_grace() =>
        base.should_remove_a_committing_upload_once_past_its_expiry_and_the_grace();
}
