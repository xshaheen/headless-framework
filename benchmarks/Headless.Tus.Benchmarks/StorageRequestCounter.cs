// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Globalization;
using Amazon.Runtime;

namespace Headless.Tus.Benchmarks;

/// <summary>
/// An HTTP handler in the S3 client's pipeline that counts every request by S3 operation, so a run reports how many
/// storage round trips each store spends on the same upload. The integration tests carry their own copy with a write
/// gate; a benchmark only needs the count.
/// </summary>
internal sealed class StorageRequestCounter : DelegatingHandler
{
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);

#pragma warning disable CA2000 // False positive: DelegatingHandler disposes its inner handler when it is disposed.
    public StorageRequestCounter()
        : base(new SocketsHttpHandler()) { }
#pragma warning restore CA2000

    /// <summary>A snapshot of the per-operation counts since the last <see cref="Reset" />.</summary>
    public IReadOnlyDictionary<string, int> Snapshot()
    {
        return new Dictionary<string, int>(_counts, StringComparer.Ordinal);
    }

    public void Reset()
    {
        _counts.Clear();
    }

    /// <summary>Formats per-operation counts as <c>Operation=n</c> pairs, the busiest first.</summary>
    public static string Describe(IReadOnlyDictionary<string, int> counts, int divisor = 1)
    {
        return string.Join(
            ", ",
            counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair =>
                    string.Create(CultureInfo.InvariantCulture, $"{pair.Key}={pair.Value / (double)divisor:0.#}")
                )
        );
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        _counts.AddOrUpdate(_OperationOf(request), 1, static (_, count) => count + 1);

        return base.SendAsync(request, cancellationToken);
    }

    // Names the S3 operation from the method, query, and copy header. The clients use path-style addressing, so a
    // path with one segment addresses the bucket itself.
    private static string _OperationOf(HttpRequestMessage request)
    {
        var query = (request.RequestUri?.Query ?? "")
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)[0])
            .ToHashSet(StringComparer.Ordinal);
        var copy = request.Headers.Contains("x-amz-copy-source");
        var bucketLevel =
            (request.RequestUri?.AbsolutePath ?? "/").Trim('/').IndexOf('/', StringComparison.Ordinal) < 0;

        return request.Method.Method switch
        {
            "PUT" when bucketLevel && query.Count == 0 => "CreateBucket",
            "PUT" when query.Contains("partNumber") && copy => "UploadPartCopy",
            "PUT" when query.Contains("partNumber") => "UploadPart",
            "PUT" when query.Contains("tagging") => "PutObjectTagging",
            "PUT" when query.Count == 0 && copy => "CopyObject",
            "PUT" when query.Count == 0 => "PutObject",
            "POST" when query.Contains("uploads") => "CreateMultipartUpload",
            "POST" when query.Contains("uploadId") => "CompleteMultipartUpload",
            "POST" when query.Contains("delete") => "DeleteObjects",
            "DELETE" when query.Contains("uploadId") => "AbortMultipartUpload",
            "DELETE" when query.Count == 0 => "DeleteObject",
            "GET" when query.Contains("list-type") => "ListObjectsV2",
            "GET" when query.Contains("uploadId") => "ListParts",
            "GET" when query.Contains("uploads") => "ListMultipartUploads",
            "GET" when query.Contains("tagging") => "GetObjectTagging",
            "GET" when query.Count == 0 => "GetObject",
            "HEAD" => "HeadObject",
            _ => request.Method.Method + " " + string.Join('&', query),
        };
    }
}

/// <summary>Hands the S3 SDK one <see cref="HttpClient" /> that sends through a <see cref="StorageRequestCounter" />.</summary>
internal sealed class CountingHttpClientFactory(StorageRequestCounter counter) : HttpClientFactory
{
    private readonly Lazy<HttpClient> _httpClient = new(() => new HttpClient(counter, disposeHandler: false));

    public override HttpClient CreateHttpClient(IClientConfig clientConfig)
    {
        return _httpClient.Value;
    }

    public override bool UseSDKHttpClientCaching(IClientConfig clientConfig)
    {
        return false;
    }

    public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig)
    {
        return false;
    }
}
