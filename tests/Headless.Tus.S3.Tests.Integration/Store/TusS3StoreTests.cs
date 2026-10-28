// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Microsoft.Extensions.Time.Testing;
using Tests.TestSetup;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Concatenation;

namespace Tests.Store;

/// <summary>
/// S3-only store behavior: the state object behind creation and deferred length, zero-byte uploads, the abort-window
/// check, and the write fence. Stored offsets are staged in the state object directly, because appends are not what
/// these scenarios check.
/// </summary>
public abstract class TusS3StoreTestsBase(IS3TestBackend backend) : TestBase
{
    private const long _MiB = 1024 * 1024;

    // 10,000 parts less four claim windows of 64 at the defaults.
    private const long _DefaultPartBudget = 9_744;

    private readonly S3TestClient _client = backend.CreateClient();

    private readonly TusS3StoreOptions _options = new() { Bucket = TusS3StoreFactory.Bucket, Prefix = "s3-store/" };

    private TusS3Store _store = null!;
    private TusS3RawObjects _raw = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _options.Provider = _client.Provider;
        await new TusS3BucketProvisioner(_client.S3, _options, LoggerFactory).ProvisionAsync(AbortToken);
        _store = _CreateStore(_options);
        _raw = new TusS3RawObjects(_client, _options);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_store_length_offset_and_metadata_at_creation()
    {
        // given
        const string metadata = "filename dGVzdC5iaW4=,empty";

        // when
        var fileId = await _store.CreateFileAsync(10 * _MiB, metadata, AbortToken);

        // then
        (await _store.GetUploadLengthAsync(fileId, AbortToken))
            .Should()
            .Be(10 * _MiB);
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(0);
        (await _store.GetUploadMetadataAsync(fileId, AbortToken)).Should().Be(metadata);
        (await _store.FileExistAsync(fileId, AbortToken)).Should().BeTrue();

        var state = await _raw.ReadStateAsync(fileId, AbortToken);
        state!.PartSize.Should().Be((int)(8 * _MiB));
        state.Status.Should().Be(TusS3UploadStatus.Active);
        state.MultipartUploadId.Should().BeNull();
        (await _raw.GetSizeAsync(_raw.Keys.FinishedObject(fileId), AbortToken)).Should().BeNull();
    }

    public virtual async Task should_round_trip_the_largest_metadata_the_azure_store_accepts()
    {
        // given - 7,168 characters: a three-letter key, a space, and 7,164 base64 characters
        var metadata = "abc " + Convert.ToBase64String(Faker.Random.Bytes(5_373));
        metadata.Should().HaveLength(7 * 1024);

        // when
        var fileId = await _store.CreateFileAsync(100, metadata, AbortToken);

        // then
        (await _store.GetUploadMetadataAsync(fileId, AbortToken))
            .Should()
            .Be(metadata);
    }

    public virtual async Task should_reject_malformed_metadata_at_creation()
    {
        var act = () => _store.CreateFileAsync(100, "filename not-base64!", AbortToken);

        await act.Should().ThrowAsync<TusStoreException>();
    }

    public virtual async Task should_complete_a_zero_length_upload_at_creation()
    {
        // given
        const string metadata = "filename ZW1wdHkudHh0";

        // when
        var fileId = await _store.CreateFileAsync(0, metadata, AbortToken);

        // then - an empty finished object and a completed record exist before any PATCH, and no state object
        (await _raw.GetSizeAsync(_raw.Keys.FinishedObject(fileId), AbortToken))
            .Should()
            .Be(0);
        (await _raw.ReadStateAsync(fileId, AbortToken)).Should().BeNull();
        var record = await _raw.ReadCompletedRecordAsync(fileId, AbortToken);
        record!.UploadLength.Should().Be(0);
        record.Metadata.Should().Be(metadata);

        (await _store.FileExistAsync(fileId, AbortToken)).Should().BeTrue();
        (await _store.GetUploadLengthAsync(fileId, AbortToken)).Should().Be(0);
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(0);
        (await _store.GetUploadMetadataAsync(fileId, AbortToken)).Should().Be(metadata);
        (await _store.GetUploadConcatAsync(fileId, AbortToken)).Should().BeNull();

        // and a completed upload keeps no expiry
        await _store.SetExpirationAsync(fileId, DateTimeOffset.UtcNow.AddMinutes(-5), AbortToken);
        (await _store.GetExpirationAsync(fileId, AbortToken)).Should().BeNull();
    }

