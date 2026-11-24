// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;

namespace Headless.Dashboard;

/// <summary>
/// A module capability: the module's objects that carry one correlation id, which the shell's trace page draws as one
/// tree with every other module's objects.
/// </summary>
/// <remarks>
/// <para>
/// The shell asks every module the user may view in parallel with a per-module timeout; a source that times out or
/// throws shows as <c>Stale</c> in its place while the others still answer. Match the correlation id exactly and
/// ordinally, through an index, and return at most the module's own limit of objects, oldest first. Set
/// <see cref="DashboardTraceAnswer.Limited"/> when more existed.
/// </para>
/// <para>
/// The shell builds the tree from each item's <see cref="DashboardTraceItem.Key"/> and
/// <see cref="DashboardTraceItem.Causation"/>, so a module never needs to know which module caused its objects.
/// </para>
/// <para>
/// Implemented by module packages on their <see cref="IDashboardModule"/>. New members arrive as default interface
/// members; existing members never gain a parameter, and the request and answer records grow instead.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardTraceSource
{
    /// <summary>Returns the module's objects whose correlation id is <see cref="DashboardTraceRequest.CorrelationId"/>.</summary>
    /// <remarks>
    /// A module that reads several stores or nodes never throws for one of them: it answers with what the others
    /// returned and names the ones that did not answer in <see cref="DashboardTraceAnswer.UnavailableSources"/>.
    /// </remarks>
    /// <param name="request">The correlation id and who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the request ends or the shell's timeout passes.</param>
    /// <returns>The objects, oldest first, whether the module stopped at its limit, and the sources that did not answer.</returns>
    Task<DashboardTraceAnswer> GetTraceAsync(DashboardTraceRequest request, CancellationToken cancellationToken);
}

/// <summary>A request for a module's objects that carry one correlation id.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardTraceRequest
{
    /// <summary>
    /// Gets the correlation id, exactly as the operator gave it: it is never trimmed, so <c>c-42</c> and
    /// <c>c-42 </c> are two ids. It holds at most 200 characters.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>Gets the signed-in user who is asking.</summary>
    public required ClaimsPrincipal User { get; init; }
}

/// <summary>A module's part of a trace: its objects, whether it stopped at its limit, and the sources that did not answer.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardTraceAnswer
{
    /// <summary>Gets the objects that carry the correlation id, oldest first, each once.</summary>
    public required IReadOnlyList<DashboardTraceItem> Items { get; init; }

    /// <summary>
    /// Gets the names of the sources, such as stores or nodes, that did not answer, so <see cref="Items"/> covers only
    /// the others; empty when every source answered.
    /// </summary>
    public IReadOnlyList<string> UnavailableSources { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether more objects carry the correlation id than the module's limit let it return,
    /// so the page shows the module's count as <c>N+</c>.
    /// </summary>
    public bool Limited { get; init; }

    /// <summary>
    /// Gets the module's own list filtered by the correlation id, which the page links to beside a limited answer, or
    /// <see langword="null"/> when the module has no such list.
    /// </summary>
    public DashboardTraceListLink? ListLink { get; init; }
}

/// <summary>
/// A module's own list, opened with a filter: the page navigates to <see cref="Path"/> and hands it
/// <see cref="State"/> as history state, so the correlation id never travels in the URL.
/// </summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardTraceListLink
{
    /// <summary>Gets the list's path inside the module, such as <c>runs</c>, without the base path or module id.</summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets the history state the list reads its filter from, in the module's own shape, such as
    /// <c>{ "correlationId": "ord-7" }</c>, or <see langword="null"/> when the list needs none.
    /// </summary>
    public JsonElement? State { get; init; }
}

/// <summary>One object on a trace: a job run, a published message, a received generation, or a scheduled delivery.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardTraceItem
{
    /// <summary>Gets the object's reference, which the row opens; its <see cref="ObjectRef.Kind"/> is the object's kind.</summary>
    public required ObjectRef Target { get; init; }

    /// <summary>
    /// Gets the name of the store that holds the object. With <see cref="StorageId"/> it identifies the row, so the
    /// shell lists an object once even when two nodes that share a store both return it.
    /// </summary>
    public required string Store { get; init; }

    /// <summary>Gets the object's id inside <see cref="Store"/>.</summary>
    public required string StorageId { get; init; }

    /// <summary>Gets the object's title, such as a job's function name or a message's topic.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the object's current status as its module names it, such as <c>Failed</c>, or <see langword="null"/>.</summary>
    public string? Status { get; init; }

    /// <summary>
    /// Gets the state role the status paints in: <c>sched</c>, <c>enq</c>, <c>proc</c>, <c>ok</c>, <c>fail</c>, or
    /// <c>off</c>, or <see langword="null"/> when the object has no status.
    /// </summary>
    public string? StatusRole { get; init; }

    /// <summary>
    /// Gets the instant the tree orders the object by, and <see cref="TimestampKind"/> says what it marks; or
    /// <see langword="null"/> for an object with no time yet, such as a chain run waiting on its parent. The tree lists
    /// such an object after its timed siblings, and the page shows it as waiting on its parent.
    /// </summary>
    public required DateTimeOffset? At { get; init; }

    /// <summary>Gets what <see cref="At"/> marks: when the object ran, when it is due, or when it was added.</summary>
    public DashboardTraceTimestampKind TimestampKind { get; init; }

    /// <summary>Gets the node that answered for the object, or <see langword="null"/> when its store is shared.</summary>
    public string? Node { get; init; }

    /// <summary>Gets the correlation id the object carries.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    /// Gets the object's own causal key: <see cref="DashboardTraceKeyKind.Run"/> for a job run,
    /// <see cref="DashboardTraceKeyKind.Message"/> for a published message, and
    /// <see cref="DashboardTraceKeyKind.Delivery"/> for a received generation.
    /// </summary>
    public required DashboardTraceKey Key { get; init; }

    /// <summary>
    /// Gets the key of the object that caused this one, as the object stored it, or <see langword="null"/> when it
    /// records none. The shell places the object under the item whose <see cref="Key"/> matches.
    /// </summary>
    public DashboardTraceKey? Causation { get; init; }

    /// <summary>
    /// Gets, for a received generation, the id of the message it delivers, or <see langword="null"/>. The shell places
    /// a received generation under that published message, not under its <see cref="Causation"/>: the causation a
    /// received envelope carries names the publisher's parent.
    /// </summary>
    public string? DeliversMessageId { get; init; }
}

/// <summary>A typed causal key: a kind of object and its id, so a raw id never matches an object of another kind.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardTraceKey
{
    /// <summary>Gets the kind of object the key names.</summary>
    public required DashboardTraceKeyKind Kind { get; init; }

    /// <summary>Gets the object's id, as its store holds it.</summary>
    public required string Value { get; init; }
}

/// <summary>The kind of object a <see cref="DashboardTraceKey"/> names.</summary>
[PublicAPI]
public enum DashboardTraceKeyKind
{
    /// <summary>A job run, by its run id.</summary>
    Run = 0,

    /// <summary>A published message, by its message id.</summary>
    Message = 1,

    /// <summary>A received generation, by its storage id.</summary>
    Delivery = 2,
}

/// <summary>What a <see cref="DashboardTraceItem.At"/> marks.</summary>
[PublicAPI]
public enum DashboardTraceTimestampKind
{
    /// <summary>When the object ran or was handled.</summary>
    Ran = 0,

    /// <summary>When the object is due, for one that has not run yet.</summary>
    Due = 1,

    /// <summary>When the object was added, such as a message's publish time.</summary>
    Added = 2,
}
