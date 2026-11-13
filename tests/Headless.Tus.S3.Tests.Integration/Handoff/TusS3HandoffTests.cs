// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3.Model;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Microsoft.AspNetCore.Http;
using Tests.TestSetup;

namespace Tests.Handoff;

/// <summary>
/// The store's record that a completed upload was handed off to the application: when an upload reads as owing its
/// completion handler, what keeps the record, and what removes it.
/// </summary>
public abstract class TusS3HandoffTestsBase(IS3TestBackend backend) : TestBase
{
    private readonly S3TestClient _client = backend.CreateClient();

    // A prefix per test: the purge scenario removes everything under it.
    private readonly TusS3StoreOptions _options = new()
    {
        Bucket = TusS3StoreFactory.Bucket,
        Prefix = $"handoff-{Guid.NewGuid():N}/",
    };

    private TusS3RawObjects _raw = null!;
    private TusS3Store _store = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _options.Provider = _client.Provider;
        await new TusS3BucketProvisioner(_client.S3, _options, LoggerFactory).ProvisionAsync(AbortToken);
        _raw = new TusS3RawObjects(_client, _options);

        // No HTTP context accessor: each call reads storage itself, as requests on separate hosts would.
        _store = new TusS3Store(_client.S3, _options, loggerFactory: LoggerFactory);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _client.Gate.Disarm();
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_report_an_upload_as_pending_only_once_its_last_byte_is_stored()
    {
        // given
        var content = Faker.Random.Bytes(1_000);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);

        // when
        var created = await _store.GetHandoffStateAsync(fileId, AbortToken);
        await _AppendAsync(_store, fileId, content[..400]);
        var partial = await _store.GetHandoffStateAsync(fileId, AbortToken);
        await _AppendAsync(_store, fileId, content[400..]);
        var complete = await _store.GetHandoffStateAsync(fileId, AbortToken);
        var unknown = await _store.GetHandoffStateAsync(Guid.NewGuid().ToString("n"), AbortToken);

