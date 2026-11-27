// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

/// <summary>The filters and page of an <see cref="ISequenceInspector.ListCountersAsync" /> call.</summary>
[PublicAPI]
public sealed record SequenceCounterQuery
{
    /// <summary>
    /// Gets the tenant whose counters to list: an id, the empty string for the host scope, or
    /// <see langword="null" /> (the default) for every tenant.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the exact counter name to list, or <see langword="null" /> (the default) for every name.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets text the counter name or the tenant id must contain, ignoring case for ASCII letters only, or
    /// <see langword="null" /> (the default) for no search. At most 128 characters.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>
    /// Gets the mode to list, decided by the policy each name resolves to now, or <see langword="null" /> (the default)
    /// for every mode.
    /// </summary>
    public SequenceMode? Mode { get; init; }

    /// <summary>
    /// Gets whether to list only current partitions: for a resetting policy, the partition it chooses now; for a
    /// policy that never resets, every partition. The default is <see langword="false" />.
    /// </summary>
    /// <remarks>
    /// A partition the caller chose under a resetting policy, such as a device id on a monthly name, is never the
    /// period the policy chooses, so it is not current and this filter drops it.
    /// </remarks>
    public bool CurrentPartitionsOnly { get; init; }

    /// <summary>
    /// Gets the key to continue after: the previous page's <see cref="SequenceCounterPage.Next" />, or
    /// <see langword="null" /> (the default) for the first page.
    /// </summary>
    public SequenceKey? After { get; init; }

    /// <summary>Gets the most counters to return, 1 to 200. The default is 50.</summary>
    public int Limit { get; init; } = 50;
}

/// <summary>The counter and page of an <see cref="ISequenceInspector.ListPartitionsAsync" /> call.</summary>
[PublicAPI]
public sealed record SequencePartitionQuery
{
    /// <summary>Gets the owning tenant id, or the empty string (the default) for the host scope.</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Gets the counter name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the partition to continue before: the previous page's <see cref="SequencePartitionPage.Next" />, or
    /// <see langword="null" /> (the default) for the first page.
    /// </summary>
    public string? Before { get; init; }

    /// <summary>Gets the most partitions to return, 1 to 200. The default is 50.</summary>
    public int Limit { get; init; } = 50;
}

/// <summary>The filters of an <see cref="ISequenceInspector.GetSummaryAsync" /> call.</summary>
[PublicAPI]
public sealed record SequenceSummaryQuery
{
    /// <summary>
    /// Gets the tenant whose counters to count: an id, the empty string for the host scope, or
    /// <see langword="null" /> (the default) for every tenant.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the exact counter name to count, or <see langword="null" /> (the default) for every name.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets text the counter name or the tenant id must contain, ignoring case for ASCII letters only, or
    /// <see langword="null" /> (the default) for no search.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Gets whether to count only current partitions, as <see cref="SequenceCounterQuery.CurrentPartitionsOnly" />.</summary>
    public bool CurrentPartitionsOnly { get; init; }
}
