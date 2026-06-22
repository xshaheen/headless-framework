// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.Extensions.Web;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.Abstractions;

internal sealed class HttpWebClientInfoProvider(IHttpContextAccessor accessor) : IWebClientInfoProvider
{
    public string? IpAddress => accessor.HttpContext?.GetIpAddress();

    public string? UserAgent => accessor.HttpContext?.GetUserAgent();

    public string? DeviceInfo => WebHelper.GetDeviceInfo(UserAgent);
}
