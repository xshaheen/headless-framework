// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Api.Cors;

/// <summary>
/// Consults the <see cref="ICorsOriginSource"/> registered for a policy when the policy's static origins miss.
/// </summary>
internal sealed class HeadlessCorsPolicyProvider(ICorsPolicyProvider inner, IOptions<CorsOptions> corsOptions)
    : ICorsPolicyProvider
{
    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var policy = await inner.GetPolicyAsync(context, policyName).ConfigureAwait(false);

        if (policy is not { AllowAnyOrigin: false })
        {
            return policy;
        }

        // Two Origin values are malformed input, and 'null' is the opaque origin of sandboxed frames and file pages,
        // which no source can vouch for; both fall through to the static policy, which refuses them.
        var origins = context.Request.Headers.Origin;

        if (origins.Count != 1)
        {
            return policy;
        }

        var origin = origins[0];

        if (
            string.IsNullOrEmpty(origin)
            || string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase)
            || policy.IsOriginAllowed(origin)
        )
        {
            return policy;
        }

        var source = context.RequestServices.GetKeyedService<ICorsOriginSource>(
            policyName ?? corsOptions.Value.DefaultPolicyName
        );

        if (
            source is null
            || !await source.IsOriginAllowedAsync(origin, context, context.RequestAborted).ConfigureAwait(false)
        )
        {
            return policy;
        }

        return _Approve(policy, origin);
    }

    private static CorsPolicy _Approve(CorsPolicy policy, string origin)
    {
        // The copied IsOriginAllowed delegate stays bound to the original policy's origin list, so replace it with
        // one that approves exactly this origin. The policy lives for this request only, and a custom delegate also
        // makes the CORS service add 'Vary: Origin', keeping caches from reusing the approval for another origin.
        return new CorsPolicyBuilder(policy)
            .WithOrigins(origin)
            .SetIsOriginAllowed(candidate => string.Equals(candidate, origin, StringComparison.Ordinal))
            .Build();
    }
}
