// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Tests.S3;

/// <summary>
/// An <see cref="IAmazonS3"/> that keeps objects and uploaded parts in memory and honors the conditional writes the
/// store relies on, so the part writer's arithmetic can be checked without a server.
/// </summary>
internal sealed class InMemoryS3
{
    private readonly ConcurrentDictionary<string, (byte[] Content, string ETag)> _objects = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, byte[]> _parts = new();
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly ConcurrentBag<int> _attempted = [];
    private int _etagCounter;

    public InMemoryS3()
    {
        Client = Substitute.For<IAmazonS3>();

        Client
            .PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => _PutObject(call.Arg<PutObjectRequest>()));

        Client
            .GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => _GetObject(call.Arg<GetObjectRequest>()));

        Client
            .UploadPartAsync(Arg.Any<UploadPartRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => _UploadPartAsync(call.Arg<UploadPartRequest>()));

        Client
            .InitiateMultipartUploadAsync(Arg.Any<InitiateMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _requests.Enqueue("CreateMultipartUpload");

                return new InitiateMultipartUploadResponse { UploadId = "upload-1" };
            });

        Client
            .AbortMultipartUploadAsync(Arg.Any<AbortMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _requests.Enqueue("AbortMultipartUpload");

                return new AbortMultipartUploadResponse();
            });

        Client
            .DeleteObjectsAsync(Arg.Any<DeleteObjectsRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _requests.Enqueue("DeleteObjects");

                foreach (var key in call.Arg<DeleteObjectsRequest>().Objects)
                {
                    _objects.TryRemove(key.Key, out _);
                }

                return new DeleteObjectsResponse();
            });
    }

    public IAmazonS3 Client { get; }

    /// <summary>Fails the upload of a part number with this exception instead of storing it.</summary>
    public Func<int, Exception?> FailPart { get; set; } = _ => null;

    /// <summary>Fails the write of an object key with this exception instead of storing it.</summary>
    public Func<string, Exception?> FailPut { get; set; } = _ => null;

    /// <summary>Runs before each part upload completes, so a test can hold uploads in flight.</summary>
    public Func<int, Task> BeforePartStored { get; set; } = _ => Task.CompletedTask;

    /// <summary>The S3 operations sent, in order.</summary>
    public IReadOnlyList<string> Requests => [.. _requests];

    public IReadOnlyDictionary<int, byte[]> Parts => _parts;

    /// <summary>Every part number an upload was sent for, stored or not.</summary>
    public IReadOnlyCollection<int> AttemptedParts => [.. _attempted];

    public bool Exists(string key)
    {
        return _objects.ContainsKey(key);
    }

    public byte[] Read(string key)
    {
        return _objects[key].Content;
    }

    public void ClearRequests()
    {
        _requests.Clear();
    }

    private PutObjectResponse _PutObject(PutObjectRequest request)
    {
        _requests.Enqueue("PutObject");

        if (FailPut(request.Key) is { } refusal)
        {
            throw refusal;
        }

        using var buffer = new MemoryStream();
        request.InputStream.CopyTo(buffer);

        lock (_objects)
        {
            var exists = _objects.TryGetValue(request.Key, out var current);

            if (string.Equals(request.IfNoneMatch, "*", StringComparison.Ordinal) && exists)
            {
                throw _Error(HttpStatusCode.PreconditionFailed, "PreconditionFailed");
            }

            if (request.IfMatch is { } ifMatch)
            {
                if (!exists)
                {
                    throw _Error(HttpStatusCode.NotFound, "NoSuchKey");
                }

                if (!string.Equals(current.ETag, ifMatch, StringComparison.Ordinal))
                {
                    throw _Error(HttpStatusCode.PreconditionFailed, "PreconditionFailed");
                }
            }

            var etag = _NextETag();
            _objects[request.Key] = (buffer.ToArray(), etag);

            return new PutObjectResponse { ETag = etag };
        }
    }

    private GetObjectResponse _GetObject(GetObjectRequest request)
    {
        _requests.Enqueue("GetObject");

        if (!_objects.TryGetValue(request.Key, out var stored))
        {
            throw _Error(HttpStatusCode.NotFound, "NoSuchKey");
        }

        var content = stored.Content;

        if (request.ByteRange is { } range)
        {
            var end = (int)Math.Min(range.End, content.Length - 1);
            content = content[(int)range.Start..(end + 1)];
        }

        return new GetObjectResponse { ResponseStream = new MemoryStream(content), ETag = stored.ETag };
    }

    private async Task<UploadPartResponse> _UploadPartAsync(UploadPartRequest request)
    {
        _requests.Enqueue("UploadPart");

        var number = request.PartNumber!.Value;
        _attempted.Add(number);
        await using var buffer = new MemoryStream();
        await request.InputStream.CopyToAsync(buffer);
        await BeforePartStored(number);

        if (FailPart(number) is { } failure)
        {
            throw failure;
        }

        _parts[number] = buffer.ToArray();

        return new UploadPartResponse { ETag = $"part-{number}", PartNumber = number };
    }

    private string _NextETag()
    {
        return $"\"etag-{Interlocked.Increment(ref _etagCounter)}\"";
    }

    public static AmazonS3Exception Error(HttpStatusCode status, string errorCode)
    {
        return _Error(status, errorCode);
    }

    private static AmazonS3Exception _Error(HttpStatusCode status, string errorCode)
    {
        return new AmazonS3Exception(errorCode, ErrorType.Sender, errorCode, "request-id", status);
    }
}
