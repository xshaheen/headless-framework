// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// The actionable failures of one function with one failure fingerprint: how many runs, when the first and last of them
/// failed, and how many failed recently.
/// </summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record JobFailureGroup
{
    /// <summary>Gets the function the runs invoke.</summary>
    public required string Function { get; init; }

    /// <summary>Gets the runs' failure fingerprint, or <see langword="null"/> for failures stored without one.</summary>
    public string? FailureFingerprint { get; init; }

    /// <summary>Gets how many actionable failed runs the group holds.</summary>
    public required int Count { get; init; }

    /// <summary>Gets when the group's earliest run failed.</summary>
    public required DateTimeOffset FirstFailedAt { get; init; }

    /// <summary>Gets when the group's latest run failed.</summary>
    public required DateTimeOffset LastFailedAt { get; init; }

    /// <summary>Gets how many of the group's runs failed at or after the recent bound the read was given.</summary>
    public required int RecentCount { get; init; }
}