    public virtual async Task should_complete_a_zero_length_partial_upload_at_creation()
    {
        // when
        var fileId = await _store.CreatePartialFileAsync(0, metadata: null, AbortToken);

        // then
        (await _raw.GetSizeAsync(_raw.Keys.FinishedObject(fileId), AbortToken))
            .Should()
            .Be(0);
        (await _store.GetUploadConcatAsync(fileId, AbortToken)).Should().BeOfType<FileConcatPartial>();
    }

    public virtual async Task should_reject_a_known_length_needing_parts_above_max_part_size()
    {
        // given - one byte more than 9,744 parts of MaxPartSize (64 MiB) hold
        const long length = (_DefaultPartBudget * 64 * _MiB) + 1;

        // when
        var act = () => _store.CreateFileAsync(length, metadata: null, AbortToken);

        // then
        await act.Should().ThrowAsync<TusStoreException>().WithMessage("*too large*");
    }

    public virtual async Task should_size_parts_of_a_large_upload_to_fit_the_budget()
    {
        // when - 200 GiB needs about 21.02 MiB per part to fit 9,744 parts
        var fileId = await _store.CreateFileAsync(200 * 1024 * _MiB, metadata: null, AbortToken);

        // then
        (await _raw.ReadStateAsync(fileId, AbortToken))!
            .PartSize.Should()
            .Be((int)(22 * _MiB));
    }

    public virtual async Task should_report_a_deferred_length_until_it_is_declared()
    {
        // given
        var fileId = await _store.CreateFileAsync(-1, metadata: null, AbortToken);
        (await _store.GetUploadLengthAsync(fileId, AbortToken)).Should().BeNull();

        // when
        await _store.SetUploadLengthAsync(fileId, 5_000, AbortToken);

        // then
        (await _store.GetUploadLengthAsync(fileId, AbortToken))
            .Should()
            .Be(5_000);
        (await _raw.ReadStateAsync(fileId, AbortToken))!.Status.Should().Be(TusS3UploadStatus.Active);
    }

    public virtual async Task should_reject_a_declared_length_below_the_offset()
    {
        // given
        var fileId = await _store.CreateFileAsync(-1, metadata: null, AbortToken);
        await _raw.RewriteStateAsync(fileId, state => state.Offset = 1_000, AbortToken);

        // when
        var act = () => _store.SetUploadLengthAsync(fileId, 999, AbortToken);

        // then
        await act.Should().ThrowAsync<TusStoreException>();
        (await _store.GetUploadLengthAsync(fileId, AbortToken)).Should().BeNull();
    }

    public virtual async Task should_reject_a_declared_length_above_the_deferred_cap()
    {
        // given - 9,744 parts of the default 8 MiB part size
        const long cap = _DefaultPartBudget * 8 * _MiB;
        var fileId = await _store.CreateFileAsync(-1, metadata: null, AbortToken);

        // when
        var act = () => _store.SetUploadLengthAsync(fileId, cap + 1, AbortToken);

        // then
        await act.Should().ThrowAsync<TusStoreException>().WithMessage($"*{cap}-byte limit*");
        (await _store.GetUploadLengthAsync(fileId, AbortToken)).Should().BeNull();

        // and the cap itself is accepted
        await _store.SetUploadLengthAsync(fileId, cap, AbortToken);
        (await _store.GetUploadLengthAsync(fileId, AbortToken)).Should().Be(cap);
    }

