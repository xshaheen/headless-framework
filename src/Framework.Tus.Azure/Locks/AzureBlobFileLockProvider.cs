// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Framework.Tus.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using tusdotnet.Interfaces;

namespace Framework.Tus.Locks;

public sealed class AzureBlobFileLockProvider(
    BlobServiceClient blobServiceClient,
    TusAzureStoreOptions options,
    ILoggerFactory? loggerFactory = null
) : ITusFileLockProvider
{
    private readonly BlobContainerClient _containerClient = blobServiceClient.GetBlobContainerClient(
        options.ContainerName
    );

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
