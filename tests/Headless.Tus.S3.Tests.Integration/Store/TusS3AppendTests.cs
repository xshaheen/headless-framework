// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Pipelines;
using System.Net;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Tests.TestSetup;
using tusdotnet.Models;

namespace Tests.Store;

/// <summary>
/// How appends store bytes on a real S3 endpoint: equal-size parts, tail segments, part numbering, and persisting
/// what a disconnected client sent. Durable content is checked byte for byte by completing the recorded parts and
/// reading the recorded tail.
/// </summary>
public abstract class TusS3AppendTestsBase(IS3TestBackend backend) : TestBase
{
    private const int _MiB = 1024 * 1024;

    private readonly S3TestClient _client = backend.CreateClient();

    // A prefix per test keeps one scenario's objects apart from another's in the shared bucket.
    private readonly string _prefix = $"append-{Guid.NewGuid():N}/";

    protected override async ValueTask DisposeAsyncCore()
    {
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual Task should_store_every_received_byte_when_the_client_disconnects_mid_chunk_through_a_stream()
    {
        return _DisconnectMidChunkAndResumeAsync(pipe: false);
    }

    public virtual Task should_store_every_received_byte_when_the_client_disconnects_mid_chunk_through_a_pipe()
    {
        return _DisconnectMidChunkAndResumeAsync(pipe: true);
    }

    public virtual async Task should_keep_part_numbers_consecutive_across_two_disconnects()
    {
        // given - one body of 32 MiB in 5 MiB parts
        var (store, options, raw) = await _CreateStoreAsync();
        var content = Faker.Random.Bytes(32 * _MiB);
        var fileId = await store.CreateFileAsync(content.Length, metadata: null, AbortToken);
        const int firstStop = (int)(13.5 * _MiB);
        const int secondStop = (int)(27.25 * _MiB);

        // when - the client disconnects 13.5 MiB in
        (await _AppendDisconnectingAsync(store, fileId, content, deliver: firstStop, pipe: false))
            .Should()
            .Be(firstStop);

        // then - two whole parts and the counter just past them
        var state = await raw.ReadStateAsync(fileId, AbortToken);
        state!.Parts.Select(p => p.Number).Should().Equal(1, 2);
        state.NextPartNumber.Should().Be(3);
        (await store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(firstStop);

        // when - it resumes and disconnects again at 27.25 MiB
        (await _AppendDisconnectingAsync(store, fileId, content[firstStop..], secondStop - firstStop, pipe: true))
            .Should()
            .Be(secondStop - firstStop);

        // then - the next parts continue the numbering
        state = await raw.ReadStateAsync(fileId, AbortToken);
        state!.Parts.Select(p => p.Number).Should().Equal(1, 2, 3, 4, 5);
        state.NextPartNumber.Should().Be(6);
        (await store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(secondStop);

        // when - it resumes to the end
        (await _AppendAsync(store, fileId, content[secondStop..], pipe: false))
            .Should()
            .Be(content.Length - secondStop);

        // then - six whole parts and the 2 MiB rest as the seventh, assembled into the original bytes
        (await raw.GetFinishedObjectPartSizesAsync(fileId, AbortToken))
            .Should()
            .Equal(Enumerable.Repeat((long)options.PartSize, 6).Append(2 * _MiB));
        (await raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().Equal(content);
    }

    public virtual async Task should_upload_one_part_and_write_no_segment_for_a_chunk_of_exactly_one_part()
    {
        // given
        var (store, options, raw) = await _CreateStoreAsync();
        var content = Faker.Random.Bytes(options.PartSize);
        var fileId = await store.CreateFileAsync(4L * options.PartSize, metadata: null, AbortToken);

        // when
        var appended = await _AppendAsync(store, fileId, content, pipe: true);

        // then
        appended.Should().Be(content.Length);
        var state = await raw.ReadStateAsync(fileId, AbortToken);
        state!.Parts.Should().ContainSingle().Which.Size.Should().Be(options.PartSize);
        state.Segments.Should().BeEmpty();
        (await raw.ListSegmentKeysAsync(fileId, AbortToken)).Should().BeEmpty();
        (await raw.ReadDurableBytesAsync(fileId, AbortToken)).Should().Equal(content);
    }

    public virtual async Task should_fold_small_chunks_into_one_segment_past_the_segment_limit()
    {
        // given - at most four tail segments
        var (store, _, raw) = await _CreateStoreAsync(options => options.MaxTailSegments = 4);
        var chunks = Enumerable.Range(0, 6).Select(_ => Faker.Random.Bytes(64 * 1024)).ToList();
        // One byte more than the chunks, so the last chunk does not complete the upload and its tail stays readable.
        var fileId = await store.CreateFileAsync(chunks.Sum(c => c.Length) + 1, metadata: null, AbortToken);

        for (var i = 0; i < 4; i++)
        {
            await _AppendAsync(store, fileId, chunks[i], pipe: i % 2 == 0);
        }

        (await raw.ReadStateAsync(fileId, AbortToken))!.Segments.Should().HaveCount(4);

        // when - a fifth segment would pass the limit
        await _AppendAsync(store, fileId, chunks[4], pipe: false);

        // then - the tail is one segment, and the folded ones are deleted
        var state = await raw.ReadStateAsync(fileId, AbortToken);
        state!.Segments.Should().ContainSingle().Which.Length.Should().Be(5 * 64 * 1024);
        (await raw.ListSegmentKeysAsync(fileId, AbortToken)).Should().ContainSingle();

        // and the next chunk adds a segment after it
        await _AppendAsync(store, fileId, chunks[5], pipe: true);
        (await raw.ReadStateAsync(fileId, AbortToken))!.Segments.Should().HaveCount(2);
        (await raw.ReadDurableBytesAsync(fileId, AbortToken)).Should().Equal(chunks.SelectMany(c => c));
    }

    public virtual async Task should_store_a_failed_part_and_the_bytes_after_it_as_segments_when_persisting()
    {
        // given - two parts upload at once, and the second part upload fails
        var (store, options, raw) = await _CreateStoreAsync(options => options.MaxConcurrentPartUploads = 2);
        var content = Faker.Random.Bytes(30 * _MiB);
        var fileId = await store.CreateFileAsync(content.Length, metadata: null, AbortToken);
        _client.Gate.FailWhen(request =>
            string.Equals(S3Operation.Of(request), S3Operation.UploadPart, StringComparison.Ordinal)
            && S3Operation.PartNumber(request) == 2
        );
        var received = (4 * options.PartSize) + _MiB;

        // when - the client disconnects after four parts and one more MiB
        var appended = await _AppendDisconnectingAsync(store, fileId, content, received, pipe: false);
        _client.Gate.FailWhen(null);

        // then - the part before the failure is committed, and every byte from the failed part on is tail
        appended.Should().Be(received);
        (await store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(received);
        var state = await raw.ReadStateAsync(fileId, AbortToken);
        state!.Parts.Select(p => p.Number).Should().Equal(1);
        state.Segments[0].Offset.Should().Be(options.PartSize);
        (await raw.ReadDurableBytesAsync(fileId, AbortToken)).Should().Equal(content[..received]);
    }

    public virtual async Task should_report_the_upload_gone_once_its_part_numbers_run_out()
    {
        // given - crashes and lost fences used every part number
        var options = _Options();
        await new TusS3BucketProvisioner(_client.S3, options, LoggerFactory).ProvisionAsync(AbortToken);
        using var host = await TusS3TestHost.StartAsync(
            _client,
            options,
            LoggerFactory,
            timeProvider: null,
            AbortToken
        );
        var location = await host.CreateUploadAsync(4L * options.PartSize, metadata: null, AbortToken);
        var fileId = TusS3TestHost.FileIdOf(location);
        var raw = new TusS3RawObjects(_client, options);
        await raw.RewriteStateAsync(fileId, state => state.NextPartNumber = TusS3PartSize.MaxPartCount + 1, AbortToken);

        // when - a PATCH needs a part number
        using var patch = await host.PatchAsync(location, Faker.Random.Bytes(options.PartSize), 0, AbortToken);

        // then - the client is told the upload cannot continue, and it is reported missing from then on
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await raw.ReadStateAsync(fileId, AbortToken))!.Status.Should().Be(TusS3UploadStatus.Gone);
        using var head = await host.HeadAsync(location, AbortToken);
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public virtual async Task should_report_the_offset_before_a_patch_whose_commit_never_landed()
    {
        // given - a short persist window, and 1 MiB already stored
        var (store, _, raw) = await _CreateStoreAsync(options => options.PersistTimeout = TimeSpan.FromSeconds(1));
        var content = Faker.Random.Bytes(4 * _MiB);
        var fileId = await store.CreateFileAsync(content.Length, metadata: null, AbortToken);
        await _AppendAsync(store, fileId, content[.._MiB], pipe: false);

        // when - the next PATCH writes its segment, then the process stalls before the commit until the persist
        // window closes, as a process that died after the segment write would
        _client.Gate.Arm(S3Operation.PutObject, "/info");
        var dying = _AppendDisconnectingAsync(store, fileId, content[_MiB..], deliver: _MiB, pipe: true);
        await _client.Gate.WaitForArrivalAsync(AbortToken);
        var act = async () => await dying;
        await act.Should().ThrowAsync<OperationCanceledException>();
        _client.Gate.Disarm();

        // then - the offset is the one before that PATCH; its segment exists but is recorded nowhere
        (await store.GetUploadOffsetAsync(fileId, AbortToken))
            .Should()
            .Be(_MiB);
        (await raw.ReadStateAsync(fileId, AbortToken))!.Segments.Should().ContainSingle();
        (await raw.ListSegmentKeysAsync(fileId, AbortToken)).Should().HaveCount(2);

        // and the client resumes from there to a byte-identical upload
        await _AppendAsync(store, fileId, content[_MiB..], pipe: false);
        (await store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);
        (await raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().Equal(content);
    }

    // The client sends 1 MiB chunks into 8 MiB parts and disconnects 0.5 MiB into its seventh chunk.
    private async Task _DisconnectMidChunkAndResumeAsync(bool pipe)
    {
        // given
        var (store, options, raw) = await _CreateStoreAsync(options => options.PartSize = 8 * _MiB);
        var content = Faker.Random.Bytes(20 * _MiB);
        var fileId = await store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        for (var offset = 0; offset < 6 * _MiB; offset += _MiB)
        {
            await _AppendAsync(store, fileId, content[offset..(offset + _MiB)], pipe);
        }

        // when
        var appended = await _AppendDisconnectingAsync(
            store,
            fileId,
            content[(6 * _MiB)..(7 * _MiB)],
            deliver: _MiB / 2,
            pipe
        );

        // then - the offset covers every received byte
        appended.Should().Be(_MiB / 2);
        (await store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be((int)(6.5 * _MiB));

        // and the resumed upload stores the rest in equal parts
        for (var offset = (int)(6.5 * _MiB); offset < content.Length; offset += _MiB)
        {
            var end = Math.Min(offset + _MiB, content.Length);
            (await _AppendAsync(store, fileId, content[offset..end], pipe)).Should().Be(end - offset);
        }

        (await raw.GetFinishedObjectPartSizesAsync(fileId, AbortToken))
            .Should()
            .Equal(options.PartSize, options.PartSize, 4 * _MiB);
        (await raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().Equal(content);
    }

    private TusS3StoreOptions _Options(Action<TusS3StoreOptions>? configure = null)
    {
        var options = new TusS3StoreOptions
        {
            Bucket = TusS3StoreFactory.Bucket,
            Prefix = _prefix,
            Provider = _client.Provider,
            PartSize = 5 * _MiB,
        };
        configure?.Invoke(options);

        return options;
    }

    private async Task<(TusS3Store Store, TusS3StoreOptions Options, TusS3RawObjects Raw)> _CreateStoreAsync(
        Action<TusS3StoreOptions>? configure = null
    )
    {
        var options = _Options(configure);
        await new TusS3BucketProvisioner(_client.S3, options, LoggerFactory).ProvisionAsync(AbortToken);

        return (new TusS3Store(_client.S3, options, loggerFactory: LoggerFactory), options, new(_client, options));
    }

    private static async Task<long> _AppendAsync(TusS3Store store, string fileId, byte[] body, bool pipe)
    {
        await using var stream = new MemoryStream(body, writable: false);

        return pipe
            ? await store.AppendDataAsync(fileId, PipeReader.Create(stream), AbortToken)
            : await store.AppendDataAsync(fileId, stream, AbortToken);
    }

    private static async Task<long> _AppendDisconnectingAsync(
        TusS3Store store,
        string fileId,
        byte[] body,
        int deliver,
        bool pipe
    )
    {
        using var disconnect = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        if (pipe)
        {
            return await store.AppendDataAsync(
                fileId,
                DisconnectingBody.Pipe(body, deliver, disconnect),
                disconnect.Token
            );
        }

        await using var stream = DisconnectingBody.Stream(body, deliver, disconnect);

        return await store.AppendDataAsync(fileId, stream, disconnect.Token);
    }
}

[Collection<TusS3MinioFixture>]
public sealed class MinioTusS3AppendTests(TusS3MinioFixture fixture) : TusS3AppendTestsBase(fixture)
{
    [Fact]
    public override Task should_store_every_received_byte_when_the_client_disconnects_mid_chunk_through_a_stream() =>
        base.should_store_every_received_byte_when_the_client_disconnects_mid_chunk_through_a_stream();

    [Fact]
    public override Task should_store_every_received_byte_when_the_client_disconnects_mid_chunk_through_a_pipe() =>
        base.should_store_every_received_byte_when_the_client_disconnects_mid_chunk_through_a_pipe();

    [Fact]
    public override Task should_keep_part_numbers_consecutive_across_two_disconnects() =>
        base.should_keep_part_numbers_consecutive_across_two_disconnects();

    [Fact]
    public override Task should_upload_one_part_and_write_no_segment_for_a_chunk_of_exactly_one_part() =>
        base.should_upload_one_part_and_write_no_segment_for_a_chunk_of_exactly_one_part();

    [Fact]
    public override Task should_fold_small_chunks_into_one_segment_past_the_segment_limit() =>
        base.should_fold_small_chunks_into_one_segment_past_the_segment_limit();

    [Fact]
    public override Task should_store_a_failed_part_and_the_bytes_after_it_as_segments_when_persisting() =>
        base.should_store_a_failed_part_and_the_bytes_after_it_as_segments_when_persisting();

    [Fact]
    public override Task should_report_the_upload_gone_once_its_part_numbers_run_out() =>
        base.should_report_the_upload_gone_once_its_part_numbers_run_out();

    [Fact]
    public override Task should_report_the_offset_before_a_patch_whose_commit_never_landed() =>
        base.should_report_the_offset_before_a_patch_whose_commit_never_landed();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackTusS3AppendTests(TusS3LocalStackFixture fixture) : TusS3AppendTestsBase(fixture)
{
    [Fact]
    public override Task should_store_every_received_byte_when_the_client_disconnects_mid_chunk_through_a_stream() =>
        base.should_store_every_received_byte_when_the_client_disconnects_mid_chunk_through_a_stream();

    [Fact]
    public override Task should_store_every_received_byte_when_the_client_disconnects_mid_chunk_through_a_pipe() =>
        base.should_store_every_received_byte_when_the_client_disconnects_mid_chunk_through_a_pipe();

    [Fact]
    public override Task should_keep_part_numbers_consecutive_across_two_disconnects() =>
        base.should_keep_part_numbers_consecutive_across_two_disconnects();

    [Fact]
    public override Task should_upload_one_part_and_write_no_segment_for_a_chunk_of_exactly_one_part() =>
        base.should_upload_one_part_and_write_no_segment_for_a_chunk_of_exactly_one_part();

    [Fact]
    public override Task should_fold_small_chunks_into_one_segment_past_the_segment_limit() =>
        base.should_fold_small_chunks_into_one_segment_past_the_segment_limit();

    [Fact]
    public override Task should_store_a_failed_part_and_the_bytes_after_it_as_segments_when_persisting() =>
        base.should_store_a_failed_part_and_the_bytes_after_it_as_segments_when_persisting();

    [Fact]
    public override Task should_report_the_upload_gone_once_its_part_numbers_run_out() =>
        base.should_report_the_upload_gone_once_its_part_numbers_run_out();

    [Fact]
    public override Task should_report_the_offset_before_a_patch_whose_commit_never_landed() =>
        base.should_report_the_offset_before_a_patch_whose_commit_never_landed();
}
