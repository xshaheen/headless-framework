// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Security;
using Microsoft.AspNetCore.Http;

namespace Headless.LiveQueries.Internal;

/// <summary>Who sent a request, as live endpoints tell callers apart.</summary>
internal static class LiveCaller
{
    /// <summary>
    /// Returns the user within its tenant, or <see langword="null" /> for a caller that is anonymous or carries no
    /// id.
    /// </summary>
    public static string? UserOf(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var id =
            user.FindFirst(UserClaimTypes.UserId)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.Identity.Name;

        // The same user id in two tenants is two users.
        return string.IsNullOrEmpty(id) ? null : $"{user.GetTenantId()}:{id}";
    }

    /// <summary>
    /// Returns the client session of <paramref name="httpContext" /> for command acknowledgement, or
    /// <see langword="null" /> when the request names no usable session.
    /// </summary>
    public static LiveSubscriber? SubscriberOf(HttpContext httpContext)
    {
        // Several values join with a comma, which no session id may hold, so a repeated header is ignored.
        var session = httpContext.Request.Headers[LiveCommandHeaders.Session].ToString();

        // Most requests name no session, and resolving the user reads its claims.
        return session.Length == 0 ? null : LiveSubscriber.TryCreate(session, UserOf(httpContext.User));
    }
}
