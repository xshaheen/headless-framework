// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Tus;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests.Conformance;

/// <summary>
/// Registers the Azure store for the upload endpoint scenarios the way an application does, through
/// <c>UseAzure</c>, under a blob prefix of its own inside the container the conformance suites share.
/// </summary>
internal sealed class TusAzureUploadLayerBackend(TusAzureFixture fixture) : ITusUploadLayerConformanceBackend
{
    private readonly BlobServiceClient _blobServiceClient = fixture.CreateBlobServiceClient();
    private readonly string _blobPrefix = $"upload-layer-{Guid.NewGuid():N}/";

    // The store keeps the string in the blob's metadata, which Azure caps at 8 KB for the whole blob, and refuses
    // a longer one so that its own tracking entries always fit beside it.
    public int MaxMetadataLength => 7 * 1024;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(_blobServiceClient);
    }

    public void UseStore(HeadlessTusSetupBuilder tus)
    {
        tus.UseAzure(options =>
        {
            options.ContainerName = TusAzureFixture.ContainerName;
            options.BlobPrefix = _blobPrefix;
        });
    }

    public async Task<IReadOnlyList<string>> ListStoredNamesAsync(CancellationToken cancellationToken)
    {
        var names = new List<string>();
        var container = _blobServiceClient.GetBlobContainerClient(TusAzureFixture.ContainerName);

        await foreach (
            var blob in container.GetBlobsAsync(
                traits: BlobTraits.None,
                states: BlobStates.None,
                prefix: _blobPrefix,
                cancellationToken: cancellationToken
            )
        )
        {
            names.Add(blob.Name);
        }

        return names;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
