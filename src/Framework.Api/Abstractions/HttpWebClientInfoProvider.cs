// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Api.Extensions.Web;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.Abstractions;

public sealed class HttpWebClientInfoProvider(IHttpContextAccessor accessor) : IWebClientInfoProvider
{
    private HttpContext HttpContext =>
        accessor.HttpContext ?? throw new InvalidOperationException("User context is not available");

    public string? IpAddress => HttpContext.GetIpAddress();

    public string? UserAgent => HttpContext.GetUserAgent();

    public string? DeviceInfo => WebHelper.GetDeviceInfo(UserAgent);
}
