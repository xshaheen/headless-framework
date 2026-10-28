// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Tests.TestSetup;
using tusdotnet.Models;

namespace Tests.Flows;

/// <summary>
/// How an upload becomes one finished object, and how completion recovers when a step fails, a stale request races
/// the finisher, or the provider lost the parts. Scenarios that a client drives run through the real tusdotnet
/// middleware, so the offsets and the completion event are the ones a client sees.
/// </summary>
public abstract class CompletionRecoveryTestsBase(IS3TestBackend backend) : TestBase
{
    private const int _MiB = 1024 * 1024;

    private readonly S3TestClient _client = backend.CreateClient();

    private readonly TusS3StoreOptions _options = new()
    {
        Bucket = TusS3StoreFactory.Bucket,
        Prefix = $"completion-{Guid.NewGuid():N}/",
        PartSize = 5 * _MiB,
    };

    private TusS3RawObjects _raw = null!;
    private TusS3TestHost _host = null!;
    private int _fileCompleteEvents;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _options.Provider = _client.Provider;
        await new TusS3BucketProvisioner(_client.S3, _options, LoggerFactory).ProvisionAsync(AbortToken);
        _raw = new TusS3RawObjects(_client, _options);
        _host = await TusS3TestHost.StartAsync(
            _client,
            _options,
            LoggerFactory,
            timeProvider: null,
            AbortToken,
            onFileComplete: _ =>
            {
                Interlocked.Increment(ref _fileCompleteEvents);

                return Task.CompletedTask;
            }
        );
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _client.Gate.Disarm();
        _client.Gate.FailWhen(null);
        _host.Dispose();
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_write_an_upload_below_one_part_as_one_object_and_remove_its_working_set()
    {
        // given - two PATCHes that together stay below one part
        var content = Faker.Random.Bytes(300 * 1024);
        var location = await _host.CreateUploadAsync(content.Length, metadata: null, AbortToken);
        var fileId = TusS3TestHost.FileIdOf(location);
        await _PatchExpectingOffsetAsync(location, content[..1000], 0, 1000);

        // when
        await _PatchExpectingOffsetAsync(location, content[1000..], 1000, content.Length);

        // then - one object written in one request, the record kept, and nothing left in the working set
        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken))
            .Should()
            .Equal(content);
        (await _raw.GetFinishedObjectPartSizesAsync(fileId, AbortToken)).Should().Equal(content.Length);
        _client.Gate.Count(S3Operation.CreateMultipartUpload).Should().Be(0);
        (await _raw.ReadCompletedRecordAsync(fileId, AbortToken))!.UploadLength.Should().Be(content.Length);
        (await _raw.ReadStateAsync(fileId, AbortToken)).Should().BeNull();
        (await _raw.ListSegmentKeysAsync(fileId, AbortToken)).Should().BeEmpty();
        _fileCompleteEvents.Should().Be(1);
    }

    public virtual async Task should_assemble_a_multipart_upload_from_equal_parts_and_a_shorter_last_one()
    {
        // given - 12 MiB in 5 MiB parts, sent in three 4 MiB PATCHes
        var content = Faker.Random.Bytes(12 * _MiB);
        var location = await _host.CreateUploadAsync(content.Length, metadata: null, AbortToken);
        var fileId = TusS3TestHost.FileIdOf(location);

        // when
        for (var offset = 0; offset < content.Length; offset += 4 * _MiB)
        {
            await _PatchExpectingOffsetAsync(
                location,
                content[offset..(offset + (4 * _MiB))],
                offset,
                offset + (4 * _MiB)
            );
        }

        // then
        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken))
            .Should()
            .Equal(content);
        (await _raw.GetFinishedObjectPartSizesAsync(fileId, AbortToken)).Should().Equal(5 * _MiB, 5 * _MiB, 2 * _MiB);
        (await _raw.ReadStateAsync(fileId, AbortToken)).Should().BeNull();
        (await _raw.ListSegmentKeysAsync(fileId, AbortToken)).Should().BeEmpty();
        _client.Gate.Count(S3Operation.CompleteMultipartUpload).Should().Be(1);
        _fileCompleteEvents.Should().Be(1);
    }

    public virtual async Task should_finish_on_the_resent_final_patch_after_complete_failed()
    {
        // given - 3 MiB stored, and a final PATCH whose multipart complete fails after the Completing write
        var content = Faker.Random.Bytes(7 * _MiB);
        var location = await _host.CreateUploadAsync(content.Length, metadata: null, AbortToken);
        var fileId = TusS3TestHost.FileIdOf(location);
        await _PatchExpectingOffsetAsync(location, content[..(3 * _MiB)], 0, 3 * _MiB);
        _FailComplete();

        await _PatchExpectingServerErrorAsync(location, content[(3 * _MiB)..], 3 * _MiB);

        // then - the client is sent back to the start of its final PATCH, with no completion event yet
        using (var head = await _host.HeadAsync(location, AbortToken))
        {
            head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be($"{3 * _MiB}");
            head.Headers.GetValues("Upload-Length").Should().ContainSingle().Which.Should().Be($"{7 * _MiB}");
        }

        (await _raw.ReadStateAsync(fileId, AbortToken))!.Status.Should().Be(TusS3UploadStatus.Completing);
        _fileCompleteEvents.Should().Be(0);

        // when - it re-sends the final PATCH
        _client.Gate.FailWhen(null);
        await _PatchExpectingOffsetAsync(location, content[(3 * _MiB)..], 3 * _MiB, content.Length);

        // then - the upload completes once, with the original bytes
        _fileCompleteEvents.Should().Be(1);
        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().Equal(content);
        (await _raw.ReadStateAsync(fileId, AbortToken)).Should().BeNull();
    }

    public virtual async Task should_finish_on_a_redeclared_length_after_completing_from_a_declared_length_failed()
    {
        // given - a deferred upload holding one part and a 1 MiB tail
        var content = Faker.Random.Bytes(6 * _MiB);
        var location = await _host.CreateDeferredUploadAsync(AbortToken);
        var fileId = TusS3TestHost.FileIdOf(location);
        await _PatchExpectingOffsetAsync(location, content, 0, content.Length);
        _FailComplete();

        // when - an empty PATCH declares the length the upload already holds, and its completion fails
        await _PatchExpectingServerErrorAsync(location, [], content.Length, uploadLength: content.Length);

        // then - the length still reads as deferred, so the client declares it again
        using (var head = await _host.HeadAsync(location, AbortToken))
        {
            head.Headers.GetValues("Upload-Defer-Length").Should().ContainSingle().Which.Should().Be("1");
            head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be($"{content.Length}");
        }

        _fileCompleteEvents.Should().Be(0);

        // when
        _client.Gate.FailWhen(null);
        await _PatchExpectingOffsetAsync(location, [], content.Length, content.Length, uploadLength: content.Length);

        // then - the tail became the last part, and the upload completed once
        _fileCompleteEvents.Should().Be(1);
        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().Equal(content);
        (await _raw.GetFinishedObjectPartSizesAsync(fileId, AbortToken)).Should().Equal(5 * _MiB, _MiB);
        (await _raw.ListSegmentKeysAsync(fileId, AbortToken)).Should().BeEmpty();
    }

    public virtual async Task should_complete_a_deferred_upload_whose_fourth_patch_declares_the_length()
    {
        // given - three PATCHes before the client knows the length
        var content = Faker.Random.Bytes(10 * _MiB);
        var location = await _host.CreateDeferredUploadAsync(AbortToken);
        var fileId = TusS3TestHost.FileIdOf(location);
        int[] ends = [2 * _MiB, 6 * _MiB, 9 * _MiB];
        var start = 0;

        foreach (var end in ends)
        {
            await _PatchExpectingOffsetAsync(location, content[start..end], start, end);
            start = end;
        }

        // when - the fourth carries the last bytes and the length
        await _PatchExpectingOffsetAsync(
            location,
            content[start..],
            start,
            content.Length,
            uploadLength: content.Length
        );

        // then
        _fileCompleteEvents.Should().Be(1);
        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().Equal(content);

        using var head = await _host.HeadAsync(location, AbortToken);
        head.Headers.GetValues("Upload-Length").Should().ContainSingle().Which.Should().Be($"{content.Length}");
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be($"{content.Length}");
    }

    public virtual async Task should_let_one_of_two_finishers_win_the_completing_write()
    {
        // given - two final appends that both read the same state of an upload below one part
        var store = _CreateStore();
        var head = Faker.Random.Bytes(400);
        var first = Faker.Random.Bytes(600);
        var second = Faker.Random.Bytes(600);
        var fileId = await store.CreateFileAsync(1_000, metadata: null, AbortToken);
        await _AppendAsync(store, fileId, head);
        _client.Gate.Arm(S3Operation.PutObject, "/info");

        var firstAppend = _AppendAsync(store, fileId, first);
        var secondAppend = _AppendAsync(store, fileId, second);
        await _client.Gate.WaitForArrivalAsync(AbortToken);
        await _client.Gate.WaitForArrivalAsync(AbortToken);

        // when - one Completing write lands and that request finishes, then the other proceeds
        _client.Gate.ReleaseOne();
        var winnerTask = await Task.WhenAny(firstAppend, secondAppend);
        var loserTask = winnerTask == firstAppend ? secondAppend : firstAppend;
        (await winnerTask).Should().Be(600);
        _client.Gate.Disarm();
        _client.Gate.ReleaseOne();

        // then - the loser's write is stale, which tusdotnet answers with a retryable 500
        var act = async () => await loserTask;
        var failure = (await act.Should().ThrowAsync<AmazonS3Exception>()).Which;
        TusS3StateStore.IsStaleWrite(failure).Should().BeTrue();

        // and the finished object holds the winner's bytes
        var winner = winnerTask == firstAppend ? first : second;
        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().Equal([.. head, .. winner]);
        (await store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(1_000);
    }

    public virtual async Task should_complete_the_multipart_upload_once_when_two_finishers_race()
    {
        // given - one part stored, and two final appends that both read that state
        var store = _CreateStore();
        var head = Faker.Random.Bytes(5 * _MiB);
        var first = Faker.Random.Bytes(2 * _MiB);
        var second = Faker.Random.Bytes(2 * _MiB);
        var fileId = await store.CreateFileAsync(7 * _MiB, metadata: null, AbortToken);
        await _AppendAsync(store, fileId, head);
        _client.Gate.Reset();
        _client.Gate.Arm(S3Operation.PutObject, "/info");

        var firstAppend = _AppendAsync(store, fileId, first);
        var secondAppend = _AppendAsync(store, fileId, second);
        await _client.Gate.WaitForArrivalAsync(AbortToken);
        await _client.Gate.WaitForArrivalAsync(AbortToken);

        // when - one reserves its part number and reaches its Completing write while the other still waits
        _client.Gate.ReleaseOne();
        await _client.Gate.WaitForArrivalAsync(AbortToken);
        _client.Gate.Disarm();
        _client.Gate.ReleaseOne();
        _client.Gate.ReleaseOne();

        var outcomes = await Task.WhenAll(_Settle(firstAppend), _Settle(secondAppend));

        // then - one finished the upload and the other failed on a stale write, so only one complete was sent
        outcomes.Count(o => o.Appended is not null).Should().Be(1);
        var loser = outcomes.Single(o => o.Error is not null).Error;
        TusS3StateStore.IsStaleWrite(loser.Should().BeOfType<AmazonS3Exception>().Subject).Should().BeTrue();
        _client.Gate.Count(S3Operation.CompleteMultipartUpload).Should().Be(1);

        var winner = outcomes[0].Appended is not null ? first : second;
        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().Equal([.. head, .. winner]);
    }

    public virtual async Task should_treat_a_missing_multipart_upload_as_done_when_the_finished_object_is_whole()
    {
        // given - a final PATCH whose complete failed, then an earlier attempt's complete landed after all
        var content = Faker.Random.Bytes(7 * _MiB);
        var location = await _host.CreateUploadAsync(content.Length, metadata: null, AbortToken);
        var fileId = TusS3TestHost.FileIdOf(location);
        await _PatchExpectingOffsetAsync(location, content[..(3 * _MiB)], 0, 3 * _MiB);
        _FailComplete();
        await _PatchExpectingServerErrorAsync(location, content[(3 * _MiB)..], 3 * _MiB);
        _client.Gate.FailWhen(null);
        await _CompleteRecordedPartsAsync(fileId);

        // when - the client re-sends the final PATCH and the provider no longer knows the multipart upload
        await _PatchExpectingOffsetAsync(location, content[(3 * _MiB)..], 3 * _MiB, content.Length);

        // then - the whole object counts as done
        _fileCompleteEvents.Should().Be(1);
        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().Equal(content);
        (await _raw.ReadCompletedRecordAsync(fileId, AbortToken))!.UploadLength.Should().Be(content.Length);
        (await _raw.ReadStateAsync(fileId, AbortToken)).Should().BeNull();
    }

    public virtual async Task should_report_the_upload_gone_when_its_multipart_upload_was_aborted_out_of_band()
    {
        // given - one part stored, then the provider aborts the multipart upload
        var location = await _host.CreateUploadAsync(12 * _MiB, metadata: null, AbortToken);
        var fileId = TusS3TestHost.FileIdOf(location);
        await _PatchExpectingOffsetAsync(location, Faker.Random.Bytes(6 * _MiB), 0, 6 * _MiB);
        var state = await _raw.ReadStateAsync(fileId, AbortToken);
        await _client.S3.AbortMultipartUploadAsync(
            _options.Bucket,
            _raw.Keys.FinishedObject(fileId),
            state!.MultipartUploadId,
            AbortToken
        );

        // when - the next PATCH uploads a part
        using var patch = await _host.PatchAsync(location, Faker.Random.Bytes(5 * _MiB), 6 * _MiB, AbortToken);

        // then - the client is told the upload cannot continue, and it reads as missing from then on
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var head = await _host.HeadAsync(location, AbortToken);
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public virtual async Task should_report_the_upload_gone_when_complete_rejects_a_recorded_part()
    {
        // given - a recorded part whose ETag no longer matches what the provider holds
        var content = Faker.Random.Bytes(7 * _MiB);
        var location = await _host.CreateUploadAsync(content.Length, metadata: null, AbortToken);
        var fileId = TusS3TestHost.FileIdOf(location);
        await _PatchExpectingOffsetAsync(location, content[..(6 * _MiB)], 0, 6 * _MiB);
        await _raw.RewriteStateAsync(
            fileId,
            state => state.Parts[0] = state.Parts[0] with { ETag = "\"00000000000000000000000000000000\"" },
            AbortToken
        );

        // when - the final PATCH completes the multipart upload
        using var patch = await _host.PatchAsync(location, content[(6 * _MiB)..], 6 * _MiB, AbortToken);

        // then - the provider refuses the parts, so the upload is gone rather than stuck completing
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _raw.ReadStateAsync(fileId, AbortToken))!.Status.Should().Be(TusS3UploadStatus.Gone);
        using var head = await _host.HeadAsync(location, AbortToken);
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _fileCompleteEvents.Should().Be(0);
    }

    public virtual async Task should_read_a_completed_upload_and_only_the_metadata_of_an_incomplete_one()
    {
        // given
        var store = _CreateStore();
        const string fileName = "report.pdf";
        var metadata = $"filename {fileName.ToBase64()},filetype {"application/pdf".ToBase64()}";
        var content = Faker.Random.Bytes(2_000);
        var completed = await store.CreateFileAsync(content.Length, metadata, AbortToken);
        await _AppendAsync(store, completed, content);
        var incomplete = await store.CreateFileAsync(content.Length, metadata, AbortToken);
        await _AppendAsync(store, incomplete, content[..1_000]);

        // when
        var file = await store.GetFileAsync(completed, AbortToken);

        // then - the content streams the finished object and the metadata decodes as the client sent it
        file.Should().NotBeNull();
        file!.Id.Should().Be(completed);
        await using (var stream = await file.GetContentAsync(AbortToken))
        {
            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, AbortToken);
            buffer.ToArray().Should().Equal(content);
        }

        var decoded = await file.GetMetadataAsync(AbortToken);
        decoded["filename"].GetString(Encoding.UTF8).Should().Be(fileName);

        // and an incomplete upload exposes its metadata but no content yet, while an unknown upload has no file
        var incompleteFile = await store.GetFileAsync(incomplete, AbortToken);
        incompleteFile.Should().NotBeNull();
        (await incompleteFile!.GetMetadataAsync(AbortToken))["filename"].GetString(Encoding.UTF8).Should().Be(fileName);
        await FluentActions
            .Awaiting(() => incompleteFile.GetContentAsync(AbortToken))
            .Should()
            .ThrowAsync<InvalidOperationException>();
        (await store.GetFileAsync(Guid.NewGuid().ToString("N"), AbortToken)).Should().BeNull();
    }

    public virtual async Task should_read_an_empty_file_when_a_zero_byte_upload_completes()
    {
        // given - a completion handler that reads the file, as an application does
        byte[]? read = null;
        using var host = await TusS3TestHost.StartAsync(
            _client,
            _options,
            LoggerFactory,
            timeProvider: null,
            AbortToken,
            onFileComplete: async context =>
            {
                var file = await context.GetFileAsync();
                await using var stream = await file.GetContentAsync(context.CancellationToken);
                await using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, context.CancellationToken);
                read = buffer.ToArray();
            }
        );

        // when
        await host.CreateUploadAsync(0, metadata: null, AbortToken);

        // then
        read.Should().NotBeNull().And.BeEmpty();
    }

    private TusS3Store _CreateStore()
    {
        // No HTTP context accessor: each call reads the state itself, as requests on separate hosts would.
        return new TusS3Store(_client.S3, _options, loggerFactory: LoggerFactory);
    }

    private void _FailComplete()
    {
        _client.Gate.FailWhen(request =>
            string.Equals(S3Operation.Of(request), S3Operation.CompleteMultipartUpload, StringComparison.Ordinal)
        );
    }

    // Completes the multipart upload from the recorded parts, as an earlier attempt whose response was lost would.
    private async Task _CompleteRecordedPartsAsync(string fileId)
    {
        var state = await _raw.ReadStateAsync(fileId, AbortToken);
        state!.Segments.Should().BeEmpty("the final PATCH uploads its last bytes as the last part");

        await _client.S3.CompleteMultipartUploadAsync(
            new CompleteMultipartUploadRequest
            {
                BucketName = _options.Bucket,
                Key = _raw.Keys.FinishedObject(fileId),
                UploadId = state.MultipartUploadId,
                PartETags = [.. state.Parts.OrderBy(p => p.Number).Select(p => new PartETag(p.Number, p.ETag))],
            },
            AbortToken
        );
    }

    private async Task _PatchExpectingOffsetAsync(
        string location,
        byte[] body,
        long offset,
        long expectedOffset,
        long? uploadLength = null
    )
    {
        using var patch = await _host.PatchAsync(location, body, offset, AbortToken, uploadLength);
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        patch.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be($"{expectedOffset}");
    }

    // An exception the store does not translate surfaces as a 500, or the test server rethrows it to the client.
    private async Task _PatchExpectingServerErrorAsync(
        string location,
        byte[] body,
        long offset,
        long? uploadLength = null
    )
    {
        HttpStatusCode status;

        try
        {
            using var patch = await _host.PatchAsync(location, body, offset, AbortToken, uploadLength);
            status = patch.StatusCode;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            status = HttpStatusCode.InternalServerError;
        }

        status.Should().Be(HttpStatusCode.InternalServerError);
    }

    private static async Task<long> _AppendAsync(TusS3Store store, string fileId, byte[] body)
    {
        // Leave the test's thread first, so racing appends reach the gate before either is awaited.
        await Task.Yield();
        await using var stream = new MemoryStream(body, writable: false);

        return await store.AppendDataAsync(fileId, stream, AbortToken);
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
public sealed class MinioCompletionRecoveryTests(TusS3MinioFixture fixture) : CompletionRecoveryTestsBase(fixture)
{
    [Fact]
    public override Task should_write_an_upload_below_one_part_as_one_object_and_remove_its_working_set() =>
        base.should_write_an_upload_below_one_part_as_one_object_and_remove_its_working_set();

    [Fact]
    public override Task should_assemble_a_multipart_upload_from_equal_parts_and_a_shorter_last_one() =>
        base.should_assemble_a_multipart_upload_from_equal_parts_and_a_shorter_last_one();

    [Fact]
    public override Task should_finish_on_the_resent_final_patch_after_complete_failed() =>
        base.should_finish_on_the_resent_final_patch_after_complete_failed();

    [Fact]
    public override Task should_finish_on_a_redeclared_length_after_completing_from_a_declared_length_failed() =>
        base.should_finish_on_a_redeclared_length_after_completing_from_a_declared_length_failed();

    [Fact]
    public override Task should_complete_a_deferred_upload_whose_fourth_patch_declares_the_length() =>
        base.should_complete_a_deferred_upload_whose_fourth_patch_declares_the_length();

    [Fact]
    public override Task should_let_one_of_two_finishers_win_the_completing_write() =>
        base.should_let_one_of_two_finishers_win_the_completing_write();

    [Fact]
    public override Task should_complete_the_multipart_upload_once_when_two_finishers_race() =>
        base.should_complete_the_multipart_upload_once_when_two_finishers_race();

    [Fact]
    public override Task should_treat_a_missing_multipart_upload_as_done_when_the_finished_object_is_whole() =>
        base.should_treat_a_missing_multipart_upload_as_done_when_the_finished_object_is_whole();

    [Fact]
    public override Task should_report_the_upload_gone_when_its_multipart_upload_was_aborted_out_of_band() =>
        base.should_report_the_upload_gone_when_its_multipart_upload_was_aborted_out_of_band();

    [Fact]
    public override Task should_report_the_upload_gone_when_complete_rejects_a_recorded_part() =>
        base.should_report_the_upload_gone_when_complete_rejects_a_recorded_part();

    [Fact]
    public override Task should_read_a_completed_upload_and_only_the_metadata_of_an_incomplete_one() =>
        base.should_read_a_completed_upload_and_only_the_metadata_of_an_incomplete_one();

    [Fact]
    public override Task should_read_an_empty_file_when_a_zero_byte_upload_completes() =>
        base.should_read_an_empty_file_when_a_zero_byte_upload_completes();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackCompletionRecoveryTests(TusS3LocalStackFixture fixture)
    : CompletionRecoveryTestsBase(fixture)
{
    [Fact]
    public override Task should_write_an_upload_below_one_part_as_one_object_and_remove_its_working_set() =>
        base.should_write_an_upload_below_one_part_as_one_object_and_remove_its_working_set();

    [Fact]
    public override Task should_assemble_a_multipart_upload_from_equal_parts_and_a_shorter_last_one() =>
        base.should_assemble_a_multipart_upload_from_equal_parts_and_a_shorter_last_one();

    [Fact]
    public override Task should_finish_on_the_resent_final_patch_after_complete_failed() =>
        base.should_finish_on_the_resent_final_patch_after_complete_failed();

    [Fact]
    public override Task should_finish_on_a_redeclared_length_after_completing_from_a_declared_length_failed() =>
        base.should_finish_on_a_redeclared_length_after_completing_from_a_declared_length_failed();

    [Fact]
    public override Task should_complete_a_deferred_upload_whose_fourth_patch_declares_the_length() =>
        base.should_complete_a_deferred_upload_whose_fourth_patch_declares_the_length();

    [Fact]
    public override Task should_let_one_of_two_finishers_win_the_completing_write() =>
        base.should_let_one_of_two_finishers_win_the_completing_write();

    [Fact]
    public override Task should_complete_the_multipart_upload_once_when_two_finishers_race() =>
        base.should_complete_the_multipart_upload_once_when_two_finishers_race();

    [Fact]
    public override Task should_treat_a_missing_multipart_upload_as_done_when_the_finished_object_is_whole() =>
        base.should_treat_a_missing_multipart_upload_as_done_when_the_finished_object_is_whole();

    [Fact]
    public override Task should_report_the_upload_gone_when_its_multipart_upload_was_aborted_out_of_band() =>
        base.should_report_the_upload_gone_when_its_multipart_upload_was_aborted_out_of_band();

    [Fact]
    public override Task should_report_the_upload_gone_when_complete_rejects_a_recorded_part() =>
        base.should_report_the_upload_gone_when_complete_rejects_a_recorded_part();

    [Fact]
    public override Task should_read_a_completed_upload_and_only_the_metadata_of_an_incomplete_one() =>
        base.should_read_a_completed_upload_and_only_the_metadata_of_an_incomplete_one();

    [Fact]
    public override Task should_read_an_empty_file_when_a_zero_byte_upload_completes() =>
        base.should_read_an_empty_file_when_a_zero_byte_upload_completes();
}
