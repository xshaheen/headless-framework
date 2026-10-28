// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Checks;
using Headless.Tus.Internal;
using Microsoft.Extensions.Logging;
using tusdotnet.Models;

namespace Headless.Tus;

public sealed partial class TusS3Store
{
    /// <summary>
    /// Builds a final upload from completed partial uploads and returns its file identifier.
    /// </summary>
    /// <param name="partialFiles">
    /// the partial uploads to concatenate, in order; a partial may be listed more than once, as the tus spec allows
    /// </param>
    /// <param name="metadata">raw TUS metadata string for the final upload, or <see langword="null"/></param>
    /// <param name="cancellationToken">token to cancel the build</param>
    /// <returns>the file identifier of the final upload, complete when this returns</returns>
    /// <remarks>
    /// <para>
    /// The final upload gets its own part size, chosen as for an upload of the summed length. A part whose bytes lie
    /// inside one partial's finished object is copied by the provider with a ranged part copy and never passes through
    /// the store; a part that spans partials is read into a pooled buffer and uploaded. Every part except the last is
    /// exactly the part size, which R2 requires whatever the partials' sizes are. A final upload below one part is
    /// written in one request, and an empty one as an empty object.
    /// </para>
    /// <para>
    /// The completed record is written last, so a build that fails or is interrupted leaves no upload a client can
    /// see; a failed build aborts its multipart upload. The partial uploads are kept.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="partialFiles"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="partialFiles"/> is empty.</exception>
    /// <exception cref="TusStoreException">
    /// A partial id is invalid, the upload does not exist or is not a partial upload, a partial is incomplete or its
    /// finished object is missing, the metadata is malformed, or the summed length needs parts larger than
    /// <see cref="TusS3StoreOptions.MaxPartSize"/>.
    /// </exception>
    public async Task<string> CreateFinalFileAsync(
        string[] partialFiles,
        string? metadata,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNullOrEmpty(partialFiles);

        try
        {
            _EnsureValidMetadata(metadata);

            var lengths = await _LoadPartialLengthsAsync(partialFiles, cancellationToken).ConfigureAwait(false);
            var length = lengths.Sum();
            var partSize = TusS3PartSize.ForUpload(length, _options);
            var fileId = TusS3Keys.EnsureValid(await _fileIdProvider.CreateId(metadata).ConfigureAwait(false));
            var plan = TusS3ConcatPlan.Create(lengths, partSize);

            if (plan.Count <= 1)
            {
                await _PutFinalObjectAsync(fileId, partialFiles, metadata, plan, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _BuildFinalMultipartUploadAsync(fileId, partialFiles, metadata, plan, cancellationToken)
                    .ConfigureAwait(false);
            }

            var record = new TusS3CompletedRecord
            {
                UploadLength = length,
                Metadata = metadata,
                Concat = new TusS3ConcatInfo(TusS3ConcatKind.Final, partialFiles),
            };

            // Last, and only if absent: until this record exists the final upload is invisible, so a build that dies
            // before here never surfaces as an upload whose object is half-built.
            await _stateStore.CreateCompletedRecordAsync(fileId, record, cancellationToken).ConfigureAwait(false);

            _logger.LogS3FinalFileCreated(
                fileId,
                partialFiles.Length,
                length,
                plan.Count,
                plan.Count > 1 ? plan.Count(part => part.IsCopy) : 0
            );

            return fileId;
        }
        catch (Exception e)
        {
            _logger.LogS3FinalFileCreateFailed(e, string.Join(", ", partialFiles));

            throw;
        }
    }

    // tusdotnet checks the partials before calling the store, but holds no lock while it builds the final upload, so
    // a partial may have changed since; the store checks again and reads each length from the completed record.
    private async Task<long[]> _LoadPartialLengthsAsync(string[] partialFiles, CancellationToken cancellationToken)
    {
        var lengths = new long[partialFiles.Length];

        for (var i = 0; i < partialFiles.Length; i++)
        {
            var partialFileId = partialFiles[i];
            await _EnsureValidFileIdAsync(partialFileId).ConfigureAwait(false);

            var upload =
                await _stateStore.LoadAsync(partialFileId, cancellationToken).ConfigureAwait(false)
                ?? throw new TusStoreException($"Partial file {partialFileId} does not exist");

            if ((upload.Completed?.Concat ?? upload.State?.Concat) is not { Kind: TusS3ConcatKind.Partial })
            {
                throw new TusStoreException($"File {partialFileId} is not a partial file");
            }

            if (upload.State is { } state)
            {
                if (state.Status == TusS3UploadStatus.Gone || _MultipartUploadLifetimePassed(state))
                {
                    throw new TusStoreException($"Partial file {partialFileId} does not exist");
                }

                throw new TusStoreException(
                    state.UploadLength is { } uploadLength
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $"Partial file {partialFileId} is incomplete ({state.Offset}/{uploadLength} bytes)"
                        )
                        : $"Partial file {partialFileId} is incomplete (its length is not declared yet)"
                );
            }

            lengths[i] = upload.Completed!.UploadLength;
        }

        return lengths;
    }