        // then
        created.Should().Be(TusHandoffState.NotComplete);
        partial.Should().Be(TusHandoffState.NotComplete);
        complete.Should().Be(TusHandoffState.Pending);
        unknown.Should().Be(TusHandoffState.NotComplete);
    }

    public virtual async Task should_report_an_empty_upload_as_pending_from_its_creation()
    {
        // when - an upload with no bytes is complete as soon as it exists
        var fileId = await _store.CreateFileAsync(0, metadata: null, AbortToken);

        // then
        (await _store.GetHandoffStateAsync(fileId, AbortToken))
            .Should()
            .Be(TusHandoffState.Pending);
    }

    public virtual async Task should_report_a_completed_upload_as_handed_off_once_marked_and_ignore_a_second_mark()
    {
        // given
        var fileId = await _CreateCompletedAsync();

        // when
        await _store.MarkHandedOffAsync(fileId, AbortToken);
        var marked = await _store.GetHandoffStateAsync(fileId, AbortToken);
        await _store.MarkHandedOffAsync(fileId, AbortToken);
        var markedAgain = await _store.GetHandoffStateAsync(fileId, AbortToken);

        // then
        marked.Should().Be(TusHandoffState.HandedOff);
        markedAgain.Should().Be(TusHandoffState.HandedOff);
        (await _ListKeysAsync(_options.Prefix + TusS3Keys.HandedOffFolder))
            .Should()
            .Equal(_raw.Keys.HandedOffMark(fileId));
    }

    public virtual async Task should_keep_the_mark_when_a_second_request_commits_the_same_completion_again()
    {
        // given - two requests that finish the same upload, both held at the write of its completed record
        var content = Faker.Random.Bytes(1_000);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);
        _client.Gate.Arm(S3Operation.PutObject, "/" + TusS3Keys.CompletedFolder + fileId);

        var first = _AppendAsync(_store, fileId, content);
        await _client.Gate.WaitForArrivalAsync(AbortToken);
        var second = _AppendAsync(_store, fileId, []);
        await _client.Gate.WaitForArrivalAsync(AbortToken);

        // and - one of them commits, and the upload it completed is handed off
        _client.Gate.ReleaseOne();
        var committed = await Task.WhenAny(first, second);
        await committed;
        await _store.MarkHandedOffAsync(fileId, AbortToken);

        // when - the other request writes the completed record again
        _client.Gate.Disarm();
        _client.Gate.ReleaseOne();
        await (committed == first ? second : first);

        // then
        (await _raw.ReadCompletedRecordAsync(fileId, AbortToken))
            .Should()
            .NotBeNull();
        (await _store.GetHandoffStateAsync(fileId, AbortToken)).Should().Be(TusHandoffState.HandedOff);
    }

    public virtual async Task should_read_the_state_from_storage_when_the_request_already_loaded_the_upload()
    {
        // given - a store inside an HTTP request, which keeps what it loaded for the rest of that request
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var requestStore = new TusS3Store(
            _client.S3,
            _options,
            loggerFactory: LoggerFactory,
            httpContextAccessor: accessor
        );
        var content = Faker.Random.Bytes(1_000);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);
        (await requestStore.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(0);

        // when - another request completes the upload and hands it off
        await _AppendAsync(_store, fileId, content);
        var pending = await requestStore.GetHandoffStateAsync(fileId, AbortToken);
        await _store.MarkHandedOffAsync(fileId, AbortToken);
        var handedOff = await requestStore.GetHandoffStateAsync(fileId, AbortToken);

        // then - the request still holds what it loaded first, and the handoff state did not come from it
        (await requestStore.GetUploadOffsetAsync(fileId, AbortToken))
            .Should()
            .Be(0);
        pending.Should().Be(TusHandoffState.Pending);
        handedOff.Should().Be(TusHandoffState.HandedOff);
    }

    public virtual async Task should_remove_the_mark_when_the_upload_is_terminated()
    {
        // given
        var fileId = await _CreateCompletedAsync();
        await _store.MarkHandedOffAsync(fileId, AbortToken);

        // when
        await _store.DeleteFileAsync(fileId, AbortToken);

        // then
        (await _ListKeysAsync(_options.Prefix))
            .Should()
            .BeEmpty();
        (await _store.GetHandoffStateAsync(fileId, AbortToken)).Should().Be(TusHandoffState.NotComplete);
    }

    public virtual async Task should_remove_the_marks_when_the_store_is_purged()
    {
        // given - a handed-off upload, a pending one, and one in progress
        var handedOff = await _CreateCompletedAsync();
        await _store.MarkHandedOffAsync(handedOff, AbortToken);
        await _CreateCompletedAsync();
        await _store.CreateFileAsync(1_000, metadata: null, AbortToken);

        // when
        await new TusS3BucketProvisioner(_client.S3, _options, LoggerFactory).PurgeAsync(AbortToken);

        // then
        (await _ListKeysAsync(_options.Prefix))
            .Should()
            .BeEmpty();
    }

    public virtual async Task should_write_no_mark_for_an_upload_that_no_longer_exists()
    {
        // given - the application deleted the upload it took over
        var fileId = await _CreateCompletedAsync();
        await _store.DeleteFileAsync(fileId, AbortToken);

        // when
        await _store.MarkHandedOffAsync(fileId, AbortToken);

        // then
        (await _ListKeysAsync(_options.Prefix))
            .Should()
            .BeEmpty();
    }

    public virtual async Task should_refuse_to_mark_an_upload_that_is_not_complete()
    {
        // given
        var fileId = await _store.CreateFileAsync(1_000, metadata: null, AbortToken);
        await _AppendAsync(_store, fileId, Faker.Random.Bytes(400));

        // when
        var act = () => _store.MarkHandedOffAsync(fileId, AbortToken);

        // then - a mark written now would make the upload read as handed off the moment it completes
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _ListKeysAsync(_options.Prefix + TusS3Keys.HandedOffFolder)).Should().BeEmpty();
    }

    private async Task<string> _CreateCompletedAsync()
    {
        var content = Faker.Random.Bytes(1_000);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);
        await _AppendAsync(_store, fileId, content);

        return fileId;
    }

    private static async Task<long> _AppendAsync(TusS3Store store, string fileId, byte[] body)
    {
        // Leave the test's thread first, so racing appends reach the gate before either is awaited.
        await Task.Yield();
        await using var stream = new MemoryStream(body, writable: false);

        return await store.AppendDataAsync(fileId, stream, AbortToken);
    }

    private async Task<List<string>> _ListKeysAsync(string prefix)
    {
        var response = await _client.S3.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = _options.Bucket, Prefix = prefix },
            AbortToken
        );

        return [.. (response.S3Objects ?? []).Select(o => o.Key)];
    }
}

