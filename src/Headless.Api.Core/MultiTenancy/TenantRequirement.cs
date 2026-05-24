// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Authorization;

namespace Headless.Api.MultiTenancy;

[PublicAPI]
public sealed class TenantRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// Discriminator attached to <see cref="Microsoft.AspNetCore.Authorization.AuthorizationFailureReason.Message"/>
    /// when the handler fails. Used internally for diagnostics — consumers that need to detect tenant
    /// failures should inspect <see cref="Microsoft.AspNetCore.Authorization.AuthorizationFailure.FailedRequirements"/>
    /// for a <see cref="TenantRequirement"/> instance instead of matching this string.
    /// </summary>
    internal const string FailureReason = "TenantContextRequired";


}
