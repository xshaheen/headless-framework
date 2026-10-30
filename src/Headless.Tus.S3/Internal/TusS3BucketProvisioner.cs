// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Tus.Internal;

/// <summary>
/// Offers the upload bucket as a provisioned resource, so a deploy step creates it with
/// <c>resources provision -k S3Bucket</c> and a serving host needs only object permissions, never bucket-create.
/// </summary>
internal sealed class TusS3BucketProvisioner(IAmazonS3 s3, TusS3StoreOptions options, ILoggerFactory? loggerFactory)
    : IResourceProvisioner
{
    private readonly ILogger _logger = (
        loggerFactory ?? NullLoggerFactory.Instance
    ).CreateLogger<TusS3BucketProvisioner>();

    private readonly string _prefix = options.NormalizedPrefix;

    public string Kind => "S3Bucket";

    public string Name => options.Bucket;

    public async Task ProvisionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await s3.PutBucketAsync(new PutBucketRequest { BucketName = options.Bucket }, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogS3BucketCreated(options.Bucket);
        }
        catch (AmazonS3Exception e) when (e.HasErrorCode(TusS3ErrorCodes.BucketAlreadyOwnedByYou))
        {
            // The bucket already exists and this account owns it, so provisioning is a repeatable deploy step.
        }
    }

    // One cheap read: ListObjectsV2 answers 404 NoSuchBucket for a missing bucket on S3, R2, and compatible servers,
    // and needs only the object-list permission a serving host already has.
    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        if (!await _BucketExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ResourceProvisioningException(
                $"The tus upload bucket '{options.Bucket}' does not exist. Run 'resources provision -k S3Bucket' to create it."
            );
        }
    }

    // Purge removes only what the store owns: the bucket and even the prefix can hold objects another writer put there.
    public async Task PurgeAsync(CancellationToken cancellationToken)
    {
        if (await _BucketExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            await _DeleteStoreObjectsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // S3 refuses to delete a non-empty bucket, and the bucket may hold another feature's objects, so the bucket goes
    // only when nothing is left after the store's own objects are removed.
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (!await _BucketExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await _DeleteStoreObjectsAsync(cancellationToken).ConfigureAwait(false);

        var remaining = await s3.ListObjectsV2Async(
                new ListObjectsV2Request { BucketName = options.Bucket, MaxKeys = 1 },
                cancellationToken
            )
            .ConfigureAwait(false);

        if (remaining.S3Objects is { Count: > 0 })
        {
            _logger.LogS3BucketKeptWithForeignObjects(options.Bucket);

            return;
        }

        try
        {
            await s3.DeleteBucketAsync(new DeleteBucketRequest { BucketName = options.Bucket }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            // Another deploy step removed it first.
        }
    }

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var exists = await _BucketExistsAsync(cancellationToken).ConfigureAwait(false);

        return [new("bucket", exists ? "exists" : "missing"), new("prefix", _prefix)];
    }

    private async Task<bool> _BucketExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await s3.ListObjectsV2Async(
                    new ListObjectsV2Request { BucketName = options.Bucket, MaxKeys = 1 },
                    cancellationToken
                )
                .ConfigureAwait(false);

            return true;
        }
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task _DeleteStoreObjectsAsync(CancellationToken cancellationToken)
    {
        var completedPrefix = _prefix + TusS3Keys.CompletedFolder;

        // A completed record names its finished object, which lives directly under the prefix beside foreign keys.
        // Each finished object goes before its record, so a purge interrupted part-way can simply run again.
        await _ForEachPageAsync(
                completedPrefix,
                async (keys, token) =>
                {
                    var finishedObjects = keys.ConvertAll(key => _prefix + key[completedPrefix.Length..]);

                    await _DeleteKeysAsync(finishedObjects, token).ConfigureAwait(false);
                    await _DeleteKeysAsync(keys, token).ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        await _ForEachPageAsync(_prefix + TusS3Keys.WorkingSetFolder, _DeleteKeysAsync, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task _ForEachPageAsync(
        string keyPrefix,
        Func<List<string>, CancellationToken, Task> processPage,
        CancellationToken cancellationToken
    )
    {
        var request = new ListObjectsV2Request
        {
            BucketName = options.Bucket,
            Prefix = keyPrefix,
            MaxKeys = TusS3Requests.MaxKeysPerDelete,
        };

        ListObjectsV2Response response;

        do
        {
            response = await s3.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);

            var keys = (response.S3Objects ?? []).ConvertAll(o => o.Key);

            if (keys.Count > 0)
            {
                await processPage(keys, cancellationToken).ConfigureAwait(false);
            }

            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated is true);
    }

    private Task _DeleteKeysAsync(List<string> keys, CancellationToken cancellationToken)
    {
        return TusS3Requests.DeleteKeysAsync(
            s3,
            options.Bucket,
            keys,
            message => new ResourceProvisioningException(message),
            cancellationToken
        );
    }
}