[Collection<TusS3MinioFixture>]
public sealed class MinioTusS3HandoffTests(TusS3MinioFixture fixture) : TusS3HandoffTestsBase(fixture)
{
    [Fact]
    public override Task should_report_an_upload_as_pending_only_once_its_last_byte_is_stored() =>
        base.should_report_an_upload_as_pending_only_once_its_last_byte_is_stored();

    [Fact]
    public override Task should_report_an_empty_upload_as_pending_from_its_creation() =>
        base.should_report_an_empty_upload_as_pending_from_its_creation();

    [Fact]
    public override Task should_report_a_completed_upload_as_handed_off_once_marked_and_ignore_a_second_mark() =>
        base.should_report_a_completed_upload_as_handed_off_once_marked_and_ignore_a_second_mark();

    [Fact]
    public override Task should_keep_the_mark_when_a_second_request_commits_the_same_completion_again() =>
        base.should_keep_the_mark_when_a_second_request_commits_the_same_completion_again();

    [Fact]
    public override Task should_read_the_state_from_storage_when_the_request_already_loaded_the_upload() =>
        base.should_read_the_state_from_storage_when_the_request_already_loaded_the_upload();

    [Fact]
    public override Task should_remove_the_mark_when_the_upload_is_terminated() =>
        base.should_remove_the_mark_when_the_upload_is_terminated();

    [Fact]
    public override Task should_remove_the_marks_when_the_store_is_purged() =>
        base.should_remove_the_marks_when_the_store_is_purged();

    [Fact]
    public override Task should_write_no_mark_for_an_upload_that_no_longer_exists() =>
        base.should_write_no_mark_for_an_upload_that_no_longer_exists();

    [Fact]
    public override Task should_refuse_to_mark_an_upload_that_is_not_complete() =>
        base.should_refuse_to_mark_an_upload_that_is_not_complete();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackTusS3HandoffTests(TusS3LocalStackFixture fixture) : TusS3HandoffTestsBase(fixture)
{
    [Fact]
    public override Task should_report_an_upload_as_pending_only_once_its_last_byte_is_stored() =>
        base.should_report_an_upload_as_pending_only_once_its_last_byte_is_stored();

    [Fact]
    public override Task should_report_an_empty_upload_as_pending_from_its_creation() =>
        base.should_report_an_empty_upload_as_pending_from_its_creation();

    [Fact]
    public override Task should_report_a_completed_upload_as_handed_off_once_marked_and_ignore_a_second_mark() =>
        base.should_report_a_completed_upload_as_handed_off_once_marked_and_ignore_a_second_mark();

    [Fact]
    public override Task should_keep_the_mark_when_a_second_request_commits_the_same_completion_again() =>
        base.should_keep_the_mark_when_a_second_request_commits_the_same_completion_again();

    [Fact]
    public override Task should_read_the_state_from_storage_when_the_request_already_loaded_the_upload() =>
        base.should_read_the_state_from_storage_when_the_request_already_loaded_the_upload();

    [Fact]
    public override Task should_remove_the_mark_when_the_upload_is_terminated() =>
        base.should_remove_the_mark_when_the_upload_is_terminated();

    [Fact]
    public override Task should_remove_the_marks_when_the_store_is_purged() =>
        base.should_remove_the_marks_when_the_store_is_purged();

    [Fact]
    public override Task should_write_no_mark_for_an_upload_that_no_longer_exists() =>
        base.should_write_no_mark_for_an_upload_that_no_longer_exists();

    [Fact]
    public override Task should_refuse_to_mark_an_upload_that_is_not_complete() =>
        base.should_refuse_to_mark_an_upload_that_is_not_complete();
}