    public virtual async Task should_finish_on_a_redeclared_length_when_completing_from_a_declared_length_failed()
    {
        // given - a deferred upload holding 1,000 bytes, whose finished object cannot be written yet
        var content = Faker.Random.Bytes(1_000);
        var fileId = await _store.CreateFileAsync(-1, metadata: null, AbortToken);
        await using (var body = new MemoryStream(content))
        {
            await _store.AppendDataAsync(fileId, body, AbortToken);
        }

        var expires = DateTimeOffset.UtcNow.AddMinutes(30);
        await _store.SetExpirationAsync(fileId, expires, AbortToken);
        var finishedObjectPath = "/" + _raw.Keys.FinishedObject(fileId);
        _client.Gate.FailWhen(request =>
            string.Equals(S3Operation.Of(request), S3Operation.PutObject, StringComparison.Ordinal)
            && request.RequestUri!.AbsolutePath.EndsWith(finishedObjectPath, StringComparison.Ordinal)
        );

        // when - the client declares a length equal to the offset
        var declare = () => _store.SetUploadLengthAsync(fileId, content.Length, AbortToken);

        // then - the declaration fails with a retryable error after one write recorded the length and the Completing
        // status together
        await declare.Should().ThrowAsync<AmazonS3Exception>();
        var state = await _raw.ReadStateAsync(fileId, AbortToken);
        state!.Status.Should().Be(TusS3UploadStatus.Completing);
        state.UploadLength.Should().Be(content.Length);
        state.PreFinalOffset.Should().Be(content.Length);
        state.CompletingFromDeclaredLength.Should().BeTrue();
        state.ExpiresAtBeforeCompleting.Should().BeCloseTo(expires, TimeSpan.FromMilliseconds(1));

        // and the client still sees a deferred length at the same offset, with no expiry, so it declares again
        (await _store.GetUploadLengthAsync(fileId, AbortToken))
            .Should()
            .BeNull();
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);
        (await _store.GetExpirationAsync(fileId, AbortToken)).Should().BeNull();
        var different = () => _store.SetUploadLengthAsync(fileId, 2_000, AbortToken);
        await different.Should().ThrowAsync<TusStoreException>();

        // when - it declares the same length again once storage accepts the write
        _client.Gate.FailWhen(null);
        await _store.SetUploadLengthAsync(fileId, content.Length, AbortToken);

