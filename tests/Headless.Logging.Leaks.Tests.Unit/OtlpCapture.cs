// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text;

namespace Tests;

/// <summary>
/// Stands in for an OTLP collector: the OpenTelemetry Protocol exporter sends its HTTP/protobuf requests here, and the
/// suite searches the request bodies, the bytes that would have crossed the network.
/// </summary>
/// <remarks>
/// Protobuf writes strings as raw UTF-8, so a value present in any log body, attribute, span tag, or event is present
/// in these bytes as written. Exporters run on the simple (synchronous) processor, which sends through
/// <see cref="HttpClient.Send(HttpRequestMessage, CancellationToken)"/>, so <see cref="Send"/> is the path taken.
/// </remarks>
internal sealed class OtlpCapture : HttpMessageHandler
{
    private readonly Lock _lock = new();
    private readonly List<(string Path, byte[] Body)> _requests = [];

    public Uri Endpoint(string signal) => new($"http://otlp.capture/v1/{signal}");

    public HttpClient CreateClient() => new(this, disposeHandler: false);

    /// <summary>Every body the exporter sent to <c>/v1/<paramref name="signal"/></c>, concatenated.</summary>
    public byte[] Bytes(string signal)
    {
        lock (_lock)
        {
            return
            [
                .. _requests
                    .Where(r => r.Path.EndsWith("/" + signal, StringComparison.Ordinal))
                    .SelectMany(r => r.Body),
            ];
        }
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _Record(request, cancellationToken);

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) => Task.FromResult(Send(request, cancellationToken));

    private void _Record(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();

        if (request.Content is not null)
        {
#pragma warning disable CA1849 // False positive: the exporter calls the synchronous Send, so the content is read synchronously here too.
            request.Content.CopyTo(body, null, cancellationToken);
#pragma warning restore CA1849
        }

        lock (_lock)
        {
            _requests.Add((request.RequestUri?.AbsolutePath ?? string.Empty, body.ToArray()));
        }
    }
}
