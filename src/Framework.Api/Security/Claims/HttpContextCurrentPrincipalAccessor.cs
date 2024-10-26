// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.Security.Claims;

public sealed class HttpContextCurrentPrincipalAccessor(IHttpContextAccessor accessor) : ThreadCurrentPrincipalAccessor
{
    protected override ClaimsPrincipal GetClaimsPrincipal()
    {
        return accessor.HttpContext?.User ?? base.GetClaimsPrincipal();
    }
}
