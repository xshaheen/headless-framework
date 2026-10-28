// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using tusdotnet.Models;

namespace Tests.S3;

/// <summary>
/// The arithmetic of one append: how body bytes and an existing tail become equal-size parts and a remainder, which
/// part numbers they get, and what the commit records. Parts are 16 bytes so every boundary is easy to see.
/// </summary>
public sealed class TusS3PartWriterTests : TestBase
{
    private const int _PartSize = 16;
    private const string _FileId = "upload-1";

    private readonly InMemoryS3 _s3 = new();
    private readonly CountingPool _pool = new();
    private readonly TusS3StoreOptions _options = new() { Bucket = "bucket", Prefix = "uploads/" };
    private readonly TusS3Keys _keys = new("uploads/");

    private TusS3StateStore _StateStore => new(_s3.Client, _options, _keys, new TusS3RequestMemo(null), Logger);

    [Fact]
    public async Task should_store_a_body_below_one_part_as_one_segment_without_reading_anything_back()
    {
        // given
        await _SeedAsync(new TusS3UploadState { PartSize = _PartSize, UploadLength = 100 });
        var body = Faker.Random.Bytes(10);

        // when
        var appended = await _AppendAsync(body);

        // then
        appended.Should().Be(10);
        var state = _ReadState();
        state.Offset.Should().Be(10);
        state.Parts.Should().BeEmpty();
        state.MultipartUploadId.Should().BeNull();
        state
            .Segments.Should()
            .ContainSingle()
            .Which.Should()
            .Match<TusS3CommittedSegment>(s => s.Offset == 0 && s.Length == 10);
        _s3.Requests.Should().Equal("PutObject", "PutObject");
        _DurableBytes(state).Should().Equal(body);
    }

    [Fact]
    public async Task should_upload_a_body_of_exactly_one_part_without_writing_a_segment()
    {
        // given
        await _SeedAsync(new TusS3UploadState { PartSize = _PartSize, UploadLength = 100 });
        var body = Faker.Random.Bytes(_PartSize);

        // when
        await _AppendAsync(body);

        // then - create the multipart upload, reserve numbers, upload the part, commit
        var state = _ReadState();
        _s3.Requests.Should().Equal("CreateMultipartUpload", "PutObject", "UploadPart", "PutObject");
        state.Parts.Should().ContainSingle().Which.Should().Be(new TusS3CommittedPart(1, "part-1", _PartSize));
        state.Segments.Should().BeEmpty();
        state.Offset.Should().Be(_PartSize);
        state.MultipartUploadId.Should().Be("upload-1");
        // the reserved window's unused numbers go back to the counter
        state.NextPartNumber.Should().Be(2);
        _DurableBytes(state).Should().Equal(body);
    }

    [Fact]
    public async Task should_put_the_tail_at_the_front_of_the_first_part_and_keep_the_remainder()
    {
        // given - 6 bytes already stored as a tail segment
        await _SeedAsync(new TusS3UploadState { PartSize = _PartSize, UploadLength = 100 });
        var first = Faker.Random.Bytes(6);
        await _AppendAsync(first);
        var oldSegment = _ReadState().Segments.Single();

        // when - 40 more bytes: 46 in all, two full parts and 14 left over
        var second = Faker.Random.Bytes(40);
        var appended = await _AppendAsync(second);

        // then
        appended.Should().Be(40);
        var state = _ReadState();
        state.Parts.Select(p => (p.Number, p.Size)).Should().Equal((1, 16L), (2, 16L));
        _s3.Parts[1].Should().Equal([.. first, .. second[..10]]);
        state
            .Segments.Should()
            .ContainSingle()
            .Which.Should()
            .Match<TusS3CommittedSegment>(s => s.Offset == 32 && s.Length == 14);
        state.Offset.Should().Be(46);
        state.NextPartNumber.Should().Be(3);
        _DurableBytes(state).Should().Equal([.. first, .. second]);
        // the consumed segment is deleted once the commit recorded its bytes in a part
        _s3.Exists(oldSegment.Key).Should().BeFalse();
    }

