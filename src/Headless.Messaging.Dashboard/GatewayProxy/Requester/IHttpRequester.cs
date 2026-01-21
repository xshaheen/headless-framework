// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard.GatewayProxy.Requester;

public interface IHttpRequester
{
    Task<HttpResponseMessage> GetResponse(HttpRequestMessage request);
}
