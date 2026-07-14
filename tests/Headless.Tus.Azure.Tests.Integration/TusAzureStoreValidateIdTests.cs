// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Pipelines;
using Azure.Storage.Blobs;
using Headless.Testing.Tests;
using Headless.Tus;
using Tests.TestSetup;
using tusdotnet.Models;

namespace Tests;

/// <summary>
/// Pins that <c>ITusFileIdProvider.ValidateId</c> is enforced at every public store entry point:
/// an id the provider rejects (here: anything that is not a GUID from the default provider) must
/// throw <see cref="TusStoreException"/> — mapped to 400 by tusdotnet — instead of being silently
/// processed into a blob name.
/// </summary>
[Collection<TusAzureFixture>]
public sealed class TusAzureStoreValidateIdTests : TestBase
{
    private const string _InvalidFileId = "not-a-guid-!!";

    private readonly TusAzureStore _store;

    public TusAzureStoreValidateIdTests(TusAzureFixture fixture)
    {
        var blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        var storeOptions = new TusAzureStoreOptions { ContainerName = "tusvalidate", BlobPrefix = "tusvalidate/" };
        _store = new TusAzureStore(blobServiceClient, storeOptions, loggerFactory: LoggerFactory);
    }

    public static TheoryData<string, Func<TusAzureStore, CancellationToken, Task>> EntryPoints()
    {
        return new TheoryData<string, Func<TusAzureStore, CancellationToken, Task>>
        {
            { "FileExistAsync", async (store, ct) => _ = await store.FileExistAsync(_InvalidFileId, ct) },
            { "GetUploadLengthAsync", async (store, ct) => _ = await store.GetUploadLengthAsync(_InvalidFileId, ct) },
            { "GetUploadOffsetAsync", async (store, ct) => _ = await store.GetUploadOffsetAsync(_InvalidFileId, ct) },
            {
                "GetUploadMetadataAsync",
                async (store, ct) => _ = await store.GetUploadMetadataAsync(_InvalidFileId, ct)
            },
            { "SetUploadLengthAsync", (store, ct) => store.SetUploadLengthAsync(_InvalidFileId, 100, ct) },
            { "GetFileAsync", async (store, ct) => _ = await store.GetFileAsync(_InvalidFileId, ct) },
            { "DeleteFileAsync", (store, ct) => store.DeleteFileAsync(_InvalidFileId, ct) },
            {
                "SetExpirationAsync",
                (store, ct) => store.SetExpirationAsync(_InvalidFileId, DateTimeOffset.UtcNow, ct)
            },
            { "GetExpirationAsync", async (store, ct) => _ = await store.GetExpirationAsync(_InvalidFileId, ct) },
            { "GetUploadConcatAsync", async (store, ct) => _ = await store.GetUploadConcatAsync(_InvalidFileId, ct) },
            {
                "VerifyChecksumAsync",
                // VerifyChecksumAsync swallows most errors into `false`, but an invalid id must
                // still throw: reaching it means tusdotnet's earlier requirements were bypassed.
                async (store, ct) => _ = await store.VerifyChecksumAsync(_InvalidFileId, "sha256", new byte[32], ct)
            },
            {
                "AppendDataAsync(Stream)",
                async (store, ct) =>
                {
                    await using var body = new MemoryStream([1, 2, 3]);
                    _ = await store.AppendDataAsync(_InvalidFileId, body, ct);
                }
            },
            {
                "AppendDataAsync(PipeReader)",
                async (store, ct) =>
                {
                    var pipe = new Pipe();
                    await pipe.Writer.CompleteAsync();
                    _ = await store.AppendDataAsync(_InvalidFileId, pipe.Reader, ct);
                }
            },
            {
                "CreateFinalFileAsync(partial id)",
                async (store, ct) => _ = await store.CreateFinalFileAsync([_InvalidFileId], metadata: null, ct)
            },
        };
    }

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public async Task should_reject_invalid_file_id(string entryPoint, Func<TusAzureStore, CancellationToken, Task> act)
    {
        // when
        var invoke = () => act(_store, AbortToken);

        // then
        await invoke
            .Should()
            .ThrowAsync<TusStoreException>($"{entryPoint} must enforce ITusFileIdProvider.ValidateId")
            .WithMessage($"*Invalid TUS file id: '{_InvalidFileId}'*");
    }
}
