// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using FluentValidation;
using Headless.Checks;
using Headless.Tus.Internal;
using Headless.Tus.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Stores.FileIdProviders;

namespace Headless.Tus;

/// <summary>
/// TUS resumable-upload store backed by Azure Block Blob Storage.
/// </summary>
/// <remarks>
/// <para>
/// Implements the core TUS protocol extensions — Creation, CreationDeferLength, Checksum,
/// Concatenation, Expiration, Termination, and pipeline-aware append — all on top of Azure
/// Block Blob staged blocks. Each PATCH request stages one or more blocks and commits them
/// atomically alongside updated metadata.
/// </para>
/// <para>
/// When <see cref="TusAzureStoreOptions.EnableChunkSplitting"/> is enabled (the default), incoming
/// PATCH bodies are split into fixed-size blocks no larger than the configured chunk size (capped
/// at 100 MB, which also bounds per-request memory; Azure's own per-block maximum is 4,000 MiB on
/// current service versions). Without splitting, the entire PATCH body is staged as one block.
/// </para>
/// <para>
/// <b>Checksum deferred commit:</b> when a PATCH request carries a TUS-Checksum header, blocks
/// are staged but <em>not</em> committed until <c>VerifyChecksumAsync</c> confirms the digest.
/// The staged block range (a constant-size token/index/count triple) and the pre-calculated
/// digest are stored in blob metadata (<c>tus_last_chunk_blocks</c> /
/// <c>tus_last_chunk_checksum</c>) so verification and commit happen in a separate call. Failed
/// verification leaves the blocks uncommitted; Azure automatically discards uncommitted blocks
/// after seven days.
/// </para>
/// <para>
/// <b>Write fencing:</b> the per-file lock is best-effort — a request can lose it without noticing
/// (a process pause, a lock-backend failover) while a second request for the same upload proceeds.
/// The blob itself therefore rejects stale writers: every block-list commit and metadata write
/// sends the ETag the calling request read as <c>If-Match</c>, and carries the ETag each write
/// returns into its next write. A writer whose view is stale fails with Azure's
/// <c>RequestFailedException</c> (HTTP 412), which is deliberately <em>not</em> translated into a
/// <c>TusStoreException</c>: tusdotnet answers that with 400, which tus clients treat as fatal,
/// while the untranslated exception surfaces as a 500 that tus clients retry by issuing HEAD and
/// resuming from the committed offset. The exception also aborts the request before tusdotnet
/// writes <c>Upload-Offset</c>, so a client never sees a success offset for bytes that did not land.
/// Each store call fences from its own read: the checksum header flow's verify step re-reads the
/// blob, and its digest comparison is what rejects blocks a different writer staged in between.
/// </para>
/// <para>
/// The store never creates its container. <c>AddTusAzureStore</c> registers it as a provisioned
/// resource, so a deploy step creates it with <c>resources provision -k AzureBlob</c> and a
/// starting host only checks that it exists; a manually constructed store needs the container
/// created beforehand.
/// </para>
/// </remarks>
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

    // Dedicated buffer pool for staging chunks up to BlobMaxChunkSize. ArrayPool.Shared only pools
    // arrays up to 1 MB, so renting 4-16 MB chunks against it forces continuous LOH allocations that
    // are never reused.
    private readonly ArrayPool<byte> _chunkPool;

    /// <summary>
    /// Initializes a new <c>TusAzureStore</c> over an existing Blob Storage container. Construction
    /// makes no network call.
    /// </summary>
    /// <param name="blobServiceClient">authenticated Azure Blob service client</param>
    /// <param name="options">store configuration</param>
    /// <param name="blobHttpHeadersProvider">
    /// optional provider for customizing blob HTTP headers (content type, cache control, etc.);
    /// defaults to <c>DefaultTusAzureBlobHttpHeadersProvider</c> which sets
    /// <c>application/octet-stream</c>
    /// </param>
    /// <param name="fileIdProvider">
    /// optional strategy for generating TUS file identifiers; defaults to a GUID-based provider
    /// </param>
    /// <param name="loggerFactory">optional logger factory; defaults to the null logger</param>
    /// <param name="timeProvider">optional time abstraction; defaults to <c>TimeProvider.System</c></param>
    public TusAzureStore(
        BlobServiceClient blobServiceClient,
        TusAzureStoreOptions options,
        ITusAzureBlobHttpHeadersProvider? blobHttpHeadersProvider = null,
        ITusFileIdProvider? fileIdProvider = null,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? timeProvider = null
    )
    {
        Argument.IsNotNull(options);
        // The store is factory-constructed (no DI/IOptions pipeline), so validation runs here to fail fast on
        // invalid options (empty container, out-of-range chunk size/lease) instead of surfacing as Azure errors.
        new TusAzureStoreOptionsValidator().ValidateAndThrow(options);

        _options = options;
        _blobHttpHeadersProvider = blobHttpHeadersProvider ?? new DefaultTusAzureBlobHttpHeadersProvider();
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _fileIdProvider = fileIdProvider ?? _DefaultFileIdProvider;
        _logger = _loggerFactory.CreateLogger<TusAzureStore>();
        _chunkPool = ArrayPool<byte>.Create(_options.BlobMaxChunkSize, maxArraysPerBucket: 8);

        _containerClient = blobServiceClient.GetBlobContainerClient(_options.ContainerName);
    }

    /// <summary>
    /// Replaces the blob metadata with <paramref name="file"/>'s, only if the blob still carries the
    /// ETag this request last observed, then records the new ETag for the request's next write.
    /// </summary>
    private async Task _UpdateMetadataAsync(BlobClient blobClient, TusAzureFile file, CancellationToken token)
    {
        try
        {
            var response = await blobClient
                .SetMetadataAsync(file.Metadata.ToAzure(), _IfUnchanged(file), token)
                .ConfigureAwait(false);

            file.ETag = response.Value.ETag;
        }
        catch (RequestFailedException e) when (e.Status == 412)
        {
            _logger.StaleWriteRejected(file.FileId);

            throw;
        }
    }

    /// <summary>
    /// Commits <paramref name="blockIds"/> together with <paramref name="file"/>'s metadata, only if
    /// the blob still carries the ETag this request last observed, then records the new ETag.
    /// </summary>
    /// <remarks>
    /// The HTTP headers must be re-supplied: Put Block List clears any <c>x-ms-blob-*</c> property
    /// omitted from the request, which would wipe headers set at creation (custom content type,
    /// cache control).
    /// </remarks>
    private async Task _CommitBlockListAsync(
        BlockBlobClient client,
        TusAzureFile file,
        IEnumerable<string> blockIds,
        CancellationToken token
    )
    {
        var options = new CommitBlockListOptions
        {
            Metadata = file.Metadata.ToAzure(),
            HttpHeaders = file.HttpHeaders,
            Conditions = _IfUnchanged(file),
        };

        try
        {
            var response = await client.CommitBlockListAsync(blockIds, options, token).ConfigureAwait(false);

            file.ETag = response.Value.ETag;
        }
        catch (RequestFailedException e) when (e.Status == 412)
        {
            _logger.StaleWriteRejected(file.FileId);

            throw;
        }
    }

    private static BlobRequestConditions _IfUnchanged(TusAzureFile file)
    {
        return new BlobRequestConditions { IfMatch = file.ETag };
    }

    private Task<List<BlobBlock>> _GetCommittedBlocksAsync(string fileId, CancellationToken token)
    {
        return _GetCommittedBlocksAsync(_GetBlockBlobClient(fileId), token);
    }

    private static async Task<List<BlobBlock>> _GetCommittedBlocksAsync(BlockBlobClient client, CancellationToken token)
    {
        try
        {
            var blockListResponse = await client
                .GetBlockListAsync(BlockListTypes.Committed, cancellationToken: token)
                .ConfigureAwait(false);

            return blockListResponse.Value.CommittedBlocks.AsList();
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return [];
        }
    }

    private async Task<TusAzureFile?> _GetTusFileInfoAsync(string fileId, CancellationToken token)
    {
        var blobClient = _GetBlobClient(fileId);

        return await _GetTusFileInfoAsync(blobClient, fileId, token).ConfigureAwait(false);
    }

    private static async Task<TusAzureFile?> _GetTusFileInfoAsync(
        BlobClient client,
        string fileId,
        CancellationToken token
    )
    {
        try
        {
            var propertiesResponse = await client.GetPropertiesAsync(cancellationToken: token).ConfigureAwait(false);

            return propertiesResponse.HasValue
                ? TusAzureFile.FromBlobProperties(fileId, client.Name, propertiesResponse.Value)
                : null;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Validates an externally-supplied file id against the configured
    /// <c>ITusFileIdProvider</c>, mirroring <c>TusDiskStore</c>'s <c>InternalFileId.Parse</c>.
    /// <c>TusBlobName</c> still applies its own traversal defense when the id becomes a blob name.
    /// </summary>
    /// <exception cref="TusStoreException">thrown if the id fails provider validation (mapped to 400 by tusdotnet)</exception>
    private async Task _EnsureValidFileIdAsync(string fileId)
    {
        if (!await _fileIdProvider.ValidateId(fileId).ConfigureAwait(false))
        {
            throw new TusStoreException($"Invalid TUS file id: '{fileId}'.");
        }
    }

    /// <summary>
    /// Clears stale chunk-tracking metadata after an append that received zero bytes. Without
    /// this, a checksum-trailer fallback (<c>VerifyChecksumAsync</c> with tusdotnet's sentinel)
    /// following an empty append would act on the PREVIOUS append's rollback point and discard a
    /// chunk that was already committed and verified. Skips the write when the state is already
    /// clean; must-complete because the caller's token is cancelled in exactly this scenario.
    /// </summary>
    private async Task _RefreshChunkTrackingForEmptyAppendAsync(
        BlobClient blobClient,
        TusAzureFile file,
        long currentOffset
    )
    {
        var metadata = file.Metadata;

        if (
            metadata.LastChunkBlocks is null
            && metadata.LastChunkChecksum is null
            && metadata.LastChunkOffset == currentOffset
        )
        {
            return;
        }

        metadata.LastChunkBlocks = null;
        metadata.LastChunkChecksum = null;
        metadata.LastChunkOffset = currentOffset;

        await _UpdateMetadataAsync(blobClient, file, CancellationToken.None).ConfigureAwait(false);
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
        return TusBlobName.Build(_options.BlobPrefix, fileId);
    }

    private string _ExtractFileIdFromBlobName(string blobName)
    {
        var prefix = _options.BlobPrefix.EnsureEndsWith('/');

        return blobName.StartsWith(prefix, StringComparison.Ordinal) ? blobName[prefix.Length..] : string.Empty;
    }
}

internal static partial class TusAzureStoreLog
{
    [LoggerMessage(
        EventId = 3251,
        Level = LogLevel.Warning,
        Message = "Rejected a write to file {FileId}: the blob changed since this request read it, so another request wrote it concurrently"
    )]
    internal static partial void StaleWriteRejected(this ILogger logger, string fileId);
}
