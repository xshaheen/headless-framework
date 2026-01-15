// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;
using Azure.Storage.Blobs.Models;
using Framework.Checks;
using Framework.Tus.Internal;
using Microsoft.Extensions.Logging;
using tusdotnet.Extensions.Store;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Framework.Tus.Services;

public sealed partial class TusAzureStore : ITusPipelineStore
{
    public async Task<long> AppendDataAsync(string fileId, PipeReader pipeReader, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(fileId);
        Argument.IsNotNull(pipeReader);

        _logger.LogTrace("Appending data using the PipeReader for file '{FileId}'", fileId);

        var blobClient = _GetBlobClient(fileId);
        var blockBlobClient = _GetBlockBlobClient(fileId);

        var azureFile =
            await _GetTusFileInfoAsync(blobClient, fileId, cancellationToken)
            ?? throw new InvalidOperationException($"File {fileId} does not exist");

        var committedBlocks = await _GetCommittedBlocksAsync(blockBlobClient, cancellationToken);
        var currentOffset = committedBlocks.Sum(b => b.SizeLong);

        // Get checksum info if provided (from request headers via tusdotnet extension method)
        var checksumInfo = pipeReader.GetUploadChecksumInfo();
        using var hasher = checksumInfo is not null ? _CreateHashAlgorithm(checksumInfo.Algorithm) : null;

        if (checksumInfo is not null && hasher is null)
        {
            var supportedAlgorithms = await GetSupportedAlgorithmsAsync(cancellationToken);

            throw new NotSupportedException(
                $"Checksum algorithm '{checksumInfo.Algorithm}' is not supported. Supported algorithms: {string.Join(", ", supportedAlgorithms)}"
            );
        }

        var optimalChunkSize = _CalculateOptimalChunkSize(azureFile.Metadata.UploadLength);
        var nextBlockNumber = committedBlocks.Count;
        var bytesWrittenThisRequest = 0L;
        var chunkBlockIds = new List<string>();

        ReadResult result = default;

        try
        {
            while (!_PipeReadingIsDone(result, cancellationToken))
            {
                // Read at least optimalChunkSize bytes, or whatever remains
                result = await pipeReader.ReadAtLeastAsync(optimalChunkSize, cancellationToken).AnyContext();

                if (result.Buffer.IsEmpty)
                {
                    break;
                }

                // Validate we're not exceeding declared upload length
                _AssertNotToMuchData(currentOffset, result.Buffer.Length, azureFile.Metadata.UploadLength);

                // Process the buffer in optimal-sized chunks
                var buffer = result.Buffer;
                var consumed = buffer.Start;

                while (buffer.Length > 0)
                {
                    // Take up to optimalChunkSize bytes for this block
                    var chunkLength = (int)Math.Min(optimalChunkSize, buffer.Length);
                    var chunk = buffer.Slice(0, chunkLength);

                    // Calculate hash for this chunk if checksum verification is needed
                    if (hasher is not null)
                    {
                        _HashSequence(hasher, chunk);
                    }

                    // Stage the block
                    var blockId = _GenerateBlockId(nextBlockNumber++);
                    await using var chunkStream = new ReadOnlySequenceStream(chunk);
                    await blockBlobClient
                        .StageBlockAsync(blockId, chunkStream, cancellationToken: cancellationToken)
                        .AnyContext();

                    chunkBlockIds.Add(blockId);
                    bytesWrittenThisRequest += chunkLength;
                    currentOffset += chunkLength;

                    // Advance to next chunk
                    buffer = buffer.Slice(chunkLength);
                    consumed = buffer.Start;
                }

                // Tell the PipeReader we've consumed up to this point
                pipeReader.AdvanceTo(consumed);

                _logger.LogDebug(
                    "Staged {BytesWritten} bytes for file '{FileId}' ({BlockCount} blocks)",
                    bytesWrittenThisRequest,
                    fileId,
                    chunkBlockIds.Count
                );
            }

            await pipeReader.CompleteAsync().AnyContext();

            // Finalize hash if checksum verification is needed
            hasher?.TransformFinalBlock([], 0, 0);

            // ATOMIC: Commit blocks + update metadata
            if (hasher is null)
            {
                // No checksum - commit immediately
                List<string> allBlockIds = [.. committedBlocks.Select(b => b.Name), .. chunkBlockIds];
                var options = new CommitBlockListOptions { Metadata = azureFile.Metadata.ToAzure() };
                await blockBlobClient.CommitBlockListAsync(allBlockIds, options, cancellationToken).AnyContext();
            }
            else
            {
                // With checksum - store chunk info for later verification
                azureFile.Metadata.LastChunkBlocks = chunkBlockIds.ToArray();
                azureFile.Metadata.LastChunkChecksum = Convert.ToBase64String(hasher.Hash ?? []);
                await _UpdateMetadataAsync(blobClient, azureFile, cancellationToken).AnyContext();

                _logger.LogDebug(
                    "Stored chunk metadata for file '{FileId}': {BlockCount} blocks staged for checksum verification",
                    fileId,
                    chunkBlockIds.Count
                );
            }
        }
        catch (Exception e)
        {
            // Clear memory and complete the reader to prevent
            // Microsoft.AspNetCore.Connections.ConnectionAbortedException inside Kestrel
            try
            {
                pipeReader.AdvanceTo(result.Buffer.End);
                await pipeReader.CompleteAsync().AnyContext();
            }
#pragma warning disable ERP022
            catch
            {
                // Ignore cleanup errors so the real exception propagates
            }
#pragma warning restore ERP022

            if (e is OperationCanceledException or TaskCanceledException)
            {
                _logger.LogWarning("Cancelled the upload operation for file id '{FileId}'", fileId);
            }
            else
            {
                throw;
            }
#pragma warning disable ERP022 // Justification: Swallowing exceptions from cleanup code to not hide the original exception.
        }
#pragma warning restore ERP022

        return bytesWrittenThisRequest;
    }

    /// <summary>
    /// Hashes a <see cref="ReadOnlySequence{T}"/> by processing each segment.
    /// </summary>
    private static void _HashSequence(HashAlgorithm hasher, ReadOnlySequence<byte> sequence)
    {
        foreach (var segment in sequence)
        {
            hasher.TransformBlock(segment.ToArray(), 0, segment.Length, outputBuffer: null, 0);
        }
    }

    /// <summary>
    /// Determines if PipeReader reading should stop.
    /// </summary>
    private static bool _PipeReadingIsDone(ReadResult result, CancellationToken cancellationToken)
    {
        return result.IsCompleted || result.IsCanceled || cancellationToken.IsCancellationRequested;
    }

    private static void _AssertNotToMuchData(
        long uploadOffsetLength,
        long numberOfBytesReadFromClient,
        long? fileUploadLengthProvidedDuringCreate
    )
    {
        var requestDataLength = uploadOffsetLength + numberOfBytesReadFromClient;

        if (requestDataLength > fileUploadLengthProvidedDuringCreate)
        {
            FormattableString message =
                $"Request contains more data than the file's upload length. Request data: {requestDataLength}, upload length: {fileUploadLengthProvidedDuringCreate}.";

            throw new TusStoreException(message.ToString(CultureInfo.InvariantCulture));
        }
    }
}
