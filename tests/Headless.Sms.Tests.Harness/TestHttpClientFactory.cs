// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http;

namespace Headless.Sms.Testing;

/// <summary>
/// An <see cref="IHttpClientFactory"/> that hands out clients backed by a single shared handler, so a sender
/// that creates (and disposes) a client per send still sees the same recording handler across calls.
/// </summary>
public sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        // disposeHandler:false so the shared handler survives the sender's `using var httpClient`.
        return new HttpClient(handler, disposeHandler: false);
    }
}
