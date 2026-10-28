// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Threading.Channels;

namespace Tests.TestSetup;

/// <summary>
/// An HTTP handler in the S3 client's pipeline that counts every request by S3 operation and can hold back one kind of
/// request for one key until the test releases it. Counting proves how many storage round trips a store operation
/// costs; holding lines up two writers racing on the same upload deterministically, as the Azure suite's blob write
/// gate does.
/// </summary>
internal sealed class S3RequestGate : DelegatingHandler
{
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _release = new(0);
    private readonly Channel<string> _arrivals = Channel.CreateUnbounded<string>();
    private volatile Armed? _armed;
    private volatile Func<HttpRequestMessage, bool>? _fail;
    private int _total;

#pragma warning disable CA2000 // False positive: DelegatingHandler disposes its inner handler when it is disposed.
    public S3RequestGate()
        : base(new SocketsHttpHandler()) { }
#pragma warning restore CA2000

    /// <summary>The number of requests sent through this gate since the last <see cref="Reset" />.</summary>
    public int TotalRequests => Volatile.Read(ref _total);

    /// <summary>The number of requests of one <see cref="S3Operation" /> since the last <see cref="Reset" />.</summary>
    public int Count(string operation)
    {
        return _counts.GetValueOrDefault(operation);
    }

    /// <summary>A snapshot of the per-operation counts, for failure messages.</summary>
    public IReadOnlyDictionary<string, int> Counts => new Dictionary<string, int>(_counts, StringComparer.Ordinal);

    public void Reset()
    {
        _counts.Clear();
        Interlocked.Exchange(ref _total, 0);
    }

    /// <summary>
    /// Holds every request of <paramref name="operation" /> whose object key ends with <paramref name="keySuffix" />
    /// until <see cref="Disarm" />.
    /// </summary>
    public void Arm(string operation, string keySuffix)
    {
        _armed = new Armed(operation, keySuffix);
    }

    public void Disarm()
    {
        _armed = null;
    }

    /// <summary>
    /// Answers every request <paramref name="predicate" /> matches with an S3 error the SDK does not retry, instead of
    /// sending it, so a test can make one storage call fail; <see langword="null" /> stops the failures.
    /// </summary>
    public void FailWhen(Func<HttpRequestMessage, bool>? predicate)
    {
        _fail = predicate;
    }

    /// <summary>Completes with the held request's key when one more matching request is waiting for release.</summary>
    public ValueTask<string> WaitForArrivalAsync(CancellationToken cancellationToken)
    {
        return _arrivals.Reader.ReadAsync(cancellationToken);
    }

    /// <summary>Lets exactly one held request proceed to the server.</summary>
    public void ReleaseOne()
    {
        _release.Release();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var operation = S3Operation.Of(request);
        _counts.AddOrUpdate(operation, 1, static (_, count) => count + 1);
        Interlocked.Increment(ref _total);

        if (_fail?.Invoke(request) == true)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                RequestMessage = request,
                Content = new StringContent(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Error><Code>InvalidRequest</Code><Message>Failed by the test gate</Message></Error>",
                    Encoding.UTF8,
                    "application/xml"
                ),
            };
        }

        var armed = _armed;
        var key = request.RequestUri?.AbsolutePath ?? "";

        if (
            armed is not null
            && string.Equals(armed.Operation, operation, StringComparison.Ordinal)
            && key.EndsWith(armed.KeySuffix, StringComparison.Ordinal)
        )
        {
            await _arrivals.Writer.WriteAsync(key, cancellationToken).ConfigureAwait(false);
            await _release.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _release.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed record Armed(string Operation, string KeySuffix);
}

/// <summary>Names the S3 operation an HTTP request performs, from its method, query, and headers.</summary>
internal static class S3Operation
{
    public const string PutObject = nameof(PutObject);
    public const string CopyObject = nameof(CopyObject);
    public const string GetObject = nameof(GetObject);
    public const string HeadObject = nameof(HeadObject);
    public const string DeleteObject = nameof(DeleteObject);
    public const string DeleteObjects = nameof(DeleteObjects);
    public const string ListObjectsV2 = nameof(ListObjectsV2);
    public const string CreateMultipartUpload = nameof(CreateMultipartUpload);
    public const string UploadPart = nameof(UploadPart);
    public const string UploadPartCopy = nameof(UploadPartCopy);
    public const string CompleteMultipartUpload = nameof(CompleteMultipartUpload);
    public const string AbortMultipartUpload = nameof(AbortMultipartUpload);
    public const string ListParts = nameof(ListParts);
    public const string ListMultipartUploads = nameof(ListMultipartUploads);
    public const string CreateBucket = nameof(CreateBucket);
    public const string DeleteBucket = nameof(DeleteBucket);
    public const string Other = nameof(Other);

    /// <summary>The part number an <see cref="UploadPart" /> request carries, or <see langword="null" />.</summary>
    public static int? PartNumber(HttpRequestMessage request)
    {
        foreach (var pair in (request.RequestUri?.Query ?? "").TrimStart('?').Split('&'))
        {
            if (pair.StartsWith("partNumber=", StringComparison.Ordinal))
            {
                return int.Parse(pair["partNumber=".Length..], CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    public static string Of(HttpRequestMessage request)
    {
        var query = _QueryKeys(request.RequestUri?.Query);
        var method = request.Method.Method;
        var copy = request.Headers.Contains("x-amz-copy-source");
        // The fixtures use path-style addressing, so a path with one segment addresses the bucket itself.
        var bucketLevel =
            (request.RequestUri?.AbsolutePath ?? "/").Trim('/').IndexOf('/', StringComparison.Ordinal) < 0;

        return method switch
        {
            "PUT" when bucketLevel && query.Count == 0 => CreateBucket,
            "DELETE" when bucketLevel && query.Count == 0 => DeleteBucket,
            "PUT" when query.Contains("partNumber") && copy => UploadPartCopy,
            "PUT" when query.Contains("partNumber") => UploadPart,
            "PUT" when query.Count == 0 && copy => CopyObject,
            "PUT" when query.Count == 0 => PutObject,
            "POST" when query.Contains("uploads") => CreateMultipartUpload,
            "POST" when query.Contains("uploadId") => CompleteMultipartUpload,
            "POST" when query.Contains("delete") => DeleteObjects,
            "DELETE" when query.Contains("uploadId") => AbortMultipartUpload,
            "DELETE" when query.Count == 0 => DeleteObject,
            "GET" when query.Contains("list-type") => ListObjectsV2,
            "GET" when query.Contains("uploadId") => ListParts,
            "GET" when query.Contains("uploads") => ListMultipartUploads,
            "GET" when query.Count == 0 => GetObject,
            "HEAD" => HeadObject,
            _ => Other,
        };
    }

    private static HashSet<string> _QueryKeys(string? query)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        if (string.IsNullOrEmpty(query))
        {
            return keys;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            keys.Add(separator < 0 ? pair : pair[..separator]);
        }

        return keys;
    }
}
