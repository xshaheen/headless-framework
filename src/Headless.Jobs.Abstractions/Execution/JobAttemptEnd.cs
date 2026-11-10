// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// The end of the attempt an executor write closes, carried on <see cref="JobExecutionState.AttemptEnd"/>: a provider
/// closes the run's open attempt with it in the same atomic write that applies the state, or inserts a closed attempt
/// when no open one exists.
/// </summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record JobAttemptEnd
{
    /// <summary>How the attempt ended.</summary>
    public required JobAttemptOutcome Outcome { get; init; }

    /// <summary>
    /// When the executor invoked the handler for this attempt (UTC), or <see langword="null"/> when the run ended
    /// before the handler ran. A provider uses it only when it inserts a closed attempt because the open one is missing.
    /// </summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>When the attempt ended (UTC).</summary>
    public required DateTime EndedAt { get; init; }

    /// <summary>The attempt's exception text, or the reason it ended; <see langword="null"/> when there is none.</summary>
    public string? ExceptionMessage { get; init; }

    /// <summary>The failure fingerprint of a failed attempt; see <see cref="JobFailureFingerprints"/>.</summary>
    public string? FailureFingerprint { get; init; }
}
