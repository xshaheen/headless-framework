// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Tus.Internal;
using tusdotnet.Models;

namespace Headless.Tus;

public sealed partial class TusS3Store
{
    /// <summary>
    /// Turns a completing upload into its finished object and completed record. Safe to run again, by the same or
    /// another request, after any step failed: every step derives from the completing state, which no request changes
    /// except to mark the upload gone.
    /// </summary>
    /// <remarks>
    /// Storage calls take no request token. The bytes are durable before this starts, and a client that disconnects
    /// after sending its last byte should still get a finished upload rather than one it must finish by re-sending.
    /// </remarks>
    /// <exception cref="TusStoreException">The provider no longer holds the upload's parts; the upload is now gone.</exception>
    /// <exception cref="InvalidDataException">The state's parts and segments do not add up to the upload length.</exception>
    private async Task _FinishCompletionAsync(string fileId, TusS3LoadedUpload upload)
    {
        var state = upload.State ?? throw new InvalidOperationException("A completed upload needs no completion.");
        var length = state.UploadLength ?? throw new InvalidDataException("A completing upload has no length.");
        var parts = state.Parts.OrderBy(part => part.Number).ToList();

        _EnsureContiguous(fileId, parts, state.Segments, length);

        if (state.MultipartUploadId is null)
        {
            await _WriteFinishedObjectFromSegmentsAsync(fileId, state, length).ConfigureAwait(false);
        }
        else
        {
            await _CompleteMultipartUploadAsync(fileId, upload, parts, length).ConfigureAwait(false);
        }

        var record = new TusS3CompletedRecord
        {
            UploadLength = length,
            Metadata = state.Metadata,
            Concat = state.Concat,
        };

        await _stateStore.CommitCompletionAsync(fileId, record, CancellationToken.None).ConfigureAwait(false);
        await _DeleteSegmentsAsync(fileId, state.Segments).ConfigureAwait(false);

        _logger.LogS3UploadCompleted(fileId, length);
    }

