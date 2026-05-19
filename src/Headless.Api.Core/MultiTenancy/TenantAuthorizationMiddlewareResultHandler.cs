// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Abstractions;
using Headless.Checks;
using Headless.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.MultiTenancy;

internal sealed class TenantAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly IAuthorizationMiddlewareResultHandler _inner;
    private readonly IProblemDetailsCreator _problemDetailsCreator;
    private readonly IProblemDetailsService? _problemDetailsService;

    public TenantAuthorizationMiddlewareResultHandler(
        IAuthorizationMiddlewareResultHandler inner,
        IProblemDetailsCreator problemDetailsCreator,
        IProblemDetailsService? problemDetailsService = null
    )
    {
        _inner = Argument.IsNotNull(inner);
        _problemDetailsCreator = Argument.IsNotNull(problemDetailsCreator);
        _problemDetailsService = problemDetailsService;
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
            await _inner.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);

            return;
        }

        var problemDetails = _problemDetailsCreator.Forbidden(
            detail: HeadlessProblemDetailsConstants.Details.TenantContextRequired,
            error: HeadlessProblemDetailsConstants.Errors.TenantContextRequired
        );

        // Route through IProblemDetailsService so any registered CustomizeProblemDetails callbacks
        // (including consumer-supplied customizers) run on the auth-path body just like they do on
        // the exception-path body in HeadlessApiExceptionHandler.TryHandleAsync. Fall back to
        // Results.Problem when the service is unregistered or when no writer accepts the request.
        if (_problemDetailsService is not null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            if (
                await _problemDetailsService
                    .TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problemDetails })
                    .ConfigureAwait(false)
            )
            {
                return;
            }
        }

        await Results.Problem(problemDetails).ExecuteAsync(context).ConfigureAwait(false);
    }

    private static bool _IsTenantRequirementFailure(PolicyAuthorizationResult result)
    {
        var failure = result.AuthorizationFailure;

        if (failure is null)
        {
            return false;
        }

        // Match by typed identity, never by the free-form discriminator string.
        // - FailedRequirements: populated when a TenantRequirement is left unsatisfied (no Succeed
        //   call), e.g., handler never executed.
        // - FailureReasons: populated when TenantRequirementHandler calls context.Fail(reason);
        //   we identify it by the typed handler reference rather than the reason.Message string,
        //   because any handler can emit a reason with an arbitrary message.
        return failure.FailedRequirements.OfType<TenantRequirement>().Any()
            || failure.FailureReasons.Any(reason => reason.Handler is TenantRequirementHandler);
    }
}
