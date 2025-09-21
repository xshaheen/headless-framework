// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Framework.Tus.Options;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Framework.Tus.Locks;

public class AzureBlobFileLockProvider(
    BlobContainerClient containerClient,
    TusAzureStoreOptions options,
    ILogger<AzureBlobFileLock> logger
) : ITusFileLockProvider
{
    public Task<ITusFileLock> AquireLock(string fileId)
    {
        var blobName = _GetBlobName(fileId);
        var blobClient = containerClient.GetBlobClient(blobName);
        var fileLock = new AzureBlobFileLock(blobClient, options.DefaultLeaseTime, logger);

        return Task.FromResult<ITusFileLock>(fileLock);
    }

    private string _GetBlobName(string fileId)
    {
        return $"{options.BlobPrefix.EnsureEndsWith('/')}{fileId}";
    }
}
