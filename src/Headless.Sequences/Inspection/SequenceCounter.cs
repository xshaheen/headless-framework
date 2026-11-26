// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

/// <summary>One stored counter, as <see cref="ISequenceInspector" /> reads it.</summary>
[PublicAPI]
public sealed record SequenceCounter
{
    /// <summary>Gets the counter's key: the tenant id (empty for the host scope), name, and partition (empty for none).</summary>
    public required SequenceKey Key { get; init; }

    /// <summary>Gets the stored value: the last value issued, reserved, or reported.</summary>
    /// <remarks>
    /// For a fast policy with a <see cref="SequencePolicy.BlockSize" /> above 1 it is the end of the last reserved
    /// block, not the last number handed out.
    /// </remarks>
    public required long Value { get; init; }

    /// <summary>Gets when the counter last changed, by the database clock.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Gets the policy the counter's name resolves to now: its registered policy or the default policy.</summary>
    public required SequencePolicy Policy { get; init; }

    /// <summary>
    /// Gets whether this is the partition the policy chooses now, always <see langword="true" /> for a policy that
    /// never resets. A partition that is not current is closed and holds its final value.
    /// </summary>
    public required bool IsCurrentPartition { get; init; }

    /// <summary>
    /// Gets <see cref="Value" /> formatted with the policy's <see cref="SequencePolicy.Format" />, or
    /// <see langword="null" /> for a reported policy or one with no format.
    /// </summary>
    /// <remarks>
    /// The date tokens come from <see cref="UpdatedAt" /> in the policy's time zone when that date is in the
    /// partition's period, and otherwise from the period's first day. For a policy that never resets they come from
    /// <see cref="UpdatedAt" /> and may differ from the date the number was issued with.
    /// </remarks>
    public string? LastNumber { get; init; }

    /// <summary>
    /// Gets when the counter's name next starts a new partition, or <see langword="null" /> for a policy that never
    /// resets.
    /// </summary>
    public DateTimeOffset? NextResetAt { get; init; }
}

/// <summary>One page of <see cref="ISequenceInspector.ListCountersAsync" />.</summary>
/// <param name="Items">The counters, in key order.</param>
/// <param name="Next">The key to pass as <see cref="SequenceCounterQuery.After" />, or <see langword="null" /> on the last page.</param>
[PublicAPI]
public sealed record SequenceCounterPage(IReadOnlyList<SequenceCounter> Items, SequenceKey? Next);

/// <summary>One page of <see cref="ISequenceInspector.ListPartitionsAsync" />.</summary>
/// <param name="Items">The partitions of one counter, newest first.</param>
/// <param name="Next">The partition to pass as <see cref="SequencePartitionQuery.Before" />, or <see langword="null" /> on the last page.</param>
[PublicAPI]
public sealed record SequencePartitionPage(IReadOnlyList<SequenceCounter> Items, string? Next);

/// <summary>The counts of <see cref="ISequenceInspector.GetSummaryAsync" />.</summary>
[PublicAPI]
public sealed record SequenceCounterSummary
{
    /// <summary>Gets how many counters match, at most 10,000.</summary>
    public required int Total { get; init; }

    /// <summary>Gets how many of the counted counters resolve to a fast policy.</summary>
    public required int Fast { get; init; }

    /// <summary>Gets how many of the counted counters resolve to a gap-free policy.</summary>
    public required int GapFree { get; init; }

    /// <summary>Gets how many of the counted counters resolve to a reported policy.</summary>
    public required int Reported { get; init; }

    /// <summary>
    /// Gets whether more than 10,000 counters match: counting stopped, and the counts cover the first 10,000 in key
    /// order.
    /// </summary>
    public required bool Capped { get; init; }

    /// <summary>
    /// Gets the earliest next reset of the registered policies and the default policy, whether or not a counter
    /// exists under them yet, or <see langword="null" /> when none resets.
    /// </summary>
    public DateTimeOffset? NextResetAt { get; init; }
}
