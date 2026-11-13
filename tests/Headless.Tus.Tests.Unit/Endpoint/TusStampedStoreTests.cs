// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Pipelines;
using System.Security.Cryptography;
using Headless.Testing.Tests;
using Headless.Tus.Internal;
using tusdotnet.Models;
using tusdotnet.Models.Concatenation;
using tusdotnet.Stores;

namespace Tests.Endpoint;

public sealed class TusStampedStoreTests : TestBase
{
    private const string _ClientMetadata = "filename cmVwb3J0LnBkZg==,draft";

    private static readonly TusUploadStamp _Caller = new("videos", "alice", "acme");

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("headless-tus-");
    private readonly TusDiskStore _inner;
    private readonly TusStampedStore _sut;

    public TusStampedStoreTests()
    {
        _inner = new TusDiskStore(_directory.FullName);
        _sut = new TusStampedStore(_inner, _Caller);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _directory.Delete(recursive: true);

        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_write_the_callers_stamp_after_the_clients_metadata()
    {
        // when
        var fileId = await _sut.CreateFileAsync(10, _ClientMetadata, AbortToken);

        // then
        var stored = await _inner.GetUploadMetadataAsync(fileId, AbortToken);
        stored.Should().StartWith($"{_ClientMetadata},");
        TusEndpointTestHost
            .DecodeMetadata(stored)
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["filename"] = "report.pdf",
                    ["draft"] = "",
                    ["headless-endpoint"] = "videos",
                    ["headless-owner"] = "alice",
                    ["headless-tenant"] = "acme",
                }
            );
    }

    [Fact]
    public async Task should_return_the_clients_metadata_without_the_stamp()
    {
        // given
        var fileId = await _sut.CreateFileAsync(10, _ClientMetadata, AbortToken);

        // when
        var metadata = await _sut.GetUploadMetadataAsync(fileId, AbortToken);
        var file = await _sut.GetFileAsync(fileId, AbortToken);
        var fileMetadata = await file!.GetMetadataAsync(AbortToken);

        // then
        metadata.Should().Be(_ClientMetadata);
        file.Id.Should().Be(fileId);
        fileMetadata.Keys.Should().BeEquivalentTo("filename", "draft");
        fileMetadata["filename"].GetString(Encoding.UTF8).Should().Be("report.pdf");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task should_return_no_metadata_when_the_client_sent_none(string? clientMetadata)
    {
        // given
        var fileId = await _sut.CreateFileAsync(10, clientMetadata, AbortToken);

        // when
        var metadata = await _sut.GetUploadMetadataAsync(fileId, AbortToken);
        var file = await _sut.GetFileAsync(fileId, AbortToken);

        // then
        metadata.Should().BeNull();
        (await file!.GetMetadataAsync(AbortToken)).Should().BeEmpty();
        TusEndpointTestHost
            .DecodeMetadata(await _inner.GetUploadMetadataAsync(fileId, AbortToken))
            .Keys.Should()
            .BeEquivalentTo("headless-endpoint", "headless-owner", "headless-tenant");
    }

    [Fact]
    public async Task should_keep_the_stored_metadata_parseable_when_the_clients_ends_with_a_comma()
    {
        // given - a trailing comma is legal in Upload-Metadata, and a second one after it is not
        var fileId = await _sut.CreateFileAsync(10, "filename cmVwb3J0LnBkZg==,", AbortToken);

        // when
        var file = await _sut.GetFileAsync(fileId, AbortToken);

        // then
        (await file!.GetMetadataAsync(AbortToken))
            .Keys.Should()
            .BeEquivalentTo("filename");
        (await _sut.GetUploadMetadataAsync(fileId, AbortToken)).Should().Be("filename cmVwb3J0LnBkZg==");
    }

    [Fact]
    public async Task should_write_only_the_owner_for_the_default_endpoint_with_no_tenant()
    {
        // given - the default endpoint's name is empty
        var sut = new TusStampedStore(_inner, new TusUploadStamp(string.Empty, "alice", TenantId: null));

        // when
        var fileId = await sut.CreateFileAsync(10, metadata: null, AbortToken);

        // then - no entry without a value, which a strict metadata parser would reject
        (await _inner.GetUploadMetadataAsync(fileId, AbortToken))
            .Should()
            .Be("headless-owner YWxpY2U=");
    }

    [Theory]
    [InlineData("headless-owner Ym9i", "headless-owner")]
    [InlineData("filename cmVwb3J0LnBkZg==,HEADLESS-x", "HEADLESS-x")]
    public async Task should_refuse_a_client_key_that_uses_the_reserved_prefix(string clientMetadata, string key)
    {
        // when
        var create = () => _sut.CreateFileAsync(10, clientMetadata, AbortToken);
        var createPartial = () => _sut.CreatePartialFileAsync(10, clientMetadata, AbortToken);

        // then - tusdotnet answers a store exception with 400 and its message
        (await create.Should().ThrowAsync<TusStoreException>()).WithMessage($"*\"{key}\"*");
        await createPartial.Should().ThrowAsync<TusStoreException>();
        Directory.EnumerateFileSystemEntries(_directory.FullName).Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_create_an_upload_for_a_request_that_has_no_owner()
    {
        // given
        var sut = new TusStampedStore(_inner, caller: null);

        // when
        var create = () => sut.CreateFileAsync(10, _ClientMetadata, AbortToken);

        // then
        await create.Should().ThrowAsync<InvalidOperationException>();
        Directory.EnumerateFileSystemEntries(_directory.FullName).Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_no_file_for_an_upload_that_does_not_exist()
    {
        // when
        var file = await _sut.GetFileAsync(Guid.NewGuid().ToString("n"), AbortToken);

        // then
        file.Should().BeNull();
    }

    [Fact]
    public async Task should_write_and_read_an_upload_through_the_wrapped_store()
    {
        // given - an upload whose length is set after it is created
        var content = "hello world"u8.ToArray();
        var fileId = await _sut.CreateFileAsync(-1, _ClientMetadata, AbortToken);
        await _sut.SetUploadLengthAsync(fileId, content.Length, AbortToken);

        // when - the first bytes from a stream, the rest from a pipe
        await using var first = new MemoryStream(content[..5]);
        var afterStream = await _sut.AppendDataAsync(fileId, first, AbortToken);
        await using var second = new MemoryStream(content[5..]);
        var afterPipe = await _sut.AppendDataAsync(fileId, PipeReader.Create(second), AbortToken);

        // then
        afterStream.Should().Be(5);
        afterPipe.Should().Be(6);
        (await _sut.FileExistAsync(fileId, AbortToken)).Should().BeTrue();
        (await _sut.GetUploadLengthAsync(fileId, AbortToken)).Should().Be(content.Length);
        (await _sut.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);

        var file = await _sut.GetFileAsync(fileId, AbortToken);
        await using var stored = await file!.GetContentAsync(AbortToken);
        await using var buffer = new MemoryStream();
        await stored.CopyToAsync(buffer, AbortToken);
        buffer.ToArray().Should().Equal(content);
    }

    [Fact]
    public async Task should_delete_an_upload_from_the_wrapped_store()
    {
        // given
        var fileId = await _sut.CreateFileAsync(10, _ClientMetadata, AbortToken);

        // when
        await _sut.DeleteFileAsync(fileId, AbortToken);

        // then
        (await _inner.FileExistAsync(fileId, AbortToken))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_keep_and_apply_an_uploads_expiration_in_the_wrapped_store()
    {
        // given
        var fileId = await _sut.CreateFileAsync(10, _ClientMetadata, AbortToken);
        var expired = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // when
        await _sut.SetExpirationAsync(fileId, expired, AbortToken);

        // then
        (await _sut.GetExpirationAsync(fileId, AbortToken))
            .Should()
            .Be(expired);
        (await _inner.GetExpirationAsync(fileId, AbortToken)).Should().Be(expired);
        (await _sut.GetExpiredFilesAsync(AbortToken)).Should().Equal(fileId);
        (await _sut.RemoveExpiredFilesAsync(AbortToken)).Should().Be(1);
        (await _inner.FileExistAsync(fileId, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_verify_a_checksum_in_the_wrapped_store()
    {
        // given
        var content = "hello world"u8.ToArray();
        var fileId = await _sut.CreateFileAsync(content.Length, _ClientMetadata, AbortToken);
        await using var stream = new MemoryStream(content);
        await _sut.AppendDataAsync(fileId, stream, AbortToken);

        // when
        var algorithms = await _sut.GetSupportedAlgorithmsAsync(AbortToken);
#pragma warning disable CA5350 // Not a security use: sha1 is the checksum algorithm tus clients send and the disk store verifies.
        var matches = await _sut.VerifyChecksumAsync(fileId, "sha1", SHA1.HashData(content), AbortToken);
#pragma warning restore CA5350

        // then
        algorithms.Should().Contain("sha1");
        matches.Should().BeTrue();
    }

    [Fact]
    public async Task should_stamp_the_parts_and_the_result_of_a_concatenation()
    {
        // given
        var first = await _sut.CreatePartialFileAsync(5, _ClientMetadata, AbortToken);
        var second = await _sut.CreatePartialFileAsync(6, metadata: null, AbortToken);
        await using var firstBytes = new MemoryStream("hello"u8.ToArray());
        await _sut.AppendDataAsync(first, firstBytes, AbortToken);
        await using var secondBytes = new MemoryStream(" world"u8.ToArray());
        await _sut.AppendDataAsync(second, secondBytes, AbortToken);

        // when
        var final = await _sut.CreateFinalFileAsync([first, second], _ClientMetadata, AbortToken);

        // then
        foreach (var fileId in new[] { first, second, final })
        {
            TusEndpointTestHost
                .DecodeMetadata(await _inner.GetUploadMetadataAsync(fileId, AbortToken))
                .Should()
                .Contain("headless-owner", "alice");
        }

        (await _sut.GetUploadMetadataAsync(final, AbortToken)).Should().Be(_ClientMetadata);
        (await _sut.GetUploadConcatAsync(first, AbortToken)).Should().BeOfType<FileConcatPartial>();
        (await _sut.GetUploadConcatAsync(final, AbortToken)).Should().BeOfType<FileConcatFinal>();
        (await _sut.GetUploadOffsetAsync(final, AbortToken)).Should().Be(11);
    }

    [Theory]
    [InlineData(nameof(TusStampedStore.SetUploadLengthAsync))]
    [InlineData(nameof(TusStampedStore.DeleteFileAsync))]
    [InlineData(nameof(TusStampedStore.SetExpirationAsync))]
    [InlineData(nameof(TusStampedStore.GetExpirationAsync))]
    [InlineData(nameof(TusStampedStore.GetExpiredFilesAsync))]
    [InlineData(nameof(TusStampedStore.RemoveExpiredFilesAsync))]
    [InlineData(nameof(TusStampedStore.GetSupportedAlgorithmsAsync))]
    [InlineData(nameof(TusStampedStore.VerifyChecksumAsync))]
    [InlineData(nameof(TusStampedStore.GetUploadConcatAsync))]
    [InlineData(nameof(TusStampedStore.CreatePartialFileAsync))]
    [InlineData(nameof(TusStampedStore.CreateFinalFileAsync))]
    [InlineData(nameof(TusStampedStore.GetFileAsync))]
    public async Task should_report_a_capability_the_wrapped_store_lacks(string member)
    {
        // given - a store that only creates uploads
        var sut = new TusStampedStore(new CreationOnlyTusStore(_inner), _Caller);
        var fileId = await sut.CreateFileAsync(10, _ClientMetadata, AbortToken);

        // when
        var act = () => _CallAsync(sut, member, fileId);

        // then
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*CreationOnlyTusStore*");
    }

    private static Task _CallAsync(TusStampedStore store, string member, string fileId)
    {
        return member switch
        {
            nameof(TusStampedStore.SetUploadLengthAsync) => store.SetUploadLengthAsync(fileId, 10, AbortToken),
            nameof(TusStampedStore.DeleteFileAsync) => store.DeleteFileAsync(fileId, AbortToken),
            nameof(TusStampedStore.SetExpirationAsync) => store.SetExpirationAsync(
                fileId,
                DateTimeOffset.UnixEpoch,
                AbortToken
            ),
            nameof(TusStampedStore.GetExpirationAsync) => store.GetExpirationAsync(fileId, AbortToken),
            nameof(TusStampedStore.GetExpiredFilesAsync) => store.GetExpiredFilesAsync(AbortToken),
            nameof(TusStampedStore.RemoveExpiredFilesAsync) => store.RemoveExpiredFilesAsync(AbortToken),
            nameof(TusStampedStore.GetSupportedAlgorithmsAsync) => store.GetSupportedAlgorithmsAsync(AbortToken),
            nameof(TusStampedStore.VerifyChecksumAsync) => store.VerifyChecksumAsync(fileId, "sha1", [], AbortToken),
            nameof(TusStampedStore.GetUploadConcatAsync) => store.GetUploadConcatAsync(fileId, AbortToken),
            nameof(TusStampedStore.CreatePartialFileAsync) => store.CreatePartialFileAsync(
                10,
                _ClientMetadata,
                AbortToken
            ),
            nameof(TusStampedStore.CreateFinalFileAsync) => store.CreateFinalFileAsync(
                [fileId],
                _ClientMetadata,
                AbortToken
            ),
            nameof(TusStampedStore.GetFileAsync) => store.GetFileAsync(fileId, AbortToken),
            _ => throw new ArgumentOutOfRangeException(nameof(member), member, message: null),
        };
    }
}