        // then - the upload is complete, with its bytes as one object and no working set left
        (await _store.GetUploadLengthAsync(fileId, AbortToken))
            .Should()
            .Be(content.Length);
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);
        (await _raw.ReadFinishedObjectAsync(fileId, AbortToken)).Should().Equal(content);
        (await _raw.ReadStateAsync(fileId, AbortToken)).Should().BeNull();
        (await _raw.ListSegmentKeysAsync(fileId, AbortToken)).Should().BeEmpty();
    }

    public virtual async Task should_report_an_upload_gone_once_its_multipart_upload_outlives_the_abort_window()
    {
        // given - a provider that aborts incomplete multipart uploads after seven days
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = new TusS3StoreOptions
        {
            Bucket = _options.Bucket,
            Prefix = _options.Prefix,
            Provider = _client.Provider,
            MultipartUploadLifetime = TimeSpan.FromDays(7),
        };
        using var host = await TusS3TestHost.StartAsync(_client, options, LoggerFactory, clock, AbortToken);
        var location = await host.CreateUploadAsync(100 * _MiB, metadata: null, AbortToken);
        await _raw.RewriteStateAsync(
            TusS3TestHost.FileIdOf(location),
            state =>
            {
                state.MultipartUploadId = "multipart-upload";
                state.MultipartUploadCreatedAt = clock.GetUtcNow();
            },
            AbortToken
        );

        using (var inside = await host.HeadAsync(location, AbortToken))
        {
            inside.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // when
        clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        using var after = await host.HeadAsync(location, AbortToken);

        // then - its parts are gone, so the upload is reported missing rather than failing later
        after.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public virtual async Task should_let_only_one_of_two_racing_creates_with_the_same_id_win()
    {
        // given - an id provider that hands both requests the same id
        var store = _CreateStore(_options, new FixedFileIdProvider(Guid.NewGuid().ToString("N")));

        // when
        Task<string>[] creates =
        [
            store.CreateFileAsync(100, metadata: null, AbortToken),
            store.CreateFileAsync(200, metadata: null, AbortToken),
        ];
        var act = () => Task.WhenAll(creates);

        // then - If-None-Match lets exactly one write the state; the other fails without overwriting it
        var failure = (await act.Should().ThrowAsync<AmazonS3Exception>()).Which;
        failure.StatusCode.Should().BeOneOf(HttpStatusCode.PreconditionFailed, HttpStatusCode.Conflict);
        creates.Count(task => task.IsCompletedSuccessfully).Should().Be(1);
    }

    public virtual async Task should_reject_a_stale_state_write_without_overwriting_the_winner()
    {
        // given - two stores on separate clients, so one writer can be held after it read the state
        using var otherClient = backend.CreateClient();
        var stale = _CreateStore(_options, client: otherClient);
        var fileId = await _store.CreateFileAsync(-1, metadata: null, AbortToken);
        otherClient.Gate.Arm(S3Operation.PutObject, "/info");

        var staleWrite = stale.SetUploadLengthAsync(fileId, 500, AbortToken);
        await otherClient.Gate.WaitForArrivalAsync(AbortToken);

        // when - another request writes first, then the held writer proceeds with its old ETag
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);
        await _store.SetExpirationAsync(fileId, expires, AbortToken);
        otherClient.Gate.Disarm();
        otherClient.Gate.ReleaseOne();
        var act = () => staleWrite;

        // then - the stale write surfaces untranslated (tusdotnet answers 500, which clients retry)
        var exception = (await act.Should().ThrowAsync<AmazonS3Exception>()).Which;
        TusS3StateStore.IsStaleWrite(exception).Should().BeTrue();
        exception.Should().NotBeAssignableTo<TusStoreException>();

        var state = await _raw.ReadStateAsync(fileId, AbortToken);
        state!.UploadLength.Should().BeNull();
        state.ExpiresAt.Should().BeCloseTo(expires, TimeSpan.FromMilliseconds(1));
    }

    public virtual async Task should_reject_an_invalid_file_id_at_every_read()
    {
        var act = () => _store.FileExistAsync("a/b", AbortToken);

        await act.Should().ThrowAsync<TusStoreException>();
    }

    private TusS3Store _CreateStore(
        TusS3StoreOptions options,
        ITusFileIdProvider? fileIdProvider = null,
        S3TestClient? client = null
    )
    {
        return new TusS3Store(
            (client ?? _client).S3,
            options,
            fileIdProvider: fileIdProvider,
            loggerFactory: LoggerFactory
        );
    }

    private sealed class FixedFileIdProvider(string fileId) : ITusFileIdProvider
    {
        public Task<string> CreateId(string metadata) => Task.FromResult(fileId);

        public Task<bool> ValidateId(string fileId) => Task.FromResult(true);
    }
}

[Collection<TusS3MinioFixture>]
public sealed class MinioTusS3StoreTests(TusS3MinioFixture fixture) : TusS3StoreTestsBase(fixture)
{
    [Fact]
    public override Task should_store_length_offset_and_metadata_at_creation() =>
        base.should_store_length_offset_and_metadata_at_creation();

    [Fact]
    public override Task should_round_trip_the_largest_metadata_the_azure_store_accepts() =>
        base.should_round_trip_the_largest_metadata_the_azure_store_accepts();

    [Fact]
    public override Task should_reject_malformed_metadata_at_creation() =>
        base.should_reject_malformed_metadata_at_creation();

    [Fact]
    public override Task should_complete_a_zero_length_upload_at_creation() =>
        base.should_complete_a_zero_length_upload_at_creation();

    [Fact]
    public override Task should_complete_a_zero_length_partial_upload_at_creation() =>
        base.should_complete_a_zero_length_partial_upload_at_creation();

    [Fact]
    public override Task should_reject_a_known_length_needing_parts_above_max_part_size() =>
        base.should_reject_a_known_length_needing_parts_above_max_part_size();

    [Fact]
    public override Task should_size_parts_of_a_large_upload_to_fit_the_budget() =>
        base.should_size_parts_of_a_large_upload_to_fit_the_budget();

