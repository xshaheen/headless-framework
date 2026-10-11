// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Tests;

/// <summary>Answers the MaxMind download API from in-memory archives.</summary>
internal sealed class StubMaxMindHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Release> _releases = new(StringComparer.Ordinal);

    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    public int Checks => Requests.Count(r => r.Method == HttpMethod.Head);

    public int Downloads =>
        Requests.Count(r =>
            r.Method == HttpMethod.Get && r.RequestUri!.Query.EndsWith("=tar.gz", StringComparison.Ordinal)
        );

    public void Publish(string edition, byte[] archive, DateTimeOffset lastModified, string? sha256 = null)
    {
        _releases[edition] = new Release(
            archive,
            lastModified,
            sha256 ?? Convert.ToHexStringLower(SHA256.HashData(archive))
        );
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Enqueue(request);

        // geoip/databases/{edition}/download?suffix=...
        var segments = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var edition = Uri.UnescapeDataString(segments[^2]);
        var suffix = request.RequestUri.Query.Split("suffix=", 2)[1];

        if (!_releases.TryGetValue(edition, out var release))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        HttpContent content = string.Equals(suffix, "tar.gz.sha256", StringComparison.Ordinal)
            ? new StringContent($"{release.Sha256}  {edition}_20261010.tar.gz\n", Encoding.ASCII)
            : new ByteArrayContent(request.Method == HttpMethod.Head ? [] : release.Archive);

        content.Headers.LastModified = release.LastModified;
        content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private sealed record Release(byte[] Archive, DateTimeOffset LastModified, string Sha256);
}
