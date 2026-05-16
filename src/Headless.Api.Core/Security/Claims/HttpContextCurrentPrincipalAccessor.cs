// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.Security.Claims;

public sealed class HttpContextCurrentPrincipalAccessor(IHttpContextAccessor accessor) : ThreadCurrentPrincipalAccessor
{
    protected override ClaimsPrincipal? GetClaimsPrincipal()
    {
        return accessor.HttpContext?.User ?? base.GetClaimsPrincipal();
    }
}