    private async Task _PutFinalObjectAsync(
        string fileId,
        string[] partialFiles,
        string? metadata,
        IReadOnlyList<TusS3ConcatPart> plan,
        CancellationToken cancellationToken
    )
    {
        if (plan.Count == 0)
        {
            await _PutFinishedObjectAsync(fileId, metadata, [], 0, cancellationToken).ConfigureAwait(false);

            return;
        }

        var part = plan[0];
        var buffer = _appendServices.PartPool.Rent((int)part.Length);

        try
        {
            await _ReadConcatSourcesAsync(partialFiles, part, buffer, cancellationToken).ConfigureAwait(false);
            await _PutFinishedObjectAsync(fileId, metadata, buffer, (int)part.Length, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _appendServices.ReturnPartBuffer(buffer);
        }
    }

    /// <summary>Copies or uploads every part of a final upload larger than one part, then completes it.</summary>
    /// <remarks>
    /// The build runs on the request token: no lock covers it and the client has no answer until it ends, so a client
    /// that gives up should stop it. The abort after a failure runs without one, so it still happens then.
    /// </remarks>
    private async Task _BuildFinalMultipartUploadAsync(
        string fileId,
        string[] partialFiles,
        string? metadata,
        IReadOnlyList<TusS3ConcatPart> plan,
        CancellationToken cancellationToken
    )
    {
        var uploadId = await _CreateMultipartUploadAsync(fileId, metadata, cancellationToken).ConfigureAwait(false);

        try
        {
            var etags = new PartETag[plan.Count];
            var parallelOptions = new ParallelOptions
            {
                // Copies hold no memory, but streamed parts each hold one part buffer, so the same bound as a PATCH's
                // part uploads keeps the request's memory to the configured number of parts.
                MaxDegreeOfParallelism = _options.MaxConcurrentPartUploads,
                CancellationToken = cancellationToken,
            };

            await Parallel
                .ForEachAsync(
                    plan,
                    parallelOptions,
                    async (part, token) =>
                    {
                        var etag = part.IsCopy
                            ? await _CopyFinalPartAsync(fileId, uploadId, partialFiles, part, token)
                                .ConfigureAwait(false)
                            : await _StreamFinalPartAsync(fileId, uploadId, partialFiles, part, token)
                                .ConfigureAwait(false);

                        etags[part.Number - 1] = new PartETag(part.Number, etag);
                    }
                )
                .ConfigureAwait(false);

            var request = new CompleteMultipartUploadRequest
            {
                BucketName = _options.Bucket,
                Key = _keys.FinishedObject(fileId),
                UploadId = uploadId,
                PartETags = [.. etags],
            };

            await _s3.CompleteMultipartUploadAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await _AbortFinalMultipartUploadAsync(fileId, uploadId).ConfigureAwait(false);

            throw;
        }
    }

    private async Task<string> _CopyFinalPartAsync(
        string fileId,
        string uploadId,
        string[] partialFiles,
        TusS3ConcatPart part,
        CancellationToken cancellationToken
    )
    {
        var source = part.Sources[0];
        var partialFileId = partialFiles[source.PartialIndex];

        var request = new CopyPartRequest
        {
            SourceBucket = _options.Bucket,
            SourceKey = _keys.FinishedObject(partialFileId),
            DestinationBucket = _options.Bucket,
            DestinationKey = _keys.FinishedObject(fileId),
            UploadId = uploadId,
            PartNumber = part.Number,
            FirstByte = source.Offset,
            LastByte = source.Offset + source.Length - 1,
        };

        try
        {
            var response = await _s3.CopyPartAsync(request, cancellationToken).ConfigureAwait(false);

            return response.ETag;
        }
        catch (AmazonS3Exception e) when (_IsMissingObject(e))
        {
            throw new TusStoreException($"Partial file {partialFileId} does not exist");
        }
    }

    private async Task<string> _StreamFinalPartAsync(
        string fileId,
        string uploadId,
        string[] partialFiles,
        TusS3ConcatPart part,
        CancellationToken cancellationToken
    )
    {
        var buffer = _appendServices.PartPool.Rent((int)part.Length);

        try
        {
            await _ReadConcatSourcesAsync(partialFiles, part, buffer, cancellationToken).ConfigureAwait(false);

            return await TusS3Requests
                .UploadPartAsync(
                    _s3,
                    _options,
                    _keys.FinishedObject(fileId),
                    uploadId,
                    part.Number,
                    buffer,
                    (int)part.Length,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        finally
        {
            _appendServices.ReturnPartBuffer(buffer);
        }
    }

    // The ranges fill non-overlapping slices of the buffer, so they are read concurrently, under the same bound as
    // the part uploads because each read holds a connection.
    private async Task _ReadConcatSourcesAsync(
        string[] partialFiles,
        TusS3ConcatPart part,
        byte[] buffer,
        CancellationToken cancellationToken
    )
    {
        var positions = new int[part.Sources.Count];

        for (var i = 1; i < positions.Length; i++)
        {
            positions[i] = positions[i - 1] + (int)part.Sources[i - 1].Length;
        }

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxConcurrentPartUploads,
            CancellationToken = cancellationToken,
        };

        await Parallel
            .ForAsync(
                0,
                positions.Length,
                parallelOptions,
                async (i, token) =>
                {
                    var source = part.Sources[i];
                    var partialFileId = partialFiles[source.PartialIndex];

                    try
                    {
                        await TusS3Requests
                            .ReadRangeAsync(
                                _s3,
                                _options.Bucket,
                                _keys.FinishedObject(partialFileId),
                                source.Offset,
                                buffer.AsMemory(positions[i], (int)source.Length),
                                () =>
                                    $"The finished object of file {partialFileId} holds fewer bytes than its upload recorded.",
                                token
                            )
                            .ConfigureAwait(false);
                    }
                    catch (AmazonS3Exception e) when (_IsMissingObject(e))
                    {
                        throw new TusStoreException($"Partial file {partialFileId} does not exist");
                    }
                }
            )
            .ConfigureAwait(false);
    }

    private async Task _AbortFinalMultipartUploadAsync(string fileId, string uploadId)
    {
        try
        {
            await _s3.AbortMultipartUploadAsync(
                    new AbortMultipartUploadRequest
                    {
                        BucketName = _options.Bucket,
                        Key = _keys.FinishedObject(fileId),
                        UploadId = uploadId,
                    },
                    CancellationToken.None
                )
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The build already failed with its own exception, which the caller rethrows; a failed abort only leaves parts for the provider's abort rule.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _logger.LogS3FinalFileAbortFailed(e, fileId, uploadId);
        }
    }

    // A ranged read or part copy whose source object is gone: the partial was removed after it was checked.
    private static bool _IsMissingObject(AmazonS3Exception exception)
    {
        return exception.StatusCode is HttpStatusCode.NotFound && exception.HasErrorCode(TusS3ErrorCodes.NoSuchKey);
    }
}

internal static partial class TusS3StoreConcatenationLog
{
    [LoggerMessage(
        EventId = 3255,
        EventName = "S3FinalFileCreated",
        Level = LogLevel.Information,
        Message = "Created final file {FileId} from {PartialCount} partial files, {UploadLength} bytes in {PartCount} parts ({CopiedPartCount} copied by the provider)"
    )]
    internal static partial void LogS3FinalFileCreated(
        this ILogger logger,
        string fileId,
        int partialCount,
        long uploadLength,
        int partCount,
        int copiedPartCount
    );

    [LoggerMessage(
        EventId = 3256,
        EventName = "S3FinalFileCreateFailed",
        Level = LogLevel.Error,
        Message = "Failed to create a final file from partial files {PartialFileIds}"
    )]
    internal static partial void LogS3FinalFileCreateFailed(this ILogger logger, Exception e, string partialFileIds);

    [LoggerMessage(
        EventId = 3257,
        EventName = "S3FinalFileAbortFailed",
        Level = LogLevel.Warning,
        Message = "Failed to abort the multipart upload {UploadId} of final file {FileId}; its parts stay billed until the provider aborts it under a bucket lifecycle rule"
    )]
    internal static partial void LogS3FinalFileAbortFailed(
        this ILogger logger,
        Exception e,
        string fileId,
        string uploadId
    );
}
