// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Messaging;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.

/// <summary>A command an Operation applies to every row in its scope.</summary>
[PublicAPI]
#pragma warning disable CA1008 // Every Operation applies one command, so a zero member would name none.
public enum MessageOperationKind
{
    /// <summary>
    /// Re-execute terminal current received generations, each through
    /// <see cref="IInboxOperationsApi.ForceReprocessAsync"/>.
    /// </summary>
    ReExecute = 1,

    /// <summary>
    /// Delete terminal rows: unheld received generations through <see cref="IInboxOperationsApi.PurgeAsync"/>, or
    /// published rows through <see cref="IPublishedOperationsApi.PurgeAsync"/>.
    /// </summary>
    Delete = 2,

    /// <summary>
    /// Send published rows that failed for good again, each through <see cref="IPublishedOperationsApi.ResendAsync"/>.
    /// </summary>
    Resend = 3,
}
#pragma warning restore CA1008

/// <summary>
/// The scope of an Operation over received inbox generations or published rows, frozen when the operator confirmed
/// it: the rows <see cref="Query"/> matches that the command may apply to and whose change marker was at or before
/// <see cref="HighWaterMark"/>.
/// </summary>
/// <remarks>
/// <para>
/// Read <see cref="HighWaterMark"/> from <c>IDataStorage.GetLatestMessageChangeMarkerAsync</c> at confirm. A failure
/// written after confirm carries a later marker, so it stays out of the scope however many chunks run.
/// </para>
/// <para>
/// Re-execute keeps a generation in scope only while it is the current generation of its lineage. Re-executing a
/// generation retires it and starts a child whose first write is after the mark, so running the Operation again, or
/// serving one chunk on two nodes, re-executes no message twice: a stale chunk's write is refused by the single-row
/// call, which checks the generation again under its lock.
/// </para>
/// <para>
/// Re-send and delete over published rows keep the same promise another way: each chunk target carries the row's
/// change marker, and the single-row write applies only while the row still carries it. A row re-sent and failed
/// again after the read has a later marker, so a stale chunk's write is refused and a new read leaves the row out.
/// </para>
/// <para>This record grows by <see langword="init"/> members.</para>
/// </remarks>
[PublicAPI]
public sealed record MessageOperationScope
{
    /// <summary>Gets the command the Operation applies.</summary>
    public required MessageOperationKind Kind { get; init; }

    /// <summary>
    /// Gets the rows the Operation covers. Its <see cref="MessageListQuery.MessageType"/> is
    /// <see cref="MessageType.Subscribe"/> for <see cref="IInboxOperationsApi"/> and <see cref="MessageType.Publish"/>
    /// for <see cref="IPublishedOperationsApi"/>.
    /// </summary>
    public required MessageListQuery Query { get; init; }

    /// <summary>Gets the store's latest change marker when the Operation was confirmed.</summary>
    public required DateTimeOffset HighWaterMark { get; init; }
}

/// <summary>One row in an Operation's scope, with the state the single-row write expects.</summary>
/// <param name="StorageId">The row's storage identifier.</param>
/// <param name="IncarnationId">
/// A received generation's incarnation, the single-row write's expected incarnation; <see cref="Guid.Empty"/> for a
/// published row, which has none.
/// </param>
/// <param name="Status">The row's status, the inbox single-row write's expected status.</param>
/// <param name="ChangedAt">
/// The row's change marker when the scope read returned it, the published single-row write's expected marker.
/// </param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct MessageOperationTarget(
    Guid StorageId,
    Guid IncarnationId,
    StatusName Status,
    DateTimeOffset ChangedAt
);

/// <summary>One chunk of an Operation's scope and where the next chunk resumes.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record MessageOperationChunk
{
    /// <summary>The most targets one chunk of an Operation's scope returns.</summary>
    public const int MaxChunkSize = 500;

    /// <summary>Gets the generations of this chunk, in the store's identifier order.</summary>
    public required IReadOnlyList<MessageOperationTarget> Targets { get; init; }

    /// <summary>
    /// Gets the storage identifier the next chunk reads after, or <see langword="null"/> when this chunk is the last.
    /// A generation keeps its identifier through every write, so a chunk never repeats or skips one because an earlier
    /// chunk changed it.
    /// </summary>
    public Guid? Next { get; init; }

    /// <summary>
    /// Returns the chunk of <paramref name="targets"/> read with <paramref name="chunkSize"/>: a full chunk resumes
    /// after its last target, and a shorter one is the last.
    /// </summary>
    /// <param name="targets">The targets read, at most <paramref name="chunkSize"/>, in the store's identifier order.</param>
    /// <param name="chunkSize">The most targets the read asked for.</param>
    internal static MessageOperationChunk Of(IReadOnlyList<MessageOperationTarget> targets, int chunkSize)
    {
        // A full chunk may be the scope's last; the next read then returns an empty chunk with no cursor.
        return new MessageOperationChunk
        {
            Targets = targets,
            Next = targets.Count == chunkSize ? targets[^1].StorageId : null,
        };
    }
}

#pragma warning restore MA0048
