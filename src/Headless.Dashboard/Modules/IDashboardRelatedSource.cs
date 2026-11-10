// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;

namespace Headless.Dashboard;

/// <summary>
/// A module capability: adds related objects to another module's object, or its own, without referencing that
/// module, such as the messages a job run published.
/// </summary>
/// <remarks>
/// <para>
/// The shell asks every module with this capability in parallel with a per-module timeout, and draws each returned
/// group as a labelled list in the inspector and on the object page. A source that times out or throws shows as
/// <c>Stale</c> in its place. Every group id is a contribution the host can disable or reorder, so
/// <see cref="GroupIds"/> declares them all up front.
/// </para>
/// <para>
/// Implemented by module packages on their <see cref="IDashboardModule"/>. New members arrive as default interface
/// members; existing members never gain a parameter, and the request record grows instead.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardRelatedSource
{
    /// <summary>
    /// Gets the id of every group <see cref="GetRelatedAsync"/> can return, such as <c>messaging.published-messages</c>.
    /// The shell reads it once, when it builds the dashboard's manifest, to check the host's overrides against it.
    /// </summary>
    IReadOnlyList<string> GroupIds { get; }

    /// <summary>Returns the groups of objects related to <see cref="DashboardRelatedRequest.Target"/>.</summary>
    /// <param name="request">The object to relate to and who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the request ends or the shell's timeout passes.</param>
    /// <returns>
    /// The non-empty groups; empty when the source relates nothing to the object's kind. Each group's id must be one of
    /// <see cref="GroupIds"/>; the shell drops a group the host disabled.
    /// </returns>
    Task<IReadOnlyList<DashboardRelatedGroup>> GetRelatedAsync(
        DashboardRelatedRequest request,
        CancellationToken cancellationToken
    );
}

/// <summary>A request for the objects related to one object.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardRelatedRequest
{
    /// <summary>Gets the object to relate to, from any module.</summary>
    public required ObjectRef Target { get; init; }

    /// <summary>Gets the signed-in user who is asking.</summary>
    public required ClaimsPrincipal User { get; init; }
}

/// <summary>A labelled group of related objects, such as <c>Published messages</c>.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardRelatedGroup
{
    /// <summary>Gets the group's contribution id, one of <see cref="IDashboardRelatedSource.GroupIds"/>.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the group's label.</summary>
    public required string Label { get; init; }

    /// <summary>Gets the related objects.</summary>
    public required IReadOnlyList<DashboardObject> Items { get; init; }

    /// <summary>
    /// Gets a value indicating whether more related objects exist than <see cref="Items"/> holds, so the shell offers
    /// to open the full list.
    /// </summary>
    public bool HasMore { get; init; }
}
