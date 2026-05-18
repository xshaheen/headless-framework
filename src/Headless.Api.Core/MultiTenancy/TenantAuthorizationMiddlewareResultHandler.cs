// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Abstractions;
using Headless.Checks;
using Headless.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.MultiTenancy;

[PublicAPI]
public sealed class TenantAuthorizationMiddlewareResultHandler(IProblemDetailsCreator problemDetailsCreator)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly IProblemDetailsCreator _problemDetailsCreator = Argument.IsNotNull(problemDetailsCreator);

    private readonly HeadlessAuthorizationMiddlewareResultHandlerFallback _fallback = new(
        new AuthorizationMiddlewareResultHandler()
    );

    internal TenantAuthorizationMiddlewareResultHandler(
        IProblemDetailsCreator problemDetailsCreator,
        HeadlessAuthorizationMiddlewareResultHandlerFallback fallback
    )
        : this(problemDetailsCreator)
    {
        _fallback = Argument.IsNotNull(fallback);
    }

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
            await _fallback.Inner.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);

            return;
        }

        var problemDetails = _problemDetailsCreator.Forbidden(
            detail: HeadlessProblemDetailsConstants.Details.TenantContextRequired,
            error: HeadlessProblemDetailsConstants.Errors.TenantContextRequired
        );
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

internal sealed class HeadlessAuthorizationMiddlewareResultHandlerFallback(IAuthorizationMiddlewareResultHandler inner)
{
    public IAuthorizationMiddlewareResultHandler Inner { get; } = Argument.IsNotNull(inner);
}
