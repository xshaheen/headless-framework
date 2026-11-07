// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// A module capability: the module's rows that changed since a cursor, read by the dashboard's live stream on every
/// node, so a change made through any node reaches every open view.
/// </summary>
/// <remarks>
/// <para>
/// While a node has at least one stream open, it keeps one reader per module that calls
/// <see cref="ReadChangesAsync"/> on a fixed interval with the cursor the previous read returned, starting from
/// <see cref="GetStartCursorAsync"/>. The reader reads every change of the module, whatever the open views show, and
/// filters per connection. No broker is involved: the module's store is the only shared state, so the cursor must
/// mean the same thing on every node.
/// </para>
/// <para>What the module's store must provide:</para>
/// <list type="bullet">
///   <item><description>
///   <b>A change marker stamped by the store.</b> Every write that changes what the dashboard shows of a row, its
///   state, progress, retry count, or hold, must also set the row's marker in the same statement, from a store
///   sequence or the store's own clock. A time an application node sampled cannot serve: another node's cursor may
///   already have passed it, and the change is never read.
///   </description></item>
///   <item><description>
///   <b>An index on the marker,</b> so a read since a cursor is a range seek. Every node reads every interval while a
///   stream is open.
///   </description></item>
///   <item><description>
///   <b>An overlap window.</b> A transaction that stamped an earlier marker can commit after one that stamped a later
///   marker, so read from the cursor minus a window longer than the store's longest write transaction. Rows read twice
///   are expected: the shell drops a change whose <see cref="DashboardChange.Version"/> it already sent for that
///   object.
///   </description></item>
///   <item><description>
///   <b>A bounded read.</b> Return at most a page of changes, in marker order, and a cursor that resumes after the
///   page, so a burst of writes is read over several intervals instead of in one large query.
///   </description></item>
/// </list>
/// <para>
/// A read that throws marks the module's live views <c>Stale</c>; the reader backs off and retries with the same
/// cursor, and the stream stays up.
/// </para>
/// <para>
/// Implemented by module packages on their <see cref="IDashboardModule"/>. New members arrive as default interface
/// members; existing members never gain a parameter.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardChangeSource
{
    /// <summary>
    /// Returns a cursor at the store's current position, so the first read returns only changes made from now on.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the node's last stream closes or the host stops.</param>
    /// <returns>The cursor, in the module's own format.</returns>
    Task<string> GetStartCursorAsync(CancellationToken cancellationToken);

    /// <summary>Returns the changes after <paramref name="cursor"/>, and the cursor to read from next.</summary>
    /// <param name="cursor">A cursor <see cref="GetStartCursorAsync"/> or an earlier read returned.</param>
    /// <param name="cancellationToken">Cancelled when the node's last stream closes or the host stops.</param>
    /// <returns>The changes in marker order, and the next cursor; the same cursor when nothing changed.</returns>
    Task<DashboardChangeBatch> ReadChangesAsync(string cursor, CancellationToken cancellationToken);
}

/// <summary>One read of a module's changes.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardChangeBatch
{
    /// <summary>Gets the changes, in marker order.</summary>
    public required IReadOnlyList<DashboardChange> Changes { get; init; }

    /// <summary>Gets the cursor to read from next, in the module's own format.</summary>
    public required string Cursor { get; init; }

    /// <summary>
    /// Gets the names of the sources, such as stores or nodes, the read could not reach, so <see cref="Changes"/>
    /// covers only the others. A read that reaches some sources answers with them instead of throwing; the shell sends
    /// the names with <c>module.stale</c> while the module stays live. Empty when every source answered.
    /// </summary>
    public IReadOnlyList<string> UnavailableSources { get; init; } = [];
}

/// <summary>One changed row, as live views patch it.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardChange
{
    /// <summary>Gets the changed object.</summary>
    public required ObjectRef Target { get; init; }

    /// <summary>
    /// Gets the row's version, which changes on every change the dashboard shows, such as its change marker. The
    /// shell sends one change per object and version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets the slim projection a list row needs to patch itself, or <see langword="null"/> to make views refetch the
    /// object. The shell passes it through the module's <see cref="IDashboardPayloadFormatter"/> when one handles the
    /// object's kind.
    /// </summary>
    public JsonElement? Projection { get; init; }

    /// <summary>
    /// Gets a value indicating whether the change can add or remove an item in Needs you, so the shell tells Needs you
    /// to refresh even while no module code is loaded.
    /// </summary>
    public bool AffectsAttention { get; init; }
}
