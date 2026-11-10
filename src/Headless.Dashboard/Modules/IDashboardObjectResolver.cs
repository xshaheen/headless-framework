// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;

namespace Headless.Dashboard;

/// <summary>
/// A module capability: turns an id an operator pasted into the palette into the module's objects that carry it.
/// </summary>
/// <remarks>
/// <para>
/// The shell asks every module in parallel with a per-module timeout and lists every match; a module that times out
/// or throws shows as <c>Stale</c> in its place. Answer with an exact-id lookup, never a text search.
/// </para>
/// <para>
/// Implemented by module packages on their <see cref="IDashboardModule"/>. New members arrive as default interface
/// members; existing members never gain a parameter, and the request record grows instead.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardObjectResolver
{
    /// <summary>Returns the module's objects whose id is <see cref="DashboardResolveRequest.Query"/>.</summary>
    /// <param name="request">The pasted id and who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the request ends or the shell's timeout passes.</param>
    /// <returns>The matching objects; empty when none match.</returns>
    Task<IReadOnlyList<DashboardObject>> ResolveAsync(
        DashboardResolveRequest request,
        CancellationToken cancellationToken
    );
}

/// <summary>A request to resolve a pasted id.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardResolveRequest
{
    /// <summary>Gets the id the operator pasted, trimmed.</summary>
    public required string Query { get; init; }

    /// <summary>Gets the signed-in user who is asking.</summary>
    public required ClaimsPrincipal User { get; init; }
}
