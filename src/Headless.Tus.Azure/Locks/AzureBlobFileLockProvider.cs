// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Tus.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using tusdotnet.Interfaces;

namespace Headless.Tus.Locks;

/// <summary>
/// TUS file lock provider that creates <c>AzureBlobFileLock</c> instances backed by Azure Blob Storage leases.
/// </summary>
/// <remarks>
/// Register this as <c>ITusFileLockProvider</c> in DI to prevent concurrent PATCH requests from
/// corrupting the same upload. Each call to <c>AquireLock</c> returns a new
/// <c>AzureBlobFileLock</c> wrapping the blob identified by the requested <c>fileId</c>
/// (the actual lease acquisition happens inside <c>Lock()</c>, not here).
/// </remarks>
public sealed class AzureBlobFileLockProvider(
    BlobServiceClient blobServiceClient,
    TusAzureStoreOptions options,
    ILoggerFactory? loggerFactory = null
) : ITusFileLockProvider
{
    private readonly BlobContainerClient _containerClient = blobServiceClient.GetBlobContainerClient(
        options.ContainerName
    );

    /// <summary>
    /// Creates a new <c>AzureBlobFileLock</c> for the specified TUS file without acquiring the
    /// underlying Azure Blob lease yet.
    /// </summary>
    /// <param name="fileId">the TUS file identifier; used to derive the blob name within the container</param>
    /// <returns>
    /// a new <c>ITusFileLock</c> whose <c>Lock()</c> method must be called before exclusive
    /// access is guaranteed
    /// </returns>
    public Task<ITusFileLock> AquireLock(string fileId)
    {
        var blobName = _GetBlobName(fileId);
        var blobClient = _containerClient.GetBlobClient(blobName);
        var logger = loggerFactory?.CreateLogger<AzureBlobFileLock>() ?? NullLogger<AzureBlobFileLock>.Instance;

        return Task.FromResult<ITusFileLock>(new AzureBlobFileLock(blobClient, options.LeaseDuration, logger));
    }

    private string _GetBlobName(string fileId)
    {
        return $"{options.BlobPrefix.EnsureEndsWith('/')}{fileId}";
    }
}