    // An append commits parts and segments as one contiguous run, so anything else means the state was changed out of
    // band; assembling it would produce an object that silently differs from what the client sent.
    private static void _EnsureContiguous(
        string fileId,
        List<TusS3CommittedPart> parts,
        List<TusS3CommittedSegment> segments,
        long length
    )
    {
        var end = parts.Sum(part => part.Size);

        foreach (var segment in segments)
        {
            if (segment.Offset != end)
            {
                break;
            }

            end += segment.Length;
        }

        if (end != length)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The stored parts and tail of file {fileId} do not cover its {length}-byte upload length."
                )
            );
        }
    }

    // Without a multipart upload every byte sits in tail segments, which together stay below one part.
    private async Task _WriteFinishedObjectFromSegmentsAsync(string fileId, TusS3UploadState state, long length)
    {
        if (length > _options.MaxPartSize)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"File {fileId} holds {length} bytes in tail segments, more than one object write can take."
                )
            );
        }

        // The pool hands out and takes back only arrays of its bucket sizes, so an empty upload uses no pooled array.
        var buffer = length == 0 ? [] : _appendServices.PartPool.Rent((int)length);

        try
        {
            var position = 0;

            foreach (var segment in state.Segments)
            {
                await TusS3PartWriter
                    .ReadSegmentAsync(
                        _appendServices,
                        segment,
                        0,
                        buffer.AsMemory(position, (int)segment.Length),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                position += (int)segment.Length;
            }

            await _PutFinishedObjectAsync(fileId, state.Metadata, buffer, (int)length, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.PreconditionFailed)
        {
            // An earlier attempt already wrote it; the object is only ever written from these same segments.
            if (!await _FinishedObjectHasLengthAsync(fileId, length).ConfigureAwait(false))
            {
                throw;
            }

            _logger.LogS3FinishedObjectAlreadyWritten(fileId);
        }
        finally
        {
            if (buffer.Length > 0)
            {
                // clearArray: the buffer held upload bytes that must not reach another pool user.
                _appendServices.PartPool.Return(buffer, clearArray: true);
            }
        }
    }

    private async Task _CompleteMultipartUploadAsync(
        string fileId,
        TusS3LoadedUpload upload,
        List<TusS3CommittedPart> parts,
        long length
    )
    {
        var state = upload.State!;

        try
        {
            List<PartETag> etags = [.. parts.Select(part => new PartETag(part.Number, part.ETag))];

            // Normally empty: the final append uploads the last bytes as the last part. Segments remain when that
            // part failed and its bytes were kept as tail instead, or when a declared length ended the upload.
            if (state.Segments.Count > 0)
            {
                etags.AddRange(await _UploadSegmentsAsPartsAsync(fileId, state).ConfigureAwait(false));
            }

            var request = new CompleteMultipartUploadRequest
            {
                BucketName = _options.Bucket,
                Key = _keys.FinishedObject(fileId),
                UploadId = state.MultipartUploadId,
                PartETags = etags,
            };

            // Unconditional: R2 documents no conditional complete, and the completing state already fences who may
            // get here. A second attempt meets NoSuchUpload below once the first has completed.
            await _s3.CompleteMultipartUploadAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (_IsErrorCode(e, "NoSuchUpload"))
        {
            if (await _FinishedObjectHasLengthAsync(fileId, length).ConfigureAwait(false))
            {
                _logger.LogS3FinishedObjectAlreadyWritten(fileId);

                return;
            }

            await _MarkGoneAsync(fileId, upload, e.ErrorCode).ConfigureAwait(false);

            throw new TusStoreException($"File {fileId} does not exist: its multipart upload is gone");
        }
        catch (AmazonS3Exception e)
            when (_IsErrorCode(e, "InvalidPart")
                || _IsErrorCode(e, "InvalidPartOrder")
                || _IsErrorCode(e, "EntityTooSmall")
            )
        {
            // The provider lost or refuses the recorded parts; retrying cannot change that, so the upload is gone.
            await _MarkGoneAsync(fileId, upload, e.ErrorCode).ConfigureAwait(false);

            throw new TusStoreException($"File {fileId} does not exist: its parts can no longer be assembled");
        }
    }

    // Uploads the tail as the upload's last parts, all of the part size but the last. The numbers start at the state's
    // counter, which no committed part uses; every attempt uploads the same bytes under the same numbers, so a repeat
    // replaces a part with identical content.
    private async Task<List<PartETag>> _UploadSegmentsAsPartsAsync(string fileId, TusS3UploadState state)
    {
        var partSize = state.PartSize;
        var tailLength = state.Segments.Sum(segment => segment.Length);
        var partCount = (int)((tailLength + partSize - 1) / partSize);

        if (state.NextPartNumber + partCount - 1 > TusS3PartSize.MaxPartCount)
        {
            throw new TusStoreException($"File {fileId} has used every part number and can no longer be completed");
        }

        var etags = new List<PartETag>(partCount);
        var buffer = _appendServices.PartPool.Rent(partSize);

        try
        {
            var filled = 0;
            var partNumber = state.NextPartNumber;

            foreach (var segment in state.Segments)
            {
                var copied = 0L;

                while (copied < segment.Length)
                {
                    var take = (int)Math.Min(partSize - filled, segment.Length - copied);
                    await TusS3PartWriter
                        .ReadSegmentAsync(
                            _appendServices,
                            segment,
                            copied,
                            buffer.AsMemory(filled, take),
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);
                    copied += take;
                    filled += take;

                    if (filled == partSize)
                    {
                        etags.Add(
                            await _UploadPartAsync(fileId, state, partNumber++, buffer, filled).ConfigureAwait(false)
                        );
                        filled = 0;
                    }
                }
            }

            if (filled > 0)
            {
                etags.Add(await _UploadPartAsync(fileId, state, partNumber, buffer, filled).ConfigureAwait(false));
            }
        }
        finally
        {
            _appendServices.PartPool.Return(buffer, clearArray: true);
        }

        return etags;
    }

    private async Task<PartETag> _UploadPartAsync(
        string fileId,
        TusS3UploadState state,
        int partNumber,
        byte[] buffer,
        int length
    )
    {
        await using var body = new MemoryStream(buffer, 0, length, writable: false);

        var request = new UploadPartRequest
        {
            BucketName = _options.Bucket,
            Key = _keys.FinishedObject(fileId),
            UploadId = state.MultipartUploadId,
            PartNumber = partNumber,
            InputStream = body,
        };

        TusS3RequestPresets.Apply(request, _options.Provider);

        var response = await _s3.UploadPartAsync(request, CancellationToken.None).ConfigureAwait(false);

        return new PartETag(partNumber, response.ETag);
    }

    private async Task<bool> _FinishedObjectHasLengthAsync(string fileId, long length)
    {
        try
        {
            var metadata = await _s3.GetObjectMetadataAsync(
                    new GetObjectMetadataRequest { BucketName = _options.Bucket, Key = _keys.FinishedObject(fileId) },
                    CancellationToken.None
                )
                .ConfigureAwait(false);

            return metadata.ContentLength == length;
        }
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task _MarkGoneAsync(string fileId, TusS3LoadedUpload upload, string errorCode)
    {
        _logger.LogS3CompletionPartsRejected(fileId, errorCode);
        upload.State!.Status = TusS3UploadStatus.Gone;

        try
        {
            await _stateStore.UpdateAsync(fileId, upload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (TusS3StateStore.IsStaleWrite(e) || e.StatusCode is HttpStatusCode.NotFound)
        {
            // Another request already moved the upload on (finished it, or marked it gone itself).
        }
    }

    private async Task _DeleteSegmentsAsync(string fileId, List<TusS3CommittedSegment> segments)
    {
        if (segments.Count == 0)
        {
            return;
        }

        try
        {
            var request = new DeleteObjectsRequest
            {
                BucketName = _options.Bucket,
                Objects = [.. segments.Select(segment => new KeyVersion { Key = segment.Key })],
                Quiet = true,
            };

            await _s3.DeleteObjectsAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The upload is already complete; a leftover segment only costs storage until the upload is removed.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _logger.LogS3SegmentCleanupFailed(e, fileId, segments.Count);
        }
    }

    // Matched by code: the SDK throws a typed exception only for AbortMultipartUpload.
    private static bool _IsErrorCode(AmazonS3Exception exception, string errorCode)
    {
        return string.Equals(exception.ErrorCode, errorCode, StringComparison.Ordinal);
    }

    /// <summary>
    /// Writes a finished object in one request, with the headers the provider chose for the upload. Every finished
    /// object the store writes outside a multipart upload goes through here.
    /// </summary>
    /// <exception cref="AmazonS3Exception">A finished object already exists under the id (412).</exception>
    private async Task _PutFinishedObjectAsync(
        string fileId,
        string? metadata,
        byte[] content,
        int length,
        CancellationToken cancellationToken
    )
    {
        await using var body = new MemoryStream(content, 0, length, writable: false);

        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = _keys.FinishedObject(fileId),
            InputStream = body,
            AutoCloseStream = false,
            IfNoneMatch = "*",
        };

        _ApplyHeaders(await _GetObjectHeadersAsync(metadata).ConfigureAwait(false), request.Headers);
        TusS3RequestPresets.Apply(request, _options.Provider);

        await _s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<TusS3ObjectHeaders> _GetObjectHeadersAsync(string? metadata)
    {
        return _objectHeadersProvider.GetObjectHeadersAsync(TusS3File.DecodeMetadata(metadata));
    }

    private static void _ApplyHeaders(TusS3ObjectHeaders headers, HeadersCollection target)
    {
        if (headers.ContentType is not null)
        {
            target.ContentType = headers.ContentType;
        }

        if (headers.CacheControl is not null)
        {
            target.CacheControl = headers.CacheControl;
        }

        if (headers.ContentDisposition is not null)
        {
            target.ContentDisposition = headers.ContentDisposition;
        }

        if (headers.ContentEncoding is not null)
        {
            target.ContentEncoding = headers.ContentEncoding;
        }

        if (headers.ContentLanguage is not null)
        {
            target.ContentLanguage = headers.ContentLanguage;
        }
    }
}
