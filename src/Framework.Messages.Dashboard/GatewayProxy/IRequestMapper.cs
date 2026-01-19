// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Framework.Messages.GatewayProxy;

public interface IRequestMapper
{
    Task<HttpRequestMessage> Map(HttpRequest request);
}
