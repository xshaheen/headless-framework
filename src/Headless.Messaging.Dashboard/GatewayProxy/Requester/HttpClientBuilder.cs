// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard.GatewayProxy.Requester;

internal class HttpClientBuilder : IHttpClientBuilder
{
    private readonly Dictionary<int, Func<DelegatingHandler>> _handlers = [];

    public IHttpClient Create()
    {
#pragma warning disable CA2000 // Dispose HttpClientHandler is handled by HttpClient
        var httpclientHandler = new HttpClientHandler();
        var client = new HttpClient(_CreateHttpMessageHandler(httpclientHandler));
        return new HttpClientWrapper(client);
#pragma warning restore CA2000
    }

    private HttpMessageHandler _CreateHttpMessageHandler(HttpMessageHandler httpMessageHandler)
    {
        _handlers
            .OrderByDescending(handler => handler.Key)
            .Select(handler => handler.Value)
            .Reverse()
            .ToList()
            .ForEach(handler =>
            {
                var delegatingHandler = handler();
                delegatingHandler.InnerHandler = httpMessageHandler;
                httpMessageHandler = delegatingHandler;
            });
        return httpMessageHandler;
    }
}

/// <summary>
/// This class was made to make unit testing easier when HttpClient is used.
/// </summary>
internal class HttpClientWrapper(HttpClient client) : IHttpClient
{
    public HttpClient Client { get; } = client;

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        return Client.SendAsync(request);
    }
}
