// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Tests.TestSetup;

namespace Tests.Concurrency;

/// <summary>
/// Two appends to one upload that both believe they hold the file lock (the lock was lost without the holder
/// noticing). Both read the same state; the request gate holds them at a state write and releases them one at a time,
/// so the second write runs on a stale view and must be rejected without changing what the first one stored.
/// </summary>
public abstract class S3StaleWriterTestsBase(IS3TestBackend backend) : TestBase
{
    private const int _MiB = 1024 * 1024;

    private readonly S3TestClient _client = backend.CreateClient();

    private readonly TusS3StoreOptions _options = new()
    {
        Bucket = TusS3StoreFactory.Bucket,
        Prefix = $"stale-{Guid.NewGuid():N}/",
        PartSize = 5 * _MiB,
    };

    private TusS3Store _store = null!;
    private TusS3RawObjects _raw = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _options.Provider = _client.Provider;
        await new TusS3BucketProvisioner(_client.S3, _options, LoggerFactory).ProvisionAsync(AbortToken);
        // No HTTP context accessor: each append reads the state itself, as two requests on two hosts would.
        _store = new TusS3Store(_client.S3, _options, loggerFactory: LoggerFactory);
        _raw = new TusS3RawObjects(_client, _options);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _client.Gate.Disarm();
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_let_one_of_two_part_reservations_win_and_fail_the_other_before_any_part()
    {
        // given - two appends of one part and 1 MiB each, both holding the empty upload's state
        var first = Faker.Random.Bytes(6 * _MiB);
        var second = Faker.Random.Bytes(6 * _MiB);
        var fileId = await _store.CreateFileAsync(20 * _MiB, metadata: null, AbortToken);
        _client.Gate.Reset();
        _client.Gate.Arm(S3Operation.PutObject, "/info");

        var firstAppend = _AppendAsync(fileId, first);
        var secondAppend = _AppendAsync(fileId, second);
        await _client.Gate.WaitForArrivalAsync(AbortToken);
        await _client.Gate.WaitForArrivalAsync(AbortToken);

        // when - one reservation lands; the winner then reaches its commit while the other reservation still waits
        _client.Gate.ReleaseOne();
        await _client.Gate.WaitForArrivalAsync(AbortToken);
        _client.Gate.Disarm();
        _client.Gate.ReleaseOne();
        _client.Gate.ReleaseOne();

        var outcomes = await Task.WhenAll(_Settle(firstAppend), _Settle(secondAppend));

        // then - exactly one append stored its bytes; the other failed its reservation with a stale write, which
        // tusdotnet answers with a retryable 500
        outcomes.Count(o => o.Appended is not null).Should().Be(1);
        var loser = outcomes.Single(o => o.Error is not null).Error;
        loser.Should().BeOfType<AmazonS3Exception>().Which.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);

        // and only the winner uploaded a part; the loser left the empty multipart upload it created to the sweep
        _client.Gate.Count(S3Operation.UploadPart).Should().Be(1);
        _client.Gate.Count(S3Operation.AbortMultipartUpload).Should().Be(0);