    [Fact]
    public override Task should_report_a_deferred_length_until_it_is_declared() =>
        base.should_report_a_deferred_length_until_it_is_declared();

    [Fact]
    public override Task should_reject_a_declared_length_below_the_offset() =>
        base.should_reject_a_declared_length_below_the_offset();

    [Fact]
    public override Task should_reject_a_declared_length_above_the_deferred_cap() =>
        base.should_reject_a_declared_length_above_the_deferred_cap();

    [Fact]
    public override Task should_finish_on_a_redeclared_length_when_completing_from_a_declared_length_failed() =>
        base.should_finish_on_a_redeclared_length_when_completing_from_a_declared_length_failed();

    [Fact]
    public override Task should_report_an_upload_gone_once_its_multipart_upload_outlives_the_abort_window() =>
        base.should_report_an_upload_gone_once_its_multipart_upload_outlives_the_abort_window();

    [Fact]
    public override Task should_let_only_one_of_two_racing_creates_with_the_same_id_win() =>
        base.should_let_only_one_of_two_racing_creates_with_the_same_id_win();

    [Fact]
    public override Task should_reject_a_stale_state_write_without_overwriting_the_winner() =>
        base.should_reject_a_stale_state_write_without_overwriting_the_winner();

    [Fact]
    public override Task should_reject_an_invalid_file_id_at_every_read() =>
        base.should_reject_an_invalid_file_id_at_every_read();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackTusS3StoreTests(TusS3LocalStackFixture fixture) : TusS3StoreTestsBase(fixture)
{
    [Fact]
    public override Task should_store_length_offset_and_metadata_at_creation() =>
        base.should_store_length_offset_and_metadata_at_creation();

    [Fact]
    public override Task should_round_trip_the_largest_metadata_the_azure_store_accepts() =>
        base.should_round_trip_the_largest_metadata_the_azure_store_accepts();

    [Fact]
    public override Task should_reject_malformed_metadata_at_creation() =>
        base.should_reject_malformed_metadata_at_creation();

    [Fact]
    public override Task should_complete_a_zero_length_upload_at_creation() =>
        base.should_complete_a_zero_length_upload_at_creation();

    [Fact]
    public override Task should_complete_a_zero_length_partial_upload_at_creation() =>
        base.should_complete_a_zero_length_partial_upload_at_creation();

    [Fact]
    public override Task should_reject_a_known_length_needing_parts_above_max_part_size() =>
        base.should_reject_a_known_length_needing_parts_above_max_part_size();

    [Fact]
    public override Task should_size_parts_of_a_large_upload_to_fit_the_budget() =>
        base.should_size_parts_of_a_large_upload_to_fit_the_budget();

    [Fact]
    public override Task should_report_a_deferred_length_until_it_is_declared() =>
        base.should_report_a_deferred_length_until_it_is_declared();

    [Fact]
    public override Task should_reject_a_declared_length_below_the_offset() =>
        base.should_reject_a_declared_length_below_the_offset();

    [Fact]
    public override Task should_reject_a_declared_length_above_the_deferred_cap() =>
        base.should_reject_a_declared_length_above_the_deferred_cap();

    [Fact]
    public override Task should_finish_on_a_redeclared_length_when_completing_from_a_declared_length_failed() =>
        base.should_finish_on_a_redeclared_length_when_completing_from_a_declared_length_failed();

    [Fact]
    public override Task should_report_an_upload_gone_once_its_multipart_upload_outlives_the_abort_window() =>
        base.should_report_an_upload_gone_once_its_multipart_upload_outlives_the_abort_window();

    [Fact]
    public override Task should_let_only_one_of_two_racing_creates_with_the_same_id_win() =>
        base.should_let_only_one_of_two_racing_creates_with_the_same_id_win();

    [Fact]
    public override Task should_reject_a_stale_state_write_without_overwriting_the_winner() =>
        base.should_reject_a_stale_state_write_without_overwriting_the_winner();

    [Fact]
    public override Task should_reject_an_invalid_file_id_at_every_read() =>
        base.should_reject_an_invalid_file_id_at_every_read();
}
