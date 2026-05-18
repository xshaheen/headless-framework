// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Abstractions;
using Headless.Checks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.MultiTenancy;

[PublicAPI]
public sealed class TenantAuthorizationMiddlewareResultHandler(IProblemDetailsCreator problemDetailsCreator)
    : IAuthorizationMiddlewareResultHandler
{
    private static readonly AuthorizationMiddlewareResultHandler _DefaultHandler = new();

    private readonly IProblemDetailsCreator _problemDetailsCreator = Argument.IsNotNull(problemDetailsCreator);

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult
    )
    {
        Argument.IsNotNull(next);
        Argument.IsNotNull(context);
        Argument.IsNotNull(policy);
        Argument.IsNotNull(authorizeResult);

        if (!_IsTenantRequirementFailure(authorizeResult))
        {
            await _DefaultHandler.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);

            return;
        }

        var problemDetails = _problemDetailsCreator.TenantContextRequired();
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await Results.Problem(problemDetails).ExecuteAsync(context).ConfigureAwait(false);
    }

    private static bool _IsTenantRequirementFailure(PolicyAuthorizationResult result)
    {
        var failure = result.AuthorizationFailure;

        if (failure is null)
        {
            return false;
        }

        return failure.FailedRequirements.OfType<TenantRequirement>().Any()
            || failure.FailureReasons.Any(reason =>
                string.Equals(reason.Message, TenantRequirement.FailureReason, StringComparison.Ordinal)
            );
    }
}
