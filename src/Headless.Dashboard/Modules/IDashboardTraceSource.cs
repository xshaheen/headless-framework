// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;

namespace Headless.Dashboard;

/// <summary>
/// A module capability: finds the module's objects that carry a correlation or W3C trace id, so the shell can show
/// one request's work across modules.
/// </summary>
/// <remarks>
/// <para>
/// The shell asks every module in parallel with a per-module timeout; a module that times out or throws shows as
/// <c>Stale</c> in its place. Answer from an indexed lookup on the id.
/// </para>
/// <para>
/// Implemented by module packages on their <see cref="IDashboardModule"/>. New members arrive as default interface
/// members; existing members never gain a parameter, and the request record grows instead.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardTraceSource
{
    /// <summary>Returns the module's objects that carry <see cref="DashboardTraceRequest.TraceId"/>.</summary>
    /// <param name="request">The id to follow and who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the request ends or the shell's timeout passes.</param>
    /// <returns>The objects, oldest first; empty when none carry the id.</returns>
    Task<IReadOnlyList<DashboardObject>> FindByTraceAsync(
        DashboardTraceRequest request,
        CancellationToken cancellationToken
    );
}

/// <summary>A request for the objects that carry one trace or correlation id.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardTraceRequest
{
    /// <summary>Gets the trace or correlation id to follow.</summary>
    public required string TraceId { get; init; }

    /// <summary>Gets the signed-in user who is asking.</summary>
    public required ClaimsPrincipal User { get; init; }
}
