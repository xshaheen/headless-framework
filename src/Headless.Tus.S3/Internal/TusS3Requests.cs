// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Amazon.S3;
using Amazon.S3.Model;

namespace Headless.Tus.Internal;

/// <summary>The storage requests more than one store path sends, built in one place so every path sends them alike.</summary>
internal static class TusS3Requests
{
    /// <summary>S3's DeleteObjects accepts at most 1,000 keys per call.</summary>
    public const int MaxKeysPerDelete = 1000;

    /// <summary>Uploads the first <paramref name="length"/> bytes of <paramref name="buffer"/> as one part.</summary>
    /// <returns>the part's ETag</returns>
    public static async Task<string> UploadPartAsync(
        IAmazonS3 s3,
        TusS3StoreOptions options,
        string key,
        string? uploadId,
        int partNumber,
        byte[] buffer,
        int length,
        CancellationToken cancellationToken
    )
    {
        await using var body = new MemoryStream(buffer, 0, length, writable: false);

        var request = new UploadPartRequest
        {
            BucketName = options.Bucket,
            Key = key,
            UploadId = uploadId,
            PartNumber = partNumber,
            InputStream = body,
        };

        TusS3RequestPresets.Apply(request, options.Provider);

        var response = await s3.UploadPartAsync(request, cancellationToken).ConfigureAwait(false);

        return response.ETag;
    }

    /// <summary>
    /// Fills <paramref name="destination"/> from the object, starting <paramref name="from"/> bytes into it, with one
    /// ranged read.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The object holds fewer bytes than the range needs; <paramref name="describeShortRead"/> supplies the message.
    /// </exception>
    public static async Task ReadRangeAsync(
        IAmazonS3 s3,
        string bucket,
        string key,
        long from,
        Memory<byte> destination,
        Func<string> describeShortRead,
        CancellationToken cancellationToken
    )
    {
        var request = new GetObjectRequest
        {
            BucketName = bucket,
            Key = key,
            ByteRange = new ByteRange(from, from + destination.Length - 1),
        };

        using var response = await s3.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);
        await using var content = response.ResponseStream;

        try
        {
            await content.ReadExactlyAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException(describeShortRead(), e);
        }
    }

    /// <summary>Deletes the keys in batches, failing if storage refused any of them.</summary>
    /// <param name="createFailure">builds the caller's exception from the failure message</param>
    public static async Task DeleteKeysAsync(
        IAmazonS3 s3,
        string bucket,
        IEnumerable<string> keys,
        Func<string, Exception> createFailure,
        CancellationToken cancellationToken
    )
    {
        foreach (var batch in keys.Chunk(MaxKeysPerDelete))
        {
            var response = await s3.DeleteObjectsAsync(
                    new DeleteObjectsRequest
                    {
                        BucketName = bucket,
                        Objects = [.. batch.Select(key => new KeyVersion { Key = key })],
                        Quiet = true,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            // DeleteObjects reports per-key failures in a 200 response instead of throwing, so a removal that left
            // objects behind would otherwise look successful.
            if (response.DeleteErrors is { Count: > 0 } errors)
            {
                var first = errors[0];

                throw createFailure(
                    $"Failed to delete {errors.Count} tus object(s) from bucket '{bucket}'; first: '{first.Key}' ({first.Code}: {first.Message})."
                );
            }
        }
    }

    /// <summary>Lists every multipart upload under <paramref name="prefix"/>, one page per request as it is consumed.</summary>
    public static async IAsyncEnumerable<MultipartUpload> ListMultipartUploadsAsync(
        IAmazonS3 s3,
        string bucket,
        string prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var request = new ListMultipartUploadsRequest { BucketName = bucket, Prefix = prefix };
        ListMultipartUploadsResponse response;

        do
        {
            response = await s3.ListMultipartUploadsAsync(request, cancellationToken).ConfigureAwait(false);

            foreach (var upload in response.MultipartUploads ?? [])
            {
                yield return upload;
            }

            request.KeyMarker = response.NextKeyMarker;
            request.UploadIdMarker = response.NextUploadIdMarker;
        } while (response.IsTruncated is true);
    }
}