        var winner = outcomes[0].Appended is not null ? first : second;
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(winner.Length);
        (await _raw.ReadDurableBytesAsync(fileId, AbortToken)).Should().Equal(winner);
    }

    public virtual async Task should_keep_the_multipart_upload_when_a_landed_reservation_is_answered_with_412()
    {
        // given - the reservation of the upload's first part lands, but is answered with a 412, as an SDK retry of a
        // write whose response was lost would be
        var content = Faker.Random.Bytes(6 * _MiB);
        var fileId = await _store.CreateFileAsync(20 * _MiB, metadata: null, AbortToken);
        var infoKey = _raw.Keys.Info(fileId);
        var rejected = 0;
        _client.Gate.Reset();
        _client.Gate.LandThenRejectWhen(request =>
            string.Equals(S3Operation.Of(request), S3Operation.PutObject, StringComparison.Ordinal)
            && (request.RequestUri?.AbsolutePath ?? "").EndsWith(infoKey, StringComparison.Ordinal)
            && Interlocked.Increment(ref rejected) == 1
        );

        // when
        var (_, error) = await _Settle(_AppendAsync(fileId, content));
        _client.Gate.LandThenRejectWhen(null);

        // then - the append fails as a stale write, and the multipart upload the landed state records survives
        TusS3StateStore.IsStaleWrite(error.Should().BeAssignableTo<AmazonS3Exception>().Subject).Should().BeTrue();
        _client.Gate.Count(S3Operation.AbortMultipartUpload).Should().Be(0);
        (await _raw.ReadStateAsync(fileId, AbortToken))!.MultipartUploadId.Should().NotBeNull();

        // when - the client re-sends the PATCH from the offset HEAD reports
        var offset = await _store.GetUploadOffsetAsync(fileId, AbortToken);
        var appended = await _AppendAsync(fileId, content[(int)offset..]);

        // then - the bytes land in that multipart upload
        offset.Should().Be(0);
        appended.Should().Be(content.Length);
        (await _raw.ReadDurableBytesAsync(fileId, AbortToken)).Should().Equal(content);
    }

    public virtual async Task should_reject_the_stale_commit_of_two_tail_only_appends()
    {
        // given - two tail-only appends that both read the empty upload
        var first = Faker.Random.Bytes(_MiB);
        var second = Faker.Random.Bytes(2 * _MiB);
        var fileId = await _store.CreateFileAsync(10 * _MiB, metadata: null, AbortToken);
        _client.Gate.Arm(S3Operation.PutObject, "/info");

        var firstAppend = _AppendAsync(fileId, first);
        var secondAppend = _AppendAsync(fileId, second);
        await _client.Gate.WaitForArrivalAsync(AbortToken);
        await _client.Gate.WaitForArrivalAsync(AbortToken);

        // when - one commit lands, then the other
        _client.Gate.ReleaseOne();
        var winnerTask = await Task.WhenAny(firstAppend, secondAppend);
        var loserTask = winnerTask == firstAppend ? secondAppend : firstAppend;
        await winnerTask;
        _client.Gate.Disarm();
        _client.Gate.ReleaseOne();

        // then - the loser's commit is a stale write
        var act = async () => await loserTask;
        (await act.Should().ThrowAsync<AmazonS3Exception>())
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.PreconditionFailed);

        // and the winner's segment is the one recorded, intact and readable
        var winner = winnerTask == firstAppend ? first : second;
        var state = await _raw.ReadStateAsync(fileId, AbortToken);
        state!.Offset.Should().Be(winner.Length);
        var segment = state.Segments.Should().ContainSingle().Subject;
        (await _raw.ReadSegmentAsync(segment, AbortToken)).Should().Equal(winner);
    }

    private async Task<long> _AppendAsync(string fileId, byte[] body)
    {
        // Leave the test's thread first, so both appends reach the gate before either is awaited.
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
#pragma warning disable CA1031 // The test inspects which append failed and how.
        catch (Exception e)
#pragma warning restore CA1031
        {
            return (null, e);
        }
    }
}

[Collection<TusS3MinioFixture>]
public sealed class MinioS3StaleWriterTests(TusS3MinioFixture fixture) : S3StaleWriterTestsBase(fixture)
{
    [Fact]
    public override Task should_let_one_of_two_part_reservations_win_and_fail_the_other_before_any_part() =>
        base.should_let_one_of_two_part_reservations_win_and_fail_the_other_before_any_part();

    [Fact]
    public override Task should_keep_the_multipart_upload_when_a_landed_reservation_is_answered_with_412() =>
        base.should_keep_the_multipart_upload_when_a_landed_reservation_is_answered_with_412();

    [Fact]
    public override Task should_reject_the_stale_commit_of_two_tail_only_appends() =>
        base.should_reject_the_stale_commit_of_two_tail_only_appends();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackS3StaleWriterTests(TusS3LocalStackFixture fixture) : S3StaleWriterTestsBase(fixture)
{
    [Fact]
    public override Task should_let_one_of_two_part_reservations_win_and_fail_the_other_before_any_part() =>
        base.should_let_one_of_two_part_reservations_win_and_fail_the_other_before_any_part();

    [Fact]
    public override Task should_keep_the_multipart_upload_when_a_landed_reservation_is_answered_with_412() =>
        base.should_keep_the_multipart_upload_when_a_landed_reservation_is_answered_with_412();

    [Fact]
    public override Task should_reject_the_stale_commit_of_two_tail_only_appends() =>
        base.should_reject_the_stale_commit_of_two_tail_only_appends();
}
