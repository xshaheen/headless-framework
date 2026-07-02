// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Tests;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that records every request (reading the body eagerly, since the
/// content is disposed with the request) and answers with a canned response. Install it as a named
/// HttpClient's primary handler — see <c>StubSmsHttpClient</c> — to prove traffic flows through exactly
/// that client registration.
/// </summary>
public sealed class CapturingHttpMessageHandler(
    HttpStatusCode statusCode = HttpStatusCode.OK,
    string responseBody = "{}"
) : HttpMessageHandler
{
    private readonly Lock _lock = new();
    private readonly List<CapturedSmsHttpRequest> _requests = [];

    /// <summary>The requests captured so far, in send order.</summary>
    public IReadOnlyList<CapturedSmsHttpRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            _requests.Add(new(request.Method, request.RequestUri, body));
        }

        return new HttpResponseMessage(statusCode) { Content = new StringContent(responseBody) };
    }
}

/// <summary>A request captured by <see cref="CapturingHttpMessageHandler"/>.</summary>
public sealed record CapturedSmsHttpRequest(HttpMethod Method, Uri? Uri, string? Body);
