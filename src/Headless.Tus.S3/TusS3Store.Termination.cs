// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using Amazon.S3.Model;
using Headless.Tus.Internal;
using tusdotnet.Models;

namespace Headless.Tus;

public sealed partial class TusS3Store
{
    /// <summary>
    /// Removes everything the upload owns: every multipart upload under its finished key, its finished object and
    /// completed record, and its working set (state object and tail segments, recorded or not).
    /// </summary>
    /// <param name="fileId">the TUS file identifier to delete</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <remarks>
    /// Works for an incomplete, completing, or completed upload, and succeeds when nothing is stored under the id. The
    /// state object goes last: until it is deleted the upload still reads as present, so a termination that fails
    /// partway leaves an upload the client can terminate again rather than half an upload that reads as gone.
    /// <para>
    /// A storage failure is logged and rethrown, so a tus <c>DELETE</c> never reports success while objects remain.
    /// </para>
    /// </remarks>
    /// <exception cref="TusStoreException">The id is invalid.</exception>
    /// <exception cref="AmazonS3Exception">Storage refused a listing, an abort, or a delete.</exception>
    public async Task DeleteFileAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        try
        {
            var workingSet = await _ListWorkingSetAsync(fileId, cancellationToken).ConfigureAwait(false);

            // Every multipart upload on the key, not only the recorded one: a crashed or stale writer can leave one the
            // state never recorded, and it would otherwise hold its parts until the provider's abort rule.
            await _AbortMultipartUploadsOnFinishedKeyAsync(fileId, cancellationToken).ConfigureAwait(false);
            await _DeleteUploadObjectsAsync(fileId, workingSet, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogS3FileDeleteFailed(e, fileId);

            throw;
        }
        finally
        {
            _stateStore.Forget(fileId);
        }

        _logger.LogS3FileDeleted(fileId);
    }

    /// <summary>The keys of every object under the upload's working set, its state object included.</summary>
    private async Task<List<string>> _ListWorkingSetAsync(string fileId, CancellationToken cancellationToken)
    {
        var keys = new List<string>();
        var request = new ListObjectsV2Request { BucketName = _options.Bucket, Prefix = _keys.WorkingSet(fileId) };
        ListObjectsV2Response response;

        do
        {
            response = await _s3.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);
            keys.AddRange((response.S3Objects ?? []).Select(o => o.Key));
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated is true);

        return keys;
    }

    private async Task _AbortMultipartUploadsOnFinishedKeyAsync(string fileId, CancellationToken cancellationToken)
    {
        var finishedKey = _keys.FinishedObject(fileId);
        await foreach (
            var upload in TusS3Requests
                .ListMultipartUploadsAsync(_s3, _options.Bucket, finishedKey, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            // The prefix also matches longer keys, which belong to other uploads.
            if (string.Equals(upload.Key, finishedKey, StringComparison.Ordinal))
            {
                await _AbortMultipartUploadIfExistsAsync(fileId, upload.UploadId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>Aborts one multipart upload of the file, treating one that no longer exists as already aborted.</summary>
    private async Task _AbortMultipartUploadIfExistsAsync(
        string fileId,
        string uploadId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var request = new AbortMultipartUploadRequest
            {
                BucketName = _options.Bucket,
                Key = _keys.FinishedObject(fileId),
                UploadId = uploadId,
            };

            await _s3.AbortMultipartUploadAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (e.HasErrorCode(TusS3ErrorCodes.NoSuchUpload))
        {
            // Completed, aborted by another request, or removed by the provider's abort rule: nothing is left to free.
        }
    }

    /// <summary>
    /// Deletes the upload's finished object and completed record together with its listed working set, then its state
    /// object last.
    /// </summary>
    private async Task _DeleteUploadObjectsAsync(
        string fileId,
        List<string> workingSet,
        CancellationToken cancellationToken
    )
    {
        var infoKey = _keys.Info(fileId);
        List<string> keys =
        [
            _keys.FinishedObject(fileId),
            _keys.CompletedRecord(fileId),
            .. workingSet.Where(key => !string.Equals(key, infoKey, StringComparison.Ordinal)),
        ];

        await _DeleteObjectKeysAsync(keys, cancellationToken).ConfigureAwait(false);

        if (workingSet.Contains(infoKey, StringComparer.Ordinal))
        {
            await _s3.DeleteObjectAsync(
                    new DeleteObjectRequest { BucketName = _options.Bucket, Key = infoKey },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    /// <summary>Deletes the keys in batches, failing if storage refused any of them.</summary>
    /// <exception cref="AmazonS3Exception">Storage refused the delete of at least one key.</exception>
    private Task _DeleteObjectKeysAsync(List<string> keys, CancellationToken cancellationToken)
    {
        return TusS3Requests.DeleteKeysAsync(
            _s3,
            _options.Bucket,
            keys,
            message => new AmazonS3Exception(message),
            cancellationToken
        );
    }
}
