// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3.Model;
using Headless.Tus;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests.Conformance;

/// <summary>
/// Registers the S3 store for the upload endpoint scenarios the way an application does, through <c>UseS3</c>,
/// under a key prefix of its own inside the bucket the conformance suites share.
/// </summary>
internal sealed class TusS3UploadLayerBackend(IS3TestBackend backend) : ITusUploadLayerConformanceBackend
{
    private readonly S3TestClient _client = backend.CreateClient();
    private readonly string _prefix = $"upload-layer-{Guid.NewGuid():N}/";

    // The store keeps the metadata in an object's body and sets no limit of its own, so this is a length a
    // client can still send in one request's headers on a server with default limits.
    public int MaxMetadataLength => 16 * 1024;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(_client.S3);
    }

    public void UseStore(HeadlessTusSetupBuilder tus)
    {
        tus.UseS3(options =>
        {
            options.Bucket = TusS3StoreFactory.Bucket;
            options.Prefix = _prefix;
            options.Provider = _client.Provider;
        });
    }

    public async Task<IReadOnlyList<string>> ListStoredNamesAsync(CancellationToken cancellationToken)
    {
        var keys = new List<string>();
        var request = new ListObjectsV2Request { BucketName = TusS3StoreFactory.Bucket, Prefix = _prefix };
        ListObjectsV2Response response;

        do
        {
            response = await _client.S3.ListObjectsV2Async(request, cancellationToken);
            keys.AddRange((response.S3Objects ?? []).Select(static o => o.Key));
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);

        return keys;
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();

        return ValueTask.CompletedTask;
    }
}
