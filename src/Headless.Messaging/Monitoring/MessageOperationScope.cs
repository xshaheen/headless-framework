// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Messaging;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.

/// <summary>A command an Operation applies to every received inbox generation in its scope.</summary>
[PublicAPI]
#pragma warning disable CA1008 // Every Operation applies one command, so a zero member would name none.
public enum MessageOperationKind
{
    /// <summary>
    /// Re-execute terminal current generations, each through <see cref="IInboxOperationsApi.ForceReprocessAsync"/>.
    /// </summary>
    ReExecute = 1,

    /// <summary>Delete terminal unheld generations, each through <see cref="IInboxOperationsApi.PurgeAsync"/>.</summary>
    Delete = 2,
}
#pragma warning restore CA1008

/// <summary>
/// The scope of an Operation over received inbox generations, frozen when the operator confirmed it: the generations
/// <see cref="Query"/> matches that the command may apply to and whose change marker was at or before
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
/// <para>This record grows by <see langword="init"/> members.</para>
/// </remarks>
[PublicAPI]
public sealed record MessageOperationScope
{
    /// <summary>Gets the command the Operation applies.</summary>
    public required MessageOperationKind Kind { get; init; }

    /// <summary>
    /// Gets the generations the Operation covers. Its <see cref="MessageListQuery.MessageType"/> must be
    /// <see cref="MessageType.Subscribe"/>.
    /// </summary>
    public required MessageListQuery Query { get; init; }

    /// <summary>Gets the store's latest change marker when the Operation was confirmed.</summary>
    public required DateTimeOffset HighWaterMark { get; init; }
}

/// <summary>One generation in an Operation's scope, with the state the single-row write expects.</summary>
/// <param name="StorageId">The generation's storage identifier.</param>
/// <param name="IncarnationId">The generation's incarnation, the single-row write's expected incarnation.</param>
/// <param name="Status">The generation's status, the single-row write's expected status.</param>
/// <param name="ChangedAt">The generation's change marker when the scope read returned it.</param>
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
    /// <summary>Gets the generations of this chunk, in the store's identifier order.</summary>
    public required IReadOnlyList<MessageOperationTarget> Targets { get; init; }

    /// <summary>
    /// Gets the storage identifier the next chunk reads after, or <see langword="null"/> when this chunk is the last.
    /// A generation keeps its identifier through every write, so a chunk never repeats or skips one because an earlier
    /// chunk changed it.
    /// </summary>
    public Guid? Next { get; init; }
}

#pragma warning restore MA0048
