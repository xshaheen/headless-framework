// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.Security.Claims;

/// <summary>
/// <see cref="ThreadCurrentPrincipalAccessor"/> that resolves the current principal from
/// <see cref="Microsoft.AspNetCore.Http.HttpContext.User"/> when an active HTTP context exists,
/// and falls back to <see cref="System.Threading.Thread.CurrentPrincipal"/> otherwise.
/// </summary>
public sealed class HttpContextCurrentPrincipalAccessor(IHttpContextAccessor accessor) : ThreadCurrentPrincipalAccessor
{
    /// <summary>
    /// Returns <see cref="Microsoft.AspNetCore.Http.HttpContext.User"/> when an HTTP request is in progress,
    /// or the thread principal when called outside a request context.
    /// </summary>
    protected override ClaimsPrincipal? GetClaimsPrincipal()
    {
        return accessor.HttpContext?.User ?? base.GetClaimsPrincipal();
    }
}
