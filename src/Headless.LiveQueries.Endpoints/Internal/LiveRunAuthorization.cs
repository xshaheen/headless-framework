// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Does for one run what the authentication and authorization middleware did for the request that opened the
/// stream. A stream outlives a token and a permission, so each run establishes both again.
/// </summary>
internal static class LiveRunAuthorization
{
    // The switch the authorization middleware reads to decide what a policy handler receives as its resource.
    private const string _SuppressHttpContextResourceSwitch =
        "Microsoft.AspNetCore.Authorization.SuppressUseHttpContextAsAuthorizationResource";

    /// <summary>
    /// Authenticates the default scheme on <paramref name="run" /> and assigns the outcome as the run's user and
    /// authentication result.
    /// </summary>
    /// <param name="run">The run's request.</param>
    /// <param name="opener">The user of the request that opened the stream.</param>
    public static async Task AuthenticateAsync(HttpContext run, ClaimsPrincipal opener)
    {
        var features = new LiveAuthenticationFeatures();

        // Set before anything authenticates: the run's request reads through to the opening request's features, and
        // an assignment to the user must land on the run, not on the request that opened the stream.
        run.Features.Set<IHttpAuthenticationFeature>(features);
        run.Features.Set<IAuthenticateResultFeature>(features);

        var schemes = run.RequestServices.GetService<IAuthenticationSchemeProvider>();
        var scheme = schemes is null ? null : await schemes.GetDefaultAuthenticateSchemeAsync().ConfigureAwait(false);

        if (scheme is null)
        {
            // Nothing authenticates by default on this host, so whatever established the opener still stands. A
            // policy that names its schemes authenticates them below.
            features.User = opener;

            return;
        }

        // This is the step that notices an expired token: the policy evaluator authenticates only the schemes a
        // policy names, and most policies name none.
        var result = await run.AuthenticateAsync(scheme.Name).ConfigureAwait(false);

        if (result.Succeeded)
        {
            features.AuthenticateResult = result;
        }
        else
        {
            features.User = result.Principal ?? new ClaimsPrincipal(new ClaimsIdentity());
        }
    }

    /// <summary>
    /// Evaluates the endpoint's authorization as the authorization middleware does, then lets the host's result
    /// handler execute the endpoint, challenge or forbid.
    /// </summary>
    /// <param name="run">The run's request, already authenticated.</param>
    /// <param name="endpoint">The live endpoint.</param>
    /// <param name="opener">The user of the request that opened the stream.</param>
    /// <returns><see langword="true" /> when the run was refused and the handler did not execute.</returns>
    public static async Task<bool> ExecuteAsync(HttpContext run, Endpoint endpoint, ClaimsPrincipal opener)
    {
        var services = run.RequestServices;
        var execute = endpoint.RequestDelegate!;
        var policyProvider = services.GetService<IAuthorizationPolicyProvider>();
        var evaluator = services.GetService<IPolicyEvaluator>();

        if (policyProvider is null || evaluator is null)
        {
            // A host without authorization services cannot have authorized the opening request either.
            return await _ExecuteInOpenersTenantAsync(run, execute, opener).ConfigureAwait(false);
        }

        var policy = await _CombinePolicyAsync(policyProvider, endpoint).ConfigureAwait(false);

        if (policy is null)
        {
            return await _ExecuteInOpenersTenantAsync(run, execute, opener).ConfigureAwait(false);
        }

        var authenticateResult = await evaluator.AuthenticateAsync(policy, run).ConfigureAwait(false);

        if (authenticateResult.Succeeded)
        {
            var features = new LiveAuthenticationFeatures { AuthenticateResult = authenticateResult };
            run.Features.Set<IHttpAuthenticationFeature>(features);
            run.Features.Set<IAuthenticateResultFeature>(features);
        }

        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return await _ExecuteInOpenersTenantAsync(run, execute, opener).ConfigureAwait(false);
        }

        object resource =
            AppContext.TryGetSwitch(_SuppressHttpContextResourceSwitch, out var suppressed) && suppressed
                ? endpoint
                : run;

        var authorizeResult = await evaluator
            .AuthorizeAsync(policy, authenticateResult, run, resource)
            .ConfigureAwait(false);

        if (authorizeResult.Succeeded && _LeftOpenersTenant(run, opener))
        {
            return true;
        }

        // The host's handler decides what a refusal looks like, as it does for an ordinary request; on success it
        // executes the endpoint.
        await services
            .GetRequiredService<IAuthorizationMiddlewareResultHandler>()
            .HandleAsync(execute, run, policy, authorizeResult)
            .ConfigureAwait(false);

        return !authorizeResult.Succeeded;
    }

    private static async Task<bool> _ExecuteInOpenersTenantAsync(
        HttpContext run,
        RequestDelegate execute,
        ClaimsPrincipal opener
    )
    {
        if (_LeftOpenersTenant(run, opener))
        {
            return true;
        }

        await execute(run).ConfigureAwait(false);

        return false;
    }

    // The tenancy middleware resolved the ambient tenant once, for the principal that opened the stream, and every
    // run executes under it. A principal whose tenant claim has since changed or gone must not read under that
    // tenant, so the run is refused. A host that keeps the tenant in a claim type of its own is not covered here.
    private static bool _LeftOpenersTenant(HttpContext run, ClaimsPrincipal opener)
    {
        var openersTenant = opener.GetTenantId();
        var currentTenant = run.User.GetTenantId();

        if (string.Equals(openersTenant, currentTenant, StringComparison.Ordinal))
        {
            return false;
        }

        run.Response.StatusCode = StatusCodes.Status403Forbidden;

        return true;
    }

    // The combination the authorization middleware builds: authorize data and policy objects, which fall back to the
    // fallback policy when there are none, plus the requirements that metadata supplies itself.
    private static async Task<AuthorizationPolicy?> _CombinePolicyAsync(
        IAuthorizationPolicyProvider policyProvider,
        Endpoint endpoint
    )
    {
        var policy = await AuthorizationPolicy
            .CombineAsync(
                policyProvider,
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>()
            )
            .ConfigureAwait(false);

        var requirementData = endpoint.Metadata.GetOrderedMetadata<IAuthorizationRequirementData>();

        if (requirementData.Count == 0)
        {
            return policy;
        }

        var requirements = new AuthorizationPolicyBuilder();

        foreach (var data in requirementData)
        {
            requirements.AddRequirements([.. data.GetRequirements()]);
        }

        return policy is null ? requirements.Build() : AuthorizationPolicy.Combine(policy, requirements.Build());
    }
}
