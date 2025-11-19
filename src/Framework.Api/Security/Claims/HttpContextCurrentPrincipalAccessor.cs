// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Framework.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.Security.Claims;

public sealed class HttpContextCurrentPrincipalAccessor(IHttpContextAccessor accessor) : ThreadCurrentPrincipalAccessor
{
    protected override ClaimsPrincipal? GetClaimsPrincipal()
    {
        return accessor.HttpContext?.User ?? base.GetClaimsPrincipal();
    }
}
