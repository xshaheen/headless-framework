// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages.GatewayProxy.Requester;

public interface IHttpRequester
{
    Task<HttpResponseMessage> GetResponse(HttpRequestMessage request);
}
