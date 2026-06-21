// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text;

namespace Headless.Sms.Testing;

/// <summary>
/// A controllable <see cref="HttpMessageHandler"/> for SMS provider unit tests. Routes each request through a
/// supplied responder, records the request URIs and (read) bodies, and honors cancellation.
/// </summary>
public sealed class TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    /// <summary>The absolute URIs of every request the handler observed, in order.</summary>
    public List<Uri?> RequestUris { get; } = [];

    /// <summary>The string bodies of every request the handler observed, in order (<see langword="null"/> when no content).</summary>
    public List<string?> RequestBodies { get; } = [];

    /// <summary>Number of requests the handler observed.</summary>
    public int CallCount => RequestUris.Count;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        RequestUris.Add(request.RequestUri);
        RequestBodies.Add(
            request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
        );

        return responder(request);
    }

    /// <summary>A handler that returns the same status code and body for every request.</summary>
    public static TestHttpMessageHandler Respond(
        HttpStatusCode statusCode,
        string body,
        string mediaType = "application/json"
    )
    {
        return new TestHttpMessageHandler(_ => CreateResponse(statusCode, body, mediaType));
    }

    /// <summary>A handler that throws <paramref name="exception"/> for every request (simulates a transport fault).</summary>
    public static TestHttpMessageHandler Throws(Exception exception)
    {
        return new TestHttpMessageHandler(_ => throw exception);
    }

    /// <summary>Builds a response with a UTF-8 string body.</summary>
    public static HttpResponseMessage CreateResponse(
        HttpStatusCode statusCode,
        string body,
        string mediaType = "application/json"
    )
    {
        return new HttpResponseMessage(statusCode) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
    }
}
