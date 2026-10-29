// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Headless.Api.Security;

internal sealed class RefreshTokenCookie(
    IHttpContextAccessor accessor,
    IOptionsMonitor<RefreshTokenCookieOptions> optionsMonitor,
    TimeProvider timeProvider
) : IRefreshTokenCookie
{
    public void Write(string token, DateTimeOffset expiresAt)
    {
        Argument.IsNotNullOrEmpty(token);
        Argument.IsGreaterThan(expiresAt, timeProvider.GetUtcNow());

        var options = optionsMonitor.CurrentValue;
        var cookie = _CreateCookieOptions(options);
        cookie.Expires = expiresAt;

        _Context().Response.Cookies.Append(options.Name, token, cookie);
    }

    public string? Read()
    {
        var value = _Context().Request.Cookies[optionsMonitor.CurrentValue.Name];

        return string.IsNullOrEmpty(value) ? null : value;
    }

    public void Delete()
    {
        var options = optionsMonitor.CurrentValue;

        _Context().Response.Cookies.Delete(options.Name, _CreateCookieOptions(options));
    }

    // Write and Delete share these attributes: the browser removes a cookie only when the deleting Set-Cookie names the
    // same path and domain, and a __Host- cookie only when it is also Secure.
    private static CookieOptions _CreateCookieOptions(RefreshTokenCookieOptions options)
    {
        return new CookieOptions
        {
            Path = options.Path,
            Domain = options.Domain,
            SameSite = options.SameSite,
            Secure = true,
            HttpOnly = true,
            // The session depends on this cookie, so a cookie-consent policy must not suppress it.
            IsEssential = true,
        };
    }

    private HttpContext _Context()
    {
        return accessor.HttpContext
            ?? throw new InvalidOperationException(
                "The request is not currently available. This service can only be used within the context of an existing HTTP request."
            );
    }
}
