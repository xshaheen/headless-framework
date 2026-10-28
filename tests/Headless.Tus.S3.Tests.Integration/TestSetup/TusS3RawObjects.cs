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

    /// <summary>
    /// The bytes the stored state says are durable: its parts in number order, then its tail segments. The parts are
    /// read by completing the multipart upload, so call this once, at the end of a scenario.
    /// </summary>
    public async Task<byte[]> ReadDurableBytesAsync(string fileId, CancellationToken cancellationToken)
    {
        var state = await ReadStateAsync(fileId, cancellationToken);
        state.Should().NotBeNull();
        await using var content = new MemoryStream();

        if (state!.Parts.Count > 0)
        {
            await client.S3.CompleteMultipartUploadAsync(
                new CompleteMultipartUploadRequest
                {
                    BucketName = options.Bucket,
                    Key = Keys.FinishedObject(fileId),
                    UploadId = state.MultipartUploadId,
                    PartETags = [.. state.Parts.OrderBy(p => p.Number).Select(p => new PartETag(p.Number, p.ETag))],
                },
                cancellationToken
            );

            content.Write((await _TryGetAsync(Keys.FinishedObject(fileId), cancellationToken))!);
        }

        foreach (var segment in state.Segments)
        {
            var bytes = await _TryGetAsync(segment.Key, cancellationToken);
            bytes.Should().NotBeNull("segment {0} is recorded in the state", segment.Key);
            content.Write(bytes.AsSpan(0, (int)segment.Length));
        }

        return content.ToArray();
    }

    /// <summary>The keys of every tail segment object stored for the upload, recorded in its state or not.</summary>
    public async Task<IReadOnlyList<string>> ListSegmentKeysAsync(string fileId, CancellationToken cancellationToken)
    {
        var response = await client.S3.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = options.Bucket, Prefix = Keys.Segments(fileId) },
            cancellationToken
        );

        return [.. (response.S3Objects ?? []).Select(o => o.Key)];
    }

    /// <summary>Reads one tail segment by the length the state recorded for it.</summary>
    public async Task<byte[]> ReadSegmentAsync(TusS3CommittedSegment segment, CancellationToken cancellationToken)
    {
        var bytes = await _TryGetAsync(segment.Key, cancellationToken);
        bytes.Should().NotBeNull();

        return bytes![..(int)segment.Length];
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
