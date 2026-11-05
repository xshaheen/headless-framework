// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Tests.Streaming;

/// <summary>A live stream opened against a test host, read event by event as an EventSource reads it.</summary>
internal sealed class SseStream : IAsyncDisposable
{
    private readonly HttpRequestMessage _request;
    private readonly StreamReader _reader;

    private SseStream(HttpRequestMessage request, HttpResponseMessage response, StreamReader reader)
    {
        _request = request;
        Response = response;
        _reader = reader;
    }

    public HttpResponseMessage Response { get; }

    /// <summary>Opens a stream and asserts the server accepted it.</summary>
    public static async Task<SseStream> OpenAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null
    )
    {
        var stream = await SendAsync(client, path, cancellationToken, headers);
        stream.Response.StatusCode.Should().Be(HttpStatusCode.OK);

        return stream;
    }

    /// <summary>Opens a stream and returns whatever the server answered.</summary>
    public static async Task<SseStream> SendAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null
    )
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));

        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStreamAsync(cancellationToken);

        return new SseStream(request, response, new StreamReader(body));
    }

    /// <summary>Reads the next event, or <see langword="null"/> when the server ended the stream.</summary>
    public async Task<SseEvent?> NextAsync(CancellationToken cancellationToken)
    {
        string? type = null;
        string? id = null;
        var data = new List<string>();

        while (await _reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Count == 0 && type is null)
                {
                    continue;
                }

                return new SseEvent(type ?? "message", id, string.Join('\n', data));
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? string.Empty : line[(colon + 1)..].TrimStart(' ');

            if (string.Equals(field, "event", StringComparison.Ordinal))
            {
                type = value;
            }
            else if (string.Equals(field, "id", StringComparison.Ordinal))
            {
                id = value;
            }
            else if (string.Equals(field, "data", StringComparison.Ordinal))
            {
                data.Add(value);
            }
        }

        return null;
    }

    /// <summary>Reads the next event that is not a heartbeat; fails when the stream ends first.</summary>
    public async Task<SseEvent> NextExceptHeartbeatAsync(CancellationToken cancellationToken)
    {
        while (await NextAsync(cancellationToken) is { } next)
        {
            if (!string.Equals(next.Type, "heartbeat", StringComparison.Ordinal))
            {
                return next;
            }
        }

        throw new InvalidOperationException("The stream ended before the expected event.");
    }

    /// <summary>Reads until an event of <paramref name="type"/>; fails when the stream ends first.</summary>
    public async Task<SseEvent> NextOfTypeAsync(string type, CancellationToken cancellationToken)
    {
        while (await NextAsync(cancellationToken) is { } next)
        {
            if (string.Equals(next.Type, type, StringComparison.Ordinal))
            {
                return next;
            }
        }

        throw new InvalidOperationException($"The stream ended before a {type} event.");
    }

    /// <summary>Reads to the end of the stream and returns the types of the events read on the way.</summary>
    public async Task<IReadOnlyList<string>> ReadToEndAsync(CancellationToken cancellationToken)
    {
        var types = new List<string>();

        while (await NextAsync(cancellationToken) is { } next)
        {
            types.Add(next.Type);
        }

        return types;
    }

    public ValueTask DisposeAsync()
    {
        _reader.Dispose();
        Response.Dispose();
        _request.Dispose();

        return ValueTask.CompletedTask;
    }
}

/// <summary>One server-sent event: its type, its id when it carries one, and its data.</summary>
internal sealed record SseEvent(string Type, string? Id, string Data)
{
    public JsonElement Json
    {
        get
        {
            using var document = JsonDocument.Parse(Data);
            return document.RootElement.Clone();
        }
    }
}
