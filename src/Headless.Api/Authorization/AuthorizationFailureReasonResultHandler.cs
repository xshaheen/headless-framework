// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

/// <summary>
/// Shows the caller why a forbidden request was refused: when authorization forbids a request and its handlers
/// failed it with <see cref="AuthorizationFailureReason" />s, the status-codes rewriter writes the standard 403
/// problem response with the reasons' messages as its <c>detail</c>.
/// </summary>
/// <remarks>
/// <para>
/// The reasons travel through the request's <see cref="IStatusCodeRejectionFeature" />, which the first failure owns.
/// A handler that already set its own rejection (the tenant requirement, the tenant identifier mismatch) keeps its
/// response, so the machine tokens those handlers pass as reasons never reach the caller.
/// </para>
/// <para>
/// A challenge (401) is unchanged: the caller must sign in first, and the reasons describe a decision about a caller
/// that is not known yet.
/// </para>
/// </remarks>
internal sealed class AuthorizationFailureReasonResultHandler(IAuthorizationMiddlewareResultHandler inner)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly IAuthorizationMiddlewareResultHandler _inner = Argument.IsNotNull(inner);

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult
    )
    {
        if (authorizeResult is { Forbidden: true, AuthorizationFailure.FailureReasons: var reasons })
        {
            var messages = reasons
                .Select(static reason => reason.Message)
                .Where(static message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (messages.Count > 0)
            {
                context.TrySetStatusCodeRejection(new AuthorizationFailureReasonRejection(string.Join(' ', messages)));
            }
        }

        return _inner.HandleAsync(next, context, policy, authorizeResult);
    }
}

/// <summary>Writes the 403 problem response carrying the authorization failure reasons as its detail.</summary>
internal sealed class AuthorizationFailureReasonRejection(string detail) : IStatusCodeRejectionFeature
{
    public async Task<bool> TryWriteResponseAsync(HttpContext context)
    {
        // Only the forbid this rejection was set for; a later component that chose another status keeps it.
        if (context.Response.StatusCode != StatusCodes.Status403Forbidden)
        {
            return false;
        }

        context.Response.Clear();

        var problemDetails = context.RequestServices.GetRequiredService<IProblemDetailsCreator>().Forbidden(detail);

        await TenantCatalogRejectionWriter
            .WriteAsync(context, StatusCodes.Status403Forbidden, problemDetails)
            .ConfigureAwait(false);

        return true;
    }
}
