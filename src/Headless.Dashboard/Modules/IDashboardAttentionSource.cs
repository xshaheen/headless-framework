// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;

namespace Headless.Dashboard;

/// <summary>
/// A module capability: the items that need an operator's attention, which the shell lists in Needs you.
/// </summary>
/// <remarks>
/// <para>
/// The shell calls every module's source in parallel with a per-module timeout; a source that times out or throws
/// shows as <c>Stale</c> in its place while the others still answer. Keep each call to one cheap query, because the
/// shell calls it whenever Needs you is opened or its attention changes.
/// </para>
/// <para>
/// Implemented by module packages on their <see cref="IDashboardModule"/>. New members arrive as default interface
/// members; existing members never gain a parameter, and the request record grows instead.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardAttentionSource
{
    /// <summary>Returns the module's items that need attention now.</summary>
    /// <param name="request">The request: who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the request ends or the shell's timeout passes.</param>
    /// <returns>The items, most urgent first.</returns>
    Task<IReadOnlyList<DashboardAttentionItem>> GetAttentionAsync(
        DashboardAttentionRequest request,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Returns the module's items that need attention now, with the sources, such as stores or nodes, that did not
    /// answer. The shell calls this member; by default it returns <see cref="GetAttentionAsync"/> with every source
    /// answered.
    /// </summary>
    /// <remarks>
    /// A module that reads several sources overrides it, so one source that fails leaves the others' items listed and
    /// is named in <see cref="DashboardAttentionAnswer.UnavailableSources"/>, instead of the whole module showing
    /// <c>Stale</c>. <see cref="GetAttentionAsync"/> then returns the same items.
    /// </remarks>
    /// <param name="request">The request: who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the request ends or the shell's timeout passes.</param>
    /// <returns>The items, most urgent first, and the sources that did not answer.</returns>
    async Task<DashboardAttentionAnswer> GetAttentionAnswerAsync(
        DashboardAttentionRequest request,
        CancellationToken cancellationToken
    )
    {
        return new DashboardAttentionAnswer
        {
            Items = await GetAttentionAsync(request, cancellationToken).ConfigureAwait(false),
        };
    }
}

/// <summary>A request for a module's attention items.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardAttentionRequest
{
    /// <summary>Gets the signed-in user the items are for.</summary>
    public required ClaimsPrincipal User { get; init; }
}

/// <summary>A module's attention items, and the sources that did not answer.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardAttentionAnswer
{
    /// <summary>Gets the items, most urgent first.</summary>
    public required IReadOnlyList<DashboardAttentionItem> Items { get; init; }

    /// <summary>
    /// Gets the names of the sources, such as stores or nodes, that did not answer, so <see cref="Items"/> covers only
    /// the others; empty when every source answered.
    /// </summary>
    public IReadOnlyList<string> UnavailableSources { get; init; } = [];
}

/// <summary>One item in Needs you, such as "12 runs failed in the last hour".</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardAttentionItem
{
    /// <summary>Gets the item's id, unique within the module.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the key the shell groups items by, so items with one cause, such as one failing function, show as one row.
    /// </summary>
    public required string GroupKey { get; init; }

    /// <summary>Gets the item's title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets how urgent the item is.</summary>
    public DashboardAttentionSeverity Severity { get; init; }

    /// <summary>Gets how many objects the item covers.</summary>
    public long Count { get; init; }

    /// <summary>Gets the object the item points at, or <see langword="null"/> when it covers several.</summary>
    public ObjectRef? Target { get; init; }

    /// <summary>Gets when the condition began, from the module's store, or <see langword="null"/>.</summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>Gets when the cause last occurred, such as the latest failure of the group, or <see langword="null"/>.</summary>
    public DateTimeOffset? LastSeenAt { get; init; }

    /// <summary>
    /// Gets how many objects joined the item in the module's most recent interval, such as the last hour. Above zero,
    /// the shell marks the cause as still growing.
    /// </summary>
    public long RecentCount { get; init; }

    /// <summary>
    /// Gets the id of the module command that resolves the item, such as its Requeue, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The shell offers the command as the item's primary button and runs it as an Operation over
    /// <see cref="GroupFilter"/>, so it needs both, and a module that implements <see cref="IDashboardOperationRunner"/>.
    /// It offers nothing when the host disabled the command or hid it from the UI.
    /// </remarks>
    public string? RecommendedCommandId { get; init; }

    /// <summary>
    /// Gets the filter, in the module's own Operation filter shape, that selects every object of the item's group,
    /// such as <c>{ "status": "Failed", "error": "..." }</c>; the recommended command's Operation is scoped by it.
    /// </summary>
    public JsonElement? GroupFilter { get; init; }
}

/// <summary>How urgent a Needs-you item is.</summary>
[PublicAPI]
public enum DashboardAttentionSeverity
{
    /// <summary>Needs an operator soon, such as a retry that is still pending.</summary>
    Warning = 0,

    /// <summary>Needs an operator now, such as work that failed for good.</summary>
    Critical = 1,
}
