// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>The runs whose execution time falls in one time bucket, counted per status.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record JobRunBucket
{
    /// <summary>Gets the bucket's inclusive start (UTC).</summary>
    public required DateTime Start { get; init; }

    /// <summary>Gets the bucket's exclusive end (UTC).</summary>
    public required DateTime End { get; init; }

    /// <summary>
    /// Gets the runs per status, with no entry for a status no run has, or <see langword="null"/> when the store could
    /// not read the bucket. A chart draws a <see langword="null"/> bucket as a gap, never as zero.
    /// </summary>
    public IReadOnlyDictionary<JobStatus, int>? Counts { get; init; }
}
