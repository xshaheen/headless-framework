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

    /// <summary>
    /// Key written into <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/> by
    /// <c>TenantRequirementHandler</c> when it fails the request. <c>StatusCodesRewriterMiddleware</c>
    /// reads this marker on 403 responses to substitute the structured <c>g:tenant_required</c>
    /// body for the generic Forbidden body. The marker decouples the requirement from the
    /// authorization-result-handler pipeline so consumers can register their own
    /// <see cref="Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler"/>
    /// in any order without disabling the discriminator.
    /// </summary>
    internal const string HttpContextItemKey = "Headless.Api.MultiTenancy.TenantContextRequired";
}
