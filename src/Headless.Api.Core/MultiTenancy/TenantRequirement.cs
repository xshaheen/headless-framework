// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Authorization;

namespace Headless.Api.MultiTenancy;

[PublicAPI]
public sealed class TenantRequirement : IAuthorizationRequirement
{
    public const string FailureReason = "TenantContextRequired";
}
