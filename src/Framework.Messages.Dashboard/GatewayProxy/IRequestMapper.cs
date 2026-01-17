// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace DotNetCore.CAP.Dashboard.GatewayProxy;

public interface IRequestMapper
{
    Task<HttpRequestMessage> Map(HttpRequest request);
}
