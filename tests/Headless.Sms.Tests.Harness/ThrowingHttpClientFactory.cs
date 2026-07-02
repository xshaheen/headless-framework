// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>
/// An <see cref="IHttpClientFactory"/> whose clients throw the given exception on every request, for
/// asserting how providers classify transport-layer faults (for example the resilience pipeline's
/// <c>TimeoutRejectedException</c>) without a network dependency.
/// </summary>
public sealed class ThrowingHttpClientFactory(Exception exception) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        ThrowingHandler? handler = null;

        try
        {
            handler = new ThrowingHandler(exception);
            var client = new HttpClient(handler, disposeHandler: true);
            handler = null; // ownership transferred to the client

            return client;
        }
        finally
        {
            handler?.Dispose();
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            throw exception;
        }
    }
}
