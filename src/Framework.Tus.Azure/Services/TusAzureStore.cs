// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Framework.Tus.Models;
using Framework.Tus.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using tusdotnet.Interfaces;
using tusdotnet.Stores.FileIdProviders;

namespace Framework.Tus.Services;

[PublicAPI]
public sealed partial class TusAzureStore
{
    private static readonly GuidFileIdProvider _DefaultFileIdProvider = new();

    private readonly TusAzureStoreOptions _options;
    private readonly ITusAzureBlobHttpHeadersProvider _blobHttpHeadersProvider;
    private readonly ITusFileIdProvider _fileIdProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;

    private readonly ILogger<TusAzureStore> _logger;
    private readonly BlobContainerClient _containerClient;

    public TusAzureStore(
        BlobServiceClient blobServiceClient,
        TusAzureStoreOptions options,
        ITusAzureBlobHttpHeadersProvider? blobHttpHeadersProvider = null,
        ITusFileIdProvider? fileIdProvider = null,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? timeProvider = null
    )
    {
        _options = options;
        _blobHttpHeadersProvider = blobHttpHeadersProvider ?? new DefaultTusAzureBlobHttpHeadersProvider();
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _fileIdProvider = fileIdProvider ?? _DefaultFileIdProvider;
        _logger = _loggerFactory.CreateLogger<TusAzureStore>();

        _containerClient = blobServiceClient.GetBlobContainerClient(_options.ContainerName);

        if (_options.CreateContainerIfNotExists)
        {
            _Initialize();
        }
    }

    private void _Initialize()
    {
        try
        {
#pragma warning disable MA0045 // Use Async
            _containerClient.CreateIfNotExists(_options.ContainerPublicAccessType);
#pragma warning restore MA0045
            _logger.LogInformation("Initialized Azure Blob container: {ContainerName}", _options.ContainerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize container: {ContainerName}", _options.ContainerName);
            throw;
        }
    }

    private static async Task _UpdateMetadataAsync(BlobClient blobClient, TusAzureFile file, CancellationToken token)
    {
        await blobClient.SetMetadataAsync(file.Metadata.ToAzure(), cancellationToken: token);
    }

    private Task<List<BlobBlock>> _GetCommittedBlocksAsync(string fileId, CancellationToken token)
    {
        return _GetCommittedBlocksAsync(_GetBlockBlobClient(fileId), token);
    }

    private static async Task<List<BlobBlock>> _GetCommittedBlocksAsync(BlockBlobClient client, CancellationToken token)
    {
        try
        {
            var blockListResponse = await client.GetBlockListAsync(BlockListTypes.Committed, cancellationToken: token);

            return blockListResponse.Value.CommittedBlocks.AsList();
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return [];
        }
    }

    private static async Task<(List<BlobBlock> Committed, List<BlobBlock> Uncommitted)> _GetBlocksAsync(BlockBlobClient client, CancellationToken token)
    {
        try
        {
            var blockListResponse = await client.GetBlockListAsync(BlockListTypes.Committed, cancellationToken: token);

            return (blockListResponse.Value.CommittedBlocks.AsList(), blockListResponse.Value.UncommittedBlocks.AsList());
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return ([], []);
        }
    }

    private async Task<TusAzureFile?> _GetTusFileInfoAsync(string fileId, CancellationToken token)
    {
        var blobClient = _GetBlobClient(fileId);

        return await _GetTusFileInfoAsync(blobClient, fileId, token);
    }

    private static async Task<TusAzureFile?> _GetTusFileInfoAsync(
        BlobClient client,
        string fileId,
        CancellationToken token
    )
    {
        try
        {
            var propertiesResponse = await client.GetPropertiesAsync(cancellationToken: token);

            return propertiesResponse.HasValue
                ? TusAzureFile.FromBlobProperties(fileId, client.Name, propertiesResponse.Value)
                : null;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    private BlobClient _GetBlobClient(string fileId)
    {
        return _containerClient.GetBlobClient(_GetBlobName(fileId));
    }

    private BlockBlobClient _GetBlockBlobClient(string fileId)
    {
        return _containerClient.GetBlockBlobClient(_GetBlobName(fileId));
    }

    private string _GetBlobName(string fileId)
    {
        return $"{_options.BlobPrefix.EnsureEndsWith('/')}{fileId}";
    }

    private string _ExtractFileIdFromBlobName(string blobName)
    {
        var prefix = _options.BlobPrefix.EnsureEndsWith('/');

        return blobName.StartsWith(prefix, StringComparison.Ordinal) ? blobName[prefix.Length..] : string.Empty;
    }
}
