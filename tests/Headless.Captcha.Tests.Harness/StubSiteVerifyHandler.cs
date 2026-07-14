// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Tests;

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that drives canned siteverify responses. Responses are dequeued in
/// order; once the queue drains, the last enqueued response is repeated (so a resilience handler's retries see a
/// stable answer). Captures the last request and its form body so tests can assert on the encoded fields. Honors
/// cancellation before doing any work, so a pre-cancelled token throws without "completing" a call.
/// </summary>
public sealed class StubSiteVerifyHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();
    private (HttpStatusCode Status, string Body)? _last;

    /// <summary>The most recent request seen by the handler.</summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    /// <summary>The most recent request's form-encoded body.</summary>
    public string? LastRequestBody { get; private set; }

    /// <summary>The number of times the handler actually started processing a request.</summary>
    public int InvocationCount { get; private set; }

    /// <summary>Enqueues a canned JSON response with the given status code.</summary>
    public StubSiteVerifyHandler EnqueueJson(HttpStatusCode status, string body)
    {
        _responses.Enqueue((status, body));

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        InvocationCount++;
        LastRequest = request;

        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        (HttpStatusCode Status, string Body) chosen;

        if (_responses.Count > 0)
        {
            chosen = _responses.Dequeue();
            _last = chosen;
        }
        else
        {
            chosen = _last is { } last
                ? last
                : throw new InvalidOperationException("No stubbed siteverify response was enqueued.");
        }

        return new HttpResponseMessage(chosen.Status)
        {
            Content = new StringContent(chosen.Body, Encoding.UTF8, "application/json"),
        };
    }
}
