// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using Headless.Tus.Internal;
using tusdotnet.Models;
using tusdotnet.Parsers;

namespace Headless.Tus;

public sealed partial class TusS3Store
{
    /// <summary>
    /// Creates a new TUS upload and returns its generated file identifier.
    /// </summary>
    /// <param name="uploadLength">
    /// total number of bytes the client will upload, or <c>-1</c> when the client used the Creation-Defer-Length
    /// extension (tusdotnet passes <c>-1</c> for a deferred length)
    /// </param>
    /// <param name="metadata">
    /// raw TUS metadata string from the Upload-Metadata header, or <see langword="null"/> if the client supplied none
    /// </param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>the unique file identifier assigned to the new upload</returns>
    /// <remarks>
    /// A zero-length upload is complete at creation: tusdotnet raises its completion event without an append, so the
    /// store writes the empty finished object right away.
    /// </remarks>
    /// <exception cref="TusStoreException">
    /// The metadata is malformed, or the length needs parts larger than <see cref="TusS3StoreOptions.MaxPartSize"/>.
    /// </exception>
    /// <exception cref="AmazonS3Exception">An upload with the generated id already exists (412).</exception>
    public Task<string> CreateFileAsync(long uploadLength, string? metadata, CancellationToken cancellationToken)
    {
        return _CreateAsync(uploadLength, metadata, concat: null, cancellationToken);
    }

    /// <summary>
    /// Creates a new partial TUS upload that a final upload will later concatenate.
    /// </summary>
    /// <param name="uploadLength">total bytes the client will upload to this partial upload, or <c>-1</c> when deferred</param>
    /// <param name="metadata">raw TUS metadata string from the Upload-Metadata header, or <see langword="null"/></param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>the unique file identifier assigned to the new partial upload</returns>
    /// <exception cref="TusStoreException">
    /// The metadata is malformed, or the length needs parts larger than <see cref="TusS3StoreOptions.MaxPartSize"/>.
    /// </exception>
    public Task<string> CreatePartialFileAsync(long uploadLength, string? metadata, CancellationToken cancellationToken)
    {
        return _CreateAsync(uploadLength, metadata, new TusS3ConcatInfo(TusS3ConcatKind.Partial), cancellationToken);
    }

    private async Task<string> _CreateAsync(
        long uploadLength,
        string? metadata,
        TusS3ConcatInfo? concat,
        CancellationToken cancellationToken
    )
    {
        try
        {
            _EnsureValidMetadata(metadata);

            // tusdotnet passes -1 for Upload-Defer-Length; storing it would report a real length on HEAD.
            long? length = uploadLength >= 0 ? uploadLength : null;
            var partSize = TusS3PartSize.ForUpload(length, _options);
            var fileId = TusS3Keys.EnsureValid(await _fileIdProvider.CreateId(metadata).ConfigureAwait(false));

            if (length == 0)
            {
                // The finished object goes first: a record without its object would read as a completed upload whose
                // content is missing.
                await _PutFinishedObjectAsync(fileId, metadata, [], 0, cancellationToken).ConfigureAwait(false);

                var record = new TusS3CompletedRecord
                {
                    UploadLength = 0,
                    Metadata = metadata,
                    Concat = concat,
                };

                await _stateStore.CreateCompletedRecordAsync(fileId, record, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var state = new TusS3UploadState
                {
                    PartSize = partSize,
                    UploadLength = length,
                    Metadata = metadata,
                    Concat = concat,
                };

                await _stateStore.CreateAsync(fileId, state, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogS3FileCreated(fileId, uploadLength);

            return fileId;
        }
        catch (Exception e)
        {
            _logger.LogS3FileCreateFailed(e, uploadLength);

            throw;
        }
    }

    // tusdotnet validates the header before calling the store, but a direct caller may not; HEAD echoes the stored
    // string, so a malformed one would surface to every client that reads the upload.
    private static void _EnsureValidMetadata(string? metadata)
    {
        if (string.IsNullOrEmpty(metadata))
        {
            return;
        }

        var parseResult = MetadataParser.ParseAndValidate(MetadataParsingStrategy.AllowEmptyValues, metadata);

        if (!parseResult.Success)
        {
            throw new TusStoreException(parseResult.ErrorMessage);
        }
    }
}
