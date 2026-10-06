// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

/// <summary>
/// Wraps the authorization evaluator so that an HTTP authorization that failed only on
/// <see cref="IDescribedRequirement"/>s, such as a disabled feature, hands the status-codes rewriter the error those
/// requirements describe. The requirement's handler cannot set the rejection itself when it lives in a package without
/// the HTTP types.
/// </summary>
/// <remarks>
/// The evaluator, not the handler or the middleware result handler, is the hook because it runs once after every
/// handler, sees the whole outcome, and stays in place when an application registers its own
/// <see cref="IAuthorizationMiddlewareResultHandler"/>.
/// </remarks>
internal sealed class DescribedRequirementRejectionEvaluator(IAuthorizationEvaluator inner) : IAuthorizationEvaluator
{
    private readonly IAuthorizationEvaluator _inner = Argument.IsNotNull(inner);

    public AuthorizationResult Evaluate(AuthorizationHandlerContext context)
    {
        var result = _inner.Evaluate(context);

        // Only when described requirements are the sole reason: an unauthenticated caller of an endpoint that also
        // requires a user keeps its challenge, a missing permission keeps its 403, and an explicit Fail from any
        // handler wins.
        if (
            result.Failure is { FailCalled: false } failure
            && context.Resource is HttpContext httpContext
            && _OnlyDescribedRequirementsFailed(failure.FailedRequirements, out var requirements)
        )
        {
            httpContext.TrySetStatusCodeRejection(new DescribedRequirementRejection(requirements));
        }

        return result;
    }

    private static bool _OnlyDescribedRequirementsFailed(
        IEnumerable<IAuthorizationRequirement> failedRequirements,
        out List<IDescribedRequirement> requirements
    )
    {
        requirements = [];

        foreach (var requirement in failedRequirements)
        {
            if (requirement is not IDescribedRequirement described)
            {
                return false;
            }

            requirements.Add(described);
        }

        return requirements.Count > 0;
    }
}

/// <summary>
/// Writes the problem response that failed <see cref="IDescribedRequirement"/>s describe, in place of the challenge or
/// forbid the authorization middleware produced.
/// </summary>
internal sealed class DescribedRequirementRejection(IReadOnlyList<IDescribedRequirement> requirements)
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

        // Described here rather than at evaluation, so localized messages follow the request's culture at write time.
        var errors = requirements.Select(requirement => requirement.DescribeFailure()).ToList();
        var statusCode = _GetStatusCode(errors[0]);

        // Requirements that disagree on the kind of failure have no single honest status; the default 401/403 stays.
        if (errors.Exists(error => _GetStatusCode(error) != statusCode))
        {
            return false;
        }

        // Drops the challenge headers too: the described error is not a request to authenticate.
        context.Response.Clear();

        var creator = context.RequestServices.GetRequiredService<IProblemDetailsCreator>();
        var problemDetails = _CreateProblemDetails(creator, statusCode, errors);

        await TenantCatalogRejectionWriter.WriteAsync(context, statusCode, problemDetails).ConfigureAwait(false);

        return true;
    }

    // The same error-kind to status mapping ApiResult errors use on the Minimal API and MVC response paths.
    private static int _GetStatusCode(ApiResultError error)
    {
        return error switch
        {
            ForbiddenError => StatusCodes.Status403Forbidden,
            UnauthorizedError => StatusCodes.Status401Unauthorized,
            NotFoundError => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict,
        };
    }

    private static ProblemDetails _CreateProblemDetails(
        IProblemDetailsCreator creator,
        int statusCode,
        List<ApiResultError> errors
    )
    {
        return statusCode switch
        {
            StatusCodes.Status403Forbidden => creator.Forbidden(error: ((ForbiddenError)errors[0]).Error),
            StatusCodes.Status401Unauthorized => creator.Unauthorized(((UnauthorizedError)errors[0]).Error),
            StatusCodes.Status404NotFound => creator.EntityNotFound(),
            _ => creator.Conflict([.. errors.SelectMany(_ToDescriptors)]),
        };
    }

    private static IEnumerable<ErrorDescriptor> _ToDescriptors(ApiResultError error)
    {
        return error is ConflictError conflict ? conflict.Errors : [error.ToErrorDescriptor()];
    }
}