    [Fact]
    public async Task should_read_a_tail_of_a_whole_part_into_parts_before_the_body()
    {
        // given - two 10-byte segments, more than one part, as a failed part upload leaves behind
        var tail = Faker.Random.Bytes(20);
        var segments = new List<TusS3CommittedSegment>
        {
            await _PutSegmentAsync(tail[..10], offset: 0),
            await _PutSegmentAsync(tail[10..], offset: 10),
        };
        await _SeedAsync(
            new TusS3UploadState
            {
                PartSize = _PartSize,
                UploadLength = 100,
                Segments = segments,
                Offset = 20,
            }
        );

        // when
        var body = Faker.Random.Bytes(4);
        var appended = await _AppendAsync(body);

        // then - one part from the tail; the last 4 tail bytes and the body form the new segment
        appended.Should().Be(4);
        var state = _ReadState();
        state.Parts.Should().ContainSingle();
        _s3.Parts[1].Should().Equal(tail[..16]);
        state
            .Segments.Should()
            .ContainSingle()
            .Which.Should()
            .Match<TusS3CommittedSegment>(s => s.Offset == 16 && s.Length == 8);
        _DurableBytes(state).Should().Equal([.. tail, .. body]);
        segments.Should().AllSatisfy(s => _s3.Exists(s.Key).Should().BeFalse());
    }

    [Fact]
    public async Task should_fold_the_tail_into_one_segment_once_it_passes_the_segment_limit()
    {
        // given
        _options.MaxTailSegments = 2;
        await _SeedAsync(new TusS3UploadState { PartSize = _PartSize, UploadLength = 100 });
        var chunks = new[] { Faker.Random.Bytes(3), Faker.Random.Bytes(3), Faker.Random.Bytes(3) };
        await _AppendAsync(chunks[0]);
        await _AppendAsync(chunks[1]);
        var before = _ReadState().Segments;
        before.Should().HaveCount(2);

        // when - a third segment would pass the limit
        await _AppendAsync(chunks[2]);

        // then
        var state = _ReadState();
        state
            .Segments.Should()
            .ContainSingle()
            .Which.Should()
            .Match<TusS3CommittedSegment>(s => s.Offset == 0 && s.Length == 9);
        _DurableBytes(state).Should().Equal(chunks.SelectMany(c => c));
        before.Should().AllSatisfy(s => _s3.Exists(s.Key).Should().BeFalse());
    }

    [Fact]
    public async Task should_reserve_part_numbers_one_window_at_a_time()
    {
        // given - two numbers per reservation
        _options.PartClaimWindow = 2;
        await _SeedAsync(new TusS3UploadState { PartSize = _PartSize, UploadLength = 200 });

        // when - five whole parts
        var body = Faker.Random.Bytes(5 * _PartSize);
        await _AppendAsync(body);

        // then - three reservations and one commit write the state
        var state = _ReadState();
        _s3.Requests.Count(r => string.Equals(r, "PutObject", StringComparison.Ordinal)).Should().Be(4);
        state.Parts.Select(p => p.Number).Should().Equal(1, 2, 3, 4, 5);
        state.Parts.Should().AllSatisfy(p => p.Size.Should().Be(_PartSize));
        state.NextPartNumber.Should().Be(6);
        _DurableBytes(state).Should().Equal(body);
    }

    [Fact]
    public async Task should_keep_part_buffers_within_the_concurrency_bound()
    {
        // given - uploads that stay in flight long enough to overlap
        _options.MaxConcurrentPartUploads = 2;
        _s3.BeforePartStored = _ => Task.Delay(20, AbortToken);
        await _SeedAsync(new TusS3UploadState { PartSize = _PartSize, UploadLength = 1_000 });

        // when
        var body = Faker.Random.Bytes(8 * _PartSize);
        await _AppendAsync(body);

        // then - two uploading plus the one being filled, and every buffer returned
        _pool.MaxOutstanding.Should().BeLessThanOrEqualTo(3);
        _pool.MaxOutstanding.Should().BeGreaterThan(1);
        _pool.Outstanding.Should().Be(0);
        _DurableBytes(_ReadState()).Should().Equal(body);
    }

