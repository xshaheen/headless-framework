// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;

namespace Headless.Dashboard;

/// <summary>
/// A module capability: the counts its sidebar entries show, keyed by each entry's <c>countKey</c>, such as one count
/// per run state.
/// </summary>
/// <remarks>
/// <para>
/// The shell asks for every count of one product in one request, <c>GET api/counts?product=</c>, and calls each of the
/// product's modules with a per-module timeout; a module that times out or throws shows as <c>Stale</c> while the
/// others still answer. The shell refetches whenever the module's live topics change, so keep each count to one cheap,
/// capped query.
/// </para>
/// <para>
/// Implemented by module packages on their <see cref="IDashboardModule"/>. New members arrive as default interface
/// members; existing members never gain a parameter, and the request record grows instead.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardCountSource
{
    /// <summary>Returns the module's sidebar counts.</summary>
    /// <param name="request">The request: who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the request ends or the shell's timeout passes.</param>
    /// <returns>The counts by count key. A key the sidebar declares but the answer lacks shows no count.</returns>
    Task<IReadOnlyDictionary<string, DashboardCount>> GetCountsAsync(
        DashboardCountsRequest request,
        CancellationToken cancellationToken
    );
}

/// <summary>A request for a module's sidebar counts.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardCountsRequest
{
    /// <summary>Gets the signed-in user the counts are for.</summary>
    public required ClaimsPrincipal User { get; init; }
}

/// <summary>One sidebar count, such as the number of failed runs.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardCount
{
    /// <summary>Gets the count, or the cap the module stopped counting at when <see cref="Capped"/> is set.</summary>
    public required long Count { get; init; }

    /// <summary>
    /// Gets a value indicating whether the module stopped counting at a cap, so the real count is higher and the shell
    /// shows <c>N+</c>.
    /// </summary>
    public bool Capped { get; init; }
}
