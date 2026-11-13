// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Tests.TestSetup;
using TusAzureMetadata = Headless.Tus.Models.TusAzureMetadata;

namespace Tests.Handoff;

/// <summary>
/// The store's record that a complete upload was handed off to the application: when an upload reads as owing its
/// completion handler, what keeps the record, and what removes it.
/// </summary>
[Collection<TusAzureFixture>]
public sealed class TusAzureHandoffTests : TestBase
{
    private readonly BlobWriteGate _metadataGate = new(RequestMethod.Put, "metadata");
    private readonly TusAzureStoreOptions _options;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly TusAzureStore _store;
    private readonly TusAzureStore _gatedStore;

    public TusAzureHandoffTests(TusAzureFixture fixture)
    {
        // A prefix per test: the purge scenario removes everything under it.
        _options = new TusAzureStoreOptions
        {
            ContainerName = TusAzureFixture.ContainerName,
            BlobPrefix = $"handoff-{Guid.NewGuid():N}/",
        };

        _blobServiceClient = fixture.CreateBlobServiceClient();
        _store = new TusAzureStore(_blobServiceClient, _options, loggerFactory: LoggerFactory);

        var gatedOptions = new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04);
        gatedOptions.AddPolicy(_metadataGate, HttpPipelinePosition.PerCall);
        _gatedStore = new TusAzureStore(
            new BlobServiceClient(fixture.Container.GetConnectionString(), gatedOptions),
            _options,
            loggerFactory: LoggerFactory
        );
    }

    private BlobContainerClient _Container => _blobServiceClient.GetBlobContainerClient(_options.ContainerName);

    // The store does not create its container; the deploy-time provisioner does.
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await new TusAzureContainerProvisioner(_blobServiceClient, _options).ProvisionAsync(AbortToken);
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _metadataGate.Dispose();

        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_report_an_upload_as_pending_only_once_its_last_byte_is_stored()
    {
        // given
        var content = Faker.Random.Bytes(1_000);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);
        var deferred = await _store.CreateFileAsync(-1, metadata: null, AbortToken);

        // when
        var created = await _store.GetHandoffStateAsync(fileId, AbortToken);
        await _AppendAsync(fileId, content[..400]);
        var partial = await _store.GetHandoffStateAsync(fileId, AbortToken);
        await _AppendAsync(fileId, content[400..]);
        var complete = await _store.GetHandoffStateAsync(fileId, AbortToken);
        var withoutLength = await _store.GetHandoffStateAsync(deferred, AbortToken);
        var unknown = await _store.GetHandoffStateAsync(Guid.NewGuid().ToString("n"), AbortToken);

        // then
        created.Should().Be(TusHandoffState.NotComplete);
        partial.Should().Be(TusHandoffState.NotComplete);
        complete.Should().Be(TusHandoffState.Pending);
        withoutLength.Should().Be(TusHandoffState.NotComplete);
        unknown.Should().Be(TusHandoffState.NotComplete);
    }

    [Fact]
    public async Task should_report_an_empty_upload_as_pending_from_its_creation()
    {
        // when - an upload with no bytes is complete as soon as it exists
        var fileId = await _store.CreateFileAsync(0, metadata: null, AbortToken);

        // then
        (await _store.GetHandoffStateAsync(fileId, AbortToken))
            .Should()
            .Be(TusHandoffState.Pending);
    }

    [Fact]
    public async Task should_report_a_complete_upload_as_handed_off_once_marked_and_ignore_a_second_mark()
    {
        // given
        var fileId = await _CreateCompleteAsync();

        // when
        await _store.MarkHandedOffAsync(fileId, AbortToken);
        var marked = await _GetPropertiesAsync(fileId);
        var state = await _store.GetHandoffStateAsync(fileId, AbortToken);
        await _store.MarkHandedOffAsync(fileId, AbortToken);
        var markedAgain = await _GetPropertiesAsync(fileId);

        // then - the second mark wrote nothing
        state.Should().Be(TusHandoffState.HandedOff);
        marked.Metadata.Should().ContainKey(TusAzureMetadata.HandedOffKey);
        markedAgain.ETag.Should().Be(marked.ETag);
        (await _store.GetHandoffStateAsync(fileId, AbortToken)).Should().Be(TusHandoffState.HandedOff);
    }

    [Fact]
    public async Task should_keep_the_mark_when_the_completing_request_is_sent_again()
    {
        // given
        var metadata = $"filename {"report.pdf".ToBase64()}";
        var content = Faker.Random.Bytes(1_000);
        var fileId = await _store.CreateFileAsync(content.Length, metadata, AbortToken);
        await _AppendAsync(fileId, content);
        await _store.MarkHandedOffAsync(fileId, AbortToken);

        // when - the store's writes for a final request that is sent again: no new bytes, and a refreshed expiration
        await _AppendAsync(fileId, []);
        await _store.SetExpirationAsync(fileId, DateTimeOffset.UtcNow.AddHours(1), AbortToken);

        // then
        (await _store.GetHandoffStateAsync(fileId, AbortToken))
            .Should()
            .Be(TusHandoffState.HandedOff);
        (await _store.GetUploadMetadataAsync(fileId, AbortToken)).Should().Be(metadata);
        (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);
    }

    [Fact]
    public async Task should_not_overwrite_a_write_that_lands_between_reading_the_upload_and_marking_it()
    {
        // given - a mark that has read the blob and is held at its write
        var fileId = await _CreateCompleteAsync();
        var expires = DateTimeOffset.UtcNow.AddHours(1);
        _metadataGate.Arm(fileId);
        var mark = _gatedStore.MarkHandedOffAsync(fileId, AbortToken);
        await _metadataGate.WaitForArrivalAsync(AbortToken);

        // when - another request writes the blob's metadata first
        await _store.SetExpirationAsync(fileId, expires, AbortToken);
        _metadataGate.Disarm();
        _metadataGate.ReleaseOne();

        // then - the mark is refused instead of replacing that write with what it read before it
        var failure = (await FluentActions.Awaiting(() => mark).Should().ThrowAsync<RequestFailedException>()).Which;
        failure.Status.Should().Be(412);
        (await _store.GetHandoffStateAsync(fileId, AbortToken)).Should().Be(TusHandoffState.Pending);
        TusAzureMetadata.FromAzure((await _GetPropertiesAsync(fileId)).Metadata).ExpiresAt.Should().Be(expires);

        // when - the upload is marked again, as the next attempt to hand it off does
        await _gatedStore.MarkHandedOffAsync(fileId, AbortToken);

        // then
        (await _store.GetHandoffStateAsync(fileId, AbortToken))
            .Should()
            .Be(TusHandoffState.HandedOff);
    }

    [Fact]
    public async Task should_remove_the_mark_when_the_upload_is_terminated()
    {
        // given
        var fileId = await _CreateCompleteAsync();
        await _store.MarkHandedOffAsync(fileId, AbortToken);

        // when
        await _store.DeleteFileAsync(fileId, AbortToken);

        // then - the mark is part of the blob
        (await _ListBlobNamesAsync())
            .Should()
            .BeEmpty();
        (await _store.GetHandoffStateAsync(fileId, AbortToken)).Should().Be(TusHandoffState.NotComplete);
    }

    [Fact]
    public async Task should_remove_the_marks_when_the_store_is_purged()
    {
        // given - a handed-off upload, a pending one, and one in progress
        var handedOff = await _CreateCompleteAsync();
        await _store.MarkHandedOffAsync(handedOff, AbortToken);
        await _CreateCompleteAsync();
        await _store.CreateFileAsync(1_000, metadata: null, AbortToken);

        // when
        await new TusAzureContainerProvisioner(_blobServiceClient, _options).PurgeAsync(AbortToken);

        // then
        (await _ListBlobNamesAsync())
            .Should()
            .BeEmpty();
        (await _store.GetHandoffStateAsync(handedOff, AbortToken)).Should().Be(TusHandoffState.NotComplete);
    }

    [Fact]
    public async Task should_do_nothing_when_marking_an_upload_that_no_longer_exists()
    {
        // given - the application deleted the upload it took over
        var fileId = await _CreateCompleteAsync();
        await _store.DeleteFileAsync(fileId, AbortToken);

        // when
        var act = () => _store.MarkHandedOffAsync(fileId, AbortToken);

        // then
        await act.Should().NotThrowAsync();
        (await _ListBlobNamesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_to_mark_an_upload_that_is_not_complete()
    {
        // given
        var fileId = await _store.CreateFileAsync(1_000, metadata: null, AbortToken);
        await _AppendAsync(fileId, Faker.Random.Bytes(400));

        // when
        var act = () => _store.MarkHandedOffAsync(fileId, AbortToken);

        // then - a mark written now would make the upload read as handed off the moment it completes
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _GetPropertiesAsync(fileId)).Metadata.Should().NotContainKey(TusAzureMetadata.HandedOffKey);
    }

    private async Task<string> _CreateCompleteAsync()
    {
        var content = Faker.Random.Bytes(1_000);
        var fileId = await _store.CreateFileAsync(content.Length, metadata: null, AbortToken);
        await _AppendAsync(fileId, content);

        return fileId;
    }

    private async Task _AppendAsync(string fileId, byte[] body)
    {
        await using var stream = new MemoryStream(body, writable: false);

        await _store.AppendDataAsync(fileId, stream, AbortToken);
    }

    private async Task<BlobProperties> _GetPropertiesAsync(string fileId)
    {
        var response = await _Container
            .GetBlobClient(_options.BlobPrefix + fileId)
            .GetPropertiesAsync(cancellationToken: AbortToken);

        return response.Value;
    }

    private async Task<List<string>> _ListBlobNamesAsync()
    {
        var names = new List<string>();

        await foreach (
            var blob in _Container.GetBlobsAsync(
                traits: BlobTraits.None,
                states: BlobStates.None,
                prefix: _options.BlobPrefix,
                cancellationToken: AbortToken
            )
        )
        {
            names.Add(blob.Name);
        }

        return names;
    }
}