    [Fact]
    public async Task should_store_a_failed_part_and_every_later_byte_as_segments()
    {
        // given - the second part upload fails
        _options.MaxConcurrentPartUploads = 2;
        _s3.FailPart = number =>
            number == 2 ? InMemoryS3.Error(HttpStatusCode.InternalServerError, "InternalError") : null;
        await _SeedAsync(new TusS3UploadState { PartSize = _PartSize, UploadLength = 1_000 });

        // when - four whole parts and 5 more bytes
        var body = Faker.Random.Bytes((4 * _PartSize) + 5);
        var appended = await _AppendAsync(body);

        // then - the part before the failure is committed; everything from the failed part on is tail
        appended.Should().Be(body.Length);
        var state = _ReadState();
        state.Parts.Select(p => p.Number).Should().Equal(1);
        state.Segments[0].Offset.Should().Be(_PartSize);
        state.Offset.Should().Be(body.Length);
        _DurableBytes(state).Should().Equal(body);
        // a number that was sent, even one that failed, is never handed out again
        state.NextPartNumber.Should().BeGreaterThan(_s3.AttemptedParts.Max());
    }

    [Fact]
    public async Task should_commit_the_bytes_before_a_refused_segment_and_rethrow_the_refusal()
    {
        // given - the second part fails and storage refuses every segment as well
        _options.MaxConcurrentPartUploads = 1;
        var refusal = InMemoryS3.Error(HttpStatusCode.ServiceUnavailable, "SlowDown");
        _s3.FailPart = number => number == 2 ? refusal : null;
        _s3.FailPut = key => key.Contains("/seg/", StringComparison.Ordinal) ? refusal : null;
        await _SeedAsync(new TusS3UploadState { PartSize = _PartSize, UploadLength = 1_000 });
        var body = Faker.Random.Bytes(3 * _PartSize);

        // when
        var act = () => _AppendAsync(body);

        // then
        (await act.Should().ThrowAsync<AmazonS3Exception>())
            .Which.Should()
            .BeSameAs(refusal);
        var state = _ReadState();
        state.Parts.Select(p => p.Number).Should().Equal(1);
        state.Offset.Should().Be(_PartSize);
        _DurableBytes(state).Should().Equal(body[.._PartSize]);
    }

    [Fact]
    public async Task should_upload_the_last_bytes_as_a_shorter_last_part_and_commit_the_upload_as_completing()
    {
        // given - 6 bytes stored, 14 to go
        await _SeedAsync(new TusS3UploadState { PartSize = _PartSize, UploadLength = 20 });
        var first = Faker.Random.Bytes(6);
        await _AppendAsync(first);
        var last = Faker.Random.Bytes(14);

        // when
        var appended = await _AppendAsync(last);

        // then - a full part and a 4-byte last part, with no tail left for completion to read back
        appended.Should().Be(14);
        var state = _ReadState();
        state.Parts.Select(p => (p.Number, p.Size)).Should().Equal((1, _PartSize), (2, 4));
        state.Segments.Should().BeEmpty();
        state.Offset.Should().Be(20);
        _s3.Requests.Should()
            .Equal(
                "CreateMultipartUpload",
                "PutObject",
                "GetObject",
                "UploadPart",
                "UploadPart",
                "PutObject",
                "DeleteObjects"
            );

        // and the commit is the completion fence, reporting the offset this request started from
        state.Status.Should().Be(TusS3UploadStatus.Completing);
        state.PreFinalOffset.Should().Be(6);
        _DurableBytes(state).Should().Equal([.. first, .. last]);
        _pool.Outstanding.Should().Be(0);
    }

    [Fact]
    public async Task should_keep_the_last_bytes_of_an_upload_below_one_part_as_a_segment()
    {
        // given
        await _SeedAsync(new TusS3UploadState { PartSize = _PartSize, UploadLength = 10 });
        var body = Faker.Random.Bytes(10);

        // when
        await _AppendAsync(body);

        // then - no multipart upload exists, so completion writes the object from the segment
        var state = _ReadState();
        state.MultipartUploadId.Should().BeNull();
        state.Segments.Should().ContainSingle().Which.Length.Should().Be(10);
        state.Status.Should().Be(TusS3UploadStatus.Completing);
        state.PreFinalOffset.Should().Be(0);
        _DurableBytes(state).Should().Equal(body);
    }

