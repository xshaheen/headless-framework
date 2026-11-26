// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;

namespace Headless.Sequences;

/// <summary>
/// Defines the provider seam behind <see cref="ISequenceInspector" />: plain reads of the counter table that never
/// write, lock for update, or open a transaction.
/// </summary>
/// <remarks>
/// The reader knows nothing about policies. The inspector turns its query's mode and current-partition filters into an
/// explicit <see cref="SequenceCounterSelection" /> of names and partitions before calling it, and classifies modes
/// through <see cref="SequenceModeNames" />. Each call runs one statement that does not wait on a row another
/// transaction holds. A provider package registers the implementation; application code never calls it.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ISequenceCounterReader
{
    /// <summary>
    /// Reads at most <see cref="SequenceCounterReadRequest.Limit" /> counters that match the filter, ordered by tenant
    /// id, name, then partition, ordinally, starting after <see cref="SequenceCounterReadRequest.After" />.
    /// </summary>
    /// <param name="request">The filter, the cursor, and the row limit.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The matching rows, in key order.</returns>
    ValueTask<IReadOnlyList<SequenceCounterRecord>> ListAsync(
        SequenceCounterReadRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads at most <see cref="SequencePartitionReadRequest.Limit" /> partitions of one tenant's counter name,
    /// partition descending, before <see cref="SequencePartitionReadRequest.Before" /> when it is set.
    /// </summary>
    /// <param name="request">The tenant, the name, the cursor, and the row limit.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The rows, newest partition first.</returns>
    ValueTask<IReadOnlyList<SequenceCounterRecord>> ListPartitionsAsync(
        SequencePartitionReadRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Counts the first <see cref="SequenceCounterCountRequest.Cap" /> matching counters in key order, in total and by
    /// mode, in one statement that reads at most one row past the cap.
    /// </summary>
    /// <param name="request">The filter, the mode classification, and the cap.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The counts, and whether more rows than the cap matched.</returns>
    ValueTask<SequenceCounterCounts> CountAsync(
        SequenceCounterCountRequest request,
        CancellationToken cancellationToken = default
    );
}

/// <summary>One stored counter row.</summary>
/// <param name="Key">The counter's key.</param>
/// <param name="Value">The stored value.</param>
/// <param name="UpdatedAt">When the row last changed, by the database clock.</param>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SequenceCounterRecord(SequenceKey Key, long Value, DateTimeOffset UpdatedAt);

/// <summary>The rows <see cref="ISequenceCounterReader.ListAsync" /> reads.</summary>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SequenceCounterReadRequest
{
    /// <summary>Gets the filter every row matches.</summary>
    public required SequenceCounterFilter Filter { get; init; }

    /// <summary>Gets the key the rows sort after, or <see langword="null" /> to start at the first row.</summary>
    public SequenceKey? After { get; init; }

    /// <summary>Gets the most rows to read.</summary>
    public required int Limit { get; init; }
}

/// <summary>The rows <see cref="ISequenceCounterReader.ListPartitionsAsync" /> reads.</summary>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SequencePartitionReadRequest
{
    /// <summary>Gets the stored tenant id, empty for the host scope.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the counter name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the partition the rows sort before, or <see langword="null" /> to start at the newest.</summary>
    public string? Before { get; init; }

    /// <summary>Gets the most rows to read.</summary>
    public required int Limit { get; init; }
}

/// <summary>The rows <see cref="ISequenceCounterReader.CountAsync" /> counts.</summary>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SequenceCounterCountRequest
{
    /// <summary>Gets the filter every counted row matches.</summary>
    public required SequenceCounterFilter Filter { get; init; }

    /// <summary>Gets the names that decide each row's mode.</summary>
    public required SequenceModeNames Modes { get; init; }

    /// <summary>Gets the most rows to count.</summary>
    public required int Cap { get; init; }
}

/// <summary>The counts <see cref="ISequenceCounterReader.CountAsync" /> returns.</summary>
/// <param name="Total">The rows counted, at most the cap.</param>
/// <param name="Fast">The counted rows whose mode is <see cref="SequenceMode.Fast" />.</param>
/// <param name="GapFree">The counted rows whose mode is <see cref="SequenceMode.GapFree" />.</param>
/// <param name="Reported">The counted rows whose mode is <see cref="SequenceMode.Reported" />.</param>
/// <param name="Capped">Whether more rows than the cap matched.</param>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SequenceCounterCounts(int Total, int Fast, int GapFree, int Reported, bool Capped);

/// <summary>What a counter row must match: every set part, together.</summary>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SequenceCounterFilter
{
    /// <summary>Gets the exact stored tenant id (empty for the host scope), or <see langword="null" /> for every tenant.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the exact counter name, or <see langword="null" /> for every name.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets lowercase text the lowercased name or tenant id contains, matched literally, or <see langword="null" />
    /// for no search.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Gets the names and partitions a row must be one of, or <see langword="null" /> for every row.</summary>
    public SequenceCounterSelection? Selection { get; init; }
}

/// <summary>
/// The names and partitions a row must be one of: a name in <see cref="Names" /> in any partition, a pair in
/// <see cref="NamePartitions" />, or, when <see cref="IncludeUnregisteredNames" /> is set, a name outside
/// <see cref="RegisteredNames" /> in <see cref="UnregisteredNamePartition" /> or any partition when that is
/// <see langword="null" />.
/// </summary>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SequenceCounterSelection
{
    /// <summary>Gets every registered counter name, so a row whose name is not one of them is unregistered.</summary>
    public IReadOnlyCollection<string> RegisteredNames { get; init; } = [];

    /// <summary>Gets the registered names selected in every partition.</summary>
    public IReadOnlyCollection<string> Names { get; init; } = [];

    /// <summary>Gets the registered names selected in one partition each.</summary>
    public IReadOnlyCollection<SequenceNamePartition> NamePartitions { get; init; } = [];

    /// <summary>Gets whether names outside <see cref="RegisteredNames" /> are selected.</summary>
    public bool IncludeUnregisteredNames { get; init; }

    /// <summary>
    /// Gets the one partition unregistered names are selected in, or <see langword="null" /> for every partition.
    /// </summary>
    public string? UnregisteredNamePartition { get; init; }
}

/// <summary>A counter name and one of its partitions.</summary>
/// <param name="Name">The counter name.</param>
/// <param name="Partition">The stored partition.</param>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SequenceNamePartition(string Name, string Partition);

/// <summary>
/// The registered names of each mode, and the mode of every other name, which the default policy decides.
/// </summary>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SequenceModeNames
{
    /// <summary>Gets the registered names whose policy is fast.</summary>
    public IReadOnlyCollection<string> Fast { get; init; } = [];

    /// <summary>Gets the registered names whose policy is gap-free.</summary>
    public IReadOnlyCollection<string> GapFree { get; init; } = [];

    /// <summary>Gets the registered names whose policy is reported.</summary>
    public IReadOnlyCollection<string> Reported { get; init; } = [];

    /// <summary>Gets the mode of every name not registered.</summary>
    public SequenceMode DefaultMode { get; init; }
}
