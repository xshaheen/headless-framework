// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Hosting;

namespace Headless.Tus.Internal;

/// <summary>
/// Offers the upload container as a provisioned resource, so a deploy step creates it with
/// <c>resources provision -k AzureBlob</c> and the store never needs container-create permission at run time.
/// </summary>
internal sealed class TusAzureContainerProvisioner(BlobServiceClient blobServiceClient, TusAzureStoreOptions options)
    : IResourceProvisioner
{
    private readonly BlobContainerClient _containerClient = blobServiceClient.GetBlobContainerClient(
        options.ContainerName
    );

    public string Kind => "AzureBlob";

    public string Name => options.ContainerName;

    public async Task ProvisionAsync(CancellationToken cancellationToken)
    {
        await _containerClient
            .CreateIfNotExistsAsync(options.ContainerPublicAccessType, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        var exists = await _containerClient.ExistsAsync(cancellationToken).ConfigureAwait(false);

        if (!exists.Value)
        {
            throw new ResourceProvisioningException(
                $"The tus upload container '{options.ContainerName}' does not exist. Run 'resources provision -k AzureBlob' to create it."
            );
        }
    }

    // Purge removes only the blobs under the store's prefix: the container can be shared with other data.
    public Task PurgeAsync(CancellationToken cancellationToken) => _DeleteUploadsAsync(cancellationToken);

    // The container may hold blobs outside BlobPrefix that another feature owns, and Azure has no delete-if-empty, so
    // the container itself goes only when nothing else is left in it.
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (!await _containerClient.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await _DeleteUploadsAsync(cancellationToken).ConfigureAwait(false);

        await foreach (
            var _ in _containerClient.GetBlobsAsync(
                traits: BlobTraits.None,
                states: BlobStates.None,
                prefix: null,
                cancellationToken: cancellationToken
            )
        )
        {
            return;
        }

        await _containerClient.DeleteIfExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var exists = await _containerClient.ExistsAsync(cancellationToken).ConfigureAwait(false);

        return [new("container", exists.Value ? "exists" : "missing"), new("prefix", options.BlobPrefix)];
    }

    private async Task _DeleteUploadsAsync(CancellationToken cancellationToken)
    {
        if (!await _containerClient.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await foreach (
            var blob in _containerClient.GetBlobsAsync(
                traits: BlobTraits.None,
                states: BlobStates.None,
                prefix: options.BlobPrefix,
                cancellationToken: cancellationToken
            )
        )
        {
            await _containerClient
                .DeleteBlobIfExistsAsync(
                    blob.Name,
                    DeleteSnapshotsOption.IncludeSnapshots,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }
    }
}
