// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Api.Core.Extensions.Web;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.Core.Abstractions;

public interface IWebClientInfoProvider
{
    /// <summary>Get IpAddress.</summary>
    string? IpAddress { get; }

    /// <summary>Get UserAgent.</summary>
    string? UserAgent { get; }

    /// <summary>Get DeviceInfo.</summary>
    string? DeviceInfo { get; }
}

public sealed class NullWebClientInfoProvider : IWebClientInfoProvider
{
    public string? IpAddress => null;

    public string? UserAgent => null;

    public string? DeviceInfo => null;
}

public sealed class HttpWebClientInfoProvider(IHttpContextAccessor accessor) : IWebClientInfoProvider
{
    private HttpContext HttpContext =>
        accessor.HttpContext ?? throw new InvalidOperationException("User context is not available");

    public string? IpAddress => HttpContext.GetIpAddress();

    public string? UserAgent => HttpContext.GetUserAgent();

    public string? DeviceInfo => WebHelper.GetDeviceInfo(UserAgent);
}
