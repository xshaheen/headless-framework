// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Tus;
using Headless.Tus.Internal;

namespace Tests.TestSetup;

/// <summary>
/// Reads and writes a store's objects behind its back, to assert what it stored and to stage states (a stored offset,
/// an old multipart upload) that the operations a test can drive at this point cannot reach.
/// </summary>
internal sealed class TusS3RawObjects(S3TestClient client, TusS3StoreOptions options)
{
    public TusS3Keys Keys { get; } = new(options.NormalizedPrefix);

    public async Task<TusS3UploadState?> ReadStateAsync(string fileId, CancellationToken cancellationToken)
    {
        var content = await _TryGetAsync(Keys.Info(fileId), cancellationToken);

        return content is null ? null : TusS3UploadState.FromJson(content);
    }

    public async Task<TusS3CompletedRecord?> ReadCompletedRecordAsync(
        string fileId,
        CancellationToken cancellationToken
    )
    {
        var content = await _TryGetAsync(Keys.CompletedRecord(fileId), cancellationToken);

        return content is null ? null : TusS3CompletedRecord.FromJson(content);
    }

    /// <summary>Changes the stored state unconditionally, as another writer would.</summary>
    public async Task RewriteStateAsync(
        string fileId,
        Action<TusS3UploadState> change,
        CancellationToken cancellationToken
    )
    {
        var state = await ReadStateAsync(fileId, cancellationToken);
        state.Should().NotBeNull();
        change(state!);

        await using var body = new MemoryStream(state!.ToJson());
        var request = new PutObjectRequest
        {
            BucketName = options.Bucket,
            Key = Keys.Info(fileId),
            InputStream = body,
            AutoCloseStream = false,
        };
        TusS3RequestPresets.Apply(request, client.Provider);

        await client.S3.PutObjectAsync(request, cancellationToken);
    }

    /// <summary>The object's size, or <see langword="null" /> when it does not exist.</summary>
    public async Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await client.S3.GetObjectMetadataAsync(options.Bucket, key, cancellationToken);

            return metadata.ContentLength;
        }
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<byte[]?> _TryGetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.S3.GetObjectAsync(options.Bucket, key, cancellationToken);
            await using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);

            return buffer.ToArray();
        }
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
