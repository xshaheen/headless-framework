// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages.Dashboard.GatewayProxy.Requester;

public interface IHttpClient
{
    HttpClient Client { get; }

    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request);
}
