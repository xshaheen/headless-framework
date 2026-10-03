// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// The complete recovery decision, as a value. Says which instant to materialize at, which existing row to repurpose,
/// which to step past, and where the resolution window ends — and nothing about how any of it is written.
/// </summary>
/// <remarks>
/// The set of rows to retire is expressed as a bound rather than as identities on purpose. When a saturated coalesce
/// pass DOES establish its run inside the examined prefix, the resolution extends past the inspected window to the
/// full recovery instant, so it covers rows the snapshot never contained. The bound plus each provider's
/// still-claimable predicate is therefore the only faithful expression of the set.
/// </remarks>
[PublicAPI]
public sealed record CronRecoveryPlan
{
    /// <summary>Ordered attempts at the coalesced run; empty under <c>Skip</c>, or when every missed instant is accounted for.</summary>
    public required IReadOnlyList<CronRecoveryRunStep> RunSteps { get; init; }

    /// <summary>The resolution to apply once a step succeeded.</summary>
    public required CronRecoveryResolution WhenRunEstablished { get; init; }

    /// <summary>The resolution to apply when no step did — or when there were none.</summary>
    public required CronRecoveryResolution WhenNoRunEstablished { get; init; }
}