    [Fact]
    public async Task should_reject_a_body_past_the_upload_length_without_committing()
    {
        // given
        await _SeedAsync(new TusS3UploadState { PartSize = _PartSize, UploadLength = 20 });

        // when
        var act = () => _AppendAsync(Faker.Random.Bytes(25));

        // then
        await act.Should().ThrowAsync<TusStoreException>();
        _ReadState().Offset.Should().Be(0);
        _pool.Outstanding.Should().Be(0);
    }

    [Fact]
    public async Task should_mark_the_upload_gone_when_every_part_number_is_used()
    {
        // given - crashes and lost fences used every number
        await _SeedAsync(
            new TusS3UploadState
            {
                PartSize = _PartSize,
                UploadLength = 100,
                MultipartUploadId = "upload-1",
                NextPartNumber = 10_001,
            }
        );

        // when
        var act = () => _AppendAsync(Faker.Random.Bytes(_PartSize));

        // then
        await act.Should().ThrowAsync<TusStoreException>();
        _ReadState().Status.Should().Be(TusS3UploadStatus.Gone);
        _s3.Requests.Should().NotContain("UploadPart");
    }

    [Fact]
    public async Task should_mark_the_upload_gone_when_its_multipart_upload_no_longer_exists()
    {
        // given
        _s3.FailPart = _ => InMemoryS3.Error(HttpStatusCode.NotFound, "NoSuchUpload");
        await _SeedAsync(new TusS3UploadState { PartSize = _PartSize, UploadLength = 100 });

        // when
        var act = () => _AppendAsync(Faker.Random.Bytes(_PartSize));

        // then
        await act.Should().ThrowAsync<TusStoreException>();
        _ReadState().Status.Should().Be(TusS3UploadStatus.Gone);
    }

    [Fact]
    public async Task should_fail_a_stale_reservation_before_any_part_and_abort_its_new_multipart_upload()
    {
        // given - this request read the state, then another request wrote it
        await _SeedAsync(new TusS3UploadState { PartSize = _PartSize, UploadLength = 100 });
        var view = await _StateStore.LoadAsync(_FileId, AbortToken);
        await _StateStore.UpdateAsync(_FileId, (await _StateStore.LoadAsync(_FileId, AbortToken))!, AbortToken);
        _s3.ClearRequests();

        // when
        var act = () => _AppendAsync(Faker.Random.Bytes(_PartSize), view);

        // then - the stale write is not translated, so tusdotnet answers 500 and the client resumes
        var thrown = await act.Should().ThrowAsync<AmazonS3Exception>();
        thrown.Which.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        _s3.Requests.Should().Equal("CreateMultipartUpload", "PutObject", "AbortMultipartUpload");
        _ReadState().MultipartUploadId.Should().BeNull();
    }

    [Fact]
    public void should_reserve_a_full_window_from_the_counter()
    {
        TusS3PartNumberWindow.TryReserve(1, 64, out var window).Should().BeTrue();

        window!.First.Should().Be(1);
        window.End.Should().Be(65);
    }

    [Fact]
    public void should_hand_out_numbers_in_order_until_the_window_is_used()
    {
        TusS3PartNumberWindow.TryReserve(7, 2, out var window).Should().BeTrue();

        window!.TryTake(out var first).Should().BeTrue();
        window.TryTake(out var second).Should().BeTrue();
        window.TryTake(out _).Should().BeFalse();
        (first, second).Should().Be((7, 8));
    }

    [Fact]
    public void should_cut_a_window_short_at_the_last_part_number()
    {
        TusS3PartNumberWindow.TryReserve(9_990, 64, out var window).Should().BeTrue();

        window!.End.Should().Be(10_001);
    }

    [Theory]
    [InlineData(10_001)]
    [InlineData(0)]
    public void should_refuse_a_window_with_no_part_number_left(int nextPartNumber)
    {
        TusS3PartNumberWindow.TryReserve(nextPartNumber, 64, out var window).Should().BeFalse();
        window.Should().BeNull();
    }

