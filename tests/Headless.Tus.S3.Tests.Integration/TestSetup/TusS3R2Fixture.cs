// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3.Model;
using Headless.Tus;
using Microsoft.Extensions.Logging;

namespace Tests.TestSetup;

/// <summary>
/// Cloudflare R2, reached with the configuration the docs give an application. The run gets its own bucket, created on
/// the first scenario and dropped with everything in it when the collection finishes, so a real account keeps no test
/// state and two runs never share uploads. The token must allow bucket creation.
/// </summary>
/// <remarks>
/// There is no R2 emulator, so every scenario that asks for a client skips when the R2_* environment variables are
/// absent (local runs, forks). The release-checks workflow refuses to start without them, so a skip there cannot pass
/// for a green run.
/// </remarks>
[UsedImplicitly]
[CollectionDefinition]
public sealed class TusS3R2Fixture
    : ICollectionFixture<TusS3R2Fixture>,
        ITusConformanceBackend<TusS3Store>,
        IS3TestBackend,
        IAsyncDisposable
{
    private readonly TusS3StoreFactory _stores;
    private volatile bool _bucketTouched;

    public TusS3R2Fixture()
    {
        _stores = new TusS3StoreFactory(Bucket);
    }

    public string Bucket { get; } = $"tus-conformance-{Guid.NewGuid():N}";

    /// <summary>Creates a gated client for the run's bucket, or skips the scenario without R2 credentials.</summary>
    public S3TestClient CreateClient()
    {
        var client = CreateAccountClient();
        // Set only once the credentials exist, so a skipped run never reaches R2 to drop a bucket it never made.
        _bucketTouched = true;

        return client;
    }

    public Task<TusS3Store> CreateStoreAsync(ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        return _stores.CreateAsync(CreateClient(), loggerFactory, cancellationToken);
    }

    /// <summary>
    /// A gated client for the R2 account in the R2_* environment variables, or a skip when any of them is empty.
    /// </summary>
    internal static S3TestClient CreateAccountClient()
    {
        var accountId = Environment.GetEnvironmentVariable("R2_ACCOUNT_ID");
        var accessKey = Environment.GetEnvironmentVariable("R2_ACCESS_KEY_ID");
        var secretKey = Environment.GetEnvironmentVariable("R2_SECRET_ACCESS_KEY");

        // skip: R2 credentials (R2_ACCOUNT_ID, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY); no R2 emulator exists
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(accountId)
                || string.IsNullOrWhiteSpace(accessKey)
                || string.IsNullOrWhiteSpace(secretKey),
            "R2 credentials are not configured (R2_ACCOUNT_ID, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY)."
        );

        return new S3TestClient(
            $"https://{accountId}.r2.cloudflarestorage.com",
            accessKey!,
            secretKey!,
            "auto",
            TusS3Provider.R2
        );
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_bucketTouched)
            {
                using var client = CreateAccountClient();
                await _DeleteBucketAsync(client);
            }
        }
        finally
        {
            _stores.Dispose();
        }
    }

    // Aborts the multipart uploads the scenarios left open, empties the bucket page by page, and drops it. Teardown
    // runs after the scenarios' own tokens are gone, so it does not take one.
    private async Task _DeleteBucketAsync(S3TestClient client)
    {
        var s3 = client.S3;
        var uploads = new ListMultipartUploadsRequest { BucketName = Bucket };

        while (true)
        {
            var page = await s3.ListMultipartUploadsAsync(uploads, CancellationToken.None);

            foreach (var upload in page.MultipartUploads ?? [])
            {
                await s3.AbortMultipartUploadAsync(Bucket, upload.Key, upload.UploadId, CancellationToken.None);
            }

            if (page.IsTruncated != true)
            {
                break;
            }

            uploads.KeyMarker = page.NextKeyMarker;
            uploads.UploadIdMarker = page.NextUploadIdMarker;
        }

        var objects = new ListObjectsV2Request { BucketName = Bucket };

        while (true)
        {
            var page = await s3.ListObjectsV2Async(objects, CancellationToken.None);
            var keys = (page.S3Objects ?? []).ConvertAll(o => new KeyVersion { Key = o.Key });

            if (keys.Count > 0)
            {
                await s3.DeleteObjectsAsync(
                    new DeleteObjectsRequest { BucketName = Bucket, Objects = keys },
                    CancellationToken.None
                );
            }

            if (page.IsTruncated != true)
            {
                break;
            }

            objects.ContinuationToken = page.NextContinuationToken;
        }

        await s3.DeleteBucketAsync(Bucket, CancellationToken.None);
    }
}
