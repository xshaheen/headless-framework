// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Blobs.Internals;
using Headless.Checks;
using Headless.Threading;

namespace Headless.Blobs.Aws;

/// <summary>
/// <see cref="IBlobContainerManager"/> implementation for Amazon S3 buckets. Registered <b>only</b> by the AWS
/// provider, never by Cloudflare R2 — R2 reuses <see cref="AwsBlobStorage"/> for the data plane but its
/// object-scoped tokens cannot create buckets, so it deliberately ships no manager and the capability resolves to
/// <see langword="null"/>.
/// </summary>
/// <remarks>
/// This is a dedicated type rather than <see cref="AwsBlobStorage"/> implementing the interface, so the capability
/// is discoverable only where it is honestly supported (KTD5). It owns its own per-store <see cref="IAmazonS3"/>
/// client (mirroring the storage's per-instance isolation) and releases it on disposal.
/// </remarks>
internal sealed class AwsBlobContainerManager : IBlobContainerManager, IDisposable
{
    private readonly IAmazonS3 _s3;
    private readonly IBlobNamingNormalizer _normalizer;

    // Buckets this instance has already ensured exist, so the ensure round trip runs at most once per bucket. A
    // bucket is recorded only after a successful ensure, so a failed ensure is naturally retried (L5). The per-bucket
    // lock serializes concurrent first-time ensures of the same bucket while letting distinct buckets run in
    // parallel, and DeleteContainerAsync takes the same lock so an ensure never races a delete of the same bucket.
    private readonly ConcurrentDictionary<string, byte> _ensuredBuckets = new(StringComparer.Ordinal);
    private readonly KeyedAsyncLock _ensureBucketLock = new();

    public AwsBlobContainerManager(IAmazonS3 s3, IBlobNamingNormalizer normalizer)
    {
        _s3 = Argument.IsNotNull(s3);
        _normalizer = Argument.IsNotNull(normalizer);
    }

    public async ValueTask EnsureContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        var bucket = _NormalizeContainer(container);

        await _EnsureBucketOnceAsync(bucket, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default)
    {
        var bucket = _NormalizeContainer(container);

        // A cancellation-aware HEAD-style probe: ListObjectsV2 returns 404 (NoSuchBucket) for a missing bucket on
        // both S3 and S3-compatible endpoints, and unlike AmazonS3Util.DoesS3BucketExistV2Async it accepts a token.
        try
        {
            await _s3.ListObjectsV2Async(
                    new ListObjectsV2Request { BucketName = bucket, MaxKeys = 1 },
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

    public async ValueTask<bool> DeleteContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        var bucket = _NormalizeContainer(container);

        // Serialize with the ensure path's per-bucket lock and evict the ensured-cache entry BEFORE touching the
        // backend. Evicting only after the backend delete leaves a window where a concurrent ensure fast-path sees
        // the stale entry and reports success for a bucket mid-deletion; holding the lock makes a concurrent
        // first-time ensure wait and re-create the bucket only after this delete completes.
        using (await _ensureBucketLock.LockAsync(bucket, cancellationToken).ConfigureAwait(false))
        {
            _ensuredBuckets.TryRemove(bucket, out _);

            // S3 refuses to delete a non-empty bucket, so drain its objects first. A missing bucket reports not-found.
            if (!await _EmptyBucketAsync(bucket, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            try
            {
                await _s3.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucket }, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
            {
                return false;
            }

            return true;
        }
    }

    private async ValueTask<bool> _EmptyBucketAsync(string bucket, CancellationToken cancellationToken)
    {
        var listRequest = new ListObjectsV2Request { BucketName = bucket, MaxKeys = 1000 };
        var existed = false;

        ListObjectsV2Response listResponse;

        do
        {
            try
            {
                listResponse = await _s3.ListObjectsV2Async(listRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
            {
                return existed;
            }

            existed = true;
            listRequest.ContinuationToken = listResponse.NextContinuationToken;

            var keys = (listResponse.S3Objects ?? [])
                .Where(o => o is not null)
                .Select(o => new KeyVersion { Key = o.Key })
                .ToList();

            if (keys.Count > 0)
            {
                await _s3.DeleteObjectsAsync(
                        new DeleteObjectsRequest
                        {
                            BucketName = bucket,
                            Objects = keys,
                            Quiet = true,
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
        } while (listResponse.IsTruncated is true && !cancellationToken.IsCancellationRequested);

        return existed;
    }

    private async Task _EnsureBucketOnceAsync(string bucket, CancellationToken cancellationToken)
    {
        if (_ensuredBuckets.ContainsKey(bucket))
        {
            return;
        }

        using (await _ensureBucketLock.LockAsync(bucket, cancellationToken).ConfigureAwait(false))
        {
            // Re-check under the lock: a concurrent caller may have ensured this bucket while we waited.
            if (_ensuredBuckets.ContainsKey(bucket))
            {
                return;
            }

            await _CreateBucketAsync(bucket, cancellationToken).ConfigureAwait(false);

            // Record only after a successful create so a failed ensure is retried next time.
            _ensuredBuckets.TryAdd(bucket, 0);
        }
    }

    private async Task _CreateBucketAsync(string bucket, CancellationToken cancellationToken)
    {
        // Idempotent, cancellation-aware create: PutBucket directly (it carries a CancellationToken, unlike the
        // DoesS3BucketExistV2Async HEAD probe) and treat "already owned by you" as success.
        try
        {
            await _s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AmazonS3Exception e)
            when (string.Equals(e.ErrorCode, "BucketAlreadyOwnedByYou", StringComparison.Ordinal))
        {
            // The bucket already exists and we own it.
        }
    }

    private string _NormalizeContainer(string container)
    {
        return BlobLocationResolver.ResolveContainer(container, _normalizer);
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ensureBucketLock.Dispose();

        // This manager owns its per-store S3 client (built by the DI factory), so it releases the HTTP handler/sockets.
        (_s3 as IDisposable)?.Dispose();
    }
}