    [Fact]
    public async Task should_delete_more_segments_than_one_request_takes_in_several_requests()
    {
        // given - one segment more than S3 deletes in one request
        var segments = Enumerable
            .Range(0, TusS3Requests.MaxKeysPerDelete + 1)
            .Select(i => new TusS3CommittedSegment($"uploads/.tus/{_FileId}/seg/{i}", i, 1))
            .ToList();

        // when
        await TusS3PartWriter.DeleteSegmentsAsync(_Services(), _FileId, segments, AbortToken);

        // then
        _s3.Requests.Should().Equal("DeleteObjects", "DeleteObjects");
        await _s3
            .Client.Received(1)
            .DeleteObjectsAsync(
                Arg.Is<DeleteObjectsRequest>(r => r.Objects.Count == TusS3Requests.MaxKeysPerDelete),
                Arg.Any<CancellationToken>()
            );
        await _s3
            .Client.Received(1)
            .DeleteObjectsAsync(Arg.Is<DeleteObjectsRequest>(r => r.Objects.Count == 1), Arg.Any<CancellationToken>());
    }

    private TusS3AppendServices _Services()
    {
        return new TusS3AppendServices(
            _s3.Client,
            _options,
            _keys,
            _StateStore,
            _pool,
            Logger,
            TimeProvider.System,
            async (fileId, _, cancellationToken) =>
                (
                    await _s3.Client.InitiateMultipartUploadAsync(
                        new InitiateMultipartUploadRequest
                        {
                            BucketName = _options.Bucket,
                            Key = _keys.FinishedObject(fileId),
                        },
                        cancellationToken
                    )
                ).UploadId
        );
    }

    private async Task _SeedAsync(TusS3UploadState state)
    {
        await _StateStore.CreateAsync(_FileId, state, AbortToken);
        _s3.ClearRequests();
    }

    // Drives the writer as the store's append overloads do, in reads of 7 bytes so reads straddle part boundaries.
    private async Task<long> _AppendAsync(byte[] body, TusS3LoadedUpload? view = null)
    {
        view ??= await _StateStore.LoadAsync(_FileId, AbortToken);
        _s3.ClearRequests();

        await using var writer = new TusS3PartWriter(_Services(), _FileId, view!);
        await writer.StartAsync();

        for (var position = 0; position < body.Length; )
        {
            var buffer = writer.GetBuffer();
            var count = Math.Min(Math.Min(buffer.Length, 7), body.Length - position);
            body.AsSpan(position, count).CopyTo(buffer.Span);
            position += count;

            if (!await writer.AdvanceAsync(count))
            {
                break;
            }
        }

        var (_, appended) = await writer.CommitAsync();

        return appended;
    }

    private async Task<TusS3CommittedSegment> _PutSegmentAsync(byte[] content, long offset)
    {
        var key = _keys.Segment(_FileId, offset, Guid.NewGuid().ToString("N"));
        await using var body = new MemoryStream(content);
        await _s3.Client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = key,
                InputStream = body,
            },
            AbortToken
        );

        return new TusS3CommittedSegment(key, offset, content.Length);
    }

    private TusS3UploadState _ReadState()
    {
        return TusS3UploadState.FromJson(_s3.Read(_keys.Info(_FileId)));
    }

    // The bytes the state says are durable: its parts in number order, then its tail segments.
    private byte[] _DurableBytes(TusS3UploadState state)
    {
        var parts = state.Parts.OrderBy(p => p.Number).SelectMany(p => _s3.Parts[p.Number]);
        var tail = state.Segments.SelectMany(s => _s3.Read(s.Key)[..(int)s.Length]);

        return [.. parts, .. tail];
    }

    /// <summary>Counts the arrays rented and not yet returned, to check the memory one append holds.</summary>
    private sealed class CountingPool : ArrayPool<byte>
    {
        private int _outstanding;
        private int _maxOutstanding;

        public int Outstanding => Volatile.Read(ref _outstanding);

        public int MaxOutstanding => Volatile.Read(ref _maxOutstanding);

        public override byte[] Rent(int minimumLength)
        {
            var now = Interlocked.Increment(ref _outstanding);
            int seen;

            while (now > (seen = Volatile.Read(ref _maxOutstanding)))
            {
                Interlocked.CompareExchange(ref _maxOutstanding, now, seen);
            }

            return new byte[minimumLength];
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            Interlocked.Decrement(ref _outstanding);
        }
    }
}
