// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Features;
using Headless.Features.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

/// <summary>
/// Wraps the authorization evaluator so that an HTTP authorization that failed only on
/// <see cref="FeatureRequirement"/>s hands the status-codes rewriter a 409 rejection. The feature handler cannot set
/// the rejection itself: it lives in a package without the HTTP types.
/// </summary>
/// <remarks>
/// The evaluator, not the handler or the middleware result handler, is the hook because it runs once after every
/// handler, sees the whole outcome, and stays in place when an application registers its own
/// <see cref="Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler"/>.
/// </remarks>
internal sealed class FeatureRejectionAuthorizationEvaluator(IAuthorizationEvaluator inner) : IAuthorizationEvaluator
{
    private readonly IAuthorizationEvaluator _inner = Argument.IsNotNull(inner);

    public AuthorizationResult Evaluate(AuthorizationHandlerContext context)
    {
        var result = _inner.Evaluate(context);

        // Only when the feature is the sole reason: an unauthenticated caller of an endpoint that also requires a
        // user keeps its challenge, a missing permission keeps its 403, and an explicit Fail from any handler wins.
        if (
            result.Failure is { FailCalled: false } failure
            && context.Resource is HttpContext httpContext
            && _OnlyFeaturesFailed(failure.FailedRequirements, out var requirements)
        )
        {
            httpContext.TrySetStatusCodeRejection(new FeatureUnavailableRejection(requirements));
        }

        return result;
    }

    private static bool _OnlyFeaturesFailed(
        IEnumerable<IAuthorizationRequirement> failedRequirements,
        out List<FeatureRequirement> requirements
    )
    {
        requirements = [];

        foreach (var requirement in failedRequirements)
        {
            if (requirement is not FeatureRequirement featureRequirement)
            {
                return false;
            }

            requirements.Add(featureRequirement);
        }

        return requirements.Count > 0;
    }
}

/// <summary>
/// Writes the 409 <c>g:feature_currently_not_available</c> problem response for a request whose authorization failed
/// only because a required feature is disabled, in place of the challenge or forbid the authorization middleware
/// produced.
/// </summary>
internal sealed class FeatureUnavailableRejection(IReadOnlyList<FeatureRequirement> requirements)
    : IStatusCodeRejectionFeature
{
    public async Task<bool> TryWriteResponseAsync(HttpContext context)
    {
        // A challenge or a forbid is what a failed authorization produces. Any other status means something after
        // authorization chose the response, and the rejection must not overwrite it.
        if (context.Response.StatusCode is not (StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden))
        {
            return false;
        }

        // Drops the challenge headers too: a 409 asks the caller to wait for the feature, not to authenticate.
        context.Response.Clear();

        // Same descriptor and parameters IFeatureManager.EnsureEnabledAsync puts on its ConflictException, so a
        // client sees one error shape whether the gate ran in authorization or in application code.
        var errors = requirements
            .Select(requirement =>
                MessageDescriber
                    .FeatureCurrentlyUnavailable()
                    .WithParam("Type", requirement.RequiresAll ? "And" : "Or")
                    .WithParam("FeatureNames", requirement.FeatureNames)
            )
            .ToList();

        var problemDetails = context.RequestServices.GetRequiredService<IProblemDetailsCreator>().Conflict(errors);

        await TenantCatalogRejectionWriter
            .WriteAsync(context, StatusCodes.Status409Conflict, problemDetails)
            .ConfigureAwait(false);

        return true;
    }
}
