// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages.Dashboard.GatewayProxy.Requester;

public interface IHttpRequester
{
    Task<HttpResponseMessage> GetResponse(HttpRequestMessage request);
}
